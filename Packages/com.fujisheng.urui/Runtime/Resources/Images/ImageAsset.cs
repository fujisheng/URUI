using System;
using System.Collections.Generic;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>
    /// 将 HTML 中的图片路径映射到调用者持有的 Texture2D（无需 Read/Write）。
    /// </summary>
    [Serializable]
    public struct ImageAsset
    {
        [SerializeField]
        string source;
        [SerializeField]
        Texture2D texture;
        [SerializeField]
        SpriteRegion[] sprites;

        /// <summary>
        /// HTML/CSS 使用的相对路径，例如 icons/coin.png。
        /// </summary>
        public string Source => source;

        /// <summary>
        /// 调用者持有的原图；URUI 不销毁它。
        /// </summary>
        /// <remarks>
        /// 直接借用原 GPU 纹理，不读取/复制像素；使用期间调用者不得销毁、卸载或重建纹理。修改像素后正常 Apply 会直接更新显示。
        /// </remarks>
        public Texture2D Texture => texture;

        /// <summary>
        /// 创建图片路径与 Unity 纹理的映射；有效性在文档加载时统一检查。
        /// </summary>
        /// <param name="source">HTML/CSS 引用的非空相对路径，同一文档内不可重复。</param>
        /// <param name="texture">调用者持有的纹理；无需可读，URUI 不取得原资产所有权。</param>
        public ImageAsset(string source, Texture2D texture) : this(source, texture, Array.Empty<SpriteRegion>()) { }

        /// <summary>
        /// 创建图片和命名图集区域；复制区域数组，不取得纹理资产所有权。
        /// </summary>
        /// <param name="source">HTML/CSS 使用的图片路径。</param>
        /// <param name="texture">调用者持有的纹理。</param>
        /// <param name="sprites">左上角源像素区域，名称在所属文档内唯一。</param>
        public ImageAsset(string source, Texture2D texture, SpriteRegion[] sprites)
        {
            this.source = source;
            this.texture = texture;
            this.sprites = sprites == null ? Array.Empty<SpriteRegion>() : (SpriteRegion[])sprites.Clone();
        }
        /// <summary>
        /// 不可通过此视图修改的区域声明；区域数据随文档候选资源一起发布。
        /// </summary>
        public IReadOnlyList<SpriteRegion> Sprites => Array.AsReadOnly(sprites ?? Array.Empty<SpriteRegion>());
        internal ReadOnlySpan<SpriteRegion> SpriteSpan => sprites;
        internal ImageAsset Snapshot() => new ImageAsset(source, texture, sprites);
    }
}
