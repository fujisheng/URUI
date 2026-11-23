using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Tests
{
    /// <summary>
    /// 原位遮罩必须保留原始浮点遮罩路径的逐字节结果，包括目标颜色域和量化边界。
    /// </summary>
    public sealed class FramebufferMaskTests
    {
        static IEnumerable<TestCaseData> Cases()
        {
            foreach (var format in new[] { RenderTextureFormat.ARGBHalf, RenderTextureFormat.ARGB32 })
            {
                foreach (var linear in new[] { true, false })
                {
                    for (var variant = 0; variant < 4; variant++)
                    {
                        yield return new TestCaseData(format, linear, variant).SetName($"InPlaceMask_{format}_{(linear ? "Linear" : "Default")}_{variant}");
                    }
                }
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void InPlaceMaskMatchesOriginalStoredPixels(RenderTextureFormat format, bool linear, int variant)
        {
            var fetch = Shader.Find("URUI/LayerCompositeFetch");
            if (!fetch || !fetch.isSupported || (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
            {
                Assert.Ignore("此后端不支持 framebuffer fetch 或同格式复制；使用原有遮罩路径。");
            }
            int width = variant % 2 == 0 ? 512 : 509;
            int height = variant % 2 == 0 ? 256 : 253;
            var values = new Color[width * height];
            var masks = new Color[values.Length];
            uint random = 0x92675432u;
            float Next()
            {
                random ^= random << 13;
                random ^= random >> 17;
                random ^= random << 5;
                return (random & 0xffffff) / 16777215f;
            }
            for (var i = 0; i < values.Length; i++)
            {
                var alpha = i % 11 == 0 ? 0 : i % 11 == 1 ? 1 : Next();
                var scale = variant == 3 ? 32f : 1f;
                values[i] = new Color(Next() * alpha * scale, Next() * alpha * scale, Next() * alpha * scale, alpha);
                masks[i] = new Color(0, 0, 0, i % 17 == 0 ? 0 : i % 17 == 1 ? 1 : i % 17 == 2 ? 1f / 255 : Next());
            }
            var source = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var mask = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            RenderTexture CreateTarget()
            {
                var texture = new RenderTexture(width, height, 0, format, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.Create();
                return texture;
            }
            var input = CreateTarget();
            var expected = CreateTarget();
            var actual = CreateTarget();
            var reference = new Material(Shader.Find("URUI/LayerComposite"));
            var candidate = new Material(fetch);
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up },
                uv = new[] { Vector2.up, Vector2.one, Vector2.right, Vector2.zero },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var previous = RenderTexture.active;
            try
            {
                source.SetPixels(values);
                source.Apply();
                mask.SetPixels(masks);
                mask.Apply();
                Graphics.Blit(source, input);
                Graphics.CopyTexture(input, actual);
                using (var commands = new CommandBuffer())
                {
                    commands.SetGlobalTexture("_UruiLayerSource", input);
                    commands.SetGlobalTexture("_UruiLayerMask", mask);
                    commands.SetGlobalFloat("_UruiLayerMaskEnabled", 1);
                    commands.SetGlobalFloat("_UruiLayerClipEnabled", 0);
                    commands.SetGlobalVector("_UruiLayerMaskRect", variant < 2 ? new Vector4(0, 0, width, height) : new Vector4(-3.5f, 9.25f, width - 11.5f, height - 7.5f));
                    commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, width, height, 0, -1, 1));
                    var transform = Matrix4x4.Scale(new Vector3(width, height, 1));
                    commands.SetRenderTarget(expected);
                    commands.SetViewport(new Rect(0, 0, width, height));
                    commands.DrawMesh(mesh, transform, reference, 0, 1);
                    commands.SetRenderTarget(actual);
                    commands.SetViewport(new Rect(0, 0, width, height));
                    commands.DrawMesh(mesh, transform, candidate, 0, 0);
                    Graphics.ExecuteCommandBuffer(commands);
                }
                var readFormat = format == RenderTextureFormat.ARGBHalf ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;
                var left = Read(expected, readFormat);
                var right = Read(actual, readFormat);
                var original = Read(input, readFormat);
                int changed = 0;
                int nonzero = 0;
                for (var i = 0; i < left.Length; i++)
                {
                    if (left[i] != right[i])
                    {
                        Assert.Fail($"存储字节不同：index={i}, expected={left[i]}, actual={right[i]}");
                    }
                    if (left[i] != original[i])
                    {
                        changed++;
                    }
                    if (left[i] != 0)
                    {
                        nonzero++;
                    }
                }
                Assert.That(changed, Is.GreaterThan(1000), "必须实际应用遮罩。");
                Assert.That(nonzero, Is.GreaterThan(1000), "全黑输出不能成为等价证据。");
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var texture in new[] { input, expected, actual })
                {
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                foreach (var resource in new UnityEngine.Object[] { source, mask, reference, candidate, mesh })
                {
                    UnityEngine.Object.DestroyImmediate(resource);
                }
            }
        }

        static byte[] Read(RenderTexture target, TextureFormat format)
        {
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, format, false, true);
            try
            {
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply();
                return texture.GetRawTextureData();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
