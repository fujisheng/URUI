using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Resources
{
    /// <summary>
    /// 从标准 HTML/CSS 编译的可移植文档；动态布局和交互仍由运行时计算。
    /// </summary>
    public sealed class CompiledDocumentAsset : ScriptableObject
    {
        public const int CompilerVersion = 2;
        [SerializeField]
        string sourceUrl;
        [SerializeField]
        string fingerprint;
        [SerializeField]
        string encodedHash;
        [SerializeField]
        int compilerVersion;
        [SerializeField]
        byte[] data = Array.Empty<byte>();
        [SerializeField]
        TextResource[] dependencies = Array.Empty<TextResource>();
        [SerializeField]
        bool fragment;
        [SerializeField]
        CompiledNodeIndex nodeIndex;
        [SerializeField]
        CompiledTemplate[] templates = Array.Empty<CompiledTemplate>();
#if UNITY_EDITOR
        [SerializeField]
        TextAsset authorSource;
        internal TextAsset AuthorSource => authorSource;
#endif
        /// <summary>
        /// 编译时的规范来源 URL，用于相对资源和诊断。
        /// </summary>
        public string SourceUrl => sourceUrl;
        /// <summary>
        /// 源码、依赖内容和编译器版本的摘要。
        /// </summary>
        public string Fingerprint => fingerprint;
        /// <summary>
        /// 该资产是否为页面内使用的片段。
        /// </summary>
        public bool IsFragment => fragment;
        /// <summary>
        /// 加载时需要注册的静态文件引用。
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<TextResource> Dependencies => dependencies;
        internal byte[] EncodedData => data;
        internal string EncodedHash => encodedHash;
        internal TextResource[] StaticResources => dependencies;
        internal void SetBlueprints(CompiledNodeIndex index, CompiledTemplate[] children)
        {
            nodeIndex = index;
            templates = children ?? Array.Empty<CompiledTemplate>();
        }

        /// <summary>
        /// 取得已在本页属性作用域中编译的片段；来源使用资源清单中的规范 URL。
        /// </summary>
        public CompiledTemplate GetTemplate(string uri)
        {
            foreach (var template in templates)
            {
                if (template.SourceUrl == uri)
                {
                    return template;
                }
            }
            throw new InvalidOperationException("页面缺少编译片段：" + uri);
        }

        internal CompiledNodeIndex FindBlueprint(ulong identity)
        {
            if (nodeIndex != null && nodeIndex.Identity == identity)
            {
                return nodeIndex;
            }
            foreach (var template in templates)
            {
                if (template.NodeIndex?.Identity == identity)
                {
                    return template.NodeIndex;
                }
            }
            return null;
        }

        internal void Initialize(string uri, byte[] encoded, string contentFingerprint, TextResource[] resources, bool isFragment, TextAsset source)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                throw new ArgumentException("编译资产需要来源 URL。", nameof(uri));
            }
            if (encoded == null || encoded.Length == 0)
            {
                throw new ArgumentException("编译资产不能为空。", nameof(encoded));
            }
            sourceUrl = uri;
            data = encoded;
            fingerprint = contentFingerprint;
            compilerVersion = CompilerVersion;
            encodedHash = HashBytes(encoded);
            dependencies = resources ?? Array.Empty<TextResource>();
            fragment = isFragment;
#if UNITY_EDITOR
            authorSource = source;
#endif
        }

        internal static string HashBytes(byte[] bytes)
        {
            using var hash = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal void Validate(bool requireDocument)
        {
            if (compilerVersion != CompilerVersion || data == null || data.Length == 0 || string.IsNullOrWhiteSpace(sourceUrl))
            {
                throw new InvalidOperationException("URUI 编译资产无效或已过期，请重新导入源码：" + name);
            }
            if (requireDocument && fragment)
            {
                throw new InvalidOperationException("HTML 片段不能作为独立页面加载：" + sourceUrl);
            }
        }

        internal unsafe int Load(ulong owner)
        {
            Validate(true);
            fixed (byte* bytes = data)
            {
                return NativeMethods.urui_document_load_compiled(owner, (IntPtr)bytes, data.Length);
            }
        }
    }
}
