using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Resources
{
    /// <summary>
    /// 在创建文档前安装随包字体；原始字节由原生 Surface 持有到字体后端关闭。
    /// </summary>
    internal static class DefaultFonts
    {
        static readonly string[] Families = { "LiberationSerif", "LiberationSans", "LiberationMono" };
        static readonly string[] Faces = { "Regular", "Italic", "Bold", "BoldItalic" };

        internal static void Install()
        {
            for (var family = 0; family < Families.Length; ++family)
            {
                for (var face = 0; face < Faces.Length; ++face)
                {
                    var path = "URUI/Fonts/" + Families[family] + "-" + Faces[face] + ".ttf";
                    var asset = global::UnityEngine.Resources.Load<TextAsset>(path);
                    if (!asset)
                    {
                        throw new InvalidOperationException("缺少 URUI 默认字体资源：" + path);
                    }
                    var bytes = asset.bytes;
                    if (NativeMethods.urui_surface_set_default_font_face(family, bytes, bytes.Length, 0, face & 1, face < 2 ? 400 : 700) != 1)
                    {
                        throw new InvalidOperationException("URUI 默认字体初始化失败：" + path + ": " + NativeMethods.LastError());
                    }
                }
            }
        }
    }
}
