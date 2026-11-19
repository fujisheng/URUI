using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace URUI.Editor.Authoring
{
    /// <summary>
    /// 移动作者文本资产，并按实际 localID 修复 Unity 序列化引用；失败恢复原始文件。
    /// </summary>
    public static class TextAssetRelocation
    {
        static readonly HashSet<string> SerializedExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".asset", ".prefab", ".unity", ".mat", ".anim", ".controller", ".overrideController", ".mask", ".playable"
        };
        sealed class ReferenceFile
        {
            internal string AssetPath;
            internal string Path;
            internal string Text;
            internal byte[] Original;
            internal bool Written;
        }
        static string PhysicalPath(string path)
        {
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path) ?? throw new InvalidOperationException("无法解析包资源路径：" + path);
                return System.IO.Path.Combine(package.resolvedPath, path.Substring(("Packages/" + package.name).Length).TrimStart('/'));
            }
            return System.IO.Path.GetFullPath(path);
        }
        static void Import(string path) => AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        /// <summary>
        /// from/to 使用 AssetDatabase 路径；返回真正改写了序列化引用的资源路径。
        /// </summary>
        public static IReadOnlyList<string> Move(string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                throw new ArgumentException("需要原始和目标资源路径。");
            }
            var original = AssetDatabase.LoadAssetAtPath<TextAsset>(from);
            if (!original || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long previousId))
            {
                throw new ArgumentException("源资源必须是已导入的 TextAsset。", nameof(from));
            }
            if (from == to || AssetDatabase.LoadMainAssetAtPath(to))
            {
                throw new ArgumentException("目标路径必须尚未占用。", nameof(to));
            }
            // Unity 的对象引用是独立字段/序列项；字符串里的相同外观不能改写。
            var pattern = new Regex(@"(?<prefix>^[\t ]*(?:(?:-[\t ]+)?[^:\s'""{}]+:[\t ]*|-[\t ]+)\{[\t ]*fileID:[\t ]*)" +
                Regex.Escape(previousId.ToString(CultureInfo.InvariantCulture)) +
                @"(?<suffix>[\t ]*,[\t ]*guid:[\t ]*" + guid + @"[\t ]*,[\t ]*type:[\t ]*\d+[\t ]*\}[\t ]*\r?$)", RegexOptions.Multiline);
            var files = new List<ReferenceFile>();
            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                var physical = PhysicalPath(assetPath);
                foreach (var file in new[] { physical, physical + ".meta" })
                {
                    if (!File.Exists(file) || (file == physical && !SerializedExtensions.Contains(System.IO.Path.GetExtension(file))))
                    {
                        continue;
                    }
                    var bytes = File.ReadAllBytes(file);
                    string text;
                    try
                    {
                        text = new UTF8Encoding(false, true).GetString(bytes);
                    }
                    catch (DecoderFallbackException)
                    {
                        if (Array.IndexOf(AssetDatabase.GetDependencies(assetPath, false), from) >= 0)
                        {
                            throw new InvalidOperationException("依赖仍采用二进制序列化，请先将此资源转换成文本：" + assetPath);
                        }
                        continue;
                    }
                    if (file == physical && !text.TrimStart('\uFEFF').StartsWith("%YAML", StringComparison.Ordinal))
                    {
                        if (assetPath != from && Array.IndexOf(AssetDatabase.GetDependencies(assetPath, false), from) >= 0)
                        {
                            throw new InvalidOperationException("依赖不是 Unity 文本序列化资源：" + assetPath);
                        }
                        continue;
                    }
                    if (!pattern.IsMatch(text))
                    {
                        continue;
                    }
                    if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0)
                    {
                        throw new InvalidOperationException("引用位于只读资源：" + file);
                    }
                    files.Add(new ReferenceFile
                    {
                        AssetPath = assetPath,
                        Path = file,
                        Text = text,
                        Original = bytes
                    });
                }
            }
            var oldMeta = File.ReadAllBytes(PhysicalPath(from) + ".meta");
            bool moved = false;
            try
            {
                var error = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error))
                {
                    throw new InvalidOperationException(error);
                }
                moved = true;
                Import(to);
                var target = AssetDatabase.LoadAssetAtPath<TextAsset>(to);
                if (!target || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string currentGuid, out long currentId) || currentGuid != guid)
                {
                    throw new InvalidOperationException("移动后 TextAsset 身份校验失败：" + to);
                }
                var changed = new HashSet<string>(StringComparer.Ordinal);
                if (currentId != previousId)
                {
                    foreach (var file in files)
                    {
                        var updated = pattern.Replace(file.Text, match => match.Groups["prefix"].Value + currentId.ToString(CultureInfo.InvariantCulture) + match.Groups["suffix"].Value);
                        File.WriteAllBytes(file.Path, Encoding.UTF8.GetBytes(updated));
                        file.Written = true;
                        changed.Add(file.AssetPath);
                    }
                }
                foreach (var path in changed)
                {
                    Import(path);
                }
                return new List<string>(changed).AsReadOnly();
            }
            catch (Exception failure)
            {
                try
                {
                    foreach (var file in files)
                    {
                        if (file.Written)
                        {
                            File.WriteAllBytes(file.Path, file.Original);
                        }
                    }
                    if (moved)
                    {
                        var error = AssetDatabase.MoveAsset(to, from);
                        if (!string.IsNullOrEmpty(error))
                        {
                            throw new InvalidOperationException(error);
                        }
                        File.WriteAllBytes(PhysicalPath(from) + ".meta", oldMeta);
                        Import(from);
                    }
                    foreach (var file in files)
                    {
                        if (file.Written)
                        {
                            Import(file.AssetPath);
                        }
                    }
                }
                catch (Exception recovery)
                {
                    throw new AggregateException("资源移动失败，恢复也未完成。", failure, recovery);
                }
                throw;
            }
        }
    }
}
