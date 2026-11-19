using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Resources;
using URUI.Resources;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        static readonly NativeMethods.ImageResolveCallback imageResolveCallback = ResolveImage;
        static readonly NativeMethods.ImageReleaseCallback imageReleaseCallback = ReleaseImage;
        readonly Dictionary<ulong, URUI.Documents.Document> resolvedImageOwners = new();
        BorrowedTextureRegistry.SubmissionPins legacyImagePins = ImagePinRetirement.RentPins();
        readonly List<ImageSubmissionFence> imageSubmissions = new List<ImageSubmissionFence>();
        readonly ImageSubmissionBudget submissionBudget = new();
        ImageSubmissionFence ReserveImageSubmission()
        {
            submissionBudget.Reserve();
            ImageSubmissionFence item = null;
            try
            {
                item = idleImageSubmissions.Count > 0 ? idleImageSubmissions.Pop() : new ImageSubmissionFence { Return = returnImageSubmission ??= ReturnImageSubmission };
                item.Budget = submissionBudget;
                imageSubmissions.Add(item);
                return item;
            }
            catch
            {
                submissionBudget.Release();
                if (item != null)
                {
                    item.Budget = null;
                    ReturnImageSubmission(item);
                }
                throw;
            }
        }
        readonly Stack<ImageSubmissionFence> idleImageSubmissions = new();
        Action<ImageSubmissionFence> returnImageSubmission;
        void ReturnImageSubmission(ImageSubmissionFence submission)
        {
            if (this && !resourcesReleased && idleImageSubmissions.Count < 8)
            {
                idleImageSubmissions.Push(submission);
            }
            else
            {
                submission.DisposeBarrier();
            }
        }
        void ReleaseSubmissionPool()
        {
            while (idleImageSubmissions.Count > 0)
            {
                idleImageSubmissions.Pop().DisposeBarrier();
            }
        }
        CommandBuffer contextValidationBuffer;
        bool externalBufferExposed;
        bool hasOwnedImages;
        bool imageSubmissionFaulted;

        void StartImageResolver()
        {
            if (NativeMethods.urui_image_resolver_version() != 2
                || NativeMethods.urui_set_image_resolver(imageResolveCallback, imageReleaseCallback) != 1)
            {
                throw new InvalidOperationException("URUI 按需图片需要原生 ImageResolver2 协议。");
            }
        }

        [AOT.MonoPInvokeCallback(typeof(NativeMethods.ImageResolveCallback))]
        static int ResolveImage(ulong documentHandle, IntPtr source, out ulong resource, out ulong sourceIdentity, out int width, out int height)
        {
            resource = sourceIdentity = 0;
            width = height = 0;
            var host = ActiveHost;
            if (!host)
            {
                return 0;
            }
            var owner = URUI.Hosting.Surface.FindDocument(documentHandle);
            var resolver = owner ? owner.ResolverFor(documentHandle) : null;
            if (resolver == null)
            {
                return 0;
            }
            ImageLease lease = null;
            owner?.BeginImageCallback();
            try
            {
                lease = resolver(Marshal.PtrToStringUTF8(source) ?? "");
                if (lease == null)
                {
                    return 0;
                }
                if (lease.HasRelease && !SystemInfo.supportsGraphicsFence && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    throw new NotSupportedException("可释放图片需要 GPU fence；此设备请返回始终由调用者持有的借用图片。");
                }
                if (lease.HasRelease && host.externalBufferExposed)
                {
                    throw new InvalidOperationException("已借出无法追踪提交的 RenderCommandBuffer，不能接纳可释放图片；请先重载并使用 SubmitRendering。");
                }
                var id = host.borrowedTextures.RegisterResolved(lease);
                if (lease.HasRelease)
                {
                    host.hasOwnedImages = true;
                }
                sourceIdentity = host.borrowedTextures.SourceIdentity(lease.Texture);
                host.resolvedImageOwners.Add(id, owner);
                resource = id;
                width = lease.Texture.width;
                height = lease.Texture.height;
                lease = null;
                return 1;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                return 0;
            }
            finally
            {
                try
                {
                    lease?.Dispose();
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
                owner?.EndImageCallback();
            }
        }

        [AOT.MonoPInvokeCallback(typeof(NativeMethods.ImageReleaseCallback))]
        static void ReleaseImage(ulong resource)
        {
            var host = ActiveHost;
            if (!host)
            {
                return;
            }
            host.resolvedImageOwners.Remove(resource, out var owner);
            owner?.BeginImageCallback();
            try
            {
                host.borrowedTextures.ReleaseResolved(resource);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                owner?.EndImageCallback();
            }
        }

        void RetireLegacyImages()
        {
            legacyImagePins = ImagePinRetirement.Retire(legacyImagePins, imageSubmissions);
        }

        internal void ReleaseCachedImage(ulong owner, string source)
        {
            if (!active)
            {
                return;
            }
            if (imageSubmissionFaulted)
            {
                throw new InvalidOperationException("不能在消费绘制记录或提交状态不确定时释放图片缓存。");
            }
            InvalidateRecording(true);
            // 先撤销未来可能再次提交的旧命令，再用原有 fence 保护已提交的纹理。
            RequireRecordingReady();
            RetireLegacyImages();
            if (NativeMethods.urui_document_release_image(owner, source) < 0)
            {
                throw new InvalidOperationException("图片缓存释放失败：" + NativeMethods.LastError());
            }
        }

        internal void RetirePaintResources(List<IDisposable> resources)
            => RetirePaintResources(resources, null);

        internal void RetirePaintResource(IDisposable resource)
            => RetirePaintResources(null, resource);

        void RetirePaintResources(List<IDisposable> resources, IDisposable resource)
        {
            if (imageSubmissionFaulted)
            {
                throw new InvalidOperationException("绘制提交尚未安全结束，保留外部绘制资源。");
            }
            InvalidateRecording(true);
            RequireRecordingReady();
            legacyImagePins = ImagePinRetirement.Retire(legacyImagePins, imageSubmissions, resources, resource);
        }

        // UI 与 fence 在同一 SRP 上下文排队；管线须在当前渲染回调结束前 Submit。
        internal bool SubmitRendering(ScriptableRenderContext context, RenderTargetIdentifier target, Rect viewport, URUI.Rendering.RenderTargetInfo targetInfo = default)
            => SubmitCommands(context, target, viewport, false, targetInfo);

#if UNITY_EDITOR
        internal bool SubmitPreview(RenderTexture target)
        {
            if (!OwnerSurface || !OwnerSurface.ManualDrive || !target || !target.IsCreated())
            {
                throw new InvalidOperationException("Only an explicitly owned editor preview can submit directly.");
            }
            return SubmitCommands(default, target, new Rect(0, 0, target.width, target.height), true, URUI.Rendering.RenderTargetInfo.FromTexture(target));
        }
#endif

        bool SubmitCommands(ScriptableRenderContext context, RenderTargetIdentifier target, Rect viewport, bool immediate, URUI.Rendering.RenderTargetInfo targetInfo)
        {
            if (!active || buffer == null || !RestoreSceneCamera())
            {
                return false;
            }
            if (!immediate)
            {
                // Unity 的上下文比较路径本身会分配。空命令缓冲只触发官方有效性校验，
                // 在获取任何提交租约前拒绝失效上下文；不读取 Unity 的私有结构布局。
                contextValidationBuffer ??= new CommandBuffer { name = "URUI context validation" };
                context.ExecuteCommandBuffer(contextValidationBuffer);
            }
            RequireRecordingReady();
            if (geometryRecordingPending || colorPassGroups != 0 || !submissionTarget.HasValue || !submissionTarget.Value.Equals(target) || submissionViewport != viewport || !submissionTargetInfo.Equals(targetInfo) || submissionImmediate != immediate)
            {
                // SRP 的中间颜色目标与 CameraTarget 可以不同；目标变化必须同步重录，不能漏掉首帧。
                submissionTarget = target;
                submissionViewport = viewport;
                submissionTargetInfo = targetInfo;
                submissionImmediate = immediate;
                InvalidateRecording(true);
                // SRP 首次绘制可能早于正常 LateUpdate，绘制器读取几何前先发布布局。
                SynchronizeLayoutState();
                var phase = default(PhaseFrame);
                recordingForContext = !immediate;
                try
                {
                    if (!DrawFrame(ref phase))
                    {
                        return false;
                    }
                }
                finally
                {
                    recordingForContext = false;
                }
            }
            ImagePinRetirement.Collect();
            CollectImageSubmissions();
            ImageSubmissionFence submission = null;
            if (planResources?.HasRecordingGeometry == true || parameterPages?.Current != null || hasOwnedImages || HasLayerResources || (OwnerSurface && (OwnerSurface.HasPainters || OwnerSurface.HasEffects)))
            {
                if (imageSubmissionFaulted)
                {
                    throw new InvalidOperationException("URUI 图片提交状态不确定，Surface 保留相关资源并拒绝继续提交。");
                }
                if (!SystemInfo.supportsGraphicsFence && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    throw new NotSupportedException("可释放图片的 SRP 提交需要 GPU fence。");
                }
                submission = ReserveImageSubmission();
            }
            try
            {
                if (submission != null)
                {
#if UNITY_EDITOR
                    submission.Immediate = immediate;
#endif
                    submission.pins.Acquire(borrowedTextures);
                    submission.PinParameters(parameterPages?.Current);
                    PinLayerWrites(submission);
                    planResources?.PinSubmission(submission.Geometry);
                    externalGeometryInputs?.Programs.PinSubmission(submission.Programs);
                    submission.barrier ??= new CommandBuffer { name = "URUI SRP submitted image lease fence" };
                    submission.barrier.Clear();
                    submission.fence = submission.barrier.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
                }
                if (immediate)
                {
                    foreach (var commands in recordedBuffers)
                    {
                        Graphics.ExecuteCommandBuffer(commands);
                    }
                    if (submission != null)
                    {
                        Graphics.ExecuteCommandBuffer(submission.barrier);
                    }
                }
                else
                {
                    foreach (var commands in recordedBuffers)
                    {
                        // 外部缓冲可以切换离屏附件。先重新发布宿主相机，避免下一个
                        // 缓冲把上个缓冲的离屏目标当作 CameraTarget；不强制 Submit。
                        context.SetupCameraProperties(targetCamera);
                        context.ExecuteCommandBuffer(commands);
                    }
                    if (submission != null)
                    {
                        context.ExecuteCommandBuffer(submission.barrier);
                    }
                }
                NotifyEffectSubmission();
                // 管线拥有当前渲染上下文。通道内强制 Submit 会截断管线尚未闭合的采样范围；
                // 排在绘制后的 fence 随管线帧末提交，继续保护同一批图片资源。
                return true;
            }
            catch
            {
                if (submission != null)
                {
                    submission.uncertain = true;
                    imageSubmissionFaulted = true;
                }
                throw;
            }
        }

        void CollectImageSubmissions()
        {
            for (var i = imageSubmissions.Count - 1; i >= 0; i--)
            {
                var item = imageSubmissions[i];
                if (item.uncertain || item.AwaitingCamera)
                {
                    continue;
                }
                try
                {
                    if (!item.fence.passed)
                    {
                        continue;
                    }
                }
                catch
                {
                    item.uncertain = true;
                    imageSubmissionFaulted = true;
                    continue;
                }
                imageSubmissions.RemoveAt(i);
                item.Release();
                if (i > imageSubmissions.Count)
                {
                    i = imageSubmissions.Count;
                }
            }
        }
    }
}
