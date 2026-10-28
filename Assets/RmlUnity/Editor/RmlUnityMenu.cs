using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace RmlUnity.Editor {
 public static class RmlUnityMenu {
  [MenuItem("Tools/RmlUnity/Create MVP Demo Scene")]
  public static void CreateDemo(){
   var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
   Camera camera=Camera.main;
   if(!camera){
    var go=new GameObject("Main Camera"); camera=go.AddComponent<Camera>();
    go.tag="MainCamera";go.AddComponent<AudioListener>();
   }
   camera.clearFlags=CameraClearFlags.SolidColor;
   camera.backgroundColor=new Color(0.04f,0.06f,0.11f);
   camera.orthographic=true;
   var host=new GameObject("RmlUnity Host").AddComponent<RmlUnityHost>();
   host.document=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/RmlUnity/Demo/Resources/RmlUnityDemo.txt");
   host.shader=Shader.Find("RmlUnity/Premultiplied");
   EditorSceneManager.MarkSceneDirty(scene);
   EditorSceneManager.SaveScene(scene,"Assets/RmlUnity/Demo/RmlUnityDemo.unity");
   Selection.activeObject=host;
   Debug.Log("RmlUnity demo scene created; build the native bridge before entering Play Mode.");
  }
 }
}
