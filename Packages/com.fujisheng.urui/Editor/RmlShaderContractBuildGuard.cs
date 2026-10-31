using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
namespace RmlUnity.Editor {
 public sealed class RmlShaderContractBuildGuard:IProcessSceneWithReport {
  public int callbackOrder=>0;
  public static void Validate(RmlShaderPropertyContract contract){
   if(!contract)return;
   var path=AssetDatabase.GetAssetPath(contract.VerifiedShader);
   using var sha=SHA256.Create();string hash=File.Exists(RmlShaderSources.ResolveAssetPath(path))?BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(RmlShaderSources.ResolveAssetPath(path)))).Replace("-","").ToLowerInvariant():"";
   if(path!=RmlShaderPropertyContract.BuiltinPath||hash!=RmlShaderPropertyContract.BuiltinHash||contract.VerifiedSourceHash!=hash)throw new BuildFailedException("Rml shader dependency contract changed: re-audit or remove the contract; unknown shaders require the legacy property path.");
  }
  public void OnProcessScene(Scene scene,BuildReport report){foreach(var root in scene.GetRootGameObjects())foreach(var host in root.GetComponentsInChildren<RmlUnityHost>(true)){Validate(host.propertyContract);if(host.parameterCapability&&!host.parameterCapability.AuditEditor())throw new BuildFailedException("Parameter shader capability requires re-audit");}}
  public static RmlShaderPropertyContract Prepare(){
   System.IO.Directory.CreateDirectory("Assets/URUIGenerated");AssetDatabase.Refresh();
   const string path="Assets/URUIGenerated/BuiltinShaderPropertyContract.asset";
   var contract=AssetDatabase.LoadAssetAtPath<RmlShaderPropertyContract>(path);
   if(!contract){contract=UnityEngine.ScriptableObject.CreateInstance<RmlShaderPropertyContract>();contract.BindBuiltinForEditor(AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(RmlShaderPropertyContract.BuiltinPath));AssetDatabase.CreateAsset(contract,path);AssetDatabase.SaveAssets();}
   Validate(contract);return contract;
  }
 }
}
