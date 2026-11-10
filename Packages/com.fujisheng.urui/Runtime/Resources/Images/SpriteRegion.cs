using System;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>一张图片中的命名区域；坐标从原图左上角开始，单位为源像素。</summary>
    [Serializable]
    public struct SpriteRegion
    {
        [SerializeField] string name;
        [SerializeField] Rect region;
        [SerializeField] float displayScale;
        /// <summary>所属文档内唯一且区分大小写的区域名称。</summary>
        public string Name => name;
        /// <summary>左上角原点的源像素矩形。</summary>
        public Rect Region => region;
        /// <summary>区域源像素到 URUI 自然尺寸的显示比例。</summary>
        public float DisplayScale => displayScale;
        /// <summary>创建区域声明；名称、边界与比例在文档加载前统一验证。</summary>
        /// <param name="name">文档内唯一的名称。</param>
        /// <param name="region">不得越过所属图片的源像素边界。</param>
        /// <param name="displayScale">正有限显示比例，默认一源像素对应一自然像素。</param>
        public SpriteRegion(string name, Rect region, float displayScale = 1)
        {
            this.name = name;
            this.region = region;
            this.displayScale = displayScale;
        }
    }
}
