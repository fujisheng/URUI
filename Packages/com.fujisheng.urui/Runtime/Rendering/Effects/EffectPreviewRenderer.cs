using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Resources;
namespace URUI.Rendering.Effects
{
    /// <summary>无 DOM 的矩形图像预览；复用生产效果实例、网格和 Record 路径，不创建第二个 Surface。</summary>
    public sealed class EffectPreviewRenderer:IDisposable
    {
        readonly EffectContext context;
        readonly IElementEffect instance;
        readonly CommandBuffer commands=new(){name="URUI Effect Preview"};
        readonly PaintContextPool paintContexts = new();
        Mesh mesh;
        EffectGeometry geometry;
        Vector2 size;
        ulong geometryIdentity;
        bool disposed;
        static void DestroyObject(UnityEngine.Object value)
        {if(!value)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        public EffectPreviewRenderer(EffectDefinition definition)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            context=new EffectContext(definition,null,0,value=>GpuRetirement.Retire(value.Dispose));
            try{instance=definition.Factory.Create(context)??throw new InvalidOperationException("预览工厂返回 null。");}
            catch{context.Dispose();commands.Dispose();throw;}
        }
        /// <summary>source 是普通 Unity 直 Alpha 纹理。content 为目标中的像素矩形，时间由调用方明确给定。</summary>
        public void Render(Texture source,Rect content,double time,RenderTexture destination,Color background)
        {
            if(disposed)throw new ObjectDisposedException(nameof(EffectPreviewRenderer));
            if(!source||!destination||!double.IsFinite(time)||!float.IsFinite(content.x)||!float.IsFinite(content.y)||!float.IsFinite(content.width)||!float.IsFinite(content.height)||content.width<=0||content.height<=0)throw new ArgumentException("预览输入、尺寸、时间及输出必须有效。");
            if(!mesh||size!=content.size)
            {
                if(mesh){var old=mesh;GpuRetirement.Retire(()=>DestroyObject(old));}
                size=content.size;var vertices=new[]{Vector3.zero,new Vector3(size.x,0),new Vector3(size.x,size.y),new Vector3(0,size.y)};
                var uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};var colors=new Color32[]{Color.white,Color.white,Color.white,Color.white};var indices=new[]{0,1,2,0,2,3};
                geometry=new EffectGeometry(++geometryIdentity,vertices,uv,colors,indices);mesh=new Mesh{name="URUI Effect Preview Quad",vertices=vertices,uv=uv,colors32=colors,triangles=indices};
            }
            var transform=Matrix4x4.Translate(content.position);
            var layout=new EffectLayout(null,new Rect(Vector2.zero,size),transform,0,new Rect(Vector2.zero,size),transform);
            var frame=new EffectFrame(0,layout,source,true,time,geometry:geometry);
            instance.Prepare(frame);
            commands.Clear();commands.SetRenderTarget(destination);commands.SetViewport(new Rect(0,0,destination.width,destination.height));
            commands.ClearRenderTarget(false,true,QualitySettings.activeColorSpace==ColorSpace.Linear?background.linear:background);
            commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(0,destination.width,destination.height,0,-1,1));
            var paint=paintContexts.Open(null,commands,transform,default,false);
            var render=new EffectRenderContext(paint,mesh,frame,_=>throw new NotSupportedException("预览效果不能订阅文档提交。"));
            context.Recording=true;
            try{instance.Record(render);Graphics.ExecuteCommandBuffer(commands);}
            finally{context.Recording=false;render.Close();}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;commands.Dispose();
            var oldMesh=mesh;var oldContext=context;var oldInstance=instance;
            GpuRetirement.Retire(()=>{try{oldInstance.Dispose();}finally{oldContext.Dispose();DestroyObject(oldMesh);}});
        }
    }
}
