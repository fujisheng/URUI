using System;
using System.Collections.Generic;
using URUI.Internal.Interop;
using URUI.Rendering;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        readonly PaintContextPool paintContexts = new();
        PaintCommandNative[] paintScratch = Array.Empty<PaintCommandNative>();
        readonly Dictionary<ulong, string> paintShaderNames = new();
        readonly HashSet<ulong> livePaintShaders = new();
        readonly List<ulong> removedPaintShaders = new();

        void PrunePaintNames()
        {
            removedPaintShaders.Clear();
            foreach (var id in paintShaderNames.Keys)
            {
                if (!livePaintShaders.Contains(id))
                {
                    removedPaintShaders.Add(id);
                }
            }
            foreach (var id in removedPaintShaders)
            {
                paintShaderNames.Remove(id);
            }
        }

        unsafe void ReadPaintCommands(in RenderPlanInputPacket input)
        {
            livePaintShaders.Clear();
            var count = checked((int)input.PaintCount);
            if (count == 0)
            {
                paintShaderNames.Clear();
                return;
            }
            if (paintScratch.Length < count)
            {
                paintScratch = new PaintCommandNative[Math.Max(count, paintScratch.Length * 2 + 8)];
            }
            new ReadOnlySpan<PaintCommandNative>((void*)input.Paints, count).CopyTo(paintScratch);
            for (var index = 0; index < count; index++)
            {
                livePaintShaders.Add(paintScratch[index].shader);
            }
            PrunePaintNames();
        }
    }
}
