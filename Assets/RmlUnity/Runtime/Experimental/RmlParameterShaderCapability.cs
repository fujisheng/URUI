using System;using UnityEngine;
namespace RmlUnity {
 public sealed class RmlParameterShaderCapability:ScriptableObject {
  public const string SourcePath="Assets/RmlUnity/Runtime/Shaders/RmlParameterTable.shader",SourceHash="3217a73e8ecd726a4d549982c96f04d933fe01666815c97023fe00332f08981b";
  [SerializeField] Shader parameter,legacy;[SerializeField] string audit;[SerializeField] int revision;
  public Shader Parameter=>parameter;
  public bool Supports(Material m,RmlShaderPropertyContract old){if(revision!=1||audit!=SourceHash||!parameter||!legacy||!m||m.shader!=legacy||!old||!old.Supports(m))return false;
   #if UNITY_EDITOR
   return AuditEditor();
   #else
   return true;
   #endif
  }
  #if UNITY_EDITOR
  public bool AuditEditor(){var path=UnityEditor.AssetDatabase.GetAssetPath(parameter);if(path!=SourcePath||!System.IO.File.Exists(path)||UnityEditor.AssetDatabase.GetAssetPath(legacy)!=RmlShaderPropertyContract.BuiltinPath)return false;using var sha=System.Security.Cryptography.SHA256.Create();return revision==1&&audit==SourceHash&&BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant()==SourceHash;}
  public void BindEditor(Shader p,Shader l){parameter=p;legacy=l;audit=SourceHash;revision=1;if(!AuditEditor())throw new ArgumentException("Parameter shader capability requires audited framework sources");}
  #endif
 }
}
