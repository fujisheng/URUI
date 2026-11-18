using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Resources
{
    /// <summary>在所属文档的 CSS 注册表中编译的作者片段。</summary>
    [Serializable]
    public sealed class CompiledTemplate
    {
        static readonly Unity.Profiling.ProfilerMarker beginMarker = new("URUI.CompiledTemplate.Instantiate");
        [SerializeField] string sourceUrl, parentFingerprint;
        [SerializeField] byte[] data;
        [SerializeField] CompiledNodeIndex nodeIndex;
        public string SourceUrl => sourceUrl;
        internal string ParentFingerprint => parentFingerprint;
        internal CompiledNodeIndex NodeIndex => nodeIndex;
        internal byte[] EncodedData => data;
        internal void Initialize(string uri, byte[] encoded, string ownerFingerprint, CompiledNodeIndex index)
        { sourceUrl = uri; data = encoded; parentFingerprint = ownerFingerprint; nodeIndex = index; }
        internal unsafe int Begin(ulong document, ulong root)
        {
            using var sample = beginMarker.Auto();
            if (data == null || data.Length == 0) throw new InvalidOperationException("编译片段为空：" + sourceUrl);
            fixed (byte* bytes = data)
                return NativeMethods.urui_element_begin_compiled_template(document, root, (IntPtr)bytes, data.Length);
        }
    }
}
