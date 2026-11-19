using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Elements;

namespace URUI.Rendering
{
    /// <summary>
    /// 作者 shader 装饰器所在位置的有序绘制范围，坐标原点为该节点左上角。
    /// </summary>
    /// <remarks>
    /// 只在回调内有效。材质须使用 ElementPaint.cginc 处理裁剪；节点及祖先透明度由合成组统一处理。资源由注册者持有到退役回调。
    /// </remarks>
    public readonly struct ElementPaintContext
    {
        readonly PaintContextPool.Scope scope;
        readonly ulong generation;
        public Element Element
        {
            get;
        }
        /// <summary>
        /// 当前绘制用于 mask-image 的 alpha；遮罩不重复应用节点自身透明度。
        /// </summary>
        public bool IsMaskImage
        {
            get;
        }
        /// <summary>
        /// 祖先裁剪交集为空；不会按节点盒猜测扩展粒子的可见范围。
        /// </summary>
        public bool IsClippedOut
        {
            get;
        }
        public Vector2 Size => Element.Layout.size;
        internal ElementPaintContext(PaintContextPool.Scope scope, Element element, bool isMaskImage)
        {
            this.scope = scope;
            generation = scope.Generation;
            Element = element;
            IsMaskImage = isMaskImage;
            var clip = scope.Clip;
            IsClippedOut = scope.Clipped && (clip.z <= clip.x || clip.w <= clip.y);
        }
        internal void RequireActive()
        {
            if (scope == null || !scope.Active || scope.Generation != generation)
            {
                throw new InvalidOperationException("绘制范围已结束。");
            }
        }
        internal bool UsesOriginal
        {
            get
            {
                RequireActive();
                return scope.UsesOriginal;
            }
        }
        internal void DrawOriginal()
        {
            RequireActive();
            if (scope.WritesParent)
            {
                throw new InvalidOperationException("同一效果不能同时使用原图和替代几何。");
            }
            scope.UsesOriginal = true;
        }
        /// <summary>
        /// 在本节点局部坐标绘制；属性块在录制时由 Unity 复制，材质和网格仍须保持有效。
        /// </summary>
        public void DrawMesh(Mesh mesh, Material material, Matrix4x4 localTransform, MaterialPropertyBlock properties = null, int submesh = 0, int pass = 0)
        {
            RequireActive();
            if (scope.UsesOriginal)
            {
                throw new InvalidOperationException("同一效果不能同时使用原图和替代几何。");
            }
            if (!mesh || !material)
            {
                throw new ArgumentNullException(!mesh ? nameof(mesh) : nameof(material));
            }
            if (!material.HasProperty("_UruiPaintOpacity") || !material.HasProperty("_UruiClipEnabled") || !material.HasProperty("_UruiClipRect"))
            {
                throw new ArgumentException("外部绘制材质必须声明 URUI 的透明度和裁剪属性：" + material.shader.name, nameof(material));
            }
            if (submesh < 0 || submesh >= mesh.subMeshCount || pass < 0 || pass >= material.passCount)
            {
                throw new ArgumentOutOfRangeException(nameof(submesh));
            }
            for (var index = 0; index < 16; index++)
            {
                if (!float.IsFinite(localTransform[index]))
                {
                    throw new ArgumentOutOfRangeException(nameof(localTransform));
                }
            }
            Element?.Document.BeginPainterDraw();
            scope.WritesParent = true;
            scope.Recording?.BeginDraw();
            properties ??= scope.DefaultProperties();
            // 原生合成组已负责 CSS opacity；自定义绘制不能再次乘节点透明度。
            properties.SetFloat("_UruiPaintOpacity", 1);
            properties.SetFloat("_UruiClipEnabled", scope.Clipped ? 1 : 0);
            properties.SetVector("_UruiClipRect", scope.Clip);
            scope.Buffer.DrawMesh(mesh, scope.Transform * localTransform, material, submesh, pass, properties);
        }
        internal void RecordSurface(RenderTexture target, Mesh mesh, Material material, Matrix4x4 projection, MaterialPropertyBlock properties, int pass)
        {
            RequireActive();
            if (!target || !mesh || !material || properties == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (pass < 0 || pass >= material.passCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pass));
            }
            Element?.Document.BeginPainterDraw();
            scope.Recording?.BeginSurface();
            scope.Buffer.SetRenderTarget(target);
            scope.Buffer.SetViewport(new Rect(0, 0, target.width, target.height));
            scope.Buffer.ClearRenderTarget(false, true, Color.clear);
            scope.Buffer.SetViewProjectionMatrices(Matrix4x4.identity, projection);
            scope.Buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass, properties);
            scope.Recording?.EndSurface();
        }
        internal void Close()
        {
            scope?.Close(generation);
        }
    }
}
