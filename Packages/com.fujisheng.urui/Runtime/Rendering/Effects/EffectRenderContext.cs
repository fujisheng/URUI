using System;
using UnityEngine;
namespace URUI.Rendering.Effects
{
    /// <summary>修饰原 img 网格、顶点颜色、切片及 UV；属性块由 Unity 在录制时复制。</summary>
    public readonly struct EffectRenderContext
    {
        readonly ElementPaintContext paint;
        readonly Mesh geometry;
        readonly Action<Action> submitted;
        internal bool UsesOriginal => paint.UsesOriginal;
        public EffectFrame Frame { get; }
        public bool IsMaskImage => paint.IsMaskImage;
        internal EffectRenderContext(ElementPaintContext paint, Mesh geometry, EffectFrame frame,Action<Action> submitted)
        { this.paint=paint; this.geometry=geometry; Frame=frame;this.submitted=submitted; }
        /// <summary>使用原 img 的标准绘制路径；处理输出仍沿用原九宫格、颜色与裁剪。</summary>
        public void DrawOriginal(){Require();paint.DrawOriginal();}
        void Require(){paint.RequireActive();}
        /// <summary>录制预先分配的离屏表面。操作属于同一 Document 提交，返回时宿主恢复 UI 目标及矩阵。</summary>
        public void RecordSurface(RenderTexture target,Mesh mesh,Material material,Matrix4x4 projection,MaterialPropertyBlock properties,int pass=0)
        {Require();paint.RecordSurface(target,mesh,material,projection,properties,pass);}
        /// <summary>只有本次记录实际进入渲染提交后执行；用于提交缓存版本，不能访问 DOM。</summary>
        public void AfterSubmission(Action callback){Require();if(callback==null)throw new ArgumentNullException(nameof(callback));submitted(callback);}
        /// <summary>材质遵守 ElementPaint.cginc；元素 opacity 由宿主对子树统一合成，效果不得再次相乘。</summary>
        /// <summary>绘制 Prepare 阶段取得的不可变网格；原节点的布局、裁剪与命中仍由原 img 拥有。</summary>
        public void DrawGeometry(Mesh mesh,Material material,Matrix4x4 localTransform,MaterialPropertyBlock properties,int pass=0)
        {Require();if(UsesOriginal)throw new InvalidOperationException("同一效果不能同时使用原图和替代几何。");paint.DrawMesh(mesh,material,localTransform,properties,0,pass);}
        public void Draw(Material material, MaterialPropertyBlock properties, int pass=0)
        {
            Require();
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            properties.SetTexture("_MainTex",Frame.Source ? Frame.Source : Texture2D.whiteTexture);
            paint.DrawMesh(geometry,material,Matrix4x4.identity,properties,0,pass);
        }
        internal void Close() { paint.Close(); }
    }
}
