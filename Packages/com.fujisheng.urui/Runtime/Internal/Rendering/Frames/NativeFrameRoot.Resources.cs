using System;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Parameters;

namespace URUI.Internal.Rendering.Frames
{
    // 维护借用帧对应的 Unity 网格和纹理；上传失败的资源也必须参与退役。
    internal sealed partial class NativeFrameRoot
    {
        void Charge(long bytes)
        {
            if (bytes < 0 || retainedBytes + bytes > 64L * 1024 * 1024)
            {
                throw new InvalidOperationException("Native frame Unity immutable resource budget");
            }
        }

        void RetireFailedUpload(Mesh mesh)
        {
            if (building == null)
            {
                throw new InvalidOperationException("No native upload transaction");
            }

            building.failedMeshes.Add(mesh);
        }

        void Release(Plan plan)
        {
            if (plan == null)
            {
                return;
            }

            foreach (var mesh in plan.failedMeshes)
            {
                if (mesh)
                {
                    UnityEngine.Object.Destroy(mesh);
                }
            }

            foreach (var d in plan.draws)
            {
                var e = d.mesh;
                if (--e.refs < 0)
                {
                    throw new InvalidOperationException("Native frame mesh ownership");
                }

                if (e.refs == 0)
                {
                    meshes.Remove(e.id);
                    retainedBytes -= e.bytes;
                    UnityEngine.Object.Destroy(e.mesh);
                }
            }

            foreach (var e in plan.textures)
            {
                if (--e.refs < 0)
                {
                    throw new InvalidOperationException("Native frame texture ownership");
                }

                if (e.refs == 0)
                {
                    textures.Remove(e.id);
                    retainedBytes -= e.bytes;
                    UnityEngine.Object.Destroy(e.texture);
                }
            }
        }

        Texture AcquireTexture(Plan plan, ulong epoch, ulong id, ref ParameterTableRenderer.Stats stats)
        {
            if (id == 0)
            {
                return Texture2D.whiteTexture;
            }

            var key = (epoch, id);
            foreach (var t in plan.textures)
            {
                if (t.id == key)
                {
                    return t.texture;
                }
            }

            if (textures.TryGetValue(key, out var existing))
            {
                plan.textures.Add(existing);
                existing.refs++;
#if URUI_DEBUG
                stats.textureHits++;
#endif
                return existing.texture;
            }

            if (NativeMethods.urui_get_texture_info(id, out int w, out int h, out int bytes) != 1
                || w <= 0
                || h <= 0
                || (long)w * h * 4 != bytes
                || bytes > 32 * 1024 * 1024)
            {
                throw new InvalidOperationException("Native frame texture snapshot size");
            }

            Charge(bytes);
            var pixels = new byte[bytes];
            if (NativeMethods.urui_copy_texture(id, pixels, bytes) != 1)
            {
                throw new InvalidOperationException("Native frame texture snapshot copy");
            }

            var value = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = "URUI native final-frame immutable texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                value.LoadRawTextureData(pixels);
                value.Apply(false, true);
            }
            catch
            {
                UnityEngine.Object.Destroy(value);
                throw;
            }

            var entry = new TextureEntry
            {
                id = key,
                texture = value,
                bytes = bytes
            };
            bool added = false;
            try
            {
                textures.Add(key, entry);
                retainedBytes += bytes;
                added = true;
                plan.textures.Add(entry);
                entry.refs++;
            }
            catch
            {
                if (added)
                {
                    textures.Remove(key);
                    retainedBytes -= bytes;
                }

                UnityEngine.Object.Destroy(value);
                throw;
            }
#if URUI_DEBUG
            stats.textureCopies++;
            stats.textureCopyBytes += bytes;
            stats.textureRequestedBytes += bytes;
#endif
            return value;
        }
    }
}
