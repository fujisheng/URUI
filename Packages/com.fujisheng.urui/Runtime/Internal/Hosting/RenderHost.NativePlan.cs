using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;
using URUI.Internal.Rendering.Shaders;
using URUI.Rendering.Effects;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost : IRenderPlanPreparationBindings
    {
        const int TextureBindingBudget = 8;
#if UNITY_EDITOR
        // 只保存最后一次成功编译的数值，供诊断读取；不持有原生视图或租约。
        internal int NativePlanFrame
        {
            get;
            private set;
        }
        internal uint NativePlanCommandCount
        {
            get;
            private set;
        }
        internal ulong NativePlanGeometryBytes
        {
            get;
            private set;
        }
        internal ulong NativePlanFrameBytes
        {
            get;
            private set;
        }
        internal ulong NativePlanAllocations
        {
            get;
            private set;
        }
        internal ulong NativePlanFrees
        {
            get;
            private set;
        }
        internal ulong NativePlanAllocatedBytes
        {
            get;
            private set;
        }
#endif
        internal long GeometryUploadBytes
        {
            get;
            private set;
        }
        NativeRenderPlanLease planInputLease;
        RenderPlanResources planResources;
        RenderParameterPages parameterPages;
        readonly List<EffectInput> singleEffectInput = new(1);
        readonly Dictionary<int, ulong> preparedSourceGeometry = new();
        readonly Dictionary<ulong, Material> imagePrograms = new();
        readonly HashSet<int> parameterImageCommands = new();
        readonly Dictionary<int, ExternalCommandRecording> externalRecordings = new();
        readonly Dictionary<int, int> externalPaintIndices = new();
        readonly List<ExternalCommandRecording> externalRecordingPool = new();
        CommandBuffer directSubmissionBuffer;
        readonly Dictionary<ulong, CaptureResource> captures = new();
        readonly HashSet<ulong> usedCaptures = new();
        readonly List<ulong> removedCaptures = new();
        readonly List<CommandBuffer> mainBuffers = new();
        readonly List<CommandBuffer> recordedBuffers = new();
        readonly List<CommandBuffer> attachedBuffers = new();
        RenderPlanTextureBinding[] planTextureBindings = Array.Empty<RenderPlanTextureBinding>();
        readonly HashSet<ulong> planTextures = new();
        RenderPlanExternalBinding[] externalBindings = Array.Empty<RenderPlanExternalBinding>();
        NativeArray<float> feedbackTransforms;
        int feedbackTransformCursor;
        int mainBufferCursor;
        ulong recordingSerial;
        ulong resourceSerial;
        IntPtr nativeInputCommands;
        IntPtr nativeInputMatrices;
        IntPtr nativeInputUniforms;
#if UNITY_EDITOR
        CommandNative[] commands = Array.Empty<CommandNative>();
#endif
        const uint CompleteGeometryReason = 0x40000000u;
        const uint SharedMatrixReason = 0x08000000u;
        const uint NoMaskOrColorFilterReason = 0x10000000u;
        static readonly int parametersId = Shader.PropertyToID("_UruiParameters");
        static readonly int baseParameterId = Shader.PropertyToID("_UruiBaseParameter");
        static readonly int maskTextureId = Shader.PropertyToID("_UruiMaskTexture");
        static readonly int maskSourceId = Shader.PropertyToID("_UruiMaskSource");
        static readonly Action<Action> noOpEffectSubmission = _ => { };
        internal long ParameterUploadBytes => parameterPages?.ParameterUploadBytes ?? 0;
        internal long ParameterCopyBytes => parameterPages?.ParameterCopyBytes ?? 0;

        sealed class CaptureResource
        {
            internal EffectTargetPool.Lease Lease;
            internal Rect Bounds;
            internal uint Version;
        }

        static Rect Rectangle(Vector4 value) => new Rect(value.x, value.y, value.z, value.w);

        unsafe void ReadNativeFrame(in RenderPlanInputPacket input)
        {
            using var profile = readInputPhase.Auto();
            nativeInputCommands = input.Commands;
            nativeInputMatrices = input.Matrices;
            nativeInputUniforms = input.Uniforms;
#if UNITY_EDITOR
            NativePlanCommandCount = input.CommandCount;
            var commandCount = checked((int)input.CommandCount);
            if (commands.Length < commandCount)
            {
                commands = new CommandNative[commandCount];
            }
            if (commandCount > 0)
            {
                new ReadOnlySpan<CommandNative>((void*)input.Commands, commandCount).CopyTo(commands);
            }
#endif
            ReadPaintCommands(input);
            layerCount = checked((int)input.LayerCount);
            if (layerCommands.Length < layerCount)
            {
                layerCommands = new LayerCommandNative[layerCount];
            }
            if (layerCount > 0)
            {
                new ReadOnlySpan<LayerCommandNative>((void*)input.Layers, layerCount).CopyTo(layerCommands);
            }
            liveFilterNames.Clear();
            filterInvocations.Clear();
            elementFilterElements.Clear();
            elementComposites.Clear();
            for (var index = 0; index < layerCount; index++)
            {
                var command = layerCommands[index];
                if (command.kind != LayerCommandKind.ApplyMask || command.blend != 0)
                {
                    continue;
                }
                var status = NativeMethods.urui_filter_info(SurfaceHandle, command.mask, out var filter, filterNameBuffer, filterNameBuffer.Length, out var required);
                if (status == NativeStatus.BufferTooSmall && required > filterNameBuffer.Length && required <= 64 * 1024)
                {
                    filterNameBuffer = new byte[required];
                    status = NativeMethods.urui_filter_info(SurfaceHandle, command.mask, out filter, filterNameBuffer, filterNameBuffer.Length, out required);
                }
                if (status != NativeStatus.Ok || required <= 0 || required > filterNameBuffer.Length)
                {
                    throw new InvalidOperationException("URUI 滤镜描述读取失败。");
                }
                liveFilterNames.Add(command.mask);
                if (!filterNames.TryGetValue(command.mask, out var name))
                {
                    name = Encoding.UTF8.GetString(filterNameBuffer, 0, required - 1);
                    filterNames.Add(command.mask, name);
                }
                var offset = new Vector2(filter.x, filter.y);
                var color = new Color(filter.r, filter.g, filter.b, filter.a);
                if (!frozenFilterInvocations.TryGetValue(command.mask, out var invocation) || invocation.Name != name ||
                    invocation.Value != filter.value || invocation.Sigma != filter.sigma || !invocation.Offset.Equals(offset) || !invocation.Color.Equals(color))
                {
                    invocation = new FilterInvocation(name, filter.value, filter.sigma, offset, color, default);
                    frozenFilterInvocations[command.mask] = invocation;
                }
                filterInvocations.Add(index, invocation);
                if (filter.element != 0)
                {
                    elementFilterElements.Add(index, filter.element);
                }
            }
            removedFilterNames.Clear();
            foreach (var id in filterNames.Keys)
            {
                if (!liveFilterNames.Contains(id))
                {
                    removedFilterNames.Add(id);
                }
            }
            foreach (var id in removedFilterNames)
            {
                filterNames.Remove(id);
                frozenFilterInvocations.Remove(id);
            }
        }

        void IRenderPlanPreparationBindings.BeginFeedbackBatch(int elementLayoutCount)
        {
            feedbackTransformCursor = 0;
            if (elementLayoutCount <= 0)
            {
                return;
            }
            var needed = checked(elementLayoutCount * 64 * 16);
            if (!feedbackTransforms.IsCreated || feedbackTransforms.Length < needed)
            {
                if (feedbackTransforms.IsCreated)
                {
                    feedbackTransforms.Dispose();
                }
                feedbackTransforms = new NativeArray<float>(needed, Allocator.Persistent);
            }
        }

        unsafe RenderPlanFeedback IRenderPlanPreparationBindings.Prepare(in RenderPlanPrepareRequest request)
        {
            var feedback = new RenderPlanFeedback();
            var slot = checked((int)request.Slot);
            if (request.Kind == PlanPrepareKind.PrepareImage)
            {
                preparedSourceGeometry.Add(slot, request.Geometry);
                var owner = OwnerSurface.ElementOwner(request.Element) ?? throw new InvalidOperationException("图片效果所属文档已失效。");
                imageEffectSlots.TryGetValue(request.Element, out var input);
                imageEffectSlots[request.Element] = ++input;
                imageEffectInputKeys.Add(slot, input);
                singleEffectInput.Clear();
                var nativeCommand = ((CommandNative*)nativeInputCommands)[slot];
                var texture = LoadTexture(nativeCommand.texture);
                var programName = PaintName(request.Program);
                var borrowed = URUI.Internal.Resources.BorrowedTextureRegistry.IsBorrowed(nativeCommand.texture);
                if (owner.IsParameterImage(programName))
                {
                    var uniforms = (Vector4*)nativeInputUniforms;
                    var frozenSource = new PreparedSourceGeometry(FreezeOriginalEffectGeometry(nativeCommand.geometry), request.Geometry, uniforms[slot * 2], uniforms[slot * 2 + 1]);
                    singleEffectInput.Add(new EffectInput(request.Element, programName, texture, borrowed, null, input: input, preparedSource: frozenSource));
                }
                else
                {
                    frameUsedGeometry.Add(request.Geometry);
                    singleEffectInput.Add(new EffectInput(request.Element, programName, texture, borrowed, FreezeEffectGeometry(request.Geometry), input: input));
                }
                owner.PrepareEffects(singleEffectInput);
                var effectKind = owner.EffectKindForName(programName);
                var bounded = BindParameterProgram(owner, request, input, effectKind);
                if (bounded && recordingForContext && framebufferFetchActive && targetCamera.allowHDR && ShaderContractActive)
                {
                    feedback.Capabilities = LocalImageCoverage;
                }
                var ink = owner.EffectInkBounds(request.Element, effectKind, input);
                feedback.Ink = new Vector4(ink.x, ink.y, ink.width, ink.height);
                return feedback;
            }
            var filterOwner = OwnerSurface.FilterOwner(request.Program);
            var invocation = filterInvocations[slot];
            if (request.Kind == PlanPrepareKind.ElementLayout)
            {
                Span<Matrix4x4> fixedTransforms = stackalloc Matrix4x4[64];
                var typed = filterOwner.ResolveProgramCoverage(request.Element, invocation.Name, fixedTransforms, out var fixedCount, out var fixedExpansion, out var composite);
                Matrix4x4[] transforms = null;
                feedback.Expansion = typed ? fixedExpansion : filterOwner.ResolveEffectOutput(request.Element, invocation.Name, out composite, out transforms);
                var transformCount = typed ? fixedCount : transforms.Length;
                elementComposites[request.Layer] = composite;
                feedback.Capabilities = composite.UsesUnityBlend ? 1u : 0;
                if (transformCount > 64)
                {
                    throw new InvalidOperationException("节点效果覆盖变换超出上限。");
                }
                if (transformCount != 0)
                {
                    var offset = feedbackTransformCursor;
                    var floatsNeeded = transformCount * 16;
                    if (!feedbackTransforms.IsCreated || offset + floatsNeeded > feedbackTransforms.Length)
                    {
                        throw new InvalidOperationException("效果反馈变换缓冲容量不足。");
                    }
                    for (var matrix = 0; matrix < transformCount; matrix++)
                    {
                        var dst = offset + matrix * 16;
                        for (var entry = 0; entry < 16; entry++)
                        {
                            feedbackTransforms[dst + entry] = typed ? fixedTransforms[matrix][entry] : transforms[matrix][entry];
                        }
                    }
                    feedback.Transforms = (IntPtr)((float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(feedbackTransforms) + offset);
                    feedback.TransformCount = (uint)transformCount;
                    feedbackTransformCursor = offset + floatsNeeded;
                }
                return feedback;
            }
            if (request.Kind == PlanPrepareKind.AcquireCapture)
            {
                var bounds = Rectangle(request.Capture);
                var format = targetCamera.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                effectTargets ??= new EffectTargetPool();
                if (!captures.TryGetValue(request.Program, out var capture))
                {
                    capture = new CaptureResource();
                    captures.Add(request.Program, capture);
                }
                var texture = capture.Lease?.Texture;
                if (!texture || texture.width != (int)bounds.width || texture.height != (int)bounds.height || texture.format != format)
                {
                    if (capture.Lease != null)
                    {
                        RetirePaintResource(capture.Lease);
                    }
                    capture.Lease = effectTargets.Rent((int)bounds.width, (int)bounds.height, format);
                }
                texture = capture.Lease.Texture;
                if (!texture.IsCreated())
                {
                    texture.Create();
                }
                capture.Bounds = bounds;
                capture.Version = unchecked(++elementEffectVersion);
                if (capture.Version == 0)
                {
                    capture.Version = ++elementEffectVersion;
                }
                usedCaptures.Add(request.Program);
                feedback.Resource = request.Program;
                feedback.SourceVersion = capture.Version;
                feedback.PixelWidth = (uint)texture.width;
                feedback.PixelHeight = (uint)texture.height;
                feedback.Ink = request.Capture;
                return feedback;
            }
            if (request.Kind != PlanPrepareKind.PrepareElement)
            {
                throw new InvalidOperationException("未知效果准备请求。");
            }
            var source = captures[request.Resource];
            singleEffectInput.Clear();
            singleEffectInput.Add(new EffectInput(request.Element, invocation.Name, source.Lease.Texture, false,
                EffectGeometry.CapturedQuad, source.Bounds, Rectangle(request.SourceInk)));
            filterOwner.PrepareEffects(singleEffectInput, source.Version);
            BindParameterProgram(filterOwner, request, 0, EffectKind.Element);
            var output = filterOwner.EffectInkBounds(request.Element, EffectKind.Element);
            feedback.Ink = new Vector4(output.x, output.y, output.width, output.height);
            return feedback;
        }

        unsafe bool BindParameterProgram(URUI.Documents.Document owner, in RenderPlanPrepareRequest request, ulong input, EffectKind kind)
        {
            owner.BeginEffectProgramRead();
            try
            {
                if (owner.TryGetImageProgram(request.Element, input, out var program, out var layout, out var frame, out var renderProgram, out var coverage, kind))
                {
                    if (renderProgram is IParameterImageGeometry template)
                    {
                        var geometry = externalGeometryInputs.CaptureImageGeometry(template.Geometry);
                        var local = template.GeometryTransform;
                        NativeRenderPlanLease.Require(NativeMethods.urui_plan_image_geometry(SurfaceHandle, planInputLease.Token, request.Request, geometry, local));
                    }
                    // Unity IDs cannot be reused while the owned material is in flight.
                    var identity = (ulong)(uint)program.GetInstanceID();
                    imagePrograms[identity] = program;
                    var runtimeCount = 2 + (renderProgram is IParameterImageRuntime extra ? extra.RuntimeRowCount : 0);
                    if (runtimeCount < 2 || runtimeCount > 32)
                    {
                        throw new InvalidOperationException("程序运行时参数超出容量。");
                    }
                    Span<Vector4> runtime = stackalloc Vector4[runtimeCount];
                    var b = frame.Bounds;
                    runtime[0] = new Vector4(b.x, b.y, b.width > 0 ? 1 / b.width : 0, b.height > 0 ? 1 / b.height : 0);
                    runtime[1] = new Vector4(frame.Size.x, frame.Size.y, (float)frame.Time, frame.SourceFlags);
                    if (runtimeCount > 2)
                    {
                        ((IParameterImageRuntime)renderProgram).CopyRuntimeParameters(runtime.Slice(2));
                    }
                    Span<Vector4> bound = frame.Overrides != null || !frame.BoundValues.IsEmpty ? stackalloc Vector4[layout.RowCount] : Span<Vector4>.Empty;
                    if (!frame.BoundValues.IsEmpty)
                    {
                        frame.BoundValues.Span.CopyTo(bound);
                    }
                    else if (!bound.IsEmpty)
                    {
                        layout.ReadBoundValues(frame.Overrides, bound);
                    }
                    fixed (Vector4* pointer = runtime)
                    {
                        fixed (Vector4* overrides = bound)
                        {
                            NativeRenderPlanLease.Require(NativeMethods.urui_plan_image_program(
                                SurfaceHandle, planInputLease.Token, request.Request, identity, owner.NativeHandle,
                                layout.Identity, (IntPtr)pointer, (uint)runtimeCount, (uint)layout.RowCount, (IntPtr)overrides, frame.Clock));
                        }
                    }
                    if (request.Kind == PlanPrepareKind.PrepareImage)
                    {
                        parameterImageCommands.Add(checked((int)request.Slot));
                    }
                    return coverage == URUI.Rendering.RenderGeometry.Local && renderProgram is not IParameterImageGeometry;
                }
                return false;
            }
            finally
            {
                owner.EndEffectProgramRead();
            }
        }

        RenderTexture IRenderPlanPreparationBindings.ResolveCapture(ulong resource) => captures[resource].Lease.Texture;

        unsafe void ApplyNativeRegions(in RenderPlanPreparePacket ready)
        {
            using var profile = regionPhase.Auto();
            if (ready.TargetFlags != 0)
            {
                throw new InvalidOperationException("原生计划请求了未声明的屏幕工作目标。");
            }
            layerRegions.Clear();
            layerVersions.Clear();
            layerAliases.Clear();
            maskSourceLayers.Clear();
            scalarMaskLayers.Clear();
            layerOrderCount = checked((int)ready.LayerIndexCount);
            if (layerOrder.Length < layerOrderCount)
            {
                layerOrder = new int[layerOrderCount];
            }
            var indices = (uint*)ready.LayerIndices;
            for (var i = 0; i < layerOrderCount; i++)
            {
                layerOrder[i] = checked((int)indices[i]);
                if (layerOrder[i] >= layerCount)
                {
                    throw new InvalidOperationException("原生图层序号越界。");
                }
            }
            var regions = new ReadOnlySpan<RenderPlanLayerRegion>((void*)ready.Regions, checked((int)ready.RegionCount));
            foreach (var region in regions)
            {
                layerVersions.Add(region.Layer, (region.Version, (region.Flags & RenderPlanLayerRegion.CacheableFlag) != 0));
                var bounds = region.Bounds;
                var logical = new RectInt((int)bounds.x, (int)bounds.y, (int)bounds.z, (int)bounds.w);
                layerRegions.Add(region.Layer, logical);
                if ((region.Flags & RenderPlanLayerRegion.AliasFlag) != 0)
                {
                    if (region.AliasLayer == 0 || region.AliasLayer == region.Layer)
                    {
                        throw new InvalidOperationException("原生遮罩别名无效。");
                    }
                    layerAliases.Add(region.Layer, region.AliasLayer);
                }
                if ((region.Flags & RenderPlanLayerRegion.MaskSourceFlag) != 0)
                {
                    maskSourceLayers.Add(region.Layer);
                }
                if ((region.Flags & RenderPlanLayerRegion.ScalarMaskFlag) != 0)
                {
                    if (!SupportsScalarMask)
                    {
                        throw new InvalidOperationException("原生计划请求了不支持的单通道遮罩。");
                    }
                    scalarMaskLayers.Add(region.Layer);
                }
            }
            if (layerCount != 0)
            {
                EnsureLayerProgram();
            }
            PreparePrimaryLayerTargets();
            for (var index = 0; index < layerCount; index++)
            {
                if (!filterInvocations.TryGetValue(index, out var invocation))
                {
                    continue;
                }
                var region = Region(layerCommands[index].source);
                if (!invocation.Region.Equals(region))
                {
                    invocation = new FilterInvocation(invocation.Name, invocation.Value, invocation.Sigma, invocation.Offset, invocation.Color, region);
                    frozenFilterInvocations[layerCommands[index].mask] = invocation;
                }
                filterInvocations[index] = invocation;
                if (!elementFilterElements.ContainsKey(index))
                {
                    OwnerSurface.FilterOwner(layerCommands[index].mask).PrepareFilter(invocation);
                }
            }
        }

        unsafe int RecordExternalCommands(in RenderPlanInputPacket input, ref PhaseFrame phase)
        {
            using var profile = externalRecordingPhase.Auto();
            var count = 0;
            var recordingCount = 0;
            externalRecordings.Clear();
            externalPaintIndices.Clear();
            if (externalBindings.Length < input.PaintCount)
            {
                externalBindings = new RenderPlanExternalBinding[input.PaintCount];
            }
            var nativeCommands = (CommandNative*)input.Commands;
            var nativeMatrices = (float*)input.Matrices;
            var layers = (ulong*)input.CommandLayers;
            try
            {
                for (var paintIndex = 0; paintIndex < input.PaintCount; paintIndex++)
                {
                    var paint = paintScratch[paintIndex];
                    if (paint.reserved == 2 || parameterImageCommands.Contains(paint.command_index))
                    {
                        continue;
                    }
                    if (geometryCommands[paint.command_index])
                    {
                        externalBindings[count++] = new RenderPlanExternalBinding
                        {
                            Command = (uint)paint.command_index,
                            Flags = 8
                        };
                        continue;
                    }
                    var index = paint.command_index;
                    var command = nativeCommands[index];
                    var layer = layers[index];
                    // 绝对命令位置每帧变化；物理缓冲按同时活动的回调序号复用。
                    if (recordingCount == externalRecordingPool.Count)
                    {
                        externalRecordingPool.Add(new ExternalCommandRecording());
                    }
                    var recording = externalRecordingPool[recordingCount++];
                    externalRecordings.Add(index, recording);
                    externalPaintIndices[index] = paintIndex;
                    var region = Region(layer);
                    var target = layer == 0 ? SubmissionOutput : new RenderTargetIdentifier(LayerTexture(layer));
                    recording.Begin(target, LayerViewport(layer), Matrix4x4.Ortho(region.xMin, region.xMax, region.yMax, region.yMin, -1, 1));
                    var transform = new Matrix4x4();
                    var matrixBase = index * 16;
                    for (var component = 0; component < 16; component++)
                    {
                        transform[component] = nativeMatrices[matrixBase + component];
                    }
                    var owner = OwnerSurface.ElementOwner(paint.element) ?? throw new InvalidOperationException("绘制器所属文档已失效。");
                    var context = paintContexts.Open(owner.WrapElement(paint.element), null,
                        transform * Matrix4x4.Translate(new Vector3(command.tx, command.ty, 0)),
                        new Vector4(command.clip_x, command.clip_y, command.clip_x + command.clip_w, command.clip_y + command.clip_h),
                        command.ClipEnabled, maskSourceLayers.Contains(layer), recording);
                    var original = false;
                    try
                    {
                        if (paint.reserved == 0)
                        {
                            owner.InvokePainter(paint.element, PaintName(paint.shader), context);
                        }
                        else
                        {
                            // RenderGraph 直接录制会在 AppendExternal -> RecordExternalDirect 再次录制真实提交，
                            // 探针阶段不重复登记 EffectSubmissionSink，避免提交回调每帧双倍入队。
                            var submissionSink = directSubmissionBuffer != null ? noOpEffectSubmission : owner.EffectSubmissionSink;
                            original = !owner.RecordEffect(paint.element, context, SourceMesh(ReadGeometry(preparedSourceGeometry[index])),
                                submissionSink, owner.EffectKindForName(PaintName(paint.shader)), imageEffectInputKeys[index]);
                        }
                    }
                    finally
                    {
                        context.Close();
                    }
                    externalBindings[count++] = new RenderPlanExternalBinding
                    {
                        Command = (uint)index,
                        Flags = (recording.WritesParent ? 1u : 0) | (original ? 2u : 0) | (recording.WritesSurface ? 4u : 0)
                    };
                }
            }
            finally
            {
                for (var i = recordingCount; i < externalRecordingPool.Count; i++)
                {
                    externalRecordingPool[i].Begin(default, default, Matrix4x4.identity);
                }
            }
            return count;
        }

        void DetachRecordedBuffers()
        {
            if (targetCamera)
            {
                foreach (var commands in attachedBuffers)
                {
                    targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, commands);
                }
            }
            attachedBuffers.Clear();
        }

        void ClearNativeRecording()
        {
            DetachRecordedBuffers();
            ClearLayerRecording();
            foreach (var commands in mainBuffers)
            {
                commands.Clear();
            }
            recordedBuffers.Clear();
            hasExternalSurfaceWrites = false;
            parameterPages?.EndRecording();
            planResources?.EndRecording();
            externalGeometryInputs?.Programs.EndRecording();
        }

        void BeginNativeRecording()
        {
            DetachRecordedBuffers();
            ClearLayerRecording();
            if (directSubmissionBuffer != null)
            {
                recordedBuffers.Clear();
                hasExternalSurfaceWrites = false;
                buffer = directSubmissionBuffer;
                renderState.Reset(buffer);
                return;
            }
            if (mainBuffers.Count == 0)
            {
                mainBuffers.Add(buffer);
            }
            foreach (var commands in mainBuffers)
            {
                commands.Clear();
            }
            recordedBuffers.Clear();
            hasExternalSurfaceWrites = false;
            mainBufferCursor = 0;
            buffer = mainBuffers[0];
            renderState.Reset(buffer);
            recordedBuffers.Add(buffer);
        }

        void AppendExternal(ExternalCommandRecording recording, int commandIndex, in RenderPlanInputPacket input)
        {
            if (recording.WritesSurface)
            {
                hasExternalSurfaceWrites = true;
            }
            if (activeLayer == 0)
            {
                screenCaptureValid = false;
            }
            if (directSubmissionBuffer != null)
            {
                RecordExternalDirect(commandIndex, input);
                renderState.Reset(buffer);
                layerBound = false;
                SelectLayer(activeLayer);
                return;
            }
            for (var i = 0; i < recording.Count; i++)
            {
                recordedBuffers.Add(recording[i].Buffer);
            }
            ContinueNativeRecording();
            // 外部绘制可切换目标和坐标系，回到 URUI 时显式恢复当前图层。
            SelectLayer(activeLayer);
        }

        unsafe void RecordExternalDirect(int commandIndex, in RenderPlanInputPacket input)
        {
            if (!externalPaintIndices.TryGetValue(commandIndex, out var paintIndex))
            {
                return;
            }
            var paint = paintScratch[paintIndex];
            var command = ((CommandNative*)input.Commands)[commandIndex];
            var nativeMatrices = (float*)input.Matrices;
            var layer = activeLayer;
            var region = Region(layer);
            var target = layer == 0 ? SubmissionOutput : new RenderTargetIdentifier(LayerTexture(layer));
            buffer.SetRenderTarget(target);
            buffer.SetViewport(LayerViewport(layer));
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(region.xMin, region.xMax, region.yMax, region.yMin, -1, 1));
            var transform = new Matrix4x4();
            var matrixBase = commandIndex * 16;
            for (var component = 0; component < 16; component++)
            {
                transform[component] = nativeMatrices[matrixBase + component];
            }
            var owner = OwnerSurface.ElementOwner(paint.element) ?? throw new InvalidOperationException("绘制器所属文档已失效。");
            var context = paintContexts.Open(owner.WrapElement(paint.element), directSubmissionBuffer,
                transform * Matrix4x4.Translate(new Vector3(command.tx, command.ty, 0)),
                new Vector4(command.clip_x, command.clip_y, command.clip_x + command.clip_w, command.clip_y + command.clip_h),
                command.ClipEnabled, maskSourceLayers.Contains(layer), null);
            try
            {
                if (paint.reserved == 0)
                {
                    owner.InvokePainter(paint.element, PaintName(paint.shader), context);
                }
                else
                {
                    owner.RecordEffect(paint.element, context, SourceMesh(ReadGeometry(preparedSourceGeometry[commandIndex])),
                        owner.EffectSubmissionSink, owner.EffectKindForName(PaintName(paint.shader)), imageEffectInputKeys[commandIndex]);
                }
            }
            finally
            {
                context.Close();
            }
        }

        void ContinueNativeRecording()
        {
            layerBound = false;
            if (directSubmissionBuffer != null)
            {
                renderState.Reset(buffer);
                return;
            }
            if (++mainBufferCursor == mainBuffers.Count)
            {
                mainBuffers.Add(new CommandBuffer { name = "URUI UI continuation" });
            }
            buffer = mainBuffers[mainBufferCursor];
            renderState.Reset(buffer);
            recordedBuffers.Add(buffer);
        }

        void AttachRecordedBuffers()
        {
            if (recordingSerial == ulong.MaxValue)
            {
                throw new InvalidOperationException("绘制记录代际已耗尽。");
            }
            recordingSerial++;
            if (directSubmissionBuffer == null && mainBuffers.Count > 0)
            {
                buffer = mainBuffers[0];
            }
            // SRP 由 RendererFeature 通过显式上下文提交；相机挂载接口只适用于 Built-in。
            if (directSubmissionBuffer != null || GraphicsSettings.currentRenderPipeline)
            {
                return;
            }
            foreach (var commands in recordedBuffers)
            {
                targetCamera.AddCommandBuffer(CameraEvent.AfterEverything, commands);
                attachedBuffers.Add(commands);
            }
        }

        void ReleaseNativePlanRendering()
        {
            contextValidationBuffer?.Release();
            contextValidationBuffer = null;
#if UNITY_EDITOR
            NativePlanFrame = 0;
            NativePlanCommandCount = 0;
            NativePlanGeometryBytes = NativePlanFrameBytes = NativePlanAllocations = NativePlanFrees = NativePlanAllocatedBytes = 0;
#endif
            planResources?.Dispose();
            planResources = null;
            externalGeometryInputs?.Dispose();
            externalGeometryInputs = null;
            geometryProviders.Clear();
            parameterPages?.Dispose();
            parameterPages = null;
            if (feedbackTransforms.IsCreated)
            {
                feedbackTransforms.Dispose();
            }
            foreach (var recording in externalRecordingPool)
            {
                recording.Dispose();
            }
            externalRecordings.Clear();
            externalPaintIndices.Clear();
            externalRecordingPool.Clear();
            foreach (var commands in mainBuffers)
            {
                if (!ReferenceEquals(commands, buffer))
                {
                    commands.Release();
                }
            }
            mainBuffers.Clear();
        }

        unsafe void DrawNativeGeometry(in RenderPlanOperation operation, RenderPlanParameter* parameters)
        {
            SelectLayer(activeLayer);
            if (activeLayer == 0)
            {
                screenCaptureValid = false;
            }
            var texture = LoadTextureInfo(operation.Texture(0), out var sourceInfo0, out _, out _);
            var properties = reusableProperties;
            properties.Clear();
            properties.SetTexture(mainTextureId, texture ? texture : white);
            TextureShaderState.Apply(properties, in sourceInfo0);
            var allHardwareFast = TextureShaderState.IsHardwareFastOnlyKind(sourceInfo0.z);
            for (var index = 1; index < operation.TextureCount; index++)
            {
                var id = operation.Texture(index);
                var source = LoadTextureInfo(id, out var slotInfo, out _, out _);
                if (!source)
                {
                    source = white;
                    slotInfo = TextureShaderState.SolidWhiteSourceInfo;
                }
                properties.SetTexture(paletteTextureIds[index - 1], source);
                properties.SetVector(paletteSourceIds[index - 1], slotInfo);
                allHardwareFast &= TextureShaderState.IsHardwareFastOnlyKind(slotInfo.z);
            }
            var complete = (operation.Reason & CompleteGeometryReason) != 0 && ShaderContractActive;
            int pass;
            if (complete)
            {
                pass = allHardwareFast ? 2 : 1;
            }
            else
            {
                pass = (operation.Reason & NoMaskOrColorFilterReason) != 0 && ShaderContractActive ? 3 : 0;
            }
            if (pass == 0)
            {
                if (operation.MaskTexture != 0)
                {
                    var maskTexture = LoadTextureInfo(operation.MaskTexture, out var maskInfo, out _, out _);
                    if (!maskTexture)
                    {
                        maskTexture = white;
                        maskInfo = TextureShaderState.SolidWhiteSourceInfo;
                    }
                    properties.SetTexture(maskTextureId, maskTexture);
                    properties.SetVector(maskSourceId, maskInfo);
                }
                else
                {
                    properties.SetTexture(maskTextureId, white);
                    properties.SetVector(maskSourceId, TextureShaderState.SolidWhiteSourceInfo);
                }
            }
            if (!complete)
            {
                properties.SetTexture(parametersId, parameterPages.Current.Texture);
                properties.SetFloat(baseParameterId, operation.BaseParameter);
            }
            var modelMatrix = complete && (operation.Reason & SharedMatrixReason) != 0 && nativeInputMatrices != IntPtr.Zero
                ? ((Matrix4x4*)nativeInputMatrices)[operation.BaseParameter]
                : Matrix4x4.identity;
            buffer.DrawMesh(planResources.Mesh(operation.Geometry), modelMatrix, material, 0, pass, properties);
            RecordedGeometryDraws++;
        }

        void DrawNativeImageProgram(in RenderPlanOperation operation)
        {
            SelectLayer(activeLayer);
            if (activeLayer == 0)
            {
                screenCaptureValid = false;
            }
            var properties = reusableProperties;
            properties.Clear();
            for (var i = 0; i < operation.TextureCount; i++)
            {
                properties.SetTexture(i == 0 ? mainTextureId : paletteTextureIds[i - 1], LoadTexture(operation.Texture(i)) ?? white);
            }
            var page = parameterPages.Current.Texture;
            properties.SetTexture(parametersId, page);
            properties.SetFloat(baseParameterId, operation.BaseParameter);
            buffer.DrawMesh(planResources.Mesh(operation.Geometry), Matrix4x4.identity, imagePrograms[operation.Program], 0, 0, properties);
            RecordedGeometryDraws++;
        }

        uint RootPixelGrid()
        {
            var viewport = submissionTarget.HasValue ? submissionViewport : targetCamera.pixelRect;
            if (viewport.width != UIWidth || viewport.height != UIHeight || viewport.x != Mathf.Floor(viewport.x) || viewport.y != Mathf.Floor(viewport.y))
            {
                return 0;
            }
            if (targetCamera.targetTexture)
            {
                if (submissionTarget.HasValue && !submissionTarget.Value.Equals(new RenderTargetIdentifier(targetCamera.targetTexture)))
                {
                    return 0;
                }
                return targetCamera.targetTexture.antiAliasing == 1 ? 1u : 0;
            }
            if (submissionTarget.HasValue && !submissionTarget.Value.Equals(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget)))
            {
                return 0;
            }
            return !targetCamera.allowMSAA || QualitySettings.antiAliasing <= 1 ? 1u : 0;
        }

        unsafe int ReadNativeResources(in RenderPlanInputPacket input)
        {
            using var profile = readResourcesPhase.Auto();
            if (resourceSerial == ulong.MaxValue)
            {
                throw new InvalidOperationException("资源输入代际已耗尽。");
            }
            resourceSerial++;
            var count = 0;
            planTextures.Clear();
            if (planTextureBindings.Length < input.CommandCount)
            {
                planTextureBindings = new RenderPlanTextureBinding[input.CommandCount];
            }
            var nativeCommands = (CommandNative*)input.Commands;
            var commandCount = (int)input.CommandCount;
            ulong lastId = ulong.MaxValue;
            for (var index = 0; index < commandCount; index++)
            {
                var id = nativeCommands[index].texture;
                if (id == lastId)
                {
                    continue;
                }
                lastId = id;
                if (!planTextures.Add(id))
                {
                    continue;
                }
                var texture = LoadTextureInfo(id, out _, out var isLive, out var normalized);
                var version = resourceSerial;
                if (!isLive && texture)
                {
                    if (textureVersions.TryGetValue(id, out var previous) && Alive(previous.Dependency))
                    {
                        version = previous.Version;
                    }
                    else
                    {
                        textureVersions[id] = (Depend(texture), version);
                    }
                }
                planTextureBindings[count++] = new RenderPlanTextureBinding
                {
                    Texture = id,
                    Version = version,
                    Domain = isLive ? 1u : 0,
                    Flags = normalized ? 1u : 0
                };
            }
            if (textureVersions.Count > 0)
            {
                removedTextureVersions.Clear();
                foreach (var id in textureVersions.Keys)
                {
                    if (!planTextures.Contains(id))
                    {
                        removedTextureVersions.Add(id);
                    }
                }
                for (var i = 0; i < removedTextureVersions.Count; i++)
                {
                    textureVersions.Remove(removedTextureVersions[i]);
                }
            }
            return count;
        }

        NativeRenderPlanLease CompileNativePlan(NativeRenderPlanLease input, in RenderPlanBindings bindings, out RenderPlanPacket packet)
        {
            using var profile = planCompilePhase.Auto();
            var lease = input.Compile(bindings, out packet);
#if UNITY_EDITOR
            NativePlanFrame = Time.frameCount;
            NativePlanGeometryBytes = packet.GeometryBytes;
            NativePlanFrameBytes = packet.FrameBytes;
            NativePlanAllocations = packet.AllocationCount;
            NativePlanFrees = packet.FreeCount;
            NativePlanAllocatedBytes = packet.AllocatedBytes;
#endif
            return lease;
        }

        unsafe void DrawFrameCore(ref PhaseFrame phase)
        {
            using var profile = recordingPhase.Auto();
            using (recordStatePhase.Auto())
            {
                RequireRecordingReady();
                CollectImageSubmissions();
                ClearNativeRecording();
                if (hasReleasedResolvedImages && !imageSubmissionFaulted)
                {
                    RetireLegacyImages();
                    hasReleasedResolvedImages = false;
                }
                RecordedGeometryDraws = RecordedExternalGeometryDraws = 0;
                ShaderContractActive = propertyContract && propertyContract.Supports(material);
                framebufferFetchActive = CanUseFramebufferFetch();
            }
            using (effectParametersPhase.Auto())
            {
                foreach (var owner in OwnerSurface.DocumentsSnapshot())
                {
                    if (owner && owner.IsLoaded)
                    {
                        owner.UpdateEffectParameters();
                        owner.BeginEffectPreparation();
                    }
                }
            }
            externalGeometryInputs ??= new ExternalGeometryInputs();
            using var geometryWindow = externalGeometryInputs.Begin();
            NativeRenderPlanLease acquired;
            RenderPlanInputPacket input;
            using (nativeRenderPhase.Auto())
            {
                acquired = NativeRenderPlanLease.Begin(SurfaceHandle, out input);
            }
            using var inputLease = acquired;
            planInputLease = inputLease;
            try
            {
                borrowedTextures.Validate();
                ReadNativeFrame(input);
                foreach (var owner in OwnerSurface.DocumentsSnapshot())
                {
                    if (owner && owner.IsLoaded)
                    {
                        owner.FreezeEffectParameters();
                    }
                }
                FreezeEffectClocks();
                imagePrograms.Clear();
                parameterImageCommands.Clear();
                usedEffectGeometries.Clear();
                frameUsedGeometry.Clear();
                usedCaptures.Clear();
                preparedSourceGeometry.Clear();
                imageEffectSlots.Clear();
                imageEffectInputKeys.Clear();
                var textureCount = ReadNativeResources(input);
                var resourceFlags = RootPixelGrid() |
                    (targetCamera.allowHDR ? RenderPlanBindings.HdrCamera : 0) |
                    (IsScreenOutput ? RenderPlanBindings.ScreenOutput : 0) |
                    RenderPlanBindings.ShaderImagePaint |
                    (framebufferFetchActive ? RenderPlanBindings.FramebufferFetch : 0);
                if (SupportsScalarMask)
                {
                    resourceFlags |= RenderPlanBindings.ScalarMaskSupported;
                }
                var providerCount = PrepareGeometryProviders(input);
                RenderPlanPreparePacket ready;
                fixed (RenderPlanExternalGeometry* geometry = externalGeometryInputs.Geometry)
                {
                    fixed (RenderPlanExternalDraw* geometryDraws = externalGeometryInputs.Draws)
                    {
                        fixed (RenderPlanExternalBinding* providers = externalBindings)
                        {
                            fixed (RenderPlanTextureBinding* resources = planTextureBindings)
                            {
                                var bindings = new RenderPlanBindings
                                {
                                    Textures = (IntPtr)resources,
                                    TextureCount = (uint)textureCount,
                                    FrameFlags = resourceFlags,
                                    Externals = (IntPtr)providers,
                                    ExternalCount = (uint)providerCount,
                                    ExternalGeometry = (IntPtr)geometry,
                                    ExternalGeometryCount = (uint)externalGeometryInputs.GeometryCount,
                                    ExternalDraws = (IntPtr)geometryDraws,
                                    ExternalDrawCount = (uint)externalGeometryInputs.DrawCount
                                };
                                using (planPreparePhase.Auto())
                                {
                                    RenderPlanPreparation.Resolve(inputLease, input, this, out ready, bindings);
                                }
                            }
                        }
                    }
                }
                ApplyNativeRegions(ready);
                foreach (var owner in OwnerSurface.DocumentsSnapshot())
                {
                    if (owner && owner.IsLoaded)
                    {
                        owner.EndEffectPreparation();
                    }
                }
                removedCaptures.Clear();
                retiredLayerResources.Clear();
                foreach (var pair in captures)
                {
                    if (!usedCaptures.Contains(pair.Key))
                    {
                        removedCaptures.Add(pair.Key);
                        if (pair.Value.Lease != null)
                        {
                            retiredLayerResources.Add(pair.Value.Lease);
                        }
                    }
                }
                if (retiredLayerResources.Count != 0)
                {
                    RetirePaintResources(retiredLayerResources);
                    retiredLayerResources.Clear();
                }
                foreach (var key in removedCaptures)
                {
                    captures.Remove(key);
                }
                PruneEffectGeometries();
                var externalCount = RecordExternalCommands(input, ref phase);
                var layerBindingCount = ReadLayerBindings();
                fixed (RenderPlanLayerBinding* layerCache = layerBindings)
                {
                    fixed (RenderPlanExternalBinding* externals = externalBindings)
                    {
                        fixed (RenderPlanTextureBinding* textures = planTextureBindings)
                        {
                            var bindings = new RenderPlanBindings
                            {
                                Textures = (IntPtr)textures,
                                TextureCount = (uint)textureCount,
                                FrameFlags = resourceFlags,
                                Externals = (IntPtr)externals,
                                ExternalCount = (uint)externalCount,
                                LayerBindings = (IntPtr)layerCache,
                                LayerBindingCount = (uint)layerBindingCount
                            };
                            using var plan = CompileNativePlan(inputLease, bindings, out var packet);
                            PrepareFinalLayerTargets(packet);
                            planResources ??= new RenderPlanResources(RetirePaintResource);
                            var before = planResources.GeometryUploadBytes;
                            using (planUploadPhase.Auto())
                            {
                                planResources.Apply(plan, packet, externalGeometryInputs);
                            }
                            GeometryUploadBytes += planResources.GeometryUploadBytes - before;
                            var operations = new ReadOnlySpan<RenderPlanOperation>((void*)packet.Operations, checked((int)packet.OperationCount));
                            var usesParameters = false;
                            foreach (var operation in operations)
                            {
                                if (operation.Kind == PlanOperationKind.Geometry ||
                                    operation.Kind == PlanOperationKind.ExternalGeometry ||
                                    operation.Kind == PlanOperationKind.ParameterImage ||
                                    operation.Program != 0)
                                {
                                    usesParameters = true;
                                    break;
                                }
                            }
                            if (usesParameters)
                            {
                                parameterPages ??= new RenderParameterPages();
                                using (planUploadPhase.Auto())
                                {
                                    parameterPages.Apply(plan, packet);
                                }
                            }
                            else
                            {
                                parameterPages?.EndRecording();
                            }
                            BeginNativeRecording();
                            BeginLayers();
                            RetireReleasedTextures(ref phase);
                            var rootProjection = Matrix4x4.Ortho(0, UIWidth, UIHeight, 0, -1, 1);
                            if (submissionTarget.HasValue)
                            {
                                renderState.Bind(submissionTarget.Value, submissionViewport, rootProjection, false, default);
                            }
                            else
                            {
                                buffer.SetViewProjectionMatrices(Matrix4x4.identity, rootProjection);
                                renderState.AssumeBound(SubmissionOutput, RootOutputViewport, rootProjection);
                            }
                            layerBound = true;
                            var packetParameters = (RenderPlanParameter*)packet.Parameters;
                            using (commandPhase.Auto())
                            {
                                foreach (var operation in operations)
                                {
                                    switch (operation.Kind)
                                    {
                                        case PlanOperationKind.Geometry:
                                            DrawNativeGeometry(operation, packetParameters);
                                            break;
                                        case PlanOperationKind.ExternalGeometry:
                                            DrawNativeExternalGeometry(operation);
                                            break;
                                        case PlanOperationKind.ParameterImage:
                                            DrawNativeImageProgram(operation);
                                            break;
                                        case PlanOperationKind.ExternalRawGeometry:
                                            DrawNativeRawGeometry(operation, packetParameters[checked((int)(input.CommandCount + operation.LayerIndex))]);
                                            break;
                                        case PlanOperationKind.External:
                                        case PlanOperationKind.Image:
                                            AppendExternal(externalRecordings[checked((int)operation.CommandIndex)], checked((int)operation.CommandIndex), in input);
                                            break;
                                        case PlanOperationKind.Layer:
                                            ApplyNativeLayer(checked((int)operation.LayerIndex), (operation.Reason & 1) != 0, operation);
                                            break;
                                        default:
                                            throw new InvalidOperationException("未知原生绘制操作。");
                                    }
                                }
                            }
                            FinishLayers(checked((int)input.CommandCount), ref phase);
                            externalGeometryInputs.Programs.CommitRecording();
                            AttachRecordedBuffers();
                        }
                    }
                }
                frameStaleGeometry.Clear();
                foreach (var pair in meshes)
                {
                    if (!frameUsedGeometry.Contains(pair.Key))
                    {
                        frameStaleGeometry.Add(pair.Key);
                    }
                }
                foreach (var key in frameStaleGeometry)
                {
                    RecycleGeometry(meshes[key]);
                    meshes.Remove(key);
                }
            }
            finally
            {
                borrowedTextures.EndFrameValidation();
                planInputLease = default;
                nativeInputCommands = IntPtr.Zero;
                nativeInputMatrices = IntPtr.Zero;
                nativeInputUniforms = IntPtr.Zero;
                frozenEffectClocks.Clear();
            }
        }
    }
}
