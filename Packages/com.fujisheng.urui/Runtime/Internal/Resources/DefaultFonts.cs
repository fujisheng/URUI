using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Resources
{
    /// <summary>
    /// 在创建文档前安装随包字体；原始字节由原生引擎共享持有到最后一个 Surface 关闭。
    /// </summary>
    internal static class DefaultFonts
    {
        static readonly string[] Families = { "LiberationSerif", "LiberationSans", "LiberationMono" };
        static readonly string[] Faces = { "Regular", "Italic", "Bold", "BoldItalic" };
        static byte[][] cachedBytes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cachedBytes = null;
        }

        internal static void Install(ulong surface)
        {
            int total = Families.Length * Faces.Length;
            cachedBytes ??= new byte[total][];
            for (var family = 0; family < Families.Length; ++family)
            {
                for (var face = 0; face < Faces.Length; ++face)
                {
                    int slot = family * Faces.Length + face;
                    var path = "URUI/Fonts/" + Families[family] + "-" + Faces[face] + ".ttf";
                    var bytes = cachedBytes[slot];
                    if (bytes == null)
                    {
                        var asset = global::UnityEngine.Resources.Load<TextAsset>(path);
                        if (!asset)
                        {
                            throw new InvalidOperationException("缺少 URUI 默认字体资源：" + path);
                        }
                        bytes = asset.bytes;
                        cachedBytes[slot] = bytes;
                    }
                    if (NativeMethods.urui_surface_set_default_font_face(surface, family, bytes, bytes.Length, 0, face & 1, face < 2 ? 400 : 700) != NativeStatus.Ok)
                    {
                        throw new InvalidOperationException("URUI 默认字体初始化失败：" + path + ": " + NativeMethods.LastError(surface));
                    }
                }
            }
        }
    }
}
