using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using URUI.Internal;
namespace URUI.Editor {
 internal sealed class ShaderContractBuildGuard:IProcessSceneWithReport {
  public int callbackOrder=>0;
  public static void Validate(ShaderPropertyContract contract){
   if(!contract)return;
   var path=AssetDatabase.GetAssetPath(contract.VerifiedShader);
   using var sha=SHA256.Create();string hash=File.Exists(ShaderSources.ResolveAssetPath(path))?BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(ShaderSources.ResolveAssetPath(path)))).Replace("-","").ToLowerInvariant():"";
   if(path!=ShaderPropertyContract.BuiltinPath||hash!=ShaderPropertyContract.BuiltinHash||contract.VerifiedSourceHash!=hash)throw new BuildFailedException("Rml shader dependency contract changed: re-audit or remove the contract; unknown shaders require the legacy property path.");
  }
  public void OnProcessScene(Scene scene,BuildReport report){foreach(var root in scene.GetRootGameObjects())foreach(var host in root.GetComponentsInChildren<RenderHost>(true)){Validate(host.propertyContract);if(host.parameterCapability&&!host.parameterCapability.AuditEditor())throw new BuildFailedException("Parameter shader capability requires re-audit");}}
  public static ShaderPropertyContract Prepare(){
   System.IO.Directory.CreateDirectory("Assets/URUIGenerated");AssetDatabase.Refresh();
   const string path="Assets/URUIGenerated/BuiltinShaderPropertyContract.asset";
   var contract=AssetDatabase.LoadAssetAtPath<ShaderPropertyContract>(path);
   if(!contract){contract=UnityEngine.ScriptableObject.CreateInstance<ShaderPropertyContract>();contract.BindBuiltinForEditor(AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(ShaderPropertyContract.BuiltinPath));AssetDatabase.CreateAsset(contract,path);AssetDatabase.SaveAssets();}
   Validate(contract);return contract;
  }
 }
}
