using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Rendering;

namespace URUI.Internal.Resources
{
    internal sealed class ImageSubmissionFence
    {
        internal CommandBuffer barrier;
        internal GraphicsFence fence;
        internal bool uncertain;
        internal bool AwaitingCamera;
#if UNITY_EDITOR
        internal bool Immediate;
#endif
        internal ImageSubmissionBudget Budget;
        internal bool CameraSubmission;
        internal int CameraFrame = -1;
        internal ulong Recording;
        internal readonly BorrowedTextureRegistry.SubmissionPins pins = new();
        internal readonly List<RenderParameterPages.Page> Parameters = new();
        internal bool CanPinParameters(RenderParameterPages.Page page)
            => page == null || Parameters.Count == 0 || ReferenceEquals(Parameters[0], page);
        internal void PinParameters(RenderParameterPages.Page page)
        {
            if (!CanPinParameters(page))
            {
                throw new InvalidOperationException("一次 GPU 提交只能引用一个参数版本。");
            }
            if (page == null || Parameters.Count != 0)
            {
                return;
            }
            page.RetainSubmission();
            Parameters.Add(page);
        }
        internal readonly List<LayerContentLease> Layers = new();
        internal readonly List<LayerContentLease> ReadLayers = new();
        readonly HashSet<EffectTargetPool.Lease> targetSet = new();
        readonly List<EffectTargetPool.Lease> targets = new();
        internal void PinTarget(EffectTargetPool.Lease target)
        {
            if (!targetSet.Add(target))
            {
                return;
            }
            try
            {
                target.RetainReference();
                targets.Add(target);
            }
            catch
            {
                targetSet.Remove(target);
                throw;
            }
        }
        internal readonly List<RenderPlanResources.Entry> Geometry = new();
        internal readonly List<ExternalProgramResources.Entry> Programs = new();
        internal Action<ImageSubmissionFence> Return;
        internal void Release()
        {
            var budget = Budget;
            Budget = null;
            budget?.Release();
            pins.Release();
            for (var i = 0; i < Layers.Count; i++)
            {
                var content = Layers[i];
                Layers[i] = null;
                content.Release(true);
            }
            Layers.Clear();
            for (var i = 0; i < ReadLayers.Count; i++)
            {
                var content = ReadLayers[i];
                ReadLayers[i] = null;
                content.Release(false);
            }
            ReadLayers.Clear();
            targetSet.Clear();
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                targets[i] = null;
                target.ReleaseReference();
            }
            targets.Clear();
            for (var i = 0; i < Geometry.Count; i++)
            {
                var entry = Geometry[i];
                Geometry[i] = null;
                entry.ReleaseReference();
            }
            Geometry.Clear();
            for (var i = 0; i < Programs.Count; i++)
            {
                var entry = Programs[i];
                Programs[i] = null;
                entry.ReleaseReference();
            }
            Programs.Clear();
            foreach (var page in Parameters)
            {
                page.ReleaseSubmission();
            }
            Parameters.Clear();
            CameraSubmission = false;
            AwaitingCamera = false;
            CameraFrame = -1;
#if UNITY_EDITOR
            Immediate = false;
#endif
            Recording = 0;
            if (Return != null)
            {
                Return(this);
            }
            else
            {
                DisposeBarrier();
            }
        }
        internal void DisposeBarrier()
        {
            barrier?.Release();
            barrier = null;
            Return = null;
        }
    }
    // legacy 记录撤销后用真实 fence 归还图片；未知 GPU 状态保留租约，不能按帧数猜测安全。
    internal sealed class ImagePinRetirement : MonoBehaviour
    {
        sealed class Pending
        {
            internal readonly List<IDisposable> resources = new();
            internal readonly List<ImageSubmissionFence> submissions = new();
            internal BorrowedTextureRegistry.SubmissionPins images;
            internal CommandBuffer barrier;
            internal GraphicsFence fence;
            internal bool uncertain;
            internal bool releasing;
            internal IDisposable failedResource;
        }
        static ImagePinRetirement owner;
        static readonly List<Pending> pending = new();
        static readonly Stack<Pending> idle = new();
        static readonly Stack<BorrowedTextureRegistry.SubmissionPins> idlePins = new();
        static bool quitting;

#if !UNITY_EDITOR
        // Editor Play Mode also raises the quit notifications. Its graphics device
        // stays active and subsequent authoring previews must still submit retirement fences.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitializeShutdown()
        {
            quitting = false;
            Application.quitting -= BeginShutdown;
            Application.quitting += BeginShutdown;
        }

        void OnApplicationQuit() => BeginShutdown();
#endif

        static void ReleaseBarrierCommands(Pending item)
        {
            // ExecuteCommandBuffer已复制命令；CPU包装不拥有在途GPU资源的租约。
            // 保留fence、资源和Return契约，只防止图形设备关闭后再由终结线程释放命令。
            item.barrier?.Release();
            item.barrier = null;
            foreach (var submission in item.submissions)
            {
                submission.barrier?.Release();
                submission.barrier = null;
            }
        }

        static void ReleaseBarrierCommands()
        {
            foreach (var item in pending)
            {
                ReleaseBarrierCommands(item);
            }
            foreach (var item in idle)
            {
                ReleaseBarrierCommands(item);
            }
        }

        static void BeginShutdown()
        {
            quitting = true;
            ReleaseBarrierCommands();
        }

        internal static BorrowedTextureRegistry.SubmissionPins RentPins()
            => idlePins.Count > 0 ? idlePins.Pop() : new BorrowedTextureRegistry.SubmissionPins();

        internal static BorrowedTextureRegistry.SubmissionPins Retire(
            BorrowedTextureRegistry.SubmissionPins images, List<ImageSubmissionFence> submissions,
            IReadOnlyList<IDisposable> resources = null, IDisposable resource = null)
        {
            if ((images == null || images.Count == 0) && (submissions == null || submissions.Count == 0) &&
                (resources == null || resources.Count == 0) && resource == null)
            {
                return images;
            }
            var item = idle.Count > 0 ? idle.Pop() : new Pending();
            if (resource != null)
            {
                item.resources.Add(resource);
            }
            if (resources != null)
            {
                for (var i = 0; i < resources.Count; ++i)
                {
                    item.resources.Add(resources[i]);
                }
            }
            if (submissions != null)
            {
                item.submissions.AddRange(submissions);
            }
            if (images != null && images.Count > 0)
            {
                item.images = images;
                images = RentPins();
            }
            pending.Add(item);
            submissions?.Clear();
            if (quitting)
            {
                // OnDestroy可能在退出消息之后退役最后一帧；不再创建对象或提交GPU命令。
                item.uncertain = true;
                ReleaseBarrierCommands(item);
                return images;
            }
            // Null 设备没有 GPU 消费者；释放仍走同一个内部所有权收尾。
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                if (Release(item))
                {
                    pending.Remove(item);
                }
                return images;
            }
            if (!owner)
            {
                var go = new GameObject("URUI resource retirement") { hideFlags = HideFlags.HideAndDontSave };
                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(go);
                }
                owner = go.AddComponent<ImagePinRetirement>();
            }
            try
            {
                if (!SystemInfo.supportsGraphicsFence)
                {
                    throw new NotSupportedException("GPU fence unavailable");
                }
                foreach (var submission in item.submissions)
                {
                    if (submission.uncertain)
                    {
                        throw new InvalidOperationException("SRP Submit outcome uncertain");
                    }
                }
                item.barrier ??= new CommandBuffer { name = "URUI retired resource fence" };
                item.barrier.Clear();
                item.fence = item.barrier.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
                Graphics.ExecuteCommandBuffer(item.barrier);
            }
            catch (Exception error)
            {
                item.uncertain = true;
                Debug.LogWarning("URUI GPU 退役状态无法确认，保留资源：" + error.Message);
            }
            return images;
        }

        void Update() => Collect();
        static bool collecting;
        internal static void Collect()
        {
            if (collecting)
            {
                return;
            }
            collecting = true;
            try
            {
                CollectPending();
            }
            finally
            {
                collecting = false;
            }
        }
