using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    // 创建原生图片的 Unity 副本，并在原生资源释放后清理宿主缓存。
    internal sealed partial class RenderHost
    {
        Texture2D LoadTexture(ulong id)
        {
            if (id == 0)
            {
                return white;
            }

            if (textures.TryGetValue(id, out var old))
            {
                if (old)
                {
                    return old;
                }

                textures.Remove(id);
            }

            if (NativeMethods.urui_get_texture_info(id, out int w, out int h, out int length) == 0)
            {
                return white;
            }

            if (w <= 0 || h <= 0 || length != w * h * 4)
            {
                return white;
            }

            var rgba = new byte[length];
            if (NativeMethods.urui_copy_texture(id, rgba, length) == 0)
            {
                return white;
            }

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.LoadRawTextureData(rgba);
            texture.Apply(false, true);
            textures[id] = texture;
            return texture;
        }

        void RetireReleasedTextures(ref PhaseFrame phase)
        {
            retiredTextureIds.Clear();
            foreach (var pair in textures)
            {
                if (!pair.Value || NativeMethods.urui_texture_exists(pair.Key) == 0)
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
                if (textures[id])
                {
                    Destroy(textures[id]);
                }

                textures.Remove(id);
            }
        }
    }
}
