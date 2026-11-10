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
        readonly Stack<ImageSubmissionFence> idleImageSubmissions=new();
        Action<ImageSubmissionFence> returnImageSubmission;
        void ReturnImageSubmission(ImageSubmissionFence submission)
        {
            if(this&&!resourcesReleased&&idleImageSubmissions.Count<8)idleImageSubmissions.Push(submission);
            else submission.DisposeBarrier();
        }
        void ReleaseSubmissionPool()
        {
            while(idleImageSubmissions.Count>0)idleImageSubmissions.Pop().DisposeBarrier();
        }
        bool externalBufferExposed;
        bool hasOwnedImages;
        bool imageSubmissionFaulted;

        void StartImageResolver()
        {
            if (NativeMethods.urui_image_resolver_version() != 2
                || NativeMethods.urui_set_image_resolver(imageResolveCallback, imageReleaseCallback) != 1)
                throw new InvalidOperationException("URUI 按需图片需要原生 ImageResolver2 协议。");
        }

        [AOT.MonoPInvokeCallback(typeof(NativeMethods.ImageResolveCallback))]
        static int ResolveImage(ulong documentHandle, IntPtr source, out ulong resource, out ulong sourceIdentity, out int width, out int height)
        {
            resource = sourceIdentity = 0; width = height = 0;
            var host = ActiveHost;
            if (!host) return 0;
            var owner = URUI.Hosting.Surface.FindDocument(documentHandle);
            var resolver = owner ? owner.ResolverFor(documentHandle) : null;
            if (resolver == null) return 0;
            ImageLease lease = null;
            owner?.BeginImageCallback();
            try
            {
                lease = resolver(Marshal.PtrToStringUTF8(source) ?? "");
                if (lease == null) return 0;
                if (lease.HasRelease && !SystemInfo.supportsGraphicsFence && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                    throw new NotSupportedException("可释放图片需要 GPU fence；此设备请返回始终由调用者持有的借用图片。");
                if (lease.HasRelease && host.externalBufferExposed)
                    throw new InvalidOperationException("已借出无法追踪提交的 RenderCommandBuffer，不能接纳可释放图片；请先重载并使用 SubmitRendering。");
                var id = host.borrowedTextures.RegisterResolved(lease);
                if (lease.HasRelease) host.hasOwnedImages = true;
                sourceIdentity = host.borrowedTextures.SourceIdentity(lease.Texture);
                host.resolvedImageOwners.Add(id, owner);
                resource = id;
                width = lease.Texture.width; height = lease.Texture.height;
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
                try { lease?.Dispose(); }
                catch (Exception error) { Debug.LogException(error); }
                owner?.EndImageCallback();
            }
        }

        [AOT.MonoPInvokeCallback(typeof(NativeMethods.ImageReleaseCallback))]
        static void ReleaseImage(ulong resource)
        {
            var host = ActiveHost;
            if (!host) return;
            host.resolvedImageOwners.Remove(resource, out var owner);
            owner?.BeginImageCallback();
            try { host.borrowedTextures.ReleaseResolved(resource); }
            catch (Exception error) { Debug.LogException(error); }
            finally { owner?.EndImageCallback(); }
        }

        void RetireLegacyImages()
        {
            legacyImagePins=ImagePinRetirement.Retire(legacyImagePins,imageSubmissions);
        }

        internal void ReleaseCachedImage(ulong owner, string source)
        {
            if (!active) return;
            if (imageSubmissionFaulted)
                throw new InvalidOperationException("不能在消费绘制记录或提交状态不确定时释放图片缓存。");
            InvalidateRecording(true);
            // 先撤销未来可能再次提交的旧命令，再用原有 fence 保护已提交的纹理。
            RequireRecordingReady();
            RetireLegacyImages();
            if (NativeMethods.urui_document_release_image(owner, source) < 0)
                throw new InvalidOperationException("图片缓存释放失败：" + NativeMethods.LastError());
        }

        internal void RetirePaintResources(List<IDisposable> resources)
            =>RetirePaintResources(resources,null);

        internal void RetirePaintResource(IDisposable resource)
            =>RetirePaintResources(null,resource);

        void RetirePaintResources(List<IDisposable> resources,IDisposable resource)
        {
            if (imageSubmissionFaulted)
                throw new InvalidOperationException("绘制提交尚未安全结束，保留外部绘制资源。");
            InvalidateRecording(true);
            RequireRecordingReady();
            legacyImagePins=ImagePinRetirement.Retire(legacyImagePins,imageSubmissions,resources,resource);
        }

        // UI 与 fence 在同一 SRP 上下文排队；管线须在当前渲染回调结束前 Submit。
        internal bool SubmitRendering(ScriptableRenderContext context, RenderTargetIdentifier target, Rect viewport)
        {
            if (!active || buffer == null || !RestoreSceneCamera()) return false;
            if (context.Equals(default(ScriptableRenderContext)))
                throw new InvalidOperationException("URUI 绘制提交需要有效的 SRP 上下文。");
            RequireRecordingReady();
            if(!submissionTarget.HasValue||!submissionTarget.Value.Equals(target)||submissionViewport!=viewport)
            {
                // SRP 的中间颜色目标与 CameraTarget 可以不同；目标变化必须同步重录，不能漏掉首帧。
                submissionTarget=target;submissionViewport=viewport;
                InvalidateRecording(true);
                // SRP 首次绘制可能早于正常 LateUpdate，绘制器读取几何前先发布布局。
                SynchronizeLayoutState();
                var phase=default(PhaseFrame);
                if (!DrawFrame(ref phase)) return false;
            }
            CollectImageSubmissions();
            ImageSubmissionFence submission = null;
            if (hasOwnedImages || HasLayerResources || (OwnerSurface && (OwnerSurface.HasPainters || OwnerSurface.HasEffects)))
            {
                if (imageSubmissionFaulted)
                    throw new InvalidOperationException("URUI 图片提交状态不确定，Surface 保留相关资源并拒绝继续提交。");
                if (!SystemInfo.supportsGraphicsFence && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                    throw new NotSupportedException("可释放图片的 SRP 提交需要 GPU fence。");
                if (imageSubmissions.Count >= 8)
                    throw new InvalidOperationException("URUI 图片 GPU 提交积压，等待已提交 fence 完成后再试。");
                submission = idleImageSubmissions.Count>0?idleImageSubmissions.Pop():
                    new ImageSubmissionFence { Return=returnImageSubmission??=ReturnImageSubmission };
                imageSubmissions.Add(submission);
            }
            try
            {
                if (submission != null)
                {
                    submission.pins.Acquire(borrowedTextures);
                    submission.barrier ??= new CommandBuffer { name = "URUI SRP submitted image lease fence" };
                    submission.barrier.Clear();
                    submission.fence = submission.barrier.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
                }
                context.ExecuteCommandBuffer(buffer);
                if (submission != null) context.ExecuteCommandBuffer(submission.barrier);
                NotifyEffectSubmission();
                // 管线拥有当前渲染上下文。通道内强制 Submit 会截断管线尚未闭合的采样范围；
                // 排在绘制后的 fence 随管线帧末提交，继续保护同一批图片资源。
                return true;
            }
            catch
            {
                if (submission != null) { submission.uncertain = true; imageSubmissionFaulted = true; }
                throw;
            }
        }

        void CollectImageSubmissions()
        {
            for (var i = imageSubmissions.Count - 1; i >= 0; i--)
            {
                var item = imageSubmissions[i];
                if (item.uncertain) continue;
                try { if (!item.fence.passed) continue; }
                catch { item.uncertain = true; imageSubmissionFaulted = true; continue; }
                imageSubmissions.RemoveAt(i);
                item.Release();
                if(i>imageSubmissions.Count)i=imageSubmissions.Count;
            }
        }
    }
}
