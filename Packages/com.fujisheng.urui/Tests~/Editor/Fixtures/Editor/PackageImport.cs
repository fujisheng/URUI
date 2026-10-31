#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using RmlUnity;
using RmlUnity.Editor;

public static class PackageImport
{
 const string package="Packages/com.fujisheng.urui";
 static void Need(bool ok,string reason){if(!ok)throw new Exception(reason);}
 public static void Check(){
  var info=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(package+"/package.json");Need(info!=null&&info.name=="com.fujisheng.urui","UPM resolution");
  var runtime=CompilationPipeline.GetAssemblies().Single(x=>x.name=="RmlUnity.Runtime");Need(!runtime.assemblyReferences.Any(x=>x.name=="Unity.ugui"),"runtime uGUI dependency");
  var types=AppDomain.CurrentDomain.GetAssemblies().Single(x=>x.GetName().Name=="RmlUnity.Runtime").GetTypes();Need(!types.Any(x=>x.Name.StartsWith("Match3")||x.Name=="RmlHostCpuProbe"||x.Name=="MacThreadCpuClock"),"game/probe leaked into runtime");
  var shader=AssetDatabase.LoadAssetAtPath<Shader>(RmlShaderPropertyContract.BuiltinPath);Need(shader&&File.Exists(RmlShaderSources.ResolveAssetPath(RmlShaderPropertyContract.BuiltinPath)),"virtual shader source resolution");
  var contract=ScriptableObject.CreateInstance<RmlShaderPropertyContract>();contract.BindBuiltinForEditor(shader);RmlShaderContractBuildGuard.Validate(contract);
  var capability=ScriptableObject.CreateInstance<RmlParameterShaderCapability>();capability.BindEditor(AssetDatabase.LoadAssetAtPath<Shader>(RmlParameterShaderCapability.SourcePath),shader);Need(capability.AuditEditor(),"parameter shader audit");
  var plugin=(PluginImporter)AssetImporter.GetAtPath(package+"/Runtime/Plugins/macOS/libRmlUnityBridge.dylib");Need(plugin!=null&&!plugin.GetCompatibleWithAnyPlatform()&&plugin.GetCompatibleWithEditor()&&plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX),"plugin scope");Need(plugin.GetEditorData("CPU")=="ARM64"&&plugin.GetEditorData("OS")=="OSX"&&plugin.GetPlatformData(BuildTarget.StandaloneOSX,"CPU")=="ARM64","plugin architecture");Need(!plugin.GetCompatibleWithPlatform(BuildTarget.Android)&&!plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64),"unverified platform enabled");
  Need(Resources.Load<Shader>("RmlUnity/RmlUnityPremultiplied"),"default shader Resources");
  Directory.CreateDirectory("Evidence");File.WriteAllText("Evidence/import.json","{\"passed\":true,\"packageSource\":\""+info.source+"\",\"packageResolvedPath\":\""+info.resolvedPath.Replace("\\","/")+"\",\"runtimeNoUguiOrGameOrProbe\":true,\"shaderContracts\":true,\"pluginMacArm64Only\":true}");
  UnityEngine.Object.DestroyImmediate(contract);UnityEngine.Object.DestroyImmediate(capability);Debug.Log("UPM_IMPORT_ASSERTIONS_PASS "+info.source);
 }
}
#endif
