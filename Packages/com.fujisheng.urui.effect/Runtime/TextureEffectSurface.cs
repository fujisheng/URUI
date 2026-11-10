using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Resources;
using URUI.Rendering.Effects;

namespace URUI.Effect
{
    /// <summary>用作者材质处理 GPU 纹理，以租约发布结果，不创建 Canvas 或读取像素。</summary>
    /// <remarks>保留增亮后的颜色范围，让图片透明度及父级透明度在最终合成时应用，避免提前截断透明扫光。</remarks>
    public sealed class TextureEffectSurface : IDisposable
    {
        /// <summary>GPU 表面及处理工作量的只读计数；颜色字节不包含驱动对齐或 Unity 对象开销。</summary>
        public readonly struct Statistics
        {
            public int LiveSurfaces { get; }
            public long LiveColorBytes { get; }
            public long ProcessingPasses { get; }
            public long ClearPasses { get; }
            public long MeshRebuilds { get; }
            internal Statistics(int surfaces, long bytes, long processing, long clears, long meshes)
            { LiveSurfaces=surfaces; LiveColorBytes=bytes; ProcessingPasses=processing; ClearPasses=clears; MeshRebuilds=meshes; }
        }
        static int liveSurfaces;
        static long liveColorBytes,processingPasses,clearPasses,meshRebuilds;
        public static Statistics GetStatistics()=>new Statistics(liveSurfaces,liveColorBytes,processingPasses,clearPasses,meshRebuilds);
        readonly RenderTexture output;
        readonly ImageLeaseSource source;
        readonly CommandBuffer commands;
        readonly Mesh quad;
        Vector2 layoutSize;
        bool disposed;
        public ImageLeaseSource Output => source;
        public RenderTexture Texture => output;
        public void Clear()
        {
            if (disposed) { return; }
            commands.Clear(); commands.SetRenderTarget(output); commands.ClearRenderTarget(false, true, Color.clear);
            Graphics.ExecuteCommandBuffer(commands);
            clearPasses++;
        }
        public TextureEffectSurface(int width, int height, IDisposable inputLease = null)
        {
            if(!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {throw new NotSupportedException("纹理滤镜需要支持半精度颜色的渲染纹理。");}
            output = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
            { name = "URUI Effect Surface", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!output.Create()) { URUI.Resources.GpuRetirement.DestroyOwnedObject(output); throw new InvalidOperationException("无法创建效果输出表面。"); }
            var previous = RenderTexture.active;
            try { RenderTexture.active = output; GL.Clear(true, true, Color.clear); }
            finally { RenderTexture.active = previous; }
            quad = new Mesh { name = "URUI Effect Quad" };
            commands = new CommandBuffer { name = "URUI Effect Processing" };
            var colorBytes=checked((long)output.width*output.height*8);
            source = new ImageLeaseSource(output, () => GpuRetirement.Retire(() =>
            {
                try
                {
                    if (output) { output.Release(); URUI.Resources.GpuRetirement.DestroyOwnedObject(output); }
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(quad);
                    liveSurfaces--;liveColorBytes-=colorBytes;
                }
                finally { inputLease?.Dispose(); }
            }));
            liveSurfaces++;liveColorBytes+=colorBytes;
        }
        /// <summary>在效果 Prepare 阶段更新网格；录制时不分配或修改网格。</summary>
        public void Prepare(Vector2 size)
        {
            if(disposed)throw new ObjectDisposedException(nameof(TextureEffectSurface));
            if(!float.IsFinite(size.x)||!float.IsFinite(size.y)||size.x<=0||size.y<=0)throw new ArgumentOutOfRangeException(nameof(size));
            if (layoutSize != size)
            {
                var half = size * .5f;
                quad.vertices = new[] { new Vector3(-half.x,-half.y), new Vector3(half.x,-half.y), new Vector3(half.x,half.y), new Vector3(-half.x,half.y) };
                quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                quad.colors32 = new[] { new Color32(255,255,255,255), new Color32(255,255,255,255), new Color32(255,255,255,255), new Color32(255,255,255,255) };
                quad.triangles = new[] { 0,1,2,0,2,3 };
                quad.RecalculateBounds();
                layoutSize = size;
                meshRebuilds++;
            }
        }
        /// <summary>只录制准备好的资源；处理计数与缓存版本在实际提交后更新。</summary>
        public void Record(EffectRenderContext context,Texture input,Material material,MaterialPropertyBlock properties)
        {
            if(disposed)throw new ObjectDisposedException(nameof(TextureEffectSurface));
            if(layoutSize.x<=0||layoutSize.y<=0)throw new InvalidOperationException("先 Prepare 再 Record。");
            properties.SetTexture("_MainTex",input);
            context.RecordSurface(output,quad,material,Matrix4x4.Ortho(-layoutSize.x*.5f,layoutSize.x*.5f,-layoutSize.y*.5f,layoutSize.y*.5f,-1,1),properties);
            context.AfterSubmission(countSubmitted);
        }
        static readonly Action countSubmitted = CountSubmitted;
        static void CountSubmitted(){processingPasses++;}
        public void Render(Texture input, Material material, Vector2 size)
        {
            if (disposed) { throw new ObjectDisposedException(nameof(TextureEffectSurface)); }
            if (!input || !material || !float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0 || size.y <= 0)
            { throw new ArgumentException("纹理、材质和作者尺寸必须有效。"); }
            Prepare(size);
            material.mainTexture = input;
            material.SetFloat("_EffectTime",Time.unscaledTime);
            var projection = Matrix4x4.Ortho(-size.x * .5f, size.x * .5f, -size.y * .5f, size.y * .5f, -1, 1);
            commands.Clear(); commands.SetRenderTarget(output); commands.SetViewport(new Rect(0, 0, output.width, output.height));
            commands.ClearRenderTarget(false, true, Color.clear);
            // SetViewProjectionMatrices 接收 Unity 投影，命令执行时由后端转换；提前转换会在 Metal 上重复翻转。
            commands.SetViewProjectionMatrices(Matrix4x4.identity, projection);
            commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
            Graphics.ExecuteCommandBuffer(commands);
            processingPasses++;
        }
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            source.Dispose(); commands.Dispose();
        }
    }
}
