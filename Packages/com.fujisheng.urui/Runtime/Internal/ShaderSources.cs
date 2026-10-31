#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor.PackageManager;

namespace URUI.Internal
{
    /// <summary>
    /// 将 Unity 虚拟 Packages 路径解析为真实文件，供 shader 源码核验使用。
    /// 同时支持嵌入包、本地路径包和 PackageCache 安装。
    /// </summary>
    internal static class ShaderSources
    {
        public static string ResolveAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "";
            }

            var package = PackageInfo.FindForAssetPath(assetPath);
            if (package == null)
            {
                return assetPath;
            }

            var prefix = "Packages/" + package.name + "/";
            if (!assetPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return "";
            }

            return Path.Combine(package.resolvedPath, assetPath.Substring(prefix.Length));
        }
    }
}
#endif
