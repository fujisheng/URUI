using UnityEditor;
namespace RmlUnity.Editor {
 public sealed class RmlShaderContractAssetWatcher:AssetPostprocessor {
  static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom){
   // Editor-only invalidation. No file IO or shader-source parsing in Player frames.
   if(imported.Length+deleted.Length+moved.Length+movedFrom.Length>0)RmlShaderPropertyContract.InvalidateEditorAudit();
  }
 }
}
