using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Elements;
using URUI.Internal.Rendering;

namespace URUI.Rendering
{
    // 只复用内部存储，公开窗口携带自己的代际。旧窗口不能因下一次回调而复活。
    internal sealed class PaintContextPool
    {
        internal sealed class Scope
        {
            readonly PaintContextPool owner;
            internal Scope Next;
            internal ulong Generation;
            internal bool Active, UsesOriginal, Clipped, WritesParent;
            internal CommandBuffer DirectBuffer;
            internal ExternalCommandRecording Recording;
            internal CommandBuffer Buffer => Recording != null ? Recording.Buffer : DirectBuffer;
            internal Matrix4x4 Transform;
            internal Vector4 Clip;
            MaterialPropertyBlock defaults;

            internal Scope(PaintContextPool owner) { this.owner = owner; }
            internal MaterialPropertyBlock DefaultProperties()
            {
                defaults ??= new MaterialPropertyBlock();
                defaults.Clear();
                return defaults;
            }
            internal void Close(ulong generation)
            {
                if (!Active || Generation != generation) { return; }
                Active = false;
                DirectBuffer = null;
                Recording = null;
                Next = owner.idle;
                owner.idle = this;
            }
        }

        Scope idle;
        ulong generation;
        internal ElementPaintContext Open(Element element, CommandBuffer buffer, Matrix4x4 transform, Vector4 clip, bool clipped, bool isMaskImage = false, ExternalCommandRecording recording = null)
        {
            if (generation == ulong.MaxValue) { throw new InvalidOperationException("绘制窗口代际已耗尽。"); }
            var scope = idle;
            if (scope == null) { scope = new Scope(this); }
            else { idle = scope.Next; scope.Next = null; }
            scope.Generation = ++generation;
            scope.Active = true;
            scope.UsesOriginal = false;
            scope.WritesParent = false;
            scope.DirectBuffer = buffer;
            scope.Recording = recording;
            scope.Transform = transform;
            scope.Clip = clip;
            scope.Clipped = clipped;
            return new ElementPaintContext(scope, element, isMaskImage);
        }
    }
}
