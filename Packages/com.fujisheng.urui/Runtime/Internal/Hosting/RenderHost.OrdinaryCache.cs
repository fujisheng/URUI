using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Shaders;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        struct CachedDraw
        {
            internal Mesh Mesh;
            internal Matrix4x4 Transform;
            internal CommandNative Command;
            internal bool Batched;
            internal int TextureStart, TextureCount;
        }
        readonly List<CachedDraw> ordinaryDraws = new();
        readonly List<ulong> ordinaryTextures = new();
        int ordinaryCount = -1, ordinaryWidth, ordinaryHeight;
        ulong ordinaryEpoch, ordinaryResources, ordinaryCommands;
        Material ordinaryMaterial;
        ColorSpace ordinaryColorSpace;
        bool capturingOrdinary;
        UruiFrameStampNative ordinaryStamp;

        bool OrdinaryCommandsUnchanged(int count)
        {
            if (NativeMethods.urui_render_plan_stamp(out ordinaryStamp) != 1 || ordinaryStamp.flags != 1 ||
                ordinaryStamp.epoch == 0 || ordinaryStamp.commands == 0 || ordinaryStamp.resources == 0)
            { return false; }
            // 原生版本由完整命令与矩阵的 memcmp 产生；不是 DOM 脏标记或可能碰撞的 hash。
            return ordinaryCount == count && ordinaryEpoch == ordinaryStamp.epoch &&
                ordinaryResources == ordinaryStamp.resources && ordinaryCommands == ordinaryStamp.commands;
        }

        void InvalidateOrdinaryCache()
        {
            ordinaryCount = -1;
            capturingOrdinary = false;
            ordinaryDraws.Clear(); ordinaryTextures.Clear();
            ordinaryMaterial = null;
        }

        bool ReplayOrdinaryGeometry(int count, ref PhaseFrame phase)
        {
            capturingOrdinary = false;
            // 这些语义具有自己的有序记录；不能只比较普通几何而忽略它们的输入。
            if (!ShaderContractActive || count > 16384 || paintCommands.Count != 0 || layerCount != 0 ||
                ordinaryStamp.flags != 1 || ordinaryStamp.epoch == 0 || ordinaryStamp.commands == 0 || ordinaryStamp.resources == 0)
            { InvalidateOrdinaryCache(); return false; }
            var same = ordinaryCount == count && ordinaryEpoch == ordinaryStamp.epoch && ordinaryResources == ordinaryStamp.resources &&
                ordinaryCommands == ordinaryStamp.commands && ordinaryWidth == UiWidth && ordinaryHeight == UiHeight &&
                ordinaryMaterial == material && ordinaryColorSpace == QualitySettings.activeColorSpace;
            if (same)
            {
                for (var i = 0; same && i < ordinaryDraws.Count; ++i) { same = ordinaryDraws[i].Mesh; }
            }
            if (!same)
            {
                ordinaryCount = -1; ordinaryDraws.Clear(); ordinaryTextures.Clear();
                capturingOrdinary = true;
                return false;
            }
            foreach (var draw in ordinaryDraws)
            {
                var command = draw.Command;
                var texture = LoadTexture(command.texture);
                SelectEncodedRaster(texture);
                var properties = PrepareShaderProperties(texture ? texture : white, command, draw.Batched, ref phase);
                // 原始纹理与各槽的当前采样状态每次重绑，纹理像素更新不需要改变命令输入。
                for (var slot = 1; slot < draw.TextureCount; ++slot)
                {
                    var id = ordinaryTextures[draw.TextureStart + slot];
                    var source = LoadTexture(id); if (!source) { source = white; }
                    properties.SetTexture(paletteTextureIds[slot - 1], source);
                    properties.SetVector(paletteSourceIds[slot - 1], TextureShaderState.SourceInfo(source, id));
                }
                buffer.DrawMesh(draw.Mesh, draw.Transform, material, 0, 0, properties);
                RecordedGeometryDraws++;
            }
            EndEncodedRaster();
            return true;
        }

        void CaptureOrdinaryDraw(Mesh mesh, Matrix4x4 transform, CommandNative command, bool batched)
        {
            if (!capturingOrdinary) { return; }
            var start = ordinaryTextures.Count;
            var count = batched ? batchTextureCount : 1;
            for (var i = 0; i < count; ++i) { ordinaryTextures.Add(batched ? batchTextures[i] : command.texture); }
            ordinaryDraws.Add(new CachedDraw { Mesh = mesh, Transform = transform, Command = command,
                Batched = batched, TextureStart = start, TextureCount = count });
        }

        void CompleteOrdinaryGeometry(int count)
        {
            if (!capturingOrdinary) { return; }
            capturingOrdinary = false;
            ordinaryEpoch = ordinaryStamp.epoch; ordinaryResources = ordinaryStamp.resources;
            ordinaryCommands = ordinaryStamp.commands;
            ordinaryWidth = UiWidth; ordinaryHeight = UiHeight; ordinaryMaterial = material;
            ordinaryColorSpace = QualitySettings.activeColorSpace; ordinaryCount = count;
        }
    }
}
