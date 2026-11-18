using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using URUI.Resources;
using URUI.Editor.Authoring;
using URUI.Internal.Interop;
using UnityEditor;
using System.Globalization;

namespace URUI.Editor.Compilation
{
    /// <summary>Editor 导入与 Authoring 文本预览共用的原生编译入口。</summary>
    public static class CompiledDocumentCompiler
    {
        /// <summary>保存稳定 GUID 的编译资产；新增依赖按作者文件的相对路径发现。</summary>
        public static CompiledDocumentAsset CompileAndSave(TextAsset source, string sourceUrl, string destination,
            IReadOnlyList<TextResource> dependencies = null, bool fragment = false)
        {
            if (string.IsNullOrEmpty(destination) || !destination.StartsWith("Assets/", StringComparison.Ordinal) || !destination.EndsWith(".asset", StringComparison.Ordinal))
                throw new ArgumentException("编译资产必须保存在 Assets 下的 .asset 文件中。", nameof(destination));
            if (!source || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source)))
                throw new ArgumentException("持久化编译资产需要已导入的 HTML 源文件。", nameof(source));
            var uri = HtmlResourceDependencies.ResolveUrl("/", sourceUrl);
            var resources = Discover(source, uri, dependencies, fragment);
            var compiled = Compile(source, uri, resources, fragment);
            try
            {
                var existing = AssetDatabase.LoadAssetAtPath<CompiledDocumentAsset>(destination);
                if (existing) { EditorUtility.CopySerialized(compiled, existing); EditorUtility.SetDirty(existing); }
                else
                {
                    EnsureFolder(Path.GetDirectoryName(destination).Replace('\\', '/'));
                    AssetDatabase.CreateAsset(compiled, destination); existing = compiled; compiled = null;
                }
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }
            finally { if (compiled) UnityEngine.Object.DestroyImmediate(compiled); }
        }

        /// <summary>按已记录的作者资源重编译，不改变资产引用。</summary>
        public static void Recompile(CompiledDocumentAsset asset)
        {
            if (!asset) throw new ArgumentNullException(nameof(asset));
            CompileAndSave(asset.AuthorSource, asset.SourceUrl, AssetDatabase.GetAssetPath(asset), asset.Dependencies, asset.IsFragment);
        }

        /// <summary>构建前核对当前源码、完整依赖图、编译器和编译字节摘要。</summary>
        public static void ValidateCurrent(CompiledDocumentAsset asset)
        {
            if (!asset) throw new InvalidOperationException("缺少 URUI 编译资产。");
            asset.Validate(false);
            if (!asset.AuthorSource) throw new InvalidOperationException("编译资产的 HTML 源文件已丢失：" + asset.SourceUrl);
            var resources = Discover(asset.AuthorSource, asset.SourceUrl, asset.Dependencies, asset.IsFragment);
            if (asset.Fingerprint != Fingerprint(asset.AuthorSource, asset.SourceUrl, resources, asset.IsFragment) ||
                asset.EncodedHash != CompiledDocumentAsset.HashBytes(asset.EncodedData))
                throw new InvalidOperationException("URUI 编译资产已过期，请重新导入源码：" + asset.SourceUrl);
        }

        static TextResource[] Discover(TextAsset source, string uri, IReadOnlyList<TextResource> declared, bool fragment)
        {
            var known = new Dictionary<string, TextAsset>(StringComparer.Ordinal) { [uri] = source };
            foreach (var resource in Normalize(declared)) known.Add(resource.Source, resource.Asset);
            TextAsset Resolve(string requested)
            {
                if (known.TryGetValue(requested, out var found)) return found;
                var sourcePath = AssetDatabase.GetAssetPath(source);
                var relative = Path.GetRelativePath(Path.GetDirectoryName(uri), requested);
                var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath), Uri.UnescapeDataString(relative)));
                var path = Path.GetRelativePath(Path.GetFullPath("."), full).Replace('\\', '/');
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal))
                    throw new InvalidOperationException("作者依赖越出项目资源目录：" + requested);
                return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            }
            var graph = fragment ? HtmlResourceDependencies.CollectFragment(uri, Resolve) : HtmlResourceDependencies.Collect(uri, Resolve);
            var resources = new TextResource[graph.TextFiles.Count];
            for (var i = 0; i < resources.Length; i++) resources[i] = graph.TextFiles[i];
            return Normalize(resources);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [Serializable] sealed class NativeBuild { public string sourceSha256; }
        static string NativeSourceHash()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CompiledDocumentCompiler).Assembly);
            var path = Path.Combine(package.resolvedPath, "Documentation~/PLUGINS.json");
            var hash = JsonUtility.FromJson<NativeBuild>(File.ReadAllText(path)).sourceSha256;
            if (string.IsNullOrEmpty(hash)) throw new InvalidOperationException("URUI 原生编译清单缺少源码摘要。");
            return hash;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Resource
        {
            public IntPtr Uri, Bytes;
            public int Length;
        }
        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_compile_document([MarshalAs(UnmanagedType.LPUTF8Str)] string html,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl, int fragment, [In] Resource[] resources, int resourceCount,
            [Out] byte[] output, int capacity, out int required);
        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_compile_template([In] byte[] parent, int parentLength, [MarshalAs(UnmanagedType.LPUTF8Str)] string html,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl, [Out] byte[] output, int capacity, out int required);
        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_compiled_document_metadata([In] byte[] data, int length, [Out] byte[] output, int capacity, out int required);
        [Serializable] sealed class NodePacket { public string identity; public int nodeCount; public Node[] nodes; }
        [Serializable] sealed class Node { public int id, end; public string name; public string[] classes; }

        /// <summary>编译当前文本快照；调用者负责保存或释放返回的资产。</summary>
        public static CompiledDocumentAsset Compile(TextAsset source, string sourceUrl, IReadOnlyList<TextResource> dependencies = null, bool fragment = false)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            var uri = HtmlResourceDependencies.ResolveUrl("/", sourceUrl);
            var resources = Normalize(dependencies);
            var input = new Resource[resources.Length];
            var pins = new GCHandle[resources.Length];
            var uris = new IntPtr[resources.Length];
            var text = new UTF8Encoding(false, true).GetString(source.bytes);
            if (text.IndexOf('\0') >= 0) throw new InvalidOperationException("HTML 不能包含 NUL：" + uri);
            try
            {
                for (var i = 0; i < resources.Length; i++)
                {
                    var bytes = resources[i].Asset.bytes;
                    pins[i] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    var name = Encoding.UTF8.GetBytes(resources[i].Source + '\0');
                    uris[i] = Marshal.AllocHGlobal(name.Length);
                    Marshal.Copy(name, 0, uris[i], name.Length);
                    input[i] = new Resource { Uri = uris[i], Bytes = pins[i].AddrOfPinnedObject(), Length = bytes.Length };
                }
                var encoded = new byte[65536];
                var status = urui_compile_document(text, uri, fragment ? 1 : 0, input, input.Length, encoded, encoded.Length, out var required);
                if (status == -3 && required > encoded.Length)
                {
                    encoded = new byte[required];
                    status = urui_compile_document(text, uri, fragment ? 1 : 0, input, input.Length, encoded, encoded.Length, out required);
                }
                if (status != 1) throw new InvalidOperationException("URUI 编译失败：" + uri + "：" + NativeMethods.LastError() + " (" + status + ")");
                Array.Resize(ref encoded, required);
                var asset = ScriptableObject.CreateInstance<CompiledDocumentAsset>();
                try
                {
                    asset.name = source.name;
                    var fingerprint = Fingerprint(source, uri, resources, fragment);
                    asset.Initialize(uri, encoded, fingerprint, resources, fragment, source);
                    var templates = new List<CompiledTemplate>();
                    if (!fragment)
                        foreach (var resource in resources)
                            if (resource.Source != uri && resource.Source.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                                templates.Add(CompileTemplate(encoded, fingerprint, resource));
                    asset.SetBlueprints(ReadNodeIndex(encoded), templates.ToArray());
                    return asset;
                }
                catch { UnityEngine.Object.DestroyImmediate(asset); throw; }
            }
            finally
            {
                for (var i = 0; i < resources.Length; i++)
                {
                    if (pins[i].IsAllocated) pins[i].Free();
                    if (uris[i] != IntPtr.Zero) Marshal.FreeHGlobal(uris[i]);
                }
            }
        }

        static CompiledTemplate CompileTemplate(byte[] parent, string fingerprint, TextResource source)
        {
            var html = new UTF8Encoding(false, true).GetString(source.Asset.bytes);
            if (html.IndexOf('\0') >= 0) throw new InvalidOperationException("HTML 片段不能包含 NUL：" + source.Source);
            var encoded = new byte[65536];
            var status = urui_compile_template(parent, parent.Length, html, source.Source, encoded, encoded.Length, out var required);
            if (status == -3 && required > encoded.Length)
            {
                encoded = new byte[required];
                status = urui_compile_template(parent, parent.Length, html, source.Source, encoded, encoded.Length, out required);
            }
            if (status != 1) throw new InvalidOperationException("URUI 片段编译失败：" + source.Source + "：" + NativeMethods.LastError());
            Array.Resize(ref encoded, required);
            var template = new CompiledTemplate();
            template.Initialize(source.Source, encoded, fingerprint, ReadNodeIndex(encoded));
            return template;
        }

        static CompiledNodeIndex ReadNodeIndex(byte[] encoded)
        {
            var bytes = new byte[65536];
            var status = urui_compiled_document_metadata(encoded, encoded.Length, bytes, bytes.Length, out var required);
            if (status == -3 && required > bytes.Length)
            {
                bytes = new byte[required];
                status = urui_compiled_document_metadata(encoded, encoded.Length, bytes, bytes.Length, out required);
            }
            if (status != 1) throw new InvalidOperationException("URUI 编译节点索引读取失败：" + NativeMethods.LastError());
            var packet = JsonUtility.FromJson<NodePacket>(Encoding.UTF8.GetString(bytes, 0, required - 1));
            if (packet.nodeCount < 0 || packet.nodeCount > 1000000) throw new InvalidOperationException("编译节点数量无效。");
            var ends = new int[packet.nodeCount];
            var selectors = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
            void Add(string key, int node)
            {
                if (!selectors.TryGetValue(key, out var matches)) selectors.Add(key, matches = new List<int>());
                if (matches.Count == 0 || matches[matches.Count - 1] != node) matches.Add(node);
            }
            foreach (var node in packet.nodes ?? Array.Empty<Node>())
            {
                if (node.id < 0 || node.id >= ends.Length || node.end <= node.id || node.end > ends.Length)
                    throw new InvalidOperationException("编译节点区间无效。");
                ends[node.id] = node.end;
                if (!string.IsNullOrEmpty(node.name)) Add("#" + node.name, node.id);
                foreach (var name in node.classes ?? Array.Empty<string>()) if (!string.IsNullOrEmpty(name)) Add("." + name, node.id);
            }
            var entries = new CompiledNodeIndex.Selector[selectors.Count]; var index = 0;
            foreach (var selector in selectors) entries[index++] = new CompiledNodeIndex.Selector { key = selector.Key, queryKey = selector.Key.Substring(1), nodes = selector.Value.ToArray() };
            var result = new CompiledNodeIndex();
            result.Initialize(ulong.Parse(packet.identity, CultureInfo.InvariantCulture), ends, entries);
            return result;
        }

        internal static string Fingerprint(TextAsset source, string uri, IReadOnlyList<TextResource> dependencies, bool fragment)
        {
            using var content = new MemoryStream();
            using (var writer = new BinaryWriter(content, Encoding.UTF8, true))
            {
                writer.Write(CompiledDocumentAsset.CompilerVersion); writer.Write(NativeSourceHash()); writer.Write(fragment); writer.Write(uri);
                WriteBytes(writer, source.bytes);
                foreach (var resource in dependencies) { writer.Write(resource.Source); WriteBytes(writer, resource.Asset.bytes); }
            }
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(content.GetBuffer(), 0, checked((int)content.Length))).Replace("-", "").ToLowerInvariant();
        }

        static void WriteBytes(BinaryWriter writer, byte[] bytes) { writer.Write(bytes.Length); writer.Write(bytes); }

        static TextResource[] Normalize(IReadOnlyList<TextResource> dependencies)
        {
            var resources = new TextResource[dependencies?.Count ?? 0];
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < resources.Length; i++)
            {
                var resource = dependencies[i];
                var uri = HtmlResourceDependencies.ResolveUrl("/", resource.Source);
                if (!resource.Asset || !used.Add(uri)) throw new InvalidOperationException("编译依赖缺失或 URI 重复：" + uri);
                resources[i] = new TextResource(uri, resource.Asset);
            }
            Array.Sort(resources, (left, right) => StringComparer.Ordinal.Compare(left.Source, right.Source));
            return resources;
        }
    }
}
