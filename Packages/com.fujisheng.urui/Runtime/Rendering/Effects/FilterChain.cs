using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace URUI.Rendering.Effects
{
    /// <summary>
    /// URUI 已解析并按作者顺序发出的一个滤镜调用；长度为布局像素，颜色为直通 alpha。
    /// </summary>
    public sealed class FilterInvocation
    {
        public string Name
        {
            get;
        }
        public float Value
        {
            get;
        }
        public float Sigma
        {
            get;
        }
        public Vector2 Offset
        {
            get;
        }
        public Color Color
        {
            get;
        }
        public RectInt Region
        {
            get;
        }
        internal FilterInvocation(string name, float value, float sigma, Vector2 offset, Color color, RectInt region)
        {
            Name = name;
            Value = value;
            Sigma = sigma;
            Offset = offset;
            Color = color;
            Region = region;
        }
    }
    public interface ILayerFilterProgram : IDisposable
    {
        /// <summary>
        /// 程序按名称在文档共享。只准备与调用顺序无关的资源；参数从Record的invocation读取，不能保存某节点的可变参数或修改DOM。
        /// </summary>
        void Prepare(FilterInvocation invocation);
        /// <summary>
        /// 只使用准备好的资源；source/destination/scratch 属于宿主并有 GPU 租约保护。
        /// </summary>
        void Record(FilterRenderContext context, FilterInvocation invocation);
    }
    public enum FilterBuffer
    {
        Source,
        Scratch,
        Destination
    }
    /// <summary>
    /// 有序子树滤镜的录制范围；中间颜色采用宿主线性/伽马约定和预乘 alpha，支持 HDR。
    /// </summary>
    public sealed class FilterRenderContext
    {
        static readonly int inputTexId = Shader.PropertyToID("_InputTex");
        CommandBuffer commands;
        Mesh quad;
        RenderTexture sourceTarget;
        RenderTexture scratchTarget;
        RenderTexture destinationTarget;
        bool active;
        internal int ProcessingPasses
        {
            get;
            private set;
        }
        public Vector2Int Size
        {
            get;
            private set;
        }
        internal FilterRenderContext()
        {
        }
        internal FilterRenderContext(CommandBuffer commands, Mesh quad, RenderTexture source, RenderTexture scratch, RenderTexture destination)
        {
            Open(commands, quad, source, scratch, destination);
        }
        internal void Open(CommandBuffer commands, Mesh quad, RenderTexture source, RenderTexture scratch, RenderTexture destination)
        {
            this.commands = commands;
            this.quad = quad;
            sourceTarget = source;
            scratchTarget = scratch;
            destinationTarget = destination;
            Size = new Vector2Int(source.width, source.height);
            ProcessingPasses = 0;
            active = true;
        }
        RenderTexture TargetOf(FilterBuffer buffer) => buffer switch
        {
            FilterBuffer.Source => sourceTarget,
            FilterBuffer.Scratch => scratchTarget,
            FilterBuffer.Destination => destinationTarget,
            _ => throw new ArgumentOutOfRangeException(nameof(buffer))
        };
        public Texture Texture(FilterBuffer buffer)
        {
            Require();
            return TargetOf(buffer);
        }
        void Require()
        {
            if (!active)
            {
                throw new InvalidOperationException("滤镜录制范围已结束。");
            }
        }
        public void Draw(Material material, MaterialPropertyBlock properties, int pass, FilterBuffer source, FilterBuffer destination)
        {
            Require();
            if (!material || properties == null)
            {
                throw new ArgumentNullException(nameof(material));
            }
            if (source == destination)
            {
                throw new ArgumentException("滤镜不能同时读取和写入同一目标。");
            }
            if (pass < 0 || pass >= material.passCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pass));
            }
            properties.SetTexture(inputTexId, TargetOf(source));
            commands.SetRenderTarget(TargetOf(destination));
            commands.SetViewport(new Rect(0, 0, Size.x, Size.y));
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, Size.x, Size.y, 0, -1, 1));
            ProcessingPasses++;
            commands.DrawMesh(quad, Matrix4x4.Scale(new Vector3(Size.x, Size.y, 1)), material, 0, pass, properties);
        }
        public void Copy(FilterBuffer source, FilterBuffer destination)
        {
            Require();
            if (source == destination)
            {
                throw new ArgumentException("复制目标不能相同。");
            }
            var src = TargetOf(source);
            var dst = TargetOf(destination);
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0 &&
                src.width == dst.width && src.height == dst.height &&
                src.graphicsFormat == dst.graphicsFormat && src.antiAliasing == 1 && dst.antiAliasing == 1)
            {
                commands.CopyTexture(src, dst);
            }
            else
            {
                commands.Blit(src, dst);
            }
        }
        internal void Close()
        {
            active = false;
            commands = null;
            quad = null;
            sourceTarget = null;
            scratchTarget = null;
            destinationTarget = null;
        }
    }
}
