using UnityEngine;
using UnityEngine.Experimental.Rendering;
using URUI.Internal.Resources;

namespace URUI.Internal.Rendering.Shaders
{
    // Source kind contract shared with BorrowedTextureSampling.cginc / PremultipliedPalette.cginc / DirectMask.cginc:
    // 0: native owned sRGB-encoded premultiplied texture uploaded as linear data (software linearize in Linear space).
    // 1: borrowed Unity bottom-row-first texture requiring software 4-tap Load (Point/Repeat/HDR float/half).
    // 2: solid white sentinel (Texture2D.whiteTexture) -> shader returns (1,1,1,1) with 0 texture fetches.
    // 3: native owned pre-linearized premultiplied RGBAHalf atlas (1-tap hardware SampleLevel in Linear & Gamma).
    // 4: borrowed Unity opaque (no alpha channel) Clamp+Bilinear texture (1-tap hardware SampleLevel with flipped V).
    // 5: borrowed Unity UNorm alpha Clamp+Bilinear texture (1-tap hardware SampleLevel with exact a==1 / a==0 early-out).
    internal static class TextureShaderState
    {
        internal const float NativeSrgbKind = 0f;
        internal const float BorrowedSoftwareKind = 1f;
        internal const float SolidWhiteKind = 2f;
        internal const float NativePrelinearizedKind = 3f;
        internal const float BorrowedHardwareOpaqueKind = 4f;
        internal const float BorrowedUnormFastAlphaKind = 5f;
        internal static readonly Vector4 SolidWhiteSourceInfo = new Vector4(4f, 4f, SolidWhiteKind, 0f);

        static readonly int borrowed = Shader.PropertyToID("_UruiBorrowedTexture");
        static readonly int size = Shader.PropertyToID("_UruiSourceSize");

        internal static bool IsHardwareFastOnlyKind(float kind) => kind >= 1.5f && kind <= 4.5f;

        internal static Vector4 ComputeBorrowedSourceInfo(Texture texture, int width, int height, GraphicsFormat format, FilterMode filterMode, TextureWrapMode wrapMode)
        {
            if (ReferenceEquals(texture, Texture2D.whiteTexture))
            {
                return SolidWhiteSourceInfo;
            }
            float kind = BorrowedSoftwareKind;
            if (filterMode == FilterMode.Bilinear && wrapMode == TextureWrapMode.Clamp)
            {
                if (!GraphicsFormatUtility.HasAlphaChannel(format))
                {
                    kind = BorrowedHardwareOpaqueKind;
                }
                else if (GraphicsFormatUtility.IsUNormFormat(GraphicsFormatUtility.GetLinearFormat(format)))
                {
                    kind = BorrowedUnormFastAlphaKind;
                }
            }
            return new Vector4(width, height, kind, 0f);
        }

        internal static float SourceKind(Texture texture, ulong id)
        {
            if (ReferenceEquals(texture, Texture2D.whiteTexture))
            {
                return SolidWhiteKind;
            }
            if (!BorrowedTextureRegistry.IsBorrowed(id))
            {
                // Native owned atlases uploaded as RGBAHalf are pre-linearized premultiplied (hardware SampleLevel).
                return texture is Texture2D owned && owned.format == TextureFormat.RGBAHalf
                    ? NativePrelinearizedKind
                    : NativeSrgbKind;
            }
            if (texture.filterMode == FilterMode.Bilinear && texture.wrapMode == TextureWrapMode.Clamp)
            {
                var format = texture.graphicsFormat;
                if (!GraphicsFormatUtility.HasAlphaChannel(format))
                {
                    return BorrowedHardwareOpaqueKind;
                }
                if (GraphicsFormatUtility.IsUNormFormat(GraphicsFormatUtility.GetLinearFormat(format)))
                {
                    return BorrowedUnormFastAlphaKind;
                }
            }
            return BorrowedSoftwareKind;
        }

        internal static Vector4 SourceInfo(Texture texture, ulong id)
            => new Vector4(texture.width, texture.height, SourceKind(texture, id), 0f);

        internal static float Apply(MaterialPropertyBlock block, Texture texture, ulong id)
        {
            var kind = SourceKind(texture, id);
            block.SetFloat(borrowed, kind);
            var width = texture.width;
            var height = texture.height;
            block.SetVector(size, new Vector4(width, height, 1f / width, 1f / height));
            return kind;
        }

        internal static void Apply(MaterialPropertyBlock block, in Vector4 sourceInfo)
        {
            block.SetFloat(borrowed, sourceInfo.z);
            var width = sourceInfo.x;
            var height = sourceInfo.y;
            block.SetVector(size, new Vector4(width, height, 1f / width, 1f / height));
        }
    }
}
