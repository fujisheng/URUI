using System;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>外置 CSS 和字体等文件的规范 URL；同步借用 TextAsset 的原生只读内容，保留二进制文件字节。</summary>
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
