using UnityEngine;
using URUI.Internal.Resources;

namespace URUI.Internal.Rendering.Shaders
{
    // Owned atlases are already premultiplied/top-row-first. Borrowed textures stay
    // unchanged; shader sampling handles orientation/alpha without an image copy.
    internal static class TextureShaderState
    {
        static readonly int borrowed = Shader.PropertyToID("_UruiBorrowedTexture");
        static readonly int size = Shader.PropertyToID("_UruiSourceSize");
        static readonly int srgb = Shader.PropertyToID("_UruiSourceSrgb");
        internal static void Apply(MaterialPropertyBlock block, Texture texture, ulong id)
        {
            bool external = BorrowedTextureRegistry.IsBorrowed(id);
            block.SetFloat(borrowed, external ? 1 : 0);
            block.SetFloat(srgb, external && texture.isDataSRGB ? 1 : 0);
            block.SetVector(size, new Vector4(texture.width, texture.height, 1f / texture.width, 1f / texture.height));
        }
    }
}
