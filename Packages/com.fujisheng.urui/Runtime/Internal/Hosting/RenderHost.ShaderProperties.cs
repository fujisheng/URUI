using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Shaders;

namespace URUI.Internal.Hosting
{
    // 按 shader 契约写入属性并录制单项或批次绘制，避免共享属性块残留上次状态。
    internal sealed partial class RenderHost
    {
        static readonly int[] paletteTextureIds=PaletteIds("_UruiTexture");
        static readonly int[] paletteSourceIds=PaletteIds("_UruiSource");
        static int[] PaletteIds(string prefix)
        {
            var ids=new int[TextureBindingBudget-1];
            for(var index=0;index<ids.Length;index++)ids[index]=Shader.PropertyToID(prefix+(index+1));
            return ids;
        }
    }
}
