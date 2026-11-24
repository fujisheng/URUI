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
        List<(LayerContentLease Content, ulong Generation, bool CanPublish)> layerWrites;
        internal void PinLayerWrite(LayerContentLease content, ulong recording)
        {
            if (content.PinWrite(recording, out var generation, out var canPublish))
            {
                (layerWrites ??= new()).Add((content, generation, canPublish));
            }
        }
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
        internal readonly HashSet<RenderPlanResources.Entry> GeometrySet = new();
        internal readonly List<ExternalProgramResources.Entry> Programs = new();
        internal readonly HashSet<ExternalProgramResources.Entry> ProgramSet = new();
        internal Action<ImageSubmissionFence> Return;

        internal void Release(bool completed = false)
        {
            var budget = Budget;
            Budget = null;
            budget?.Release();
            pins.Release();
            if (layerWrites != null)
            {
                foreach (var write in layerWrites)
                {
                    write.Content.FinishWrite(write.Generation, completed && write.CanPublish);
                }
                layerWrites.Clear();
            }
            for (var i = 0; i < Layers.Count; i++)
            {
                var content = Layers[i];
                Layers[i] = null;
                content.Release(completed);
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
            GeometrySet.Clear();
            for (var i = 0; i < Geometry.Count; i++)
            {
                var entry = Geometry[i];
                Geometry[i] = null;
                entry.ReleaseReference();
            }
            Geometry.Clear();
            ProgramSet.Clear();
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

    // 记录撤销后用真实 fence 归还资源；Play Mode 零散对象销毁合并到帧尾单次 fence，避免 N 次同步 GPU 刷队列。
    internal sealed class ImagePinRetirement : MonoBehaviour
    {
        sealed class Pending
        {
            internal readonly List<IDisposable> resources = new();
            internal readonly List<ImageSubmissionFence> submissions = new();
            internal BorrowedTextureRegistry.SubmissionPins images;
            internal GraphicsFence fence;
            internal bool uncertain;
            internal bool releasing;
            internal IDisposable failedResource;
        }

        static ImagePinRetirement owner;
        static readonly List<Pending> pending = new();
        static readonly Stack<Pending> idle = new();
        static readonly Stack<BorrowedTextureRegistry.SubmissionPins> idlePins = new();
        static readonly List<IDisposable> deferredBatch = new();
        static readonly List<IDisposable> deferredFlushScratch = new();
        static bool quitting;
        static bool collecting;
        static bool destroyingOwner;
#if UNITY_EDITOR
        static bool exitingPlayMode;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitializeStatics()
        {
            quitting = false;
            collecting = false;
            destroyingOwner = false;
#if UNITY_EDITOR
            exitingPlayMode = false;
#endif
            if (owner)
            {
                var previousOwner = owner;
                owner = null;
                DestroyImmediate(previousOwner.gameObject);
            }
#if !UNITY_EDITOR
            Application.quitting -= BeginShutdown;
            Application.quitting += BeginShutdown;
#endif
        }

#if UNITY_EDITOR
        internal static void OnExitingPlayMode()
        {
            exitingPlayMode = true;
            FlushDeferred();
        }

        internal static void OnEnteredEditMode()
        {
            exitingPlayMode = false;
            Collect();
            if (owner && pending.Count == 0 && deferredBatch.Count == 0)
            {
                var previousOwner = owner;
                owner = null;
                DestroyImmediate(previousOwner.gameObject);
            }
        }
#endif

#if !UNITY_EDITOR
        void OnApplicationQuit() => BeginShutdown();
#endif

        static void ReleaseBarrierCommands(Pending item)
        {
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
            FlushDeferred();
            ReleaseBarrierCommands();
        }

        static void EnsureOwner()
        {
            if (owner || quitting || destroyingOwner)
            {
                return;
            }
#if UNITY_EDITOR
            if (exitingPlayMode)
            {
                return;
            }
#endif
            var go = new GameObject("URUI resource retirement") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(go);
            }
            owner = go.AddComponent<ImagePinRetirement>();
        }

        internal static BorrowedTextureRegistry.SubmissionPins RentPins()
            => idlePins.Count > 0 ? idlePins.Pop() : new BorrowedTextureRegistry.SubmissionPins();

        // Queues Play Mode disposals into one end-of-frame GPU fence instead of N ExecuteCommandBuffer barriers.
        internal static void RetireDeferred(IDisposable resource)
        {
            if (resource == null)
            {
                return;
            }
            if (!Application.isPlaying || quitting || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Retire(null, null, null, resource);
                return;
            }
            EnsureOwner();
            deferredBatch.Add(resource);
        }

        internal static void FlushDeferred()
        {
            if (deferredBatch.Count == 0)
            {
                return;
            }
            deferredFlushScratch.Clear();
            deferredFlushScratch.AddRange(deferredBatch);
            deferredBatch.Clear();
            try
            {
                Retire(null, null, deferredFlushScratch, null);
            }
            finally
            {
                deferredFlushScratch.Clear();
            }
        }

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
                item.uncertain = true;
                ReleaseBarrierCommands(item);
                return images;
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                if (Release(item))
                {
                    pending.Remove(item);
                }
                return images;
            }
            EnsureOwner();
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
                // Direct immediate fence avoids CommandBuffer allocation and Unity 6 Metal CommandBuffer reuse fence stalls.
                item.fence = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
            }
            catch (Exception error)
            {
                item.uncertain = true;
                Debug.LogWarning("URUI GPU 退役状态无法确认，保留资源：" + error.Message);
            }
            return images;
        }

        void Update() => Collect();
        void LateUpdate() => FlushDeferred();

        internal static void Collect()
        {
            if (collecting)
            {
                return;
            }
            FlushDeferred();
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
            FlushDeferred();
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
                        submitted &= !submission.AwaitingCamera && submission.fence.passed;
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
            IDisposable firstFailed = null;
            for (var i = 0; i < item.resources.Count; ++i)
            {
                var resource = item.resources[i];
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
                    firstFailed ??= resource;
                    Debug.LogException(error);
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
                submission.Release(completed: true);
            }
            item.submissions.Clear();
            if (firstFailed != null)
            {
                item.failedResource = firstFailed;
                item.uncertain = true;
                return false;
            }
            if (idle.Count < 32 && item.resources.Capacity <= 2048 && item.submissions.Capacity <= 16)
            {
                idle.Push(item);
            }
            return true;
        }

        void OnDestroy()
        {
            if (owner != this)
            {
                return;
            }
            owner = null;
            destroyingOwner = true;
            try
            {
                FlushDeferred();
                ReleaseBarrierCommands();
                idle.Clear();
                idlePins.Clear();
            }
            finally
            {
                destroyingOwner = false;
            }
        }
    }
}
