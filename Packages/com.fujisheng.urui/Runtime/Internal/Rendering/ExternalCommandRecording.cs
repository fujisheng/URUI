using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering
{
    // Unity 在录制 DrawMesh 时复制属性块；分段保留任意扩展绘制的顺序及目标恢复点。
    internal sealed class ExternalCommandRecording : IDisposable
    {
        internal readonly struct Segment
        {
            internal readonly CommandBuffer Buffer;
            internal readonly bool RestoreParent;
            internal Segment(CommandBuffer buffer, bool restoreParent)
            {
                Buffer = buffer;
                RestoreParent = restoreParent;
            }
        }

        readonly List<CommandBuffer> buffers = new List<CommandBuffer>();
        readonly List<Segment> segments = new List<Segment>();
        int count;
        bool surface;
        bool needsParent;
        internal bool WritesParent
        {
            get;
            private set;
        }
        internal bool WritesSurface
        {
            get;
            private set;
        }
        RenderTargetIdentifier target;
        Rect viewport;
        Matrix4x4 projection;
        internal bool HasCommands => count != 0;
        internal int Count => count;
        internal Segment this[int index] => index >= 0 && index < count ? segments[index] : throw new ArgumentOutOfRangeException(nameof(index));
        internal CommandBuffer Buffer => count > 0 ? segments[count - 1].Buffer : throw new InvalidOperationException("尚未开始录制绘制命令。");

        internal void Begin(RenderTargetIdentifier target, Rect viewport, Matrix4x4 projection)
        {
            for (var i = 0; i < count; i++)
            {
                buffers[i].Clear();
            }
            count = 0;
            surface = false;
            needsParent = false;
            WritesParent = WritesSurface = false;
            this.target = target;
            this.viewport = viewport;
            this.projection = projection;
        }

        void Start(bool restoreParent)
        {
            if (count == buffers.Count)
            {
                buffers.Add(new CommandBuffer { name = "URUI external commands" });
                segments.Add(default);
            }
            segments[count] = new Segment(buffers[count], restoreParent);
            count++;
            if (restoreParent)
            {
                Buffer.SetRenderTarget(target);
                Buffer.SetViewport(viewport);
                Buffer.SetViewProjectionMatrices(Matrix4x4.identity, projection);
            }
        }

        internal void BeginDraw()
        {
            WritesParent = true;
            if (count == 0 || surface || needsParent)
            {
                Start(count == 0 || needsParent);
            }
            surface = false;
            needsParent = false;
        }

        internal void BeginSurface()
        {
            WritesSurface = true;
            Start(false);
            surface = true;
        }

        internal void EndSurface()
        {
            needsParent = true;
        }

        public void Dispose()
        {
            for (var i = 0; i < buffers.Count; i++)
            {
                buffers[i].Release();
            }
            buffers.Clear();
            segments.Clear();
            count = 0;
        }
    }
}
