using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        const uint RasterBegin = 0x20000000u;
        const uint RasterEnd = 0x10000000u;
        Material colorInputMaterial;
        NativeArray<AttachmentDescriptor> colorAttachments;
        NativeArray<SubPassDescriptor> colorSubpasses;
        bool colorPassCapable;
        bool colorPassActive;
        bool submissionImmediate;
        bool recordingForContext;
        int colorPassGroups;
        int colorPassGroup;
        Rect colorConversionRegion;
        RenderTargetIdentifier colorPassTarget;
        readonly System.Collections.Generic.HashSet<(ulong, int)> memorylessLayerTargets = new();

        bool CanBoundColorConversions()
        {
            if (!colorPassCapable || !IsScreenOutput || !ShaderContractActive)
            {
                return false;
            }
            var format = CurrentTargetInfo.Format;
            return format == UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB ||
                format == UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB ||
                format == UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm ||
                format == UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_UNorm;
        }

        bool CanShareFinalRootColorPass()
        {
            if (!CanBoundColorConversions())
            {
                return false;
            }
            var viewport = RootOutputViewport;
            var info = CurrentTargetInfo;
            return viewport.x == 0 && viewport.y == 0 && viewport.width == info.Width && viewport.height == info.Height;
        }

        void SetColorConversionRegion(in RenderPlanOperation operation)
        {
            var full = Region(activeLayer);
            colorConversionRegion = new Rect(full.x, full.y, full.width, full.height);
            if ((operation.Reason & 0x00800000u) == 0)
            {
                return;
            }
            if (activeLayer != 0 || !CanBoundColorConversions() || operation.Kind != 1)
            {
                throw new InvalidOperationException("原生颜色转换范围缺少8bit屏幕根证明。");
            }
            var bounds = operation.Bounds;
            var left = Mathf.Max(full.xMin, Mathf.Floor(bounds.x) - 1);
            var top = Mathf.Max(full.yMin, Mathf.Floor(bounds.y) - 1);
            var right = Mathf.Min(full.xMax, Mathf.Ceil(bounds.z) + 1);
            var bottom = Mathf.Min(full.yMax, Mathf.Ceil(bounds.w) + 1);
            if (right > left && bottom > top)
            {
                colorConversionRegion = Rect.MinMaxRect(left, top, right, bottom);
            }
        }

        bool CanUseColorPass()
        {
            if (!UsesEncodedRaster || submissionImmediate || !GraphicsSettings.currentRenderPipeline ||
                !SystemInfo.supportsMemorylessTextures || RootPixelGrid() == 0)
            {
                return false;
            }
            var info = CurrentTargetInfo;
            if (!info.IsKnown || info.Samples != 1)
            {
                return false;
            }
            // 高精度根与HDR图层已直接使用framebuffer fetch，无需编码附件或改变录制时机。
            if (info.IsHighPrecision && targetCamera.allowHDR && CanUseFramebufferFetch())
            {
                return false;
            }
            if (info.IsScreen)
            {
                if (Display.displays[info.Display].requiresSrgbBlitToBackbuffer ||
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(ScreenRootFormat(), RenderTextureReadWrite.Default) != info.Format)
                {
                    return false;
                }
            }
            else if (!targetCamera.targetTexture || targetCamera.targetTexture.depth != 0 ||
                submissionTarget.HasValue && !submissionTarget.Value.Equals(new RenderTargetIdentifier(targetCamera.targetTexture)))
            {
                return false;
            }
            if (!colorInputMaterial)
            {
                var shader = UnityEngine.Resources.Load<Shader>("URUI/LayerColorInput");
                if (!shader || !shader.isSupported)
                {
                    return false;
                }
                colorInputMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return true;
        }

        static AttachmentIndexArray ColorAttachment(int index)
        {
            var indices = new AttachmentIndexArray(1);
            indices[0] = index;
            return indices;
        }

        void BeginColorPass(in RenderPlanOperation operation)
        {
            if (!colorPassCapable || colorPassActive || encodedRasterActive || operation.Kind != 1)
            {
                throw new InvalidOperationException("原生共享颜色区间起点无效。");
            }
            var scratch = LayerTexture(activeLayer, -1);
            if (scratch.memorylessMode != RenderTextureMemoryless.Color)
            {
                throw new InvalidOperationException("共享颜色区间缺少瞬时附件。");
            }
            colorPassGroups = (int)((operation.Reason >> 24) & 7) + 1;
            colorPassGroup = -1;
            if (!colorAttachments.IsCreated)
            {
                colorAttachments = new NativeArray<AttachmentDescriptor>(2, Allocator.Persistent);
            }
            if (!colorSubpasses.IsCreated)
            {
                colorSubpasses = new NativeArray<SubPassDescriptor>(16, Allocator.Persistent);
            }
            colorPassTarget = LayerTarget(activeLayer);
            var format = activeLayer == 0 && !rootTextureRequired ? CurrentTargetInfo.Format : LayerTexture(activeLayer, activeLayer == 0 ? -2 : 0).graphicsFormat;
            colorAttachments[0] = new AttachmentDescriptor(format)
            {
                loadStoreTarget = colorPassTarget,
                loadAction = RenderBufferLoadAction.Load,
                storeAction = RenderBufferStoreAction.Store
            };
            colorAttachments[1] = new AttachmentDescriptor(scratch.graphicsFormat)
            {
                loadStoreTarget = scratch,
                loadAction = RenderBufferLoadAction.DontCare,
                storeAction = RenderBufferStoreAction.DontCare
            };
            for (var i = 0; i < colorPassGroups; i++)
            {
                colorSubpasses[i * 2] = new SubPassDescriptor
                {
                    colorOutputs = ColorAttachment(1),
                    inputs = ColorAttachment(0)
                };
                colorSubpasses[i * 2 + 1] = new SubPassDescriptor
                {
                    colorOutputs = ColorAttachment(0),
                    inputs = ColorAttachment(1)
                };
            }
            // Unity按整个缓冲检查Pass内可执行命令；前后的复制/清屏即使位于
            // Begin/End之外，也会使同一缓冲的原生Pass校验失败并关闭URP原生路径。
            ContinueNativeRecording();
            buffer.BeginRenderPass(scratch.width, scratch.height, 1, colorAttachments, -1, colorSubpasses.GetSubArray(0, colorPassGroups * 2));
            colorPassActive = true;
        }

        void BeginColorEncoding()
        {
            if (colorPassGroup >= 0)
            {
                buffer.NextSubPass();
            }
            if (++colorPassGroup >= colorPassGroups)
            {
                throw new InvalidOperationException("原生颜色组数量与实际绘制不一致。");
            }
            SetColorPassProjection();
            DrawColorConversion(0);
            buffer.SetGlobalFloat(EncodedTarget, 1);
            encodedRasterActive = true;
        }

        void ResolveColorEncoding()
        {
            buffer.SetGlobalFloat(EncodedTarget, 0);
            buffer.NextSubPass();
            SetColorPassProjection();
            DrawColorConversion(1);
            encodedRasterActive = false;
            RecordedRasterResolves++;
        }

        void SetColorPassProjection()
        {
            buffer.SetViewport(LayerViewport(activeLayer));
            var region = Region(activeLayer);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(region.xMin, region.xMax, region.yMax, region.yMin, -1, 1));
        }

        void DrawColorConversion(int pass)
        {
            buffer.SetGlobalFloat(LayerReadRootColor, activeLayer == 0 && IsScreenOutput ? 1 : 0);
            var region = colorConversionRegion;
            buffer.DrawMesh(layerCompositeMesh, Matrix4x4.TRS(new Vector3(region.x, region.y, 0), Quaternion.identity,
                new Vector3(region.width, region.height, 1)), colorInputMaterial, 0, pass);
        }

        void EndColorPass()
        {
            if (!colorPassActive)
            {
                throw new InvalidOperationException("原生共享颜色区间终点无效。");
            }
            EndEncodedRaster();
            if (colorPassGroup + 1 != colorPassGroups)
            {
                throw new InvalidOperationException("原生共享颜色区间未完成全部颜色组。");
            }
            buffer.EndRenderPass();
            colorPassActive = false;
            ContinueNativeRecording();
            SelectLayer(activeLayer);
        }

        void ReleaseColorPass()
        {
            if (colorAttachments.IsCreated)
            {
                colorAttachments.Dispose();
            }
            if (colorSubpasses.IsCreated)
            {
                colorSubpasses.Dispose();
            }
            colorInputMaterial = null;
            colorPassActive = colorPassCapable = false;
            memorylessLayerTargets.Clear();
        }
    }
}