#if UNITY_EDITOR
        // 编辑器重载不会保留静态退休队列。独立完成证明只覆盖调用前已即时提交的批次，
        // 不能用一次读回替代未知或尚未 Submit 的 SRP 工作自身的 fence。
        internal static bool TryCompleteImmediateRetirements(Action confirmCompletion)
        {
            if (confirmCompletion == null)
            {
                throw new ArgumentNullException(nameof(confirmCompletion));
            }
            if (collecting)
            {
                return false;
            }
            foreach (var item in pending)
            {
                if (item.uncertain || item.releasing)
                {
                    return false;
                }
                foreach (var submission in item.submissions)
                {
                    if (submission.uncertain || submission.AwaitingCamera || (!submission.Immediate && !submission.CameraSubmission))
                    {
                        return false;
                    }
                }
            }
            if (pending.Count == 0)
            {
                return true;
            }
            var earlier = new List<Pending>(pending);
            collecting = true;
            foreach (var item in earlier)
            {
                item.releasing = true;
            }
            try
            {
                confirmCompletion();
                var completed = true;
                foreach (var item in earlier)
                {
                    item.releasing = false;
                    if (Release(item))
                    {
                        pending.Remove(item);
                    }
                    else
                    {
                        completed = false;
                    }
                }
                return completed;
            }
            finally
            {
                foreach (var item in earlier)
                {
                    item.releasing = false;
                }
                collecting = false;
            }
        }
