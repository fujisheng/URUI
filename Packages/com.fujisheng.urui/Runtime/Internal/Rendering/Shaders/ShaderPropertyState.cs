using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Shaders
{
    /// <summary>
    /// 一次命令记录内的属性块写入缓存。只有通过 shader 依赖约定验证后才能跳过重复写入。
    /// 不拥有材质或纹理；换用属性块、材质、shader 或裁剪状态时必须重新写入。
    /// </summary>
    internal sealed class ShaderPropertyState
    {
        MaterialPropertyBlock block;
        Texture texture;
        Material material;
        Shader shader;
        CommandNative previous;
        bool valid;

        public void Reset()
        {
            valid = false;
            block = null;
            texture = null;
            material = null;
            shader = null;
        }

        public bool NeedsWrite(MaterialPropertyBlock target, Texture tex, CommandNative command, bool contractAllowsReuse, Material owner)
        {
            if (!contractAllowsReuse)
            {
                Reset();
                return true;
            }
            // Unity 对象身份和实际 shader 同时比较，防止材质原地更换 shader 后复用旧状态。
            var currentShader = owner ? owner.shader : null;
            bool same = valid
                && ReferenceEquals(block, target)
                && ReferenceEquals(texture, tex)
                && ReferenceEquals(material, owner)
                && ReferenceEquals(shader, currentShader)
                // Borrowed/owned shader mode depends on the logical texture ID,
                // even when both commands happen to bind Unity's white texture.
                && previous.texture == command.texture
                && previous.clip_enabled == command.clip_enabled
                && previous.clip_x == command.clip_x
                && previous.clip_y == command.clip_y
                && previous.clip_w == command.clip_w
                && previous.clip_h == command.clip_h;
            valid = true;
            block = target;
            texture = tex;
            material = owner;
            shader = currentShader;
            previous = command;
            return !same;
        }
    }
}
