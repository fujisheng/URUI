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
        internal static Vector4 SourceInfo(Texture texture,ulong id)
            =>new Vector4(texture.width,texture.height,BorrowedTextureRegistry.IsBorrowed(id)?1:0,0);
        internal static void Apply(MaterialPropertyBlock block, Texture texture, ulong id)
        {
            bool external = BorrowedTextureRegistry.IsBorrowed(id);
            block.SetFloat(borrowed, external ? 1 : 0);
            // 导入图片保留与 URUI 原生字节贴图一致的预乘规则；实时渲染结果
            // 已经过 Unity 色彩流水线，应在 GPU 解码后的线性值上预乘 alpha。
            block.SetFloat(srgb, external && texture.isDataSRGB && !(texture is RenderTexture) ? 1 : 0);
            block.SetVector(size, new Vector4(texture.width, texture.height, 1f / texture.width, 1f / texture.height));
        }
    }
}
