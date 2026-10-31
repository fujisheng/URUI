#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor.PackageManager;
namespace URUI.Internal {
 // Resolves Unity's virtual Packages path for embedded, local-file and cache installs.
 internal static class ShaderSources {
  public static string ResolveAssetPath(string assetPath) {
   if(string.IsNullOrEmpty(assetPath)) return "";
   var package=PackageInfo.FindForAssetPath(assetPath);
   if(package==null) return assetPath;
   var prefix="Packages/"+package.name+"/";
   if(!assetPath.StartsWith(prefix,StringComparison.Ordinal)) return "";
   return Path.Combine(package.resolvedPath,assetPath.Substring(prefix.Length));
  }
 }
}
#endif
