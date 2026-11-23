using System;
using UnityEditor;
using URUI.Internal.Rendering.Shaders;

namespace URUI.Editor.Validation
{
    /// <summary>
    /// 在 shader 或契约资产变更后使 shader 审计缓存失效，避免继续使用已过期的渲染依赖契约。
    /// </summary>
    internal sealed class ShaderContractAssetWatcher : AssetPostprocessor
    {
        static bool AffectsShaderContract(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("BuiltinShaderPropertyContract.asset", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // 这是正确性校验的缓存失效，不受 URUI_DEBUG 控制；Player 帧内不读取源码。
            if (AffectsShaderContract(imported) || AffectsShaderContract(deleted) || AffectsShaderContract(moved) || AffectsShaderContract(movedFrom))
            {
                ShaderPropertyContract.InvalidateEditorAudit();
            }
        }
    }
}