#endif
        static void CollectPending()
        {
            for (var i = pending.Count - 1; i >= 0; --i)
            {
                var item = pending[i];
                if (item.uncertain || item.releasing)
                {
                    continue;
                }
                try
                {
                    if (!item.fence.passed)
                    {
                        continue;
                    }
                    var submitted = true;
                    foreach (var submission in item.submissions)
                    {
                        submitted &= submission.fence.passed;
                    }
                    if (!submitted)
                    {
                        continue;
                    }
                }
                catch (Exception error)
                {
                    item.uncertain = true;
                    Debug.LogWarning("URUI GPU fence 无法读取，保留资源：" + error.Message);
                    continue;
                }
                if (!Release(item))
                {
                    continue;
                }
                pending.RemoveAt(i);
            }
        }

        static bool Release(Pending item)
        {
            if (item.releasing)
            {
                return false;
            }
            item.releasing = true;
            try
            {
                return ReleaseResources(item);
            }
            finally
            {
                item.releasing = false;
            }
        }
        static bool ReleaseResources(Pending item)
        {
            for (var i = 0; i < item.resources.Count; ++i)
            {
                var resource = item.resources[i];
                // 在 Dispose 前移除内部别名；私有租约归池后不能被旧记录再次释放。
                item.resources[i] = null;
                if (resource == null)
                {
                    continue;
                }
                try
                {
                    resource.Dispose();
                }
                catch (Exception error)
                {
                    item.failedResource = resource;
                    item.uncertain = true;
                    Debug.LogException(error);
                    return false;
                }
            }
            item.resources.Clear();
            if (item.images != null)
            {
                var count = item.images.Count;
                item.images.Release();
                if (idlePins.Count < 32 && count <= 2048)
                {
                    idlePins.Push(item.images);
                }
                item.images = null;
            }
            foreach (var submission in item.submissions)
            {
                submission.Release();
            }
            item.submissions.Clear();
            // 只缓存已被真实 fence 确认完成的内部批次，限制长期保留容量。
            if (idle.Count < 32 && item.resources.Capacity <= 2048 && item.submissions.Capacity <= 16)
            {
                idle.Push(item);
            }
            else
            {
                item.barrier?.Release();
                item.barrier = null;
            }
            return true;
        }

        void OnDestroy()
        {
            if (owner != this)
            {
                return;
            }
            ReleaseBarrierCommands();
            owner = null;
            while (idle.Count > 0)
            {
                idle.Pop().barrier?.Release();
            }
            idlePins.Clear();
        }
    }
}
