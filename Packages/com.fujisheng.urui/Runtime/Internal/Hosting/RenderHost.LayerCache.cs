using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        readonly Dictionary<ulong, LayerContentLease> layerContents = new();
        readonly Dictionary<ulong, (ulong Version, bool Cacheable)> layerVersions = new();
        readonly List<LayerContentLease> recordedLayerWrites = new();
        RenderPlanLayerBinding[] layerBindings = Array.Empty<RenderPlanLayerBinding>();
        void ClearLayerRecording()
        {
            foreach (var content in recordedLayerWrites)
            {
                content.Release(false);
            }
            recordedLayerWrites.Clear();
        }
        void PinLayerWrites(URUI.Internal.Resources.ImageSubmissionFence submission)
        {
            foreach (var content in recordedLayerWrites)
            {
                if (submission.Layers.Contains(content))
                {
                    continue;
                }
                if (!submission.ReadLayers.Remove(content))
                {
                    content.Retain();
                }
                submission.Layers.Add(content);
            }
            // 免写缓存仍是当前计划的读取输入；不能只保护本轮写入的层。
            foreach (var content in layerContents.Values)
            {
                if (submission.Layers.Contains(content) || submission.ReadLayers.Contains(content))
                {
                    continue;
                }
                content.Retain();
                submission.ReadLayers.Add(content);
            }
            foreach (var target in preparedLayerTargets.Values)
            {
                submission.PinTarget(target);
            }
        }
        int ReadLayerBindings()
        {
            var count = 0;
            if (layerBindings.Length < layerContents.Count)
            {
                layerBindings = new RenderPlanLayerBinding[layerContents.Count];
            }
            foreach (var pair in layerContents)
            {
                var content = pair.Value;
                if (!content.Cacheable || !content.Available)
                {
                    continue;
                }
                layerBindings[count++] = new RenderPlanLayerBinding
                {
                    Layer = pair.Key,
                    Version = content.Version,
                    Flags = 1
                };
            }
            return count;
        }
        void RecordLayerWrite(ulong id)
        {
            if (!layerContents.TryGetValue(id, out var content) || !content.Cacheable)
            {
                return;
            }
            content.Retain();
            recordedLayerWrites.Add(content);
        }
        // A Texture2D sampling change is part of its input version. Live render
        // targets deliberately receive a fresh version on every native input.
        readonly Dictionary<ulong, (TextureDependency Dependency, ulong Version)> textureVersions = new();
        readonly List<ulong> removedTextureVersions = new();
        static TextureDependency Depend(Texture t) => new TextureDependency
        {
            texture = t,
            update = t.updateCount,
            width = t.width,
            height = t.height,
            filter = t.filterMode,
            u = t.wrapModeU,
            v = t.wrapModeV,
            w = t.wrapModeW,
            bias = t.mipMapBias,
            aniso = t.anisoLevel
        };
        static bool Alive(TextureDependency d)
        {
            var t = d.texture;
            return t && t.updateCount == d.update && t.width == d.width && t.height == d.height && t.filterMode == d.filter && t.wrapModeU == d.u && t.wrapModeV == d.v && t.wrapModeW == d.w && t.mipMapBias == d.bias && t.anisoLevel == d.aniso;
        }
    }
}
