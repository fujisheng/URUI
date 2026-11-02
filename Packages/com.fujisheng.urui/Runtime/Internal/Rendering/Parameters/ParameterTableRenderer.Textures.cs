using System;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Resources;

namespace URUI.Internal.Rendering.Parameters
{
    // 复制和复用当前原生图片，记录计划持有的纹理引用及内存预算。
    internal sealed partial class ParameterTableRenderer
    {
        Texture Texture(Plan plan, ulong id, ref Stats stats)
        {
            if (id == 0)
            {
                return Texture2D.whiteTexture;
            }

            if (plan.textures.TryGetValue(id, out var t))
            {
                return t;
            }

            if (BorrowedTextureRegistry.IsBorrowed(id))
            {
                if (borrowedTextures == null || !borrowedTextures.TryResolve(id, out var borrowed))
                    throw new InvalidOperationException("Borrowed texture registry is required for this context");
                plan.borrowedTextureIds.Add(id);
                plan.textures.Add(id, borrowed);
                return borrowed;
            }

            var key = (plan.epoch, id);
            if (shareResources && sharedTextures.TryGetValue(key, out var shared))
            {
                if (!shared.value || NativeMethods.urui_texture_exists(id) != 1)
                {
                    throw new InvalidOperationException("Stale immutable texture");
                }

                ChargeTexture(plan, shared.bytes);
                plan.sharedTextures.Add(shared);
                shared.references++;
                plan.textures.Add(id, shared.value);
#if URUI_DEBUG
                stats.textureHits++;
#endif
                return shared.value;
            }

            if (NativeMethods.urui_get_texture_info(id, out int w, out int h, out int count) != 1
                || w <= 0
                || h <= 0
                || (long)w * h * 4 != count)
            {
                throw new InvalidOperationException("Invalid native texture");
            }

            ChargeTexture(plan, count);
            AdmitPayload(count);
            var data = new byte[count];
            if (NativeMethods.urui_copy_texture(id, data, count) != 1)
            {
                throw new InvalidOperationException("Texture copy failed");
            }
#if URUI_DEBUG
            stats.textureCopies++;
            stats.textureCopyBytes += count;
#endif
            t = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "URUI parameter owned snapshot"
            };
            try
            {
                t.LoadRawTextureData(data);
                t.Apply(false, true);
            }
            catch
            {
                UnityEngine.Object.Destroy(t);
                throw;
            }

            if (shareResources)
            {
                var entry = new SharedTexture
                {
                    key = key,
                    value = t,
                    bytes = count
                };
                try
                {
                    sharedTextures.Add(key, entry);
                    texturePayload += count;
                }
                catch
                {
                    UnityEngine.Object.Destroy(t);
                    throw;
                }

                try
                {
                    plan.sharedTextures.Add(entry);
                    entry.references++;
                }
                catch
                {
                    if (entry.references == 0)
                    {
                        sharedTextures.Remove(key);
                        texturePayload -= count;
                        UnityEngine.Object.Destroy(t);
                    }

                    throw;
                }
            }

            plan.textures.Add(id, t);
#if URUI_DEBUG
            stats.textureRequestedBytes += count;
#endif
            return t;
        }
    }
}
