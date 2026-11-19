using System;
using UnityEngine;
using URUI.Documents;
using URUI.Rendering.Effects;
namespace URUI.Internal.Rendering
{
    /// <summary>
    /// URUI 内置的标准子树滤镜；由文档资源代际和 GPU 退役路径持有。
    /// </summary>
    internal static class BuiltinFilterPrograms
    {
        internal static bool IsBuiltin(string name) => name is "grayscale" or "saturate" or "brightness" or "contrast" or "opacity" or "blur" or "drop-shadow";
        internal static ILayerFilterProgram Create(string name) => name switch
        {
            "blur" => new BlurProgram(false),
            "drop-shadow" => new BlurProgram(true),
            "grayscale" or "saturate" or "brightness" or "contrast" or "opacity" => new ColorProgram(name),
            _ => throw new ArgumentException("Unsupported built-in filter: " + name)
        };
        sealed class ColorProgram : ILayerFilterProgram
        {
            readonly string name;
            FilterResources resources;
            readonly MaterialPropertyBlock properties = new();
            internal ColorProgram(string name)
            {
                this.name = name;
            }
            public void Prepare(FilterInvocation invocation)
            {
                resources ??= new FilterResources("ColorFilter");
            }
            public void Record(FilterRenderContext context, FilterInvocation invocation)
            {
                properties.Clear();
                properties.SetFloat("_Amount", invocation.Value);
                properties.SetFloat("_Mode", name == "grayscale" ? 0 : name == "saturate" ? 1 : name == "brightness" ? 2 : name == "contrast" ? 3 : 4);
                context.Draw(resources.Material, properties, 0, FilterBuffer.Source, FilterBuffer.Destination);
            }
            public void Dispose()
            {
                resources?.Dispose();
                resources = null;
            }
        }
        sealed class BlurProgram : ILayerFilterProgram
        {
            readonly bool shadow;
            FilterResources blur;
            FilterResources composite;
            readonly MaterialPropertyBlock properties = new();
            internal BlurProgram(bool shadow)
            {
                this.shadow = shadow;
            }
            public void Prepare(FilterInvocation invocation)
            {
                blur ??= new FilterResources("Blur");
                if (shadow)
                {
                    composite ??= new FilterResources("DropShadow");
                }
            }
            public void Record(FilterRenderContext context, FilterInvocation invocation)
            {
                properties.Clear();
                properties.SetVector("_Blur", new Vector4(Mathf.Max(0, invocation.Sigma), 1f / context.Size.x, 1f / context.Size.y, Mathf.Max(context.Size.x, context.Size.y)));
                context.Draw(blur.Material, properties, 0, FilterBuffer.Source, FilterBuffer.Scratch);
                context.Draw(blur.Material, properties, 1, FilterBuffer.Scratch, FilterBuffer.Destination);
                if (!shadow)
                {
                    return;
                }
                properties.Clear();
                properties.SetTexture("_Shadow", context.Texture(FilterBuffer.Destination));
                properties.SetVector("_Offset", new Vector4(invocation.Offset.x / context.Size.x, -invocation.Offset.y / context.Size.y, 0, 0));
                var color = QualitySettings.activeColorSpace == ColorSpace.Linear ? invocation.Color.linear : invocation.Color;
                properties.SetVector("_ShadowColor", new Vector4(color.r, color.g, color.b, color.a));
                context.Draw(composite.Material, properties, 0, FilterBuffer.Source, FilterBuffer.Scratch);
                context.Copy(FilterBuffer.Scratch, FilterBuffer.Destination);
            }
            public void Dispose()
            {
                blur?.Dispose();
                composite?.Dispose();
                blur = null;
                composite = null;
            }
        }
    }
}
