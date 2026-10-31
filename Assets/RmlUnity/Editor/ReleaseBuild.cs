using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RmlUnity.Editor {
 public static class ReleaseBuild {
  [MenuItem("Tools/RmlUnity/Build Mac ARM64/RML Match3")]
  public static void Rml()=>Build("Match3RmlAtlas","RmlUnity-Match3");
  [MenuItem("Tools/RmlUnity/Build Mac ARM64/uGUI Match3")]
  public static void Ugui()=>Build("Match3UGUIAtlas","RmlUnity-uGUI");
  static void Build(string sceneName,string product){
   string scene="Assets/RmlUnity/Demo/"+sceneName+".unity";
   string output=Path.GetFullPath(Path.Combine("Builds",product+".app"));
   if(Directory.Exists(output))throw new BuildFailedException("Output already exists: "+output);
   EditorSceneManager.OpenScene(scene);
   PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.IL2CPP);
   PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone,Il2CppCompilerConfiguration.Release);
   UnityEditor.OSXStandalone.UserBuildSettings.architecture=OSArchitecture.ARM64;
   PlayerSettings.productName=product;
   PlayerSettings.defaultScreenWidth=720;PlayerSettings.defaultScreenHeight=1280;
   PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.macRetinaSupport=false;
   var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},target=BuildTarget.StandaloneOSX,locationPathName=output,options=BuildOptions.None});
   if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Mac build failed: "+report.summary.result);
   Debug.Log("RMLUNITY_MAC_BUILD_PASS "+output);
  }
 }
}
