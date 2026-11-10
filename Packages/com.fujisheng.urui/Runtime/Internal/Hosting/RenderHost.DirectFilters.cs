using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        sealed class DirectFilterState
        {
            internal readonly MaterialPropertyBlock Properties = new();
        }
        readonly Dictionary<ulong, DirectFilterState> directColorFilters = new();
        readonly HashSet<ulong> directColorFilterUses = new();
        readonly List<ulong> removedDirectColorFilters = new();
        Material directColorMaterial;

        void PrepareDirectColorFilters()
        {
            directColorFilterUses.Clear();
            foreach (var paint in paintCommands.Values)
            {
                if (paint.reserved != 2) { continue; }
                directColorFilterUses.Add(paint.shader);
                if (directColorFilters.ContainsKey(paint.shader)) { continue; }
                var count = NativeMethods.urui_paint_color_filters(paint.shader, null, 0);
                if (count < 1 || count > 16) { throw new InvalidOperationException("标准图片滤镜链无效。"); }
                var filters = new NativeMethods.ColorFilterNative[count];
                if (NativeMethods.urui_paint_color_filters(paint.shader, filters, count) != count)
                { throw new InvalidOperationException("标准图片滤镜快照已失效。"); }
                var values = new Vector4[16];
                for (var index = 0; index < count; index++)
                {
                    if (filters[index].Kind < 0 || filters[index].Kind > 4 || !float.IsFinite(filters[index].Value))
                    { throw new InvalidOperationException("标准图片滤镜参数无效。"); }
                    values[index] = new Vector4(filters[index].Kind, filters[index].Value, 0, 0);
                }
                var state = new DirectFilterState(); state.Properties.SetInt("_CssFilterCount", count);
                state.Properties.SetVectorArray("_CssFilters", values); directColorFilters.Add(paint.shader, state);
                if (!directColorMaterial)
                {
                    var program = UnityEngine.Resources.Load<Shader>("URUI/DirectColorFilter");
                    if (!program || !program.isSupported) { throw new NotSupportedException("当前平台缺少 URUI 标准图片滤镜程序。"); }
                    directColorMaterial = new Material(program) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
            removedDirectColorFilters.Clear();
            foreach (var key in directColorFilters.Keys) { if (!directColorFilterUses.Contains(key)) { removedDirectColorFilters.Add(key); } }
            foreach (var key in removedDirectColorFilters) { directColorFilters.Remove(key); }
        }

        void DrawDirectColorFilter(PaintCommandNative paint, CommandNative command, Matrix4x4 transform, Mesh geometry, ref PhaseFrame phase)
        {
            var texture = LoadTexture(command.texture);
            SelectEncodedRaster(texture);
            var properties = directColorFilters[paint.shader].Properties;
            WriteShaderProperties(properties, texture ? texture : white, command, false, ref phase);
            buffer.DrawMesh(geometry, transform * Matrix4x4.Translate(new Vector3(command.tx, command.ty, 0)), directColorMaterial, 0, 0, properties);
#if URUI_DEBUG
            recordCallsThisFrame++;
#endif
        }
    }
}
