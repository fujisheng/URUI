using System;
using UnityEngine;

namespace URUI
{
    /// <summary>将 RML 中的图片路径映射到调用者持有的可读 Texture2D。</summary>
    [Serializable]
    public struct ImageAsset
    {
        [SerializeField] string source;
        [SerializeField] Texture2D texture;

        /// <summary>RML/RCSS 使用的相对路径，例如 icons/coin.png。</summary>
        public string Source => source;

        /// <summary>调用者持有的原图；URUI 不销毁它。</summary>
        public Texture2D Texture => texture;

        public ImageAsset(string source, Texture2D texture)
        {
            this.source = source;
            this.texture = texture;
        }
    }
}
