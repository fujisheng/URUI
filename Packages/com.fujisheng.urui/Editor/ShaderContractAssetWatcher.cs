using URUI.Internal;
using UnityEditor;
namespace URUI.Editor {
 internal sealed class ShaderContractAssetWatcher:AssetPostprocessor {
  static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom){
   // Editor-only invalidation. No file IO or shader-source parsing in Player frames.
   if(imported.Length+deleted.Length+moved.Length+movedFrom.Length>0)ShaderPropertyContract.InvalidateEditorAudit();
  }
 }
}
