using System;
using System.IO;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Preview
{
    /// <summary>
    /// Maps Unity assets onto the same author resource mounts used by compiled pages.
    /// </summary>
    internal static class AuthoringAssetPaths
    {
        const string ResourcesRoot = "Assets/Resources/URUI/";
        const string CoreRoot = "Packages/com.fujisheng.urui/Runtime/Resources/URUI/";
        const string EffectRoot = "Packages/com.fujisheng.urui.effect/Runtime/Resources/URUI.Effect/";

        internal static string DocumentUri(string root, string path) => AssetUri(Relative(root, path));

        internal static string AssetUri(string path)
        {
            if (path.StartsWith(ResourcesRoot, StringComparison.Ordinal))
            {
                return "/URUI/" + path.Substring(ResourcesRoot.Length);
            }
            if (path.StartsWith(CoreRoot, StringComparison.Ordinal))
            {
                return "/urui/" + path.Substring(CoreRoot.Length);
            }
            if (path.StartsWith(EffectRoot, StringComparison.Ordinal))
            {
                return "/urui-effect/" + path.Substring(EffectRoot.Length);
            }
            return "/" + path;
        }

        internal static string Reference(string root, string documentPath, string assetPath)
        {
            string source = DocumentUri(root, documentPath);
            string target = AssetUri(Relative(root, assetPath));
            // Keep a reference on its own mount when the source file belongs to a different mount.
            if (source.Split('/')[1] != target.Split('/')[1])
            {
                return target;
            }
            var from = new Uri("https://urui.invalid" + EscapePath(source));
            var to = new Uri("https://urui.invalid" + EscapePath(target));
            return from.MakeRelativeUri(to).ToString();
        }

        internal static string ResolveAsset(string root, string documentPath, string reference) =>
            AssetPath(RuntimeInspectionBridge.ResolveResourceUrl(DocumentUri(root, documentPath), reference));

        internal static string AssetPath(string uri)
        {
            if (string.IsNullOrEmpty(uri) || !uri.StartsWith("/", StringComparison.Ordinal) || uri.StartsWith("//", StringComparison.Ordinal))
            {
                return null;
            }
            string path = Uri.UnescapeDataString(uri);
            if (path.Contains('\\') || path.Contains('\0') || Array.Exists(path.Split('/'), part => part is "." or ".."))
            {
                return null;
            }
            if (path.StartsWith("/URUI/", StringComparison.Ordinal))
            {
                return ResourcesRoot + path.Substring(6);
            }
            if (path.StartsWith("/urui/", StringComparison.Ordinal))
            {
                return CoreRoot + path.Substring(6);
            }
            if (path.StartsWith("/urui-effect/", StringComparison.Ordinal))
            {
                return EffectRoot + path.Substring(13);
            }
            return path.StartsWith("/Assets/", StringComparison.Ordinal) || path.StartsWith("/Packages/", StringComparison.Ordinal) ? path.Substring(1) : null;
        }

        static string Relative(string root, string path)
        {
            string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Source is outside project.");
            }
            return full.Substring(prefix.Length).Replace('\\', '/');
        }

        static string EscapePath(string path) => string.Join("/", Array.ConvertAll(path.Split('/'), Uri.EscapeDataString));
    }
}
