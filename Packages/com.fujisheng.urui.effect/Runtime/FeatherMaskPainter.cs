using System;
using System.Globalization;
using URUI.Elements;
using URUI.Rendering;
using URUI.Styles;
using UnityEngine;

namespace URUI.Effect
{
    /// <summary>普通节点 --urui-mask-image 的羽化遮罩，参数由作者 CSS 自定义属性提供。</summary>
    public sealed class FeatherMaskPainter : IDisposable
    {
        readonly Element element;
        readonly MaterialPropertyBlock properties=new();
        Material material;
        Mesh mesh;
        IDisposable registration;
        bool disposed;
        ComputedStyleSnapshot featherStyle;
        Vector4 feather;
        static readonly StylePropertyDefinition Left=new("--urui-mask-feather-left",StyleValueType.Pixels);
        static readonly StylePropertyDefinition Top=new("--urui-mask-feather-top",StyleValueType.Pixels);
        static readonly StylePropertyDefinition Right=new("--urui-mask-feather-right",StyleValueType.Pixels);
        static readonly StylePropertyDefinition Bottom=new("--urui-mask-feather-bottom",StyleValueType.Pixels);
        /// <summary>连接作者声明的 shader(feather-mask)。</summary>
        public FeatherMaskPainter(Element element)
        {
            if(element==null){throw new ArgumentNullException(nameof(element));}
            this.element=element;
            try
            {
                var shader=UnityEngine.Resources.Load<Shader>("URUI.Effect/FeatherMask")
                    ?? throw new InvalidOperationException("缺少羽化遮罩 shader。");
                material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                mesh=new Mesh{name="URUI Feather Mask Quad"};
                mesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1),Vector3.up};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,1,2,0,2,3};
                registration=element.Document.AttachPainter(element,"feather-mask",Paint,Release);
            }
            catch{Release();throw;}
        }
        float Feather(ComputedStyleSnapshot style,StylePropertyDefinition property)
        {
            if(style.Contains(property))
            {
                var resolved=style.GetPixels(property.Name);
                if(float.IsFinite(resolved)&&resolved>=0)return resolved;
                throw new InvalidOperationException("羽化参数必须为有限非负 px 长度："+property.Name);
            }
            var value=element.ResolvedStyle.Get(property.Name);
            if(value==null){return 0;}
            value=value.Trim();if(value.EndsWith("px",StringComparison.OrdinalIgnoreCase)){value=value.Substring(0,value.Length-2);}
            if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var pixels)||!float.IsFinite(pixels)||pixels<0)
            {throw new InvalidOperationException("羽化参数必须为有限非负 px 长度："+property.Name);}
            return pixels;
        }
        void Paint(ElementPaintContext context)
        {
            var size=context.Size;
            var style=element.Document.Styles.GetComputed(element);
            if(!ReferenceEquals(style,featherStyle))
            {
                feather=new Vector4(Feather(style,Left),Feather(style,Top),Feather(style,Right),Feather(style,Bottom));
                featherStyle=style;
            }
            properties.SetVector("_Size",new Vector4(size.x,size.y,0,0));
            properties.SetVector("_Feather",feather);
            context.DrawMesh(mesh,material,Matrix4x4.Scale(new Vector3(size.x,size.y,1)),properties);
        }
        void Release()
        {
            if(mesh){URUI.Resources.GpuRetirement.DestroyOwnedObject(mesh);mesh=null;}
            if(material){URUI.Resources.GpuRetirement.DestroyOwnedObject(material);material=null;}
        }
        /// <summary>释放所持的绘制资源。</summary>
        public void Dispose()
        {
            if(disposed){return;}disposed=true;registration?.Dispose();registration=null;
        }
    }
}
