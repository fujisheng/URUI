using UnityEditor;
using URUI.Internal.Rendering.Shaders;

namespace URUI.Editor.Validation
{
    /// <summary>
    /// 在资产变更后使 shader 审计缓存失效，避免继续使用已过期的渲染依赖契约。
    /// </summary>
    internal sealed class ShaderContractAssetWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // 这是正确性校验的缓存失效，不受 URUI_DEBUG 控制；Player 帧内不读取源码。
            if (imported.Length + deleted.Length + moved.Length + movedFrom.Length > 0)
            {
                ShaderPropertyContract.InvalidateEditorAudit();
            }
        }
    }
}
