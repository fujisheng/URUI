using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Editor.Authoring;

namespace URUI.Authoring.Animation
{
    internal sealed class AnimationSourceSet
    {
        internal sealed class Sheet
        {
            internal string Path, Text;
        }
        internal readonly List<Sheet> Sheets = new List<Sheet>();
        internal readonly SourceDocument Html;
        internal AnimationSourceSet(AuthoringSession author, IEnumerable<SourceChange> changes = null)
        {
            var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var change in changes ?? Array.Empty<SourceChange>())
                candidates[author.Workspace.CheckPath(change.Path)] = change.Text;
            string Text(string path)
            {
                if (candidates.TryGetValue(path, out var text))
                    return text;
                var file = author.Workspace.Files.FirstOrDefault(f => f.Path == path);
                return file != null ? file.Text : File.Exists(path) ? File.ReadAllText(path) : null;
            }
            Html = SourceDocument.Parse(Text(author.File.Path));
            foreach (var node in Html.Nodes)
                if (node.Tag == "style")
                    Sheets.Add(new Sheet { Text = Html.Text.Substring(node.OpenEnd, node.CloseStart - node.OpenEnd) });
            var temporary = new List<TextAsset>();
            string documentUri = author.File.Path.Substring(author.Workspace.ProjectRoot.Length).Replace('\\', '/');
            try
            {
                var dependencies = HtmlResourceDependencies.Collect(documentUri, uri =>
                {
                    if (uri == "/urui/base.css")
                        return UnityEngine.Resources.Load<TextAsset>("URUI/base");
                    if (uri.StartsWith("/urui/Fonts/", StringComparison.Ordinal))
                        return UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + Path.GetFileNameWithoutExtension(uri));
                    foreach (var extension in AuthoringEnvironment.Extensions)
                    {
                        var asset = extension.ResolveTextResource(uri);
                        if (asset)
                            return asset;
                    }
                    if (!uri.EndsWith(".css", StringComparison.OrdinalIgnoreCase) && !uri.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        return AssetDatabase.LoadAssetAtPath<TextAsset>(uri.TrimStart('/'));
                    string path = author.Workspace.CheckPath(uri.TrimStart('/'));
                    string text = Text(path);
                    if (text == null)
                        return AssetDatabase.LoadAssetAtPath<TextAsset>(uri.TrimStart('/'));
                    var resource = new TextAsset(text) { hideFlags = HideFlags.HideAndDontSave };
                    temporary.Add(resource);
                    return resource;
                });
                foreach (var resource in dependencies.TextFiles)
                {
                    if (resource.Source.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        var fragment = SourceDocument.Parse(resource.Asset.text);
                        foreach (var node in fragment.Nodes)
                            if (node.Tag == "style")
                                Sheets.Add(new Sheet { Text = fragment.Text.Substring(node.OpenEnd, node.CloseStart - node.OpenEnd) });
                    }
                    if (resource.Source.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = null;
                        if (resource.Source.StartsWith("/Assets/", StringComparison.Ordinal) || resource.Source.StartsWith("/Packages/", StringComparison.Ordinal))
                            path = author.Workspace.CheckPath(resource.Source.TrimStart('/'));
                        Sheets.Add(new Sheet { Path = path, Text = resource.Asset.text });
                    }
                }
            }
            finally { foreach (var asset in temporary) UnityEngine.Object.DestroyImmediate(asset); }
        }
        internal Sheet Find(string name)
        {
            Sheet found = null;
            foreach (var sheet in Sheets)
            {
                if (CssKeyframes.Read(sheet.Text, name).Count == 0)
                    continue;
                if (found != null)
                    throw new InvalidOperationException("当前页面的动画定义不唯一：" + name);
                found = sheet;
            }
            return found ?? throw new InvalidOperationException("当前页面没有加载动画：" + name);
        }
    }
}
