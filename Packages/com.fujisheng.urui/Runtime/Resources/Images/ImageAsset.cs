using System;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>
    /// 将 RML 中的图片路径映射到调用者持有的 Texture2D（无需 Read/Write）。
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
        /// <remarks>直接借用原 GPU 纹理，不读取/复制像素；使用期间调用者不得销毁、卸载或重建纹理。修改像素后正常 Apply 会直接更新显示。</remarks>
        public Texture2D Texture => texture;

        /// <summary>
        /// 创建图片路径与 Unity 纹理的映射；有效性在文档加载时统一检查。
        /// </summary>
        /// <param name="source">RML/RCSS 引用的非空相对路径，同一文档内不可重复。</param>
        /// <param name="texture">调用者持有的纹理；无需可读，URUI 不取得原资产所有权。</param>
        public ImageAsset(string source, Texture2D texture)
        {
            this.source = source;
            this.texture = texture;
        }
    }
}
