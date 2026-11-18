using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Rendering;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        // 空绘制回调不改变颜色目标；未知自定义材质第一次真正写入前再结束编码域。
        readonly PaintContextPool paintContexts = new();
        internal void BeginPainterDraw()
        {
            if (preRecordingPainter) { return; }
            EndEncodedRaster();
        }
        readonly Dictionary<int, PaintCommandNative> paintCommands = new();
        PaintCommandNative[] paintScratch = Array.Empty<PaintCommandNative>();
        byte[] paintName = new byte[128];
        readonly Dictionary<ulong,string> paintShaderNames=new();
        readonly HashSet<ulong> livePaintShaders=new();
        readonly List<ulong> removedPaintShaders=new();
        void PrunePaintNames()
        {
            removedPaintShaders.Clear();foreach(var id in paintShaderNames.Keys)if(!livePaintShaders.Contains(id))removedPaintShaders.Add(id);
            foreach(var id in removedPaintShaders)paintShaderNames.Remove(id);
        }
        void ReadPaintCommands()
        {
            paintCommands.Clear();livePaintShaders.Clear();
            var count = NativeMethods.urui_get_paint_commands(null, 0);
            if (count < 0) { throw new InvalidOperationException("外部绘制命令读取失败。"); }
            if (count == 0) { paintShaderNames.Clear(); return; }
            if (paintScratch.Length < count) { paintScratch = new PaintCommandNative[count]; }
            var read = NativeMethods.urui_get_paint_commands(paintScratch, count);
            if (read != count) { throw new InvalidOperationException("外部绘制命令数量变化。"); }
            for (var index = 0; index < read; index++) { paintCommands.Add(paintScratch[index].command_index, paintScratch[index]);livePaintShaders.Add(paintScratch[index].shader); }
            PrunePaintNames();
        }
    }
}
