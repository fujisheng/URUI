using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;
using URUI.Rendering.Effects;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        LayerCommandNative[] layerCommands = Array.Empty<LayerCommandNative>();
        int layerCount;
        int layerCursor;
        int layerOrderCount;
        int[] layerOrder = Array.Empty<int>();
        ulong activeLayer;
        readonly HashSet<ulong> layerTargets = new();
        readonly HashSet<ulong> maskSourceLayers = new();
        readonly HashSet<ulong> scalarMaskLayers = new();
        readonly Dictionary<ulong, RectInt> layerRegions = new();
        readonly Dictionary<ulong, ulong> layerAliases = new();
        readonly Dictionary<(ulong, int), EffectTargetPool.Lease> preparedLayerTargets = new();
        readonly HashSet<(ulong, int)> desiredLayerTargets = new();
        readonly Dictionary<(ulong, int), Vector2Int> layerBackdropSizes = new();
        readonly List<(ulong, int)> removedLayerTargets = new();
        readonly List<IDisposable> retiredLayerResources = new();
        readonly Dictionary<int, FilterInvocation> filterInvocations = new();
        readonly Dictionary<ulong, FilterInvocation> frozenFilterInvocations = new();
        readonly Dictionary<int, ulong> elementFilterElements = new();
        readonly Dictionary<ulong, EffectComposite> elementComposites = new();
        readonly Dictionary<(BlendMode, BlendMode), Material> elementCompositeMaterials = new();
        readonly Dictionary<ulong, string> filterNames = new();
        readonly HashSet<ulong> liveFilterNames = new();
        readonly List<ulong> removedFilterNames = new();
        byte[] filterNameBuffer = new byte[128];
        uint elementEffectVersion;
        EffectTargetPool effectTargets;
        Material layerCompositeMaterial;
        Mesh layerCompositeMesh;
        RenderTargetIdentifier? submissionTarget;
        Rect submissionViewport;
        URUI.Rendering.RenderTargetInfo submissionTargetInfo;

        URUI.Rendering.RenderTargetInfo CurrentTargetInfo => submissionTarget.HasValue ? submissionTargetInfo :
            targetCamera && targetCamera.targetTexture ? URUI.Rendering.RenderTargetInfo.FromTexture(targetCamera.targetTexture) :
            URUI.Rendering.RenderTargetInfo.FromScreen(targetCamera ? targetCamera.targetDisplay : 0, targetCamera && targetCamera.allowMSAA ? Math.Max(1, QualitySettings.antiAliasing) : 1);

        const int CopyBackdropPass = 2;
        const int EffectCompositePass = 3;
        const int CopySourcePass = 4;
        const int ClearLayerPass = 5;
        const int FastCompositePass = 6;

        Rect RootOutputViewport => submissionTarget.HasValue ? submissionViewport : targetCamera.pixelRect;

        void CopyScreenRegion(RenderTexture destination, RectInt read)
        {
            // Resolve the non-sampleable screen alias once per unmodified layer-0 state,
            // then crop the requested region into the backdrop target.
            layerBound = false;
            renderState.DisableScissor();
            var capture = LayerTexture(0, -3);
            if (!screenCaptureValid)
            {
                buffer.SetRenderTarget(SubmissionOutput);
                buffer.Blit(SubmissionOutput, capture);
                renderState.InvalidateTarget();
                screenCaptureValid = true;
            }
            if (!SystemInfo.graphicsUVStartsAtTop && (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0 && capture.graphicsFormat == destination.graphicsFormat &&
                capture.antiAliasing == 1 && destination.antiAliasing == 1 && destination.width == read.width && destination.height == read.height &&
                read.x >= 0 && read.y >= 0 && read.xMax <= capture.width && read.yMax <= capture.height)
            {
                buffer.CopyTexture(capture, 0, 0, read.x, read.y, read.width, read.height, destination, 0, 0, 0, 0);
                renderState.InvalidateTarget();
            }
            else
            {
                buffer.SetGlobalVector(LayerReadRect, new Vector4(read.x, read.y, read.width, read.height));
                buffer.SetGlobalFloat(LayerReadCameraTarget, SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
                buffer.Blit(capture, destination, layerCompositeMaterial, CopyBackdropPass);
                renderState.InvalidateTarget();
            }
        }

        internal static bool CanCopyLayerData(RenderTexture source, RenderTexture destination, CopyTextureSupport capabilities)
            => (capabilities & CopyTextureSupport.Basic) != 0 && source.width == destination.width && source.height == destination.height &&
                source.graphicsFormat == destination.graphicsFormat && source.antiAliasing == 1 && destination.antiAliasing == 1;

        void CopyLayerData(RenderTexture source, RenderTexture destination)
        {
            layerBound = false;
            if (CanCopyLayerData(source, destination, SystemInfo.copyTextureSupport))
            {
                buffer.CopyTexture(source, destination);
                renderState.InvalidateTarget();
            }
            else
            {
                // 同一个颜色域内覆盖写回；不依赖 WebGL 等后端未公开的复制能力。
                renderState.DisableScissor();
                buffer.SetGlobalVector(LayerReadRect, new Vector4(0, 0, source.width, source.height));
                buffer.SetGlobalFloat(LayerReadCameraTarget, 0);
                buffer.Blit(source, destination, layerCompositeMaterial, CopyBackdropPass);
                renderState.InvalidateTarget();
            }
        }

        RenderTextureFormat ScreenCaptureFormat()
        {
            // Camera HDR describes the intermediate scene target, not the final
            // display buffer. SDR output must retain its normal LDR clamping.
            var displays = HDROutputSettings.displays;
            var display = submissionTarget.HasValue && submissionTargetInfo.IsKnown ? submissionTargetInfo.Display : targetCamera.targetDisplay;
            return displays != null && display >= 0 && display < displays.Length && displays[display].active
                ? displays[display].format : RenderTextureFormat.ARGB32;
        }

        bool framebufferFetchActive;
        bool framebufferProgramsChecked;
        Material framebufferLayerMaterial;

        bool CanUseFramebufferFetch()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear || !HasStandardRasterShader || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return false;
            }
            var output = CurrentTargetInfo;
            if (!output.IsKnown || output.Samples != 1)
            {
                return false;
            }
            if (output.IsScreen && UnityEngine.Display.displays[output.Display].requiresSrgbBlitToBackbuffer)
            {
                return false;
            }
            if (!framebufferProgramsChecked)
            {
                framebufferProgramsChecked = true;
                var layer = UnityEngine.Resources.Load<Shader>("URUI/LayerCompositeFetch");
                if (layer && layer.isSupported)
                {
                    framebufferLayerMaterial = new Material(layer) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
            return framebufferLayerMaterial;
        }

        RenderTargetIdentifier SubmissionOutput => submissionTarget ?? new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);

        bool IsScreenOutput => submissionTarget.HasValue && submissionTargetInfo.IsKnown ? submissionTargetInfo.IsScreen :
            !targetCamera.targetTexture && SubmissionOutput.Equals(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));

        Vector2Int ScreenOutputSize()
        {
            if (submissionTarget.HasValue && submissionTargetInfo.IsKnown)
            {
                return new Vector2Int(submissionTargetInfo.Width, submissionTargetInfo.Height);
            }
            var display = targetCamera.targetDisplay;
            if (display == 0)
            {
                return new Vector2Int(Screen.width, Screen.height);
            }
            if (display < 0 || display >= Display.displays.Length)
            {
                throw new InvalidOperationException("URUI 输出显示器无效。");
            }
            return new Vector2Int(Display.displays[display].renderingWidth, Display.displays[display].renderingHeight);
        }

        Shader standardRasterShader;
        bool standardRasterShaderSupported;

        bool HasStandardRasterShader
        {
            get
            {
                if (!material)
                {
                    return false;
                }
                var current = material.shader;
                if (current != standardRasterShader)
                {
                    standardRasterShader = current;
                    standardRasterShaderSupported = current && current.name == "URUI/Premultiplied";
                }
                return standardRasterShaderSupported;
            }
        }

        bool HasLayerResources => preparedLayerTargets.Count != 0;
        static readonly int LayerMask = Shader.PropertyToID("_UruiLayerMask");
        static readonly int LayerSource = Shader.PropertyToID("_UruiLayerSource");
        static readonly int LayerReadRect = Shader.PropertyToID("_UruiLayerReadRect");
        static readonly int LayerReadCameraTarget = Shader.PropertyToID("_UruiLayerReadCameraTarget");
        static readonly int LayerMaskEnabled = Shader.PropertyToID("_UruiLayerMaskEnabled");
        static readonly int LayerMaskRect = Shader.PropertyToID("_UruiLayerMaskRect");
        static readonly int LayerClip = Shader.PropertyToID("_UruiLayerClip");
        static readonly int LayerClipEnabled = Shader.PropertyToID("_UruiLayerClipEnabled");
        readonly FilterRenderContext pooledFilterContext = new();

        void DrawElementEffect(int index, LayerCommandNative command, in RenderPlanOperation operation)
        {
            var owner = OwnerSurface.FilterOwner(command.mask);
            var region = Region(command.source);
            // 捕获域已在 Prepare 前冻结；输出层扩边不会改变其采样输入。
            var capture = captures[command.mask];
            var captureBounds = capture.Bounds;
            var sourceTexture = LayerTexture(command.source);
            var captureTexture = capture.Lease.Texture;
            renderState.DisableScissor();
            // 相同像素域和格式只需保留快照；缩放/扩边或无复制能力的平台仍执行覆盖复制。
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0 &&
                captureBounds.x == region.x && captureBounds.y == region.y && captureBounds.width == region.width && captureBounds.height == region.height &&
                sourceTexture.width == captureTexture.width && sourceTexture.height == captureTexture.height &&
                sourceTexture.graphicsFormat == captureTexture.graphicsFormat && sourceTexture.antiAliasing == captureTexture.antiAliasing)
            {
                buffer.CopyTexture(sourceTexture, captureTexture);
                renderState.InvalidateTarget();
            }
            else
            {
                renderState.BindOffscreen(
                    captureTexture,
                    new Rect(0, 0, captureTexture.width, captureTexture.height),
                    Matrix4x4.Ortho(captureBounds.xMin, captureBounds.xMax, captureBounds.yMax, captureBounds.yMin, -1, 1));
                buffer.ClearRenderTarget(false, true, Color.clear);
                buffer.SetGlobalTexture(LayerSource, sourceTexture);
                buffer.DrawMesh(layerCompositeMesh, Matrix4x4.TRS(new Vector3(region.x, region.y, 0), Quaternion.identity, new Vector3(region.width, region.height, 1)), layerCompositeMaterial, 0, CopySourcePass);
            }
            var destination = LayerTexture(command.source, 2);
            renderState.BindOffscreen(
                destination,
                new Rect(0, 0, region.width, region.height),
                Matrix4x4.Ortho(region.xMin, region.xMax, region.yMax, region.yMin, -1, 1));
            buffer.ClearRenderTarget(false, true, Color.clear);
            var transform = Matrix4x4.TRS(new Vector3(captureBounds.x, captureBounds.y, 0), Quaternion.identity, new Vector3(captureBounds.width, captureBounds.height, 1));
            if (operation.Program != 0)
            {
                var properties = reusableProperties;
                properties.Clear();
                var page = parameterPages.Current.Texture;
                properties.SetTexture(mainTextureId, capture.Lease.Texture);
                properties.SetTexture(parametersId, page);
                properties.SetFloat(baseParameterId, operation.BaseParameter);
                buffer.DrawMesh(planResources.Mesh(operation.Geometry), Matrix4x4.identity, imagePrograms[operation.Program], 0, 0, properties);
                RecordedGeometryDraws++;
                effectSubmitted.Add(new EffectSubmission { ProcessingPasses = 1 });
                CopyLayerData(destination, LayerTexture(command.source));
                layerBound = false;
                SelectLayer(activeLayer);
                return;
            }
            var paint = paintContexts.Open(owner.WrapElement(elementFilterElements[index]), buffer,
                transform, Vector4.zero, false, maskSourceLayers.Contains(activeLayer));
            try
            {
                if (!owner.RecordEffect(elementFilterElements[index], paint, layerCompositeMesh,
                    owner.EffectSubmissionSink, EffectKind.Element))
                {
                    CopyLayerData(LayerTexture(command.source), destination);
                }
                else
                {
                    effectSubmitted.Add(new EffectSubmission { ProcessingPasses = 1 });
                }
            }
            finally
            {
                paint.Close();
                renderState.InvalidateTarget();
            }
            CopyLayerData(destination, LayerTexture(command.source));
            layerBound = false;
            SelectLayer(activeLayer);
        }

        void EnsureLayerProgram()
        {
            if (layerCompositeMaterial)
            {
                return;
            }
            var shader = UnityEngine.Resources.Load<Shader>("URUI/LayerComposite");
            if (!shader)
            {
                throw new InvalidOperationException("缺少 URUI 图层合成 shader。");
            }
            layerCompositeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            layerCompositeMesh = new Mesh { name = "URUI Layer Composite Quad", hideFlags = HideFlags.HideAndDontSave };
            layerCompositeMesh.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up };
            layerCompositeMesh.uv = new[] { Vector2.up, Vector2.one, Vector2.right, Vector2.zero };
            layerCompositeMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        }

        Material ElementCompositeMaterial(EffectComposite composite)
        {
            var key = (composite.Source, composite.Destination);
            if (!elementCompositeMaterials.TryGetValue(key, out var program))
            {
                program = new Material(layerCompositeMaterial) { hideFlags = HideFlags.HideAndDontSave };
                program.SetInt("_UruiCompositeSrcBlend", (int)composite.Source);
                program.SetInt("_UruiCompositeDstBlend", (int)composite.Destination);
                elementCompositeMaterials.Add(key, program);
            }
            return program;
        }

        RectInt Region(ulong id) => id == 0 ? new RectInt(0, 0, UIWidth, UIHeight) : layerRegions[id];
        bool? scalarMaskSupported;

        bool SupportsScalarMask => scalarMaskSupported ??=
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat,
                GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample | GraphicsFormatUsage.Blend | GraphicsFormatUsage.Linear);

        RenderTextureFormat LayerFormat((ulong Layer, int Slot) key, RenderTextureFormat screen, RenderTextureFormat color)
        {
            if (key == (0UL, -3))
            {
                return screen;
            }
            return key.Slot == 0 && scalarMaskLayers.Contains(key.Layer) ? RenderTextureFormat.RHalf : color;
        }

        RenderTexture LayerTexture(ulong id, int slot = 0)
        {
            if (slot == 0)
            {
                while (layerAliases.TryGetValue(id, out var source))
                {
                    id = source;
                }
            }
            return preparedLayerTargets[(id, slot)].Texture;
        }

        Rect LayerViewport(ulong id)
        {
            if (id == 0)
            {
                return RootOutputViewport;
            }
            var region = Region(id);
            return new Rect(0, 0, region.width, region.height);
        }

        RectInt BackdropReadRect(ulong source, ulong target)
        {
            var bounds = Region(source);
            var parent = Region(target);
            var viewport = LayerViewport(target);
            var scaleX = viewport.width / parent.width;
            var scaleY = viewport.height / parent.height;
            int left = Mathf.FloorToInt(viewport.x + (bounds.x - parent.x) * scaleX);
            int right = Mathf.CeilToInt(viewport.x + (bounds.xMax - parent.x) * scaleX);
            int bottom = Mathf.FloorToInt(viewport.y + viewport.height - (bounds.yMax - parent.y) * scaleY);
            int top = Mathf.CeilToInt(viewport.y + viewport.height - (bounds.y - parent.y) * scaleY);
            return new RectInt(left, bottom, Math.Max(1, right - left), Math.Max(1, top - bottom));
        }

        void CopySurfaceRegion(RenderTexture destination, RectInt read)
        {
            if (IsScreenOutput)
            {
                CopyScreenRegion(destination, read);
                return;
            }
            renderState.DisableScissor();
            buffer.SetGlobalVector(LayerReadRect, new Vector4(read.x, read.y, read.width, read.height));
            buffer.SetGlobalFloat(LayerReadCameraTarget, 0);
            buffer.Blit(SubmissionOutput, destination, layerCompositeMaterial, CopyBackdropPass);
            renderState.InvalidateTarget();
        }

        void PreparePrimaryLayerTargets()
        {
            desiredLayerTargets.Clear();
            layerBackdropSizes.Clear();
            foreach (var pair in layerRegions)
            {
                if (!layerAliases.ContainsKey(pair.Key))
                {
                    desiredLayerTargets.Add((pair.Key, 0));
                }
            }
            // 已有辅助资源暂存至最终计划就绪；准备回调只需要真实主层目标。
            foreach (var pair in preparedLayerTargets)
            {
                if (pair.Key.Item2 != 0 && desiredLayerTargets.Add(pair.Key))
                {
                    layerBackdropSizes.Add(pair.Key, new Vector2Int(pair.Value.Texture.width, pair.Value.Texture.height));
                }
            }
            ApplyLayerTargetRequests();
        }

        unsafe void PrepareFinalLayerTargets(in RenderPlanPacket packet)
        {
            using var profile = finalTargetPhase.Auto();
            desiredLayerTargets.Clear();
            layerBackdropSizes.Clear();
            var requests = new ReadOnlySpan<RenderPlanTargetRequest>((void*)packet.Targets, checked((int)packet.TargetCount));
            foreach (var request in requests)
            {
                if (request.Kind < 1 || request.Kind > 7 || request.FirstOperation > request.LastOperation || request.LastOperation >= packet.OperationCount)
                {
                    throw new InvalidOperationException("原生目标请求无效。");
                }
                var key = (request.Layer, request.Slot);
                if (!desiredLayerTargets.Add(key))
                {
                    throw new InvalidOperationException("原生目标请求重复。");
                }
                Vector2Int size;
                switch (request.Kind)
                {
                    case PlanTargetKind.Layer:
                        if (request.Slot != 0 || request.Layer == 0)
                        {
                            throw new InvalidOperationException("原生主层目标无效。");
                        }
                        size = Region(request.Layer).size;
                        break;
                    case PlanTargetKind.MaskScratch:
                        if (request.Slot != 1 && request.Slot != 2)
                        {
                            throw new InvalidOperationException("原生临时目标无效。");
                        }
                        size = Region(request.Layer).size;
                        break;
                    case PlanTargetKind.RootCapture:
                        if (!IsScreenOutput || request.Layer != 0 || request.Slot != -3)
                        {
                            throw new InvalidOperationException("原生屏幕解析目标无效。");
                        }
                        size = ScreenOutputSize();
                        break;
                    case PlanTargetKind.Backdrop:
                        if (request.Slot < 3)
                        {
                            throw new InvalidOperationException("原生背景目标无效。");
                        }
                        size = BackdropReadRect(request.Layer, request.RelatedLayer).size;
                        break;
                    default:
                        throw new InvalidOperationException("未知原生目标类型。");
                }
                if (request.Slot != 0)
                {
                    layerBackdropSizes.Add(key, size);
                }
            }
            ApplyLayerTargetRequests();
        }

        void ApplyLayerTargetRequests()
        {
            var desired = desiredLayerTargets;
            var backdropSizes = layerBackdropSizes;
            var format = targetCamera && targetCamera.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            var screenFormat = IsScreenOutput ? ScreenCaptureFormat() : format;
            var removed = removedLayerTargets;
            removed.Clear();
            var retired = retiredLayerResources;
            retired.Clear();
            foreach (var pair in preparedLayerTargets)
            {
                bool keep = desired.Contains(pair.Key);
                if (keep)
                {
                    var size = pair.Key.Item2 != 0 ? backdropSizes[pair.Key] : Region(pair.Key.Item1).size;
                    var texture = pair.Value.Texture;
                    keep = texture.width == size.x && texture.height == size.y && texture.format == LayerFormat(pair.Key, screenFormat, format);
                }
                if (keep && pair.Key.Item2 == 0)
                {
                    var expected = layerVersions[pair.Key.Item1];
                    var content = layerContents[pair.Key.Item1];
                    keep = content.Cacheable == expected.Cacheable && (!content.Cacheable || content.Version == expected.Version)
                        || content.TryUpdateUnpublished(expected.Version, expected.Cacheable);
                }
                if (!keep)
                {
                    removed.Add(pair.Key);
                    retired.Add(pair.Key.Item2 == 0 ? (IDisposable)layerContents[pair.Key.Item1] : pair.Value);
                }
            }
            if (retired.Count != 0)
            {
                RetirePaintResources(retired);
                foreach (var key in removed)
                {
                    preparedLayerTargets.Remove(key);
                    if (key.Item2 == 0)
                    {
                        layerContents.Remove(key.Item1);
                    }
                }
                retired.Clear();
            }
            effectTargets ??= new EffectTargetPool();
            foreach (var key in desired)
            {
                if (!preparedLayerTargets.ContainsKey(key))
                {
                    var size = key.Item2 != 0 ? backdropSizes[key] : Region(key.Item1).size;
                    var lease = effectTargets.Rent(size.x, size.y, LayerFormat(key, screenFormat, format));
                    preparedLayerTargets.Add(key, lease);
                    if (key.Item2 == 0)
                    {
                        var version = layerVersions[key.Item1];
                        layerContents.Add(key.Item1, LayerContentLease.Rent(lease, version.Version, version.Cacheable));
                    }
                }
            }
        }

        void BeginLayers()
        {
            layerCursor = 0;
            activeLayer = 0;
            layerBound = false;
            scissoredLayer = 0;
            hasPartialLayerWrites = false;
            screenCaptureValid = false;
            layerTargets.Clear();
        }

        RenderTargetIdentifier LayerTarget(ulong id)
        {
            if (id == 0)
            {
                return SubmissionOutput;
            }
            if (!layerTargets.Contains(id))
            {
                throw new InvalidOperationException("URUI 图层资源不存在：" + id);
            }
            return new RenderTargetIdentifier(LayerTexture(id));
        }

        void CreateLayer(ulong id)
        {
            if (id == 0 || (!preparedLayerTargets.ContainsKey((id, 0)) && !layerAliases.ContainsKey(id)) || !layerTargets.Add(id))
            {
                throw new InvalidOperationException("URUI 图层资源身份无效。");
            }
        }

        void SelectLayer(ulong id)
        {
            if (layerBound && activeLayer == id)
            {
                return;
            }
            if (scissoredLayer != 0 && scissoredLayer != id)
            {
                renderState.DisableScissor();
                scissoredLayer = 0;
            }
            activeLayer = id;
            var region = Region(id);
            renderState.Bind(
                LayerTarget(id),
                LayerViewport(id),
                Matrix4x4.Ortho(region.xMin, region.xMax, region.yMax, region.yMin, -1, 1),
                scissoredLayer != 0,
                layerScissor);
            layerBound = true;
        }

        void ReleaseLayer(ulong id)
        {
            if (id == 0 || !layerTargets.Remove(id))
            {
                throw new InvalidOperationException("URUI 图层被重复释放。");
            }
        }

        void LayerComposite(ulong source, ulong target, ulong mask, int blend, LayerCommandNative command, bool clip = true)
        {
            if (source == 0)
            {
                // Surface sampling is an explicit extension. Snapshot only the
                // requested destination bounds; filtering never writes the
                // camera target until the bounded final composite.
                CopySurfaceRegion(LayerTexture(target), BackdropReadRect(target, 0));
                layerBound = false;
                SelectLayer(activeLayer);
                return;
            }
            var sourceRegion = Region(source);
            EffectComposite effectComposite = default;
            var unityComposite = source != target && elementComposites.TryGetValue(source, out effectComposite) && effectComposite.UsesUnityBlend;
            var fetchMask = framebufferFetchActive && source == target && blend == 1 && !clip;
            var hasClip = clip && (command.flags & 1) != 0;
            if (hasClip)
            {
                var clipRight = command.clip_x + command.clip_w;
                var clipBottom = command.clip_y + command.clip_h;
                if (target != source && blend == 0 && (command.clip_w <= 0 || command.clip_h <= 0 ||
                    command.clip_x >= sourceRegion.xMax || command.clip_y >= sourceRegion.yMax ||
                    clipRight <= sourceRegion.x || clipBottom <= sourceRegion.y))
                {
                    return;
                }
                if (command.clip_x <= sourceRegion.x && command.clip_y <= sourceRegion.y &&
                    clipRight >= sourceRegion.xMax && clipBottom >= sourceRegion.yMax)
                {
                    hasClip = false;
                }
            }
            var fastAlphaOver = !fetchMask && !unityComposite && blend == 0 && mask == 0 && !hasClip && target != source;

            if (!fastAlphaOver)
            {
                buffer.SetGlobalFloat(LayerMaskEnabled, mask == 0 ? 0 : scalarMaskLayers.Contains(mask) ? 2 : 1);
                if (mask != 0)
                {
                    var region = Region(mask);
                    buffer.SetGlobalTexture(LayerMask, LayerTexture(mask));
                    buffer.SetGlobalVector(LayerMaskRect, new Vector4(region.x, region.y, region.width, region.height));
                }
                buffer.SetGlobalFloat(LayerClipEnabled, hasClip ? command.flags : 0);
                buffer.SetGlobalVector(LayerClip, new Vector4(command.clip_x, command.clip_y, command.clip_x + command.clip_w, command.clip_y + command.clip_h));
            }
            if (!fetchMask)
            {
                buffer.SetGlobalTexture(LayerSource, LayerTexture(source));
            }
            if (target == source && !fetchMask)
            {
                var bounds = Region(source);
                layerBound = false;
                renderState.BindOffscreen(
                    LayerTexture(source, 1),
                    new Rect(0, 0, bounds.width, bounds.height),
                    Matrix4x4.Ortho(bounds.xMin, bounds.xMax, bounds.yMax, bounds.yMin, -1, 1));
            }
            else
            {
                SelectLayer(target);
                if (target == 0)
                {
                    screenCaptureValid = false;
                }
            }
            var pass = fetchMask ? 0 : unityComposite ? EffectCompositePass : fastAlphaOver ? FastCompositePass : blend;
            buffer.DrawMesh(layerCompositeMesh, Matrix4x4.TRS(new Vector3(sourceRegion.x, sourceRegion.y, 0), Quaternion.identity, new Vector3(sourceRegion.width, sourceRegion.height, 1)),
                fetchMask ? framebufferLayerMaterial : unityComposite ? ElementCompositeMaterial(effectComposite) : layerCompositeMaterial,
                0, pass);
            if (target == source && !fetchMask)
            {
                CopyLayerData(LayerTexture(source, 1), LayerTexture(source));
                layerBound = false;
                SelectLayer(activeLayer);
            }
        }

        void ApplyNativeLayer(int index, bool skipWrites, in RenderPlanOperation operation)
        {
            if (layerCursor >= layerOrderCount || layerOrder[layerCursor++] != index)
            {
                throw new InvalidOperationException("原生图层顺序不一致。");
            }
            var command = layerCommands[index];
            if ((operation.Reason & 2) != 0)
            {
                if (command.kind != LayerCommandKind.Composite || command.blend != 0 || command.mask != 0 || command.source == command.target)
                {
                    throw new InvalidOperationException("原生最终裁剪不是普通组合成。");
                }
                var clip = operation.Bounds;
                var x = checked((int)clip.x);
                var y = checked((int)clip.y);
                var right = checked((int)clip.z);
                var bottom = checked((int)clip.w);
                if (x != clip.x || y != clip.y || right != clip.z || bottom != clip.w || right < x || bottom < y)
                {
                    throw new InvalidOperationException("原生最终裁剪必须是有效整数像素域。");
                }
                // 保留原始输入数组；仅本次合成消费二值遮罩等价裁剪。
                command.flags |= 1;
                command.clip_x = x;
                command.clip_y = y;
                command.clip_w = checked(right - x);
                command.clip_h = checked(bottom - y);
            }
            switch (command.kind)
            {
                case LayerCommandKind.Push:
                    CreateLayer(command.target);
                    SelectLayer(command.target);
                    if (!skipWrites)
                    {
                        ClearLayer(command.target, operation);
                    }
                    break;
                case LayerCommandKind.Pop:
                    SelectLayer(command.target);
                    ReleaseLayer(command.source);
                    break;
                case LayerCommandKind.SaveMask:
                    CreateLayer(command.target);
                    if (!skipWrites && !layerAliases.ContainsKey(command.target))
                    {
                        CopyLayerData(LayerTexture(command.source), LayerTexture(command.target));
                        RecordLayerWrite(command.target);
                    }
                    break;
                case LayerCommandKind.Composite:
                    if (command.blend < 0 || command.blend > 1)
                    {
                        throw new NotSupportedException("未支持的图层混合方式。");
                    }
                    if (!skipWrites)
                    {
                        LayerComposite(command.source, command.target, command.mask, command.blend, command);
                    }
                    break;
                case LayerCommandKind.ReleaseMask:
                    ReleaseLayer(command.source);
                    break;
                case LayerCommandKind.ApplyMask:
                    if (skipWrites)
                    {
                        break;
                    }
                    if (elementFilterElements.ContainsKey(index))
                    {
                        DrawElementEffect(index, command, operation);
                        break;
                    }
                    if (command.blend == 1)
                    {
                        LayerComposite(command.source, command.source, command.mask, 1, command, false);
                    }
                    else
                    {
                        renderState.DisableScissor();
                        layerBound = false;
                        var context = pooledFilterContext;
                        context.Open(buffer, layerCompositeMesh, LayerTexture(command.source), LayerTexture(command.source, 1), LayerTexture(command.source, 2));
                        try
                        {
                            OwnerSurface.FilterOwner(command.mask).RecordFilter(context, filterInvocations[index]);
                            effectSubmitted.Add(new EffectSubmission { ProcessingPasses = context.ProcessingPasses });
                        }
                        finally
                        {
                            context.Close();
                            renderState.InvalidateTarget();
                        }
                        CopyLayerData(LayerTexture(command.source, 2), LayerTexture(command.source));
                        layerBound = false;
                        SelectLayer(activeLayer);
                    }
                    break;
                default:
                    throw new NotSupportedException("未知 URUI 图层操作：" + command.kind);
            }
        }

        void FinishLayers(int commandCount, ref PhaseFrame phase)
        {
            using var profile = finishRecordingPhase.Auto();
            if (layerCursor != layerOrderCount || activeLayer != 0 || scissoredLayer != 0 || layerTargets.Count != 0)
            {
                throw new InvalidOperationException("URUI 图层或遮罩未平衡释放。");
            }
        }

        sealed class LayerProgramRelease : IDisposable
        {
            Material material;
            Mesh mesh;
            internal LayerProgramRelease(Material material, Mesh mesh)
            {
                this.material = material;
                this.mesh = mesh;
            }
            public void Dispose()
            {
                if (material)
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(material);
                }
                if (mesh)
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(mesh);
                }
                material = null;
                mesh = null;
            }
        }

        internal void ReleaseEmptySurfaceTargets()
        {
            if (captures.Count == 0 && preparedLayerTargets.Count == 0 && layerContents.Count == 0)
            {
                return;
            }
            retiredLayerResources.Clear();
            try
            {
                ClearLayerRecording();
                foreach (var capture in captures.Values)
                {
                    if (capture.Lease != null)
                    {
                        retiredLayerResources.Add(capture.Lease);
                    }
                }
                foreach (var pair in preparedLayerTargets)
                {
                    retiredLayerResources.Add(pair.Key.Item2 == 0 ? (IDisposable)layerContents[pair.Key.Item1] : pair.Value);
                }
                if (retiredLayerResources.Count > 0)
                {
                    RetirePaintResources(retiredLayerResources, null, tearingDown: false);
                }
            }
            finally
            {
                retiredLayerResources.Clear();
                captures.Clear();
                preparedLayerTargets.Clear();
                layerContents.Clear();
                layerVersions.Clear();
                layerTargets.Clear();
                maskSourceLayers.Clear();
                scalarMaskLayers.Clear();
                layerRegions.Clear();
                layerAliases.Clear();
                ClearNativeRecording();
            }
        }

        void ReleaseLayerRendering(bool tearingDown = false)
        {
            var retired = new List<IDisposable>();
            try
            {
                ClearLayerRecording();
                foreach (var capture in captures.Values)
                {
                    if (capture.Lease != null)
                    {
                        retired.Add(capture.Lease);
                    }
                }
                foreach (var pair in preparedLayerTargets)
                {
                    retired.Add(pair.Key.Item2 == 0 ? (IDisposable)layerContents[pair.Key.Item1] : pair.Value);
                }
                foreach (var program in elementCompositeMaterials.Values)
                {
                    retired.Add(new LayerProgramRelease(program, null));
                }
                if (layerCompositeMaterial || layerCompositeMesh)
                {
                    retired.Add(new LayerProgramRelease(layerCompositeMaterial, layerCompositeMesh));
                }
                if (framebufferLayerMaterial)
                {
                    retired.Add(new LayerProgramRelease(framebufferLayerMaterial, null));
                }
                if (retired.Count > 0)
                {
                    RetirePaintResources(retired, null, tearingDown);
                }
            }
            finally
            {
                captures.Clear();
                elementCompositeMaterials.Clear();
                elementComposites.Clear();
                framebufferLayerMaterial = null;
                framebufferFetchActive = framebufferProgramsChecked = false;
                effectGeometryCache.Clear();
                usedEffectGeometries.Clear();
                removedEffectGeometries.Clear();
                preparedLayerTargets.Clear();
                frozenFilterInvocations.Clear();
                layerContents.Clear();
                layerVersions.Clear();
                textureVersions.Clear();
                effectTargets?.Dispose();
                effectTargets = null;
                layerCompositeMaterial = null;
                layerCompositeMesh = null;
                layerTargets.Clear();
                maskSourceLayers.Clear();
                scalarMaskLayers.Clear();
                layerRegions.Clear();
                layerAliases.Clear();
                submissionTarget = null;
                submissionTargetInfo = default;
            }
        }
    }
}
