using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Resources;

namespace URUI.Resources
{
    /// <summary>在真实 GPU fence 完成后释放即时 Graphics 队列此前使用的资源。</summary>
    /// <remarks>限主线程且工作已提交；未 Submit 的外部 SRP 命令必须由其提交者管理。</remarks>
    public static class GpuRetirement
    {
        /// <summary>编辑器等没有 MonoBehaviour Update 的宿主轮询已提交的 GPU fence；不会强制等待 GPU。</summary>
        public static void Collect()=>ImagePinRetirement.Collect();
        /// <summary>释放已确认退役的自有对象；编辑器退出 Play 后仍可完成延后的释放。</summary>
        /// <remarks>不会创建 fence；GPU 对象必须先由 Retire 或提交者确认不再使用。</remarks>
        public static void DestroyOwnedObject(UnityEngine.Object value)
        {
            if (!value) { return; }
            if (Application.isPlaying) { UnityEngine.Object.Destroy(value); }
            else { UnityEngine.Object.DestroyImmediate(value); }
        }
        sealed class Release : IDisposable
        {
            Action callback;
            internal Release(Action callback) { this.callback = callback; }
            public void Dispose()
            {
                var previous = callback; callback = null;
                try { previous?.Invoke(); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
        public static void Retire(Action release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            ImagePinRetirement.Retire(null,null,null,new Release(release));
        }
    }
}
