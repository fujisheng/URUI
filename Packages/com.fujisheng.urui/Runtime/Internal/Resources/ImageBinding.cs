using System;
using UnityEngine;

namespace URUI.Internal.Resources
{
    /// <summary>宿主读取的图片配置；纹理资产仍由调用者持有。</summary>
    [Serializable]
    internal struct ImageBinding
    {
        public string source;
        public Texture2D texture;
        public int expectedWidth;
        public int expectedHeight;
    }
}
