using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Shaders;
using URUI.Internal.Resources;

namespace URUI.Internal.Hosting
{
    // 借用调用者图片；仅为原生生成纹理创建 Unity 副本，并在 Linear 空间预线性化为 RGBAHalf 以走 1× 硬件采样。
    internal sealed partial class RenderHost
    {
        struct NativeTextureEntry
        {
            internal Texture2D Texture;
            internal Vector4 SourceInfo;
            internal bool IsUNorm;
        }

        static ushort[] opaqueSrgbToLinearHalf;
        static ushort[] linearByteToHalf;
        static bool? rgbaHalfTextureSupported;
        byte[] textureCopyScratch = Array.Empty<byte>();

        static bool SupportsPrelinearizedNativeAtlas
            => QualitySettings.activeColorSpace == ColorSpace.Linear &&
               (rgbaHalfTextureSupported ??= SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf));

        static void EnsureHalfLookupTables()
        {
            if (opaqueSrgbToLinearHalf != null)
            {
                return;
            }
            var srgb = new ushort[256];
            var linear = new ushort[256];
            for (var i = 0; i < 256; i++)
            {
                var v = i * (1f / 255f);
                linear[i] = Mathf.FloatToHalf(v);
                // Matches UnityCG.cginc GammaToLinearSpace cubic approximation used by UruiLinearizePremultipliedColor.
                var lin = v * (v * (v * 0.305306011f + 0.682171111f) + 0.012522878f);
                srgb[i] = Mathf.FloatToHalf(lin);
            }
            linearByteToHalf = linear;
            opaqueSrgbToLinearHalf = srgb;
        }

        static ushort PremultipliedSrgbChannelToLinearHalf(byte channel, float alpha, float invAlpha)
        {
            if (channel == 0)
            {
                return 0;
            }
            var straight = (channel * (1f / 255f)) * invAlpha;
            var lin = straight * (straight * (straight * 0.305306011f + 0.682171111f) + 0.012522878f) * alpha;
            return Mathf.FloatToHalf(lin);
        }

        Texture LoadTexture(ulong id) => LoadTextureInfo(id, out _, out _, out _);

        unsafe Texture LoadTextureInfo(ulong id, out Vector4 sourceInfo, out bool isLive, out bool isUNorm)
        {
            if (id == 0)
            {
                sourceInfo = TextureShaderState.SolidWhiteSourceInfo;
                isLive = false;
                isUNorm = true;
                return white;
            }

            if (BorrowedTextureRegistry.IsBorrowed(id))
            {
                legacyImagePins.AcquireOneValidated(borrowedTextures, id, out var borrowed, out sourceInfo, out isLive, out isUNorm);
                if (!borrowed)
                {
                    sourceInfo = TextureShaderState.SolidWhiteSourceInfo;
                    isLive = false;
                    isUNorm = true;
                }
                return borrowed;
            }

            if (textures.TryGetValue(id, out var old))
            {
                if (old.Texture)
                {
                    sourceInfo = old.SourceInfo;
                    isLive = false;
                    isUNorm = old.IsUNorm;
                    return old.Texture;
                }
                textures.Remove(id);
            }

            if (NativeMethods.urui_get_texture_info(SurfaceHandle, id, out int w, out int h, out int length) != NativeStatus.Ok)
            {
                sourceInfo = TextureShaderState.SolidWhiteSourceInfo;
                isLive = false;
                isUNorm = true;
                return white;
            }

            if (w <= 0 || h <= 0 || length != w * h * 4)
            {
                sourceInfo = TextureShaderState.SolidWhiteSourceInfo;
                isLive = false;
                isUNorm = true;
                return white;
            }

            Texture2D texture;
            float kind;
            bool unorm;
            if (SupportsPrelinearizedNativeAtlas)
            {
                if (textureCopyScratch.Length < length)
                {
                    var next = Math.Max(length, textureCopyScratch.Length * 2 + 4096);
                    textureCopyScratch = new byte[next];
                }
                if (NativeMethods.urui_copy_texture(SurfaceHandle, id, textureCopyScratch, length) != NativeStatus.Ok)
                {
                    sourceInfo = TextureShaderState.SolidWhiteSourceInfo;
                    isLive = false;
                    isUNorm = true;
                    return white;
                }
                EnsureHalfLookupTables();
                texture = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                var raw = texture.GetRawTextureData<ushort>();
                var dst = (ushort*)NativeArrayUnsafeUtility.GetUnsafePtr(raw);
                var opaque = opaqueSrgbToLinearHalf;
                var alphaLut = linearByteToHalf;
                var pixelCount = w * h;
                fixed (byte* src = textureCopyScratch)
                {
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var off = i * 4;
                        var a = src[off + 3];
                        if (a == 0)
                        {
                            dst[off] = 0;
                            dst[off + 1] = 0;
                            dst[off + 2] = 0;
                            dst[off + 3] = 0;
                        }
                        else if (a == 255)
                        {
                            dst[off] = opaque[src[off]];
                            dst[off + 1] = opaque[src[off + 1]];
                            dst[off + 2] = opaque[src[off + 2]];
                            dst[off + 3] = alphaLut[255];
                        }
                        else
                        {
                            var alpha = a * (1f / 255f);
                            var invAlpha = 255f / a;
                            dst[off] = PremultipliedSrgbChannelToLinearHalf(src[off], alpha, invAlpha);
                            dst[off + 1] = PremultipliedSrgbChannelToLinearHalf(src[off + 1], alpha, invAlpha);
                            dst[off + 2] = PremultipliedSrgbChannelToLinearHalf(src[off + 2], alpha, invAlpha);
                            dst[off + 3] = alphaLut[a];
                        }
                    }
                }
                texture.Apply(false, true);
                kind = TextureShaderState.NativePrelinearizedKind;
                unorm = false;
            }
            else
            {
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                var raw = texture.GetRawTextureData<byte>();
                if (NativeMethods.urui_copy_texture(SurfaceHandle, id, (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(raw), length) != NativeStatus.Ok)
                {
                    DestroyRenderObject(texture);
                    sourceInfo = TextureShaderState.SolidWhiteSourceInfo;
                    isLive = false;
                    isUNorm = true;
                    return white;
                }
                texture.Apply(false, true);
                kind = TextureShaderState.NativeSrgbKind;
                unorm = GraphicsFormatUtility.IsUNormFormat(GraphicsFormatUtility.GetLinearFormat(texture.graphicsFormat));
            }

            sourceInfo = new Vector4(w, h, kind, 0f);
            isLive = false;
            isUNorm = unorm;
            textures[id] = new NativeTextureEntry
            {
                Texture = texture,
                SourceInfo = sourceInfo,
                IsUNorm = unorm
            };
            return texture;
        }

        void RetireReleasedTextures(ref PhaseFrame phase)
        {
            if (textures.Count == 0)
            {
                return;
            }
            retiredTextureIds.Clear();
            foreach (var pair in textures)
            {
                if (!pair.Value.Texture || NativeMethods.urui_texture_exists(SurfaceHandle, pair.Key) != NativeStatus.Ok)
                {
                    retiredTextureIds.Add(pair.Key);
                }
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.nativeCalls++;
                }
#endif
            }

            foreach (var id in retiredTextureIds)
            {
                if (textures[id].Texture)
                {
                    DestroyRenderObject(textures[id].Texture);
                }

                textures.Remove(id);
            }
        }
    }
}
