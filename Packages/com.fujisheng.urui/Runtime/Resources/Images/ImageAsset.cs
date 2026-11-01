using System;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>
    /// 将 RML 中的图片路径映射到调用者持有的可读 Texture2D。
    /// </summary>
    [Serializable]
    public struct ImageAsset
    {
        [SerializeField]
        string source;
        [SerializeField]
        Texture2D texture;

        /// <summary>
        /// RML/RCSS 使用的相对路径，例如 icons/coin.png。
        /// </summary>
        public string Source => source;

        /// <summary>
        /// 调用者持有的原图；URUI 不销毁它。
        /// </summary>
        /// <remarks>加载前需要开启 Read/Write，URUI 读取并复制像素供原生文档使用。</remarks>
        public Texture2D Texture => texture;

        /// <summary>
        /// 创建图片路径与 Unity 纹理的映射；有效性在文档加载时统一检查。
        /// </summary>
        /// <param name="source">RML/RCSS 引用的非空相对路径，同一文档内不可重复。</param>
        /// <param name="texture">调用者持有的可读纹理，URUI 不取得原资产所有权。</param>
        public ImageAsset(string source, Texture2D texture)
        {
            this.source = source;
            this.texture = texture;
        }
    }
}
