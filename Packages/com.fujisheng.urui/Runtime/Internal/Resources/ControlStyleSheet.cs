using System;
using UnityEngine;
using UnityResources = UnityEngine.Resources;

namespace URUI.Internal.Resources
{
    // Resources 只定位外置皮肤；内容不拼接、不注入 HTML。
    internal static class ControlStyleSheet
    {
        internal static TextAsset Load()
        {
            var asset = UnityResources.Load<TextAsset>("URUI/controls");
            if (!asset)
            {
                throw new InvalidOperationException("URUI 控件基础样式资源缺失。");
            }
            return asset;
        }
    }
}
