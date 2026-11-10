using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Resources
{
    internal sealed class ImageSubmissionFence
    {
        internal CommandBuffer barrier;
        internal GraphicsFence fence;
        internal bool uncertain;
        internal readonly BorrowedTextureRegistry.SubmissionPins pins=new();
        internal Action<ImageSubmissionFence> Return;
        internal void Release()
        {
            pins.Release();
            if(Return!=null)Return(this);
            else DisposeBarrier();
        }
        internal void DisposeBarrier(){barrier?.Release();barrier=null;Return=null;}
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

        internal static BorrowedTextureRegistry.SubmissionPins RentPins()
            => idlePins.Count > 0 ? idlePins.Pop() : new BorrowedTextureRegistry.SubmissionPins();

        internal static BorrowedTextureRegistry.SubmissionPins Retire(
            BorrowedTextureRegistry.SubmissionPins images, List<ImageSubmissionFence> submissions,
            IReadOnlyList<IDisposable> resources = null, IDisposable resource = null)
        {
            if ((images == null || images.Count == 0) && (submissions == null || submissions.Count == 0) &&
                (resources == null || resources.Count == 0) && resource == null) { return images; }
            var item = idle.Count > 0 ? idle.Pop() : new Pending();
            if (resource != null) { item.resources.Add(resource); }
            if (resources != null)
            {
                for (var i = 0; i < resources.Count; ++i) { item.resources.Add(resources[i]); }
            }
            if (submissions != null) { item.submissions.AddRange(submissions); }
            if (images != null && images.Count > 0) { item.images = images; images = RentPins(); }
            pending.Add(item);
            submissions?.Clear();
            // Null 设备没有 GPU 消费者；释放仍走同一个内部所有权收尾。
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                if (Release(item)) { pending.Remove(item); }
                return images;
            }
            if (!owner)
            {
                var go = new GameObject("URUI resource retirement") { hideFlags = HideFlags.HideAndDontSave };
                if (Application.isPlaying) { DontDestroyOnLoad(go); }
                owner = go.AddComponent<ImagePinRetirement>();
            }
            try
            {
                if (!SystemInfo.supportsGraphicsFence) { throw new NotSupportedException("GPU fence unavailable"); }
                foreach (var submission in item.submissions)
                {
                    if (submission.uncertain) { throw new InvalidOperationException("SRP Submit outcome uncertain"); }
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
            if (collecting) { return; }
            collecting = true;
            try { CollectPending(); }
            finally { collecting = false; }
        }
        static void CollectPending()
        {
            for (var i = pending.Count - 1; i >= 0; --i)
            {
                var item = pending[i];
                if (item.uncertain || item.releasing) { continue; }
                try
                {
                    if (!item.fence.passed) { continue; }
                    var submitted = true;
                    foreach (var submission in item.submissions) { submitted &= submission.fence.passed; }
                    if (!submitted) { continue; }
                }
                catch (Exception error)
                {
                    item.uncertain = true;
                    Debug.LogWarning("URUI GPU fence 无法读取，保留资源：" + error.Message);
                    continue;
                }
                if (!Release(item)) { continue; }
                pending.RemoveAt(i);
            }
        }

        static bool Release(Pending item)
        {
            if (item.releasing) { return false; }
            item.releasing = true;
            try { return ReleaseResources(item); }
            finally { item.releasing = false; }
        }
        static bool ReleaseResources(Pending item)
        {
            for (var i = 0; i < item.resources.Count; ++i)
            {
                var resource = item.resources[i];
                // 在 Dispose 前移除内部别名；私有租约归池后不能被旧记录再次释放。
                item.resources[i] = null;
                if (resource == null) { continue; }
                try { resource.Dispose(); }
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
                if (idlePins.Count < 32 && count <= 2048) { idlePins.Push(item.images); }
                item.images = null;
            }
            foreach (var submission in item.submissions) { submission.Release(); }
            item.submissions.Clear();
            // 只缓存已被真实 fence 确认完成的内部批次，限制长期保留容量。
            if (idle.Count < 32 && item.resources.Capacity <= 2048 && item.submissions.Capacity <= 16)
            { idle.Push(item); }
            else { item.barrier?.Release(); item.barrier = null; }
            return true;
        }

        void OnDestroy()
        {
            if (owner != this) { return; }
            owner = null;
            while (idle.Count > 0) { idle.Pop().barrier?.Release(); }
            idlePins.Clear();
        }
    }
}
