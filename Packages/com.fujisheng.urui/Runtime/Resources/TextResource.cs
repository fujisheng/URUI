using System;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>外置 CSS 和字体等文件的规范 URL；使用 TextAsset.bytes 保留原始内容。</summary>
    [Serializable]
    public struct TextResource
    {
        [SerializeField] string source;
        [SerializeField] TextAsset asset;
        public string Source => source;
        public TextAsset Asset => asset;
        public TextResource(string source, TextAsset asset)
        {
            this.source = source;
            this.asset = asset;
        }
    }
}
