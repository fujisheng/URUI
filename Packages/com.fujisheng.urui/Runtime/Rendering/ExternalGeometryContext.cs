using System;
using UnityEngine;
using URUI.Elements;

namespace URUI.Rendering
{
    /// <summary>
    /// 外部几何的同步准备窗口；局部坐标从作者节点左上角起，回调结束后窗口失效。
    /// </summary>
    public readonly struct ExternalGeometryContext
    {
        readonly ExternalGeometryContextPool.Scope scope;
        readonly ulong generation;
        internal ExternalGeometryContext(ExternalGeometryContextPool.Scope scope)
        {
            this.scope = scope;
            generation = scope.Generation;
        }
        void Require()
        {
            if (scope == null || !scope.Active || scope.Generation != generation)
            {
                throw new InvalidOperationException("外部几何准备窗口已结束。");
            }
        }
        public Vector2 Size
        {
            get
            {
                Require();
                return scope.Size;
            }
        }
        /// <summary>
        /// 注册时声明的连续 CSS 参数，只在当前同步回调有效。
        /// </summary>
        public ReadOnlySpan<Vector4> Parameters
        {
            get
            {
                Require();
                return scope.Values.AsSpan(0, scope.ValueCount);
            }
        }
        internal void CaptureParameters(URUI.Internal.Rendering.NativeProgramLayout layout, Element element)
        {
            if (layout != null)
            {
                layout.ReadValues(element, scope.Values);
            }
        }
        /// <summary>
        /// 声明节点局部坐标中的矩形及左上右下羽化距离；原生根据组语义决定直接遮罩或离屏绘制。
        /// </summary>
        public void DrawFeatheredRectangle(Rect rectangle, Vector4 feather)
        {
            Require();
            if (!float.IsFinite(rectangle.x) || !float.IsFinite(rectangle.y) || !float.IsFinite(rectangle.width) || !float.IsFinite(rectangle.height) || rectangle.width < 0 || rectangle.height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rectangle));
            }
            for (var i = 0; i < 4; i++)
            {
                if (!float.IsFinite(feather[i]) || feather[i] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(feather));
                }
            }
            if (rectangle.width == 0 || rectangle.height == 0)
            {
                return;
            }
            scope.Sink.RecordFeatheredRectangle(rectangle, feather, scope.Provider, scope.Command);
        }
        public bool IsClippedOut
        {
            get
            {
                Require();
                return scope.ClippedOut;
            }
        }
        public bool IsMaskImage
        {
            get
            {
                Require();
                return scope.Mask;
            }
        }
        /// <summary>
        /// 冻结当前只读网格输入；变换和裁剪交由原生计划与 Shader，回调不能修改文档。
        /// </summary>
        public void Draw(in ExternalGeometryInput input, ExternalGeometryProgram program, Matrix4x4 localTransform)
        {
            Require();
            if (program == null)
            {
                throw new ArgumentNullException(nameof(program));
            }
            if (!input.Mesh || input.GeometryVersion == 0)
            {
                throw new ArgumentException("外部几何输入无效。", nameof(input));
            }
            for (var i = 0; i < 16; i++)
            {
                if (!float.IsFinite(localTransform[i]))
                {
                    throw new ArgumentOutOfRangeException(nameof(localTransform));
                }
            }
            scope.Sink.RecordExternalGeometry(input, program, localTransform, scope.Provider, scope.Command);
        }
        internal void Close() => scope?.Close(generation);
    }
    internal interface IExternalGeometrySink
    {
        void RecordFeatheredRectangle(Rect rectangle, Vector4 feather, ulong provider, uint command);
        void RecordExternalGeometry(in ExternalGeometryInput input, ExternalGeometryProgram program, in Matrix4x4 localTransform, ulong provider, uint command);
    }
    internal sealed class ExternalGeometryContextPool
    {
        internal sealed class Scope
        {
            readonly ExternalGeometryContextPool owner;
            internal Scope Next;
            internal IExternalGeometrySink Sink;
            internal ulong Generation;
            internal ulong Provider;
            internal uint Command;
            internal Vector2 Size;
            internal Vector4[] Values = Array.Empty<Vector4>();
            internal int ValueCount;
            internal bool Active;
            internal bool ClippedOut;
            internal bool Mask;
            internal Scope(ExternalGeometryContextPool owner)
            {
                this.owner = owner;
            }
            internal void Close(ulong generation)
            {
                if (!Active || Generation != generation)
                {
                    return;
                }
                Active = false;
                Sink = null;
                Next = owner.idle;
                owner.idle = this;
            }
        }
        Scope idle;
        ulong generation;
        internal ExternalGeometryContext Open(IExternalGeometrySink sink, ulong provider, uint command, Vector2 size, bool clippedOut, bool mask, int parameterRows = 0)
        {
            if (generation == ulong.MaxValue)
            {
                throw new InvalidOperationException("外部几何窗口代际已耗尽。");
            }
            var scope = idle;
            if (scope == null)
            {
                scope = new Scope(this);
            }
            else
            {
                idle = scope.Next;
                scope.Next = null;
            }
            scope.Sink = sink;
            scope.Generation = ++generation;
            scope.Provider = provider;
            scope.Command = command;
            scope.Size = size;
            scope.ClippedOut = clippedOut;
            scope.Mask = mask;
            scope.Active = true;
            scope.ValueCount = parameterRows;
            if (scope.Values.Length < parameterRows)
            {
                scope.Values = new Vector4[parameterRows];
            }
            return new ExternalGeometryContext(scope);
        }
    }
}
