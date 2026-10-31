using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RmlUnity.Editor {
 public static class RmlUnityMenu {
  [MenuItem("Tools/RmlUnity/Create Minimal UI Scene")]
  public static void CreateMinimalScene() {
   if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
   const string folder="Assets/RmlUnityGenerated";
   Directory.CreateDirectory(folder);
   AssetDatabase.Refresh();
   var documentPath=AssetDatabase.GenerateUniqueAssetPath(folder+"/Hello.txt");
   var scenePath=AssetDatabase.GenerateUniqueAssetPath(folder+"/Hello.unity");
   File.WriteAllText(documentPath,"<rml><head><style>body { font-family:__RMLUNITY_FONT__; color:white; font-size:28px; }</style></head><body><div id='label'>Hello RmlUnity</div></body></rml>");
   AssetDatabase.Refresh();
   var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
   var host=new GameObject("RmlUnity Host").AddComponent<RmlUnityHost>();
   host.document=AssetDatabase.LoadAssetAtPath<TextAsset>(documentPath);
   host.shader=AssetDatabase.LoadAssetAtPath<Shader>(RmlShaderPropertyContract.BuiltinPath);
   EditorSceneManager.SaveScene(scene,scenePath);
   Selection.activeObject=host;
  }
 }
}
