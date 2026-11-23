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
        static readonly int amountId = Shader.PropertyToID("_Amount");
        static readonly int modeId = Shader.PropertyToID("_Mode");
        static readonly int blurId = Shader.PropertyToID("_Blur");
        static readonly int blurTapsId = Shader.PropertyToID("_BlurTaps");
        static readonly int shadowId = Shader.PropertyToID("_Shadow");
        static readonly int offsetId = Shader.PropertyToID("_Offset");
        static readonly int shadowColorId = Shader.PropertyToID("_ShadowColor");

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
            readonly float modeValue;
            FilterResources resources;
            readonly MaterialPropertyBlock properties = new();
            internal ColorProgram(string name)
            {
                modeValue = name == "grayscale" ? 0f : name == "saturate" ? 1f : name == "brightness" ? 2f : name == "contrast" ? 3f : 4f;
            }
            public void Prepare(FilterInvocation invocation)
            {
                resources ??= new FilterResources("ColorFilter");
            }
            public void Record(FilterRenderContext context, FilterInvocation invocation)
            {
                properties.Clear();
                properties.SetFloat(amountId, invocation.Value);
                properties.SetFloat(modeId, modeValue);
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
            const int MaxTapVectors = 32;
            const int MaxPairedTaps = MaxTapVectors * 2;
            readonly bool shadow;
            FilterResources blur;
            FilterResources composite;
            readonly MaterialPropertyBlock properties = new();
            readonly Vector4[] taps = new Vector4[MaxTapVectors];
            float cachedSigma = -1f;
            int cachedMaxRadius = -1;
            int cachedTapCount;
            float cachedCenterWeight = 1f;

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

            void UpdateGaussianTable(float sigma, int maxRadius)
            {
                if (cachedSigma == sigma && cachedMaxRadius == maxRadius)
                {
                    return;
                }
                cachedSigma = sigma;
                cachedMaxRadius = maxRadius;
                if (sigma <= 0.001f || maxRadius <= 0)
                {
                    cachedTapCount = 0;
                    cachedCenterWeight = 1f;
                    return;
                }
                int radius = Mathf.Min(Mathf.CeilToInt(3f * sigma), Mathf.Max(1, maxRadius));
                // Keep paired taps within the fixed 32xVector4 constant-buffer table (up to 64 pairs -> radius 128).
                float step = radius > MaxPairedTaps * 2 ? (float)radius / (MaxPairedTaps * 2) : 1f;
                int sampleCount = radius > MaxPairedTaps * 2 ? MaxPairedTaps * 2 : radius;
                double total = 1.0;
                int pairCount = 0;
                Array.Clear(taps, 0, taps.Length);
                double invSigma2 = -0.5 / ((double)sigma * sigma);
                for (int i = 1; i <= sampleCount && pairCount < MaxPairedTaps; i += 2)
                {
                    double posA = i * step;
                    double wA = Math.Exp(invSigma2 * posA * posA);
                    double wB = 0.0;
                    double posB = (i + 1) * step;
                    if (i + 1 <= sampleCount)
                    {
                        wB = Math.Exp(invSigma2 * posB * posB);
                    }
                    double weight = wA + wB;
                    if (weight <= 1e-12)
                    {
                        continue;
                    }
                    double offset = (posA * wA + posB * wB) / weight;
                    total += 2.0 * weight;
                    int vecIndex = pairCount >> 1;
                    if ((pairCount & 1) == 0)
                    {
                        taps[vecIndex].x = (float)offset;
                        taps[vecIndex].y = (float)weight;
                    }
                    else
                    {
                        taps[vecIndex].z = (float)offset;
                        taps[vecIndex].w = (float)weight;
                    }
                    pairCount++;
                }
                float invTotal = (float)(1.0 / total);
                cachedCenterWeight = invTotal;
                cachedTapCount = pairCount;
                int vecUsed = (pairCount + 1) >> 1;
                for (int k = 0; k < vecUsed; k++)
                {
                    taps[k].y *= invTotal;
                    taps[k].w *= invTotal;
                }
            }

            public void Record(FilterRenderContext context, FilterInvocation invocation)
            {
                float sigma = Mathf.Max(0f, invocation.Sigma);
                if (sigma <= 0.001f)
                {
                    if (!shadow)
                    {
                        context.Copy(FilterBuffer.Source, FilterBuffer.Destination);
                        return;
                    }
                    // Zero-blur drop-shadow: skip both blur passes and composite directly from Source -> Destination.
                    DrawDropShadow(context, invocation, FilterBuffer.Source, FilterBuffer.Destination);
                    return;
                }

                int horizontalMaxRadius = Mathf.Max(1, context.Size.x);
                int verticalMaxRadius = Mathf.Max(1, context.Size.y);
                int hPass = shadow ? 2 : 0;
                int vPass = shadow ? 3 : 1;

                UpdateGaussianTable(sigma, horizontalMaxRadius);
                properties.Clear();
                properties.SetVector(blurId, new Vector4(cachedTapCount, 1f / context.Size.x, 1f / context.Size.y, cachedCenterWeight));
                properties.SetVectorArray(blurTapsId, taps);

                if (!shadow)
                {
                    context.Draw(blur.Material, properties, hPass, FilterBuffer.Source, FilterBuffer.Scratch);
                    if (verticalMaxRadius != horizontalMaxRadius)
                    {
                        UpdateGaussianTable(sigma, verticalMaxRadius);
                        properties.SetVector(blurId, new Vector4(cachedTapCount, 1f / context.Size.x, 1f / context.Size.y, cachedCenterWeight));
                        properties.SetVectorArray(blurTapsId, taps);
                    }
                    context.Draw(blur.Material, properties, vPass, FilterBuffer.Scratch, FilterBuffer.Destination);
                    return;
                }

                // Ping-pong into Scratch using scalar-alpha passes so DropShadow composites directly into Destination.
                context.Draw(blur.Material, properties, hPass, FilterBuffer.Source, FilterBuffer.Destination);
                if (verticalMaxRadius != horizontalMaxRadius)
                {
                    UpdateGaussianTable(sigma, verticalMaxRadius);
                    properties.SetVector(blurId, new Vector4(cachedTapCount, 1f / context.Size.x, 1f / context.Size.y, cachedCenterWeight));
                    properties.SetVectorArray(blurTapsId, taps);
                }
                context.Draw(blur.Material, properties, vPass, FilterBuffer.Destination, FilterBuffer.Scratch);
                DrawDropShadow(context, invocation, FilterBuffer.Scratch, FilterBuffer.Destination);
            }

            void DrawDropShadow(FilterRenderContext context, FilterInvocation invocation, FilterBuffer shadowSource, FilterBuffer target)
            {
                properties.Clear();
                properties.SetTexture(shadowId, context.Texture(shadowSource));
                properties.SetVector(offsetId, new Vector4(invocation.Offset.x / context.Size.x, -invocation.Offset.y / context.Size.y, 0f, 0f));
                var color = QualitySettings.activeColorSpace == ColorSpace.Linear ? invocation.Color.linear : invocation.Color;
                properties.SetVector(shadowColorId, new Vector4(color.r, color.g, color.b, color.a));
                context.Draw(composite.Material, properties, 0, FilterBuffer.Source, target);
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
