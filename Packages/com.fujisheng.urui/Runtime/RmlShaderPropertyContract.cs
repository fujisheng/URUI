using UnityEngine;
namespace RmlUnity {
 // Explicit opt-in, bound to the audited framework asset, never a shader-name guess.
 // V1: pass 0/all variants read MainTex+ClipEnabled+ClipRect, not RmlTranslation.
 public sealed class RmlShaderPropertyContract:ScriptableObject {
  public const string BuiltinPath="Packages/com.fujisheng.urui/Runtime/Resources/RmlUnity/RmlUnityPremultiplied.shader";
  public const string BuiltinHash="4513fd27e88ad5bbf9c34371e39049e3efea2e12215d806f2f3907237be2230a";
  [SerializeField] Shader verifiedShader;
  [SerializeField] string verifiedSourceHash;
  [SerializeField] int revision;
  public Shader VerifiedShader=>verifiedShader;
  public string VerifiedSourceHash=>verifiedSourceHash;
  public bool Supports(Material material){
   if(revision!=1||verifiedSourceHash!=BuiltinHash||!verifiedShader||!material||material.shader!=verifiedShader)return false;
   #if UNITY_EDITOR
   return EditorAuditMatches();
   #else
   return true; // The scene build guard verified this immutable Player shader asset.
   #endif
  }
  #if UNITY_EDITOR
  static int editorAuditGeneration;
  [System.NonSerialized] int auditedGeneration=-1;
  [System.NonSerialized] Shader auditedShader;
  [System.NonSerialized] bool auditedResult;
  public static void InvalidateEditorAudit(){editorAuditGeneration++;}
  bool EditorAuditMatches(){
   if(auditedGeneration==editorAuditGeneration&&auditedShader==verifiedShader)return auditedResult;
   auditedGeneration=editorAuditGeneration;auditedShader=verifiedShader;
   string path=UnityEditor.AssetDatabase.GetAssetPath(verifiedShader);
   if(path!=BuiltinPath||!System.IO.File.Exists(RmlShaderSources.ResolveAssetPath(path)))return auditedResult=false;
   using var hash=System.Security.Cryptography.SHA256.Create();string actual=System.BitConverter.ToString(hash.ComputeHash(System.IO.File.ReadAllBytes(RmlShaderSources.ResolveAssetPath(path)))).Replace("-","").ToLowerInvariant();
   return auditedResult=actual==BuiltinHash;
  }
  public void BindBuiltinForEditor(Shader source){
   if(UnityEditor.AssetDatabase.GetAssetPath(source)!=BuiltinPath)throw new System.ArgumentException("Only the audited framework shader is supported by contract V1");
   verifiedShader=source;verifiedSourceHash=BuiltinHash;revision=1;
  }
  #endif
 }
}
