using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Editor.Authoring;
using URUI.Editor.Inspection;
using URUI.Resources;

namespace URUI.Authoring.Preview
{
    /// <summary>作者文档的生产渲染预览，资源从当前工作副本与 Unity 资产读取。</summary>
    public sealed class PreviewSession : IDisposable
    {
        readonly List<TextAsset> textAssets = new List<TextAsset>();
        readonly List<IDisposable> extensions = new List<IDisposable>();
        readonly AuthoringSession author;
        readonly string projectRoot;
        EditorPreviewHost host;
        CompiledDocumentAsset compiledPreview;
        TextAsset compiledMarkup;
        SourceMap map;
        string renderedPath, renderedHtml;
        readonly Dictionary<string, string> renderedDependencies = new Dictionary<string, string>();
        readonly Dictionary<string, string> sourcePaths = new Dictionary<string, string>();
        public event Action BeforeReload;
        public int Width { get; private set; } = 1280;
        public int Height { get; private set; } = 720;
        public long RenderedRevision { get; private set; } = -1;
        public string Error { get; private set; }
        public bool IsReady => Error == null && author.File != null && renderedPath == author.File.Path && RenderedRevision == author.Revision && Document != null && Document.IsLoaded;
        public long FrameId => host?.FrameId ?? 0;
        public RenderTexture Target => host?.Target;
        public URUI.Documents.Document Document => host?.Document;
        public bool Playing
        {
            get; set;
        }
        public PreviewSession(AuthoringSession session, string projectRoot)
        {
            author = session;
            this.projectRoot = projectRoot;
        }
        public void Resize(int width, int height)
        {
            if (width < 64 || height < 64 || width > 8192 || height > 8192)
                throw new InvalidOperationException("Viewport must be between 64 and 8192.");
            if (width == Width && height == Height)
                return;
            DisposeHost();
            Width = width;
            Height = height;
            Refresh();
        }
        public void Refresh()
        {
            try { RefreshCore(); Error = null; }
            catch (Exception error) { Error = error.Message; throw; }
        }
        void RefreshCore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Authoring preview yields to Play Mode.");
            if (author.File == null)
                return;
            if (host == null)
            {
                host = RuntimeInspectionBridge.CreatePreview(Width, Height);
                try
                {
                    foreach (var extension in AuthoringEnvironment.Extensions)
                        extensions.Add(extension.AttachPreview(host.Document, LoadAsset));
                }
                catch { DisposeHost(); throw; }
            }
            if (RenderedRevision == author.Revision && renderedPath == author.File.Path)
            {
                host.Render();
                return;
            }
            bool cssUnchanged = true;
            foreach (var pair in renderedDependencies)
                if (author.Workspace.Open(pair.Key).Text != pair.Value)
                {
                    cssUnchanged = false;
                    break;
                }
            if (renderedPath == author.File.Path && cssUnchanged)
            {
                if (renderedHtml == author.File.Text)
                {
                    RenderedRevision = author.Revision;
                    host.Render();
                    return;
                }
                if (RenderedRevision == author.PropertyEditsPreviousRevision && author.PropertyEditsRevision == author.Revision && author.LastPropertyEdits != null)
                {
                    bool patchable = true;
                    foreach (var edit in author.LastPropertyEdits)
                        if (!(edit.Property.StartsWith("style.", StringComparison.Ordinal) || edit.Property == "text" || edit.Property == "value" || edit.Property == "attr.id" || edit.Property == "attr.class" || edit.Property == "attr.title") || map?.Node(edit.Node) == null)
                        {
                            patchable = false;
                            break;
                        }
                    if (patchable)
                    {
                        BeforeReload?.Invoke();
                        foreach (var edit in author.LastPropertyEdits)
                            map.Node(edit.Node).Write(edit.Property, edit.Value);
                        host.Render();
                        RenderedRevision = author.Revision;
                        renderedHtml = author.File.Text;
                        return;
                    }
                }
            }
            BeforeReload?.Invoke();
            var previous = textAssets.ToArray();
            textAssets.Clear();
            CompiledDocumentAsset candidate = null;
            TextAsset candidateMarkup = null;
            try
            {
                string uri = "/" + Relative(author.File.Path);
                sourcePaths.Clear();
                var dependencies = HtmlResourceDependencies.Collect(uri, LoadText);
                var resources = new TextResource[dependencies.TextFiles.Count];
                for (int i = 0; i < resources.Length; i++)
                    resources[i] = dependencies.TextFiles[i];
                host.Document.TextResources = resources;
                host.Document.ImageResolver = source =>
                {
                    string assetPath = AssetPath(source);
                    if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) && !assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                        return null;
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    return texture ? new ImageLease(texture) : null;
                };
                candidateMarkup = new TextAsset(author.File.Text) { hideFlags = HideFlags.HideAndDontSave };
                candidate = URUI.Editor.Compilation.CompiledDocumentCompiler.Compile(candidateMarkup, uri, resources);
                candidate.hideFlags = HideFlags.HideAndDontSave;
                host.Document.Load(candidate);
                if (compiledPreview) UnityEngine.Object.DestroyImmediate(compiledPreview);
                if (compiledMarkup) UnityEngine.Object.DestroyImmediate(compiledMarkup);
                compiledPreview = candidate; compiledMarkup = candidateMarkup;
                candidate = null; candidateMarkup = null;
                map = new SourceMap(host.Document, author.Model);
                foreach (var extension in AuthoringEnvironment.Extensions)
                    extension.AdvancePreview(host.Document, 0);
                host.Render();
                RenderedRevision = author.Revision;
                renderedPath = author.File.Path;
                renderedHtml = author.File.Text;
                renderedDependencies.Clear();
                foreach (var resource in dependencies.TextFiles)
                    if (sourcePaths.TryGetValue(resource.Source, out string path))
                    {
                        var file = author.Workspace.Open(path);
                        renderedDependencies[file.Path] = file.Text;
                    }
                foreach (var asset in previous)
                    if (asset)
                        UnityEngine.Object.DestroyImmediate(asset);
            }
            catch
            {
                if (candidate) UnityEngine.Object.DestroyImmediate(candidate);
                if (candidateMarkup) UnityEngine.Object.DestroyImmediate(candidateMarkup);
                foreach (var asset in textAssets)
                    if (asset)
                        UnityEngine.Object.DestroyImmediate(asset);
                textAssets.Clear();
                textAssets.AddRange(previous);
                throw;
            }
        }
        TextAsset LoadText(string uri)
        {
            foreach (var provider in AuthoringEnvironment.Extensions)
            {
                var resource = provider.ResolveTextResource(uri);
                if (resource)
                    return resource;
            }
            string assetPath = AssetPath(uri);
            TextAsset resourceAsset = null;
            if (uri.StartsWith("/urui/", StringComparison.OrdinalIgnoreCase))
            {
                string key = "URUI/" + Path.ChangeExtension(Uri.UnescapeDataString(uri.Substring(6)), null);
                resourceAsset = UnityEngine.Resources.Load<TextAsset>(key);
                if (!resourceAsset)
                    throw new InvalidOperationException("缺少作者依赖：" + uri);
                assetPath = AssetDatabase.GetAssetPath(resourceAsset);
                // 包内默认资源按原 URI 注册；项目 Resources 样式仍读取可撤销的工作副本。
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                    return resourceAsset;
            }
            string extension = Path.GetExtension(assetPath);
            if (extension != ".html" && extension != ".css")
                return resourceAsset ? resourceAsset : AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            var file = author.Workspace.Open(assetPath);
            sourcePaths[uri] = file.Path;
            var asset = new TextAsset(file.Text) { hideFlags = HideFlags.HideAndDontSave };
            textAssets.Add(asset);
            return asset;
        }
        string Relative(string path)
        {
            string prefix = projectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Source is outside project.");
            return path.Substring(prefix.Length).Replace('\\', '/');
        }
        static string AssetPath(string uri) => Uri.UnescapeDataString(uri.TrimStart('/'));
        public InspectedNode Node(string sourceKey) => IsReady ? map?.Node(sourceKey) : null;
        public string Key(InspectedNode node) => IsReady ? map?.Key(node) : null;
        UnityEngine.Object LoadAsset(string source, Type type)
        {
            string path = AssetPath(RuntimeInspectionBridge.ResolveResourceUrl("/" + Relative(author.File.Path), source));
            return AssetDatabase.LoadAssetAtPath(path, type);
        }
        public void Tick(float delta)
        {
            if (IsReady && Playing)
            {
                host.Advance(delta);
                foreach (var extension in AuthoringEnvironment.Extensions)
                    extension.AdvancePreview(host.Document, delta);
                host.Render();
            }
        }
        public string Capture(string path)
        {
            Refresh();
            using (var pixels = host.Capture())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pixels.Texture.EncodeToPNG());
            }
            return path;
        }
        void DisposeHost()
        {
            BeforeReload?.Invoke();
            foreach (var extension in extensions)
                extension?.Dispose();
            extensions.Clear();
            host?.Dispose();
            host = null;
            if (compiledPreview) UnityEngine.Object.DestroyImmediate(compiledPreview);
            if (compiledMarkup) UnityEngine.Object.DestroyImmediate(compiledMarkup);
            compiledPreview = null; compiledMarkup = null;
            map = null;
            RenderedRevision = -1;
            renderedPath = null;
            renderedHtml = null;
            renderedDependencies.Clear();
            foreach (var asset in textAssets)
                if (asset)
                    UnityEngine.Object.DestroyImmediate(asset);
            textAssets.Clear();
        }
        public void Dispose() => DisposeHost();
    }
}
