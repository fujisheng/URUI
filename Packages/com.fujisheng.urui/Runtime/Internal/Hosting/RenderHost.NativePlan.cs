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
        const int TextureBindingBudget=8;
#if UNITY_EDITOR
        // 只保存最后一次成功编译的数值，供诊断读取；不持有原生视图或租约。
        internal int NativePlanFrame {get;private set;}
        internal uint NativePlanCommandCount {get;private set;}
        internal ulong NativePlanGeometryBytes {get;private set;}
        internal ulong NativePlanFrameBytes {get;private set;}
        internal ulong NativePlanAllocations {get;private set;}
        internal ulong NativePlanFrees {get;private set;}
        internal ulong NativePlanAllocatedBytes {get;private set;}
#endif
        internal long GeometryUploadBytes { get; private set; }
        NativeRenderPlanLease planInputLease;
        RenderPlanResources planResources;
        RenderParameterPages parameterPages;
        readonly List<EffectInput> singleEffectInput = new(1);
        readonly Dictionary<int, ulong> preparedSourceGeometry = new();
        readonly Dictionary<ulong,Material> imagePrograms=new();
        readonly HashSet<int> parameterImageCommands=new();
        readonly Dictionary<int, ExternalCommandRecording> externalRecordings = new();
        readonly List<ExternalCommandRecording> externalRecordingPool=new();
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
        int mainBufferCursor;
        ulong recordingSerial,resourceSerial;
        IntPtr nativeInputUniforms;
        bool preRecordingPainter;
        static readonly int parametersId = Shader.PropertyToID("_UruiParameters");
        static readonly int parameterSizeId = Shader.PropertyToID("_UruiParameterSize");
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
            nativeInputUniforms=input.Uniforms;
#if UNITY_EDITOR
            NativePlanCommandCount=input.CommandCount;
#endif
            var count = checked((int)input.CommandCount);
            if (commands.Length < count) { commands = new CommandNative[Math.Max(count, commands.Length * 2 + 16)]; }
            if (commandTransforms.Length < count * 16) { commandTransforms = new float[Math.Max(count * 16, commandTransforms.Length * 2 + 256)]; }
            new ReadOnlySpan<CommandNative>((void*)input.Commands, count).CopyTo(commands);
            new ReadOnlySpan<float>((void*)input.Matrices, count * 16).CopyTo(commandTransforms);
            ReadPaintCommands();
            layerCount = checked((int)input.LayerCount);
            if (layerCommands.Length < layerCount) { layerCommands = new LayerCommandNative[layerCount]; }
            new ReadOnlySpan<LayerCommandNative>((void*)input.Layers, layerCount).CopyTo(layerCommands);
            liveFilterNames.Clear(); layerEntries.Clear(); filterInvocations.Clear();
            elementFilterElements.Clear(); elementComposites.Clear(); effectInputRegions.Clear();
            for (var index = 0; index < layerCount; index++)
            {
                var command = layerCommands[index];
                if (command.kind == 1) { layerEntries.Add(command.target, command); }
                if (command.kind == 4) { layerEntries[command.source] = command; }
                if (command.kind != 6 || command.blend != 0) { continue; }
                var read = NativeMethods.urui_filter_info(command.mask, out var filter, filterNameBuffer, filterNameBuffer.Length);
                if (read < 0 && -read > filterNameBuffer.Length)
                { filterNameBuffer = new byte[-read]; read = NativeMethods.urui_filter_info(command.mask, out filter, filterNameBuffer, filterNameBuffer.Length); }
                if (read <= 0) { throw new InvalidOperationException("URUI 滤镜描述读取失败。"); }
                liveFilterNames.Add(command.mask);
                if (!filterNames.TryGetValue(command.mask, out var name))
                { name = Encoding.UTF8.GetString(filterNameBuffer, 0, read - 1); filterNames.Add(command.mask, name); }
                var offset=new Vector2(filter.x,filter.y);var color=new Color(filter.r,filter.g,filter.b,filter.a);
                if(!frozenFilterInvocations.TryGetValue(command.mask,out var invocation)||invocation.Name!=name||
                    invocation.Value!=filter.value||invocation.Sigma!=filter.sigma||!invocation.Offset.Equals(offset)||!invocation.Color.Equals(color))
                {
                    invocation=new FilterInvocation(name,filter.value,filter.sigma,offset,color,default);
                    frozenFilterInvocations[command.mask]=invocation;
                }
                filterInvocations.Add(index,invocation);
                if (filter.element != 0) { elementFilterElements.Add(index, filter.element); }
            }
            removedFilterNames.Clear();
            foreach (var id in filterNames.Keys) { if (!liveFilterNames.Contains(id)) { removedFilterNames.Add(id); } }
            foreach (var id in removedFilterNames) { filterNames.Remove(id);frozenFilterInvocations.Remove(id); }
        }

        unsafe RenderPlanFeedback IRenderPlanPreparationBindings.Prepare(in RenderPlanPrepareRequest request)
        {
            var feedback = new RenderPlanFeedback();
            var slot = checked((int)request.Slot);
            if (request.Kind == 1)
            {
                preparedSourceGeometry.Add(slot, request.Geometry);
                var owner = OwnerSurface.ElementOwner(request.Element) ?? throw new InvalidOperationException("图片效果所属文档已失效。");
                imageEffectSlots.TryGetValue(request.Element, out var input);
                imageEffectSlots[request.Element] = ++input; imageEffectInputKeys.Add(slot, input);
                singleEffectInput.Clear();
                var texture = LoadTexture(commands[slot].texture);
                var programName=PaintName(request.Program);var borrowed=URUI.Internal.Resources.BorrowedTextureRegistry.IsBorrowed(commands[slot].texture);
                if(owner.IsParameterImage(programName))
                {
                    var uniforms=(Vector4*)nativeInputUniforms;
                    var frozenSource=new PreparedSourceGeometry(FreezeOriginalEffectGeometry(commands[slot].geometry),request.Geometry,uniforms[slot*2],uniforms[slot*2+1]);
                    singleEffectInput.Add(new EffectInput(request.Element,programName,texture,borrowed,null,input:input,preparedSource:frozenSource));
                }
                else
                {
                    frameUsedGeometry.Add(request.Geometry);
                    singleEffectInput.Add(new EffectInput(request.Element,programName,texture,borrowed,FreezeEffectGeometry(request.Geometry),input:input));
                }
                owner.PrepareEffects(singleEffectInput);
                BindParameterProgram(owner,request,input,owner.EffectKindForName(programName));
                var ink = owner.EffectInkBounds(request.Element, owner.EffectKindForName(PaintName(request.Program)), input);
                feedback.Ink = new Vector4(ink.x, ink.y, ink.width, ink.height);
                return feedback;
            }
            var filterOwner = OwnerSurface.FilterOwner(request.Program);
            var invocation = filterInvocations[slot];
            if (request.Kind == 2)
            {
                Span<Matrix4x4> fixedTransforms=stackalloc Matrix4x4[64];
                var typed=filterOwner.ResolveProgramCoverage(request.Element,invocation.Name,fixedTransforms,out var fixedCount,out var fixedExpansion,out var composite);
                Matrix4x4[] transforms=null;
                feedback.Expansion=typed?fixedExpansion:filterOwner.ResolveEffectOutput(request.Element,invocation.Name,out composite,out transforms);
                var transformCount=typed?fixedCount:transforms.Length;
                elementComposites[request.Layer] = composite;
                feedback.Capabilities=composite.UsesUnityBlend?1u:0;
                if (transformCount > 64) { throw new InvalidOperationException("节点效果覆盖变换超出上限。"); }
                if (transformCount != 0)
                {
                    if (!feedbackTransforms.IsCreated) { feedbackTransforms = new NativeArray<float>(64 * 16, Allocator.Persistent); }
                    for (var matrix = 0; matrix < transformCount; matrix++)
                        for (var entry = 0; entry < 16; entry++) { feedbackTransforms[matrix * 16 + entry] = typed?fixedTransforms[matrix][entry]:transforms[matrix][entry]; }
                    feedback.Transforms = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(feedbackTransforms);
                    feedback.TransformCount = (uint)transformCount;
                }
                return feedback;
            }
            if (request.Kind == 3)
            {
                var bounds = Rectangle(request.Capture);
                var format = targetCamera.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                effectTargets ??= new EffectTargetPool();
                if (!captures.TryGetValue(request.Program, out var capture))
                { capture = new CaptureResource(); captures.Add(request.Program, capture); }
                var texture = capture.Lease?.Texture;
                if (!texture || texture.width != (int)bounds.width || texture.height != (int)bounds.height || texture.format != format)
                {
                    if (capture.Lease != null) { RetirePaintResource(capture.Lease); }
                    capture.Lease = effectTargets.Rent((int)bounds.width, (int)bounds.height, format);
                }
                texture = capture.Lease.Texture;
                if (!texture.IsCreated()) { texture.Create(); }
                capture.Bounds = bounds; capture.Version = unchecked(++elementEffectVersion);
                if (capture.Version == 0) { capture.Version = ++elementEffectVersion; }
                usedCaptures.Add(request.Program);
                feedback.Resource = request.Program; feedback.SourceVersion = capture.Version;
                feedback.PixelWidth = (uint)texture.width; feedback.PixelHeight = (uint)texture.height;
                feedback.Ink = request.Capture;
                return feedback;
            }
            if (request.Kind != 4) { throw new InvalidOperationException("未知效果准备请求。"); }
            var source = captures[request.Resource];
            singleEffectInput.Clear();
            singleEffectInput.Add(new EffectInput(request.Element, invocation.Name, source.Lease.Texture, false,
                EffectGeometry.CapturedQuad, source.Bounds, Rectangle(request.SourceInk)));
            filterOwner.PrepareEffects(singleEffectInput, source.Version);
            BindParameterProgram(filterOwner,request,0,EffectKind.Element);
            var output = filterOwner.EffectInkBounds(request.Element, EffectKind.Element);
            feedback.Ink = new Vector4(output.x, output.y, output.width, output.height);
            return feedback;
        }
        unsafe void BindParameterProgram(URUI.Documents.Document owner,in RenderPlanPrepareRequest request,ulong input,EffectKind kind)
        {
            owner.BeginEffectProgramRead();
            try {
            if(owner.TryGetImageProgram(request.Element,input,out var program,out var layout,out var frame,out var renderProgram,kind))
                {
                    if(renderProgram is IParameterImageGeometry template)
                    {
                        var geometry=externalGeometryInputs.CaptureImageGeometry(template.Geometry);var local=template.GeometryTransform;
                        NativeRenderPlanLease.Require(NativeMethods.urui_plan_image_geometry(planInputLease.Token,request.Request,geometry,local));
                    }
                    // Unity IDs cannot be reused while the owned material is in flight.
                    var identity=(ulong)(uint)program.GetInstanceID();imagePrograms[identity]=program;
                    var runtimeCount=2+(renderProgram is IParameterImageRuntime extra?extra.RuntimeRowCount:0);
                    if(runtimeCount<2||runtimeCount>32)throw new InvalidOperationException("程序运行时参数超出容量。");
                    Span<Vector4> runtime=stackalloc Vector4[runtimeCount];
                    var b=frame.Bounds;
                    runtime[0]=new Vector4(b.x,b.y,b.width>0?1/b.width:0,b.height>0?1/b.height:0);
                    runtime[1]=new Vector4(frame.Size.x,frame.Size.y,(float)frame.Time,frame.SourceFlags);
                    if(runtimeCount>2)((IParameterImageRuntime)renderProgram).CopyRuntimeParameters(runtime.Slice(2));
                    Span<Vector4> bound=frame.Overrides!=null||!frame.BoundValues.IsEmpty?stackalloc Vector4[layout.RowCount]:Span<Vector4>.Empty;
                    if(!frame.BoundValues.IsEmpty)frame.BoundValues.Span.CopyTo(bound);
                    else if(!bound.IsEmpty)layout.ReadBoundValues(frame.Overrides,bound);
                    fixed(Vector4* pointer=runtime)
                    fixed(Vector4* overrides=bound)
                        NativeRenderPlanLease.Require(NativeMethods.urui_plan_image_program(planInputLease.Token,request.Request,identity,owner.NativeHandle,layout.Identity,(IntPtr)pointer,(uint)runtimeCount,(uint)layout.RowCount,(IntPtr)overrides,frame.Clock));
                    if(request.Kind==1)parameterImageCommands.Add(checked((int)request.Slot));
                }
            } finally{owner.EndEffectProgramRead();}
        }
        RenderTexture IRenderPlanPreparationBindings.ResolveCapture(ulong resource) => captures[resource].Lease.Texture;

        unsafe void ApplyNativeRegions(in RenderPlanPreparePacket ready)
        {
            using var profile = regionPhase.Auto();
            if((ready.TargetFlags&~1u)!=0)throw new InvalidOperationException("原生目标准备标记无效。");
            rootTextureRequired=(ready.TargetFlags&1)!=0;rootDirectScreenSource=false;
            if(rootTextureRequired&&!IsScreenOutput)throw new InvalidOperationException("原生工作目标仅适用于屏幕输出。");
            layerRegions.Clear(); layerVersions.Clear(); layerAliases.Clear(); maskSourceLayers.Clear(); haloLayers.Clear();
            layerOrderCount=checked((int)ready.LayerIndexCount);
            if(layerOrder.Length<layerOrderCount){layerOrder=new int[layerOrderCount];}
            var indices=(uint*)ready.LayerIndices;
            for(var i=0;i<layerOrderCount;i++){layerOrder[i]=checked((int)indices[i]);if(layerOrder[i]>=layerCount)throw new InvalidOperationException("原生图层序号越界。");}
            var regions = new ReadOnlySpan<RenderPlanLayerRegion>((void*)ready.Regions, checked((int)ready.RegionCount));
            foreach (var region in regions)
            {
                layerVersions.Add(region.Layer,(region.Version,(region.Flags&16)!=0));
                var bounds = region.Bounds;
                layerRegions.Add(region.Layer, new RectInt((int)bounds.x, (int)bounds.y, (int)bounds.z, (int)bounds.w));
                if ((region.Flags & 8) != 0)
                {
                    if(region.Reserved==0||region.Reserved==region.Layer)throw new InvalidOperationException("原生遮罩别名无效。");
                    layerAliases.Add(region.Layer,region.Reserved);
                }
                if ((region.Flags & 1) != 0) { maskSourceLayers.Add(region.Layer); }
                if ((region.Flags & 2) != 0) { haloLayers.Add(region.Layer); }
            }
            if (layerCount != 0 || UsesEncodedRaster) { EnsureLayerProgram(); }
            PreparePrimaryLayerTargets();
            for (var index = 0; index < layerCount; index++)
            {
                if (!filterInvocations.TryGetValue(index, out var invocation)) { continue; }
                var region=Region(layerCommands[index].source);
                if(!invocation.Region.Equals(region))
                {
                    invocation=new FilterInvocation(invocation.Name,invocation.Value,invocation.Sigma,invocation.Offset,invocation.Color,region);
                    frozenFilterInvocations[layerCommands[index].mask]=invocation;
                }
                filterInvocations[index]=invocation;
                if (!elementFilterElements.ContainsKey(index)) { OwnerSurface.FilterOwner(layerCommands[index].mask).PrepareFilter(invocation); }
            }
        }

        unsafe int RecordExternalCommands(in RenderPlanInputPacket input, ref PhaseFrame phase)
        {
            using var profile = externalRecordingPhase.Auto();
            var count = 0;var recordingCount=0;externalRecordings.Clear();
            if (externalBindings.Length < input.PaintCount) { externalBindings = new RenderPlanExternalBinding[input.PaintCount]; }
            var layers = (ulong*)input.CommandLayers;
            preRecordingPainter = true;
            try
            {
                for (var paintIndex = 0; paintIndex < input.PaintCount; paintIndex++)
                {
                    var paint = paintScratch[paintIndex];
                    if (paint.reserved == 2||parameterImageCommands.Contains(paint.command_index)) { continue; }
                    if(geometryCommands[paint.command_index])
                    {externalBindings[count++]=new RenderPlanExternalBinding {Command=(uint)paint.command_index,Flags=8};continue;}
                    var index = paint.command_index; var command = commands[index]; var layer = layers[index];
                    // 绝对命令位置每帧变化；物理缓冲按同时活动的回调序号复用。
                    if(recordingCount==externalRecordingPool.Count)externalRecordingPool.Add(new ExternalCommandRecording());
                    var recording=externalRecordingPool[recordingCount++];externalRecordings.Add(index,recording);
                    var region = Region(layer);
                    var resolver=layer==0&&rootTextureRequired?(rootTargetResolver??=PrepareRootTarget):null;
                    var target=layer==0?SubmissionOutput:new RenderTargetIdentifier(LayerTexture(layer));
                    recording.Begin(target,LayerViewport(layer),Matrix4x4.Ortho(region.xMin,region.xMax,region.yMax,region.yMin,-1,1),resolver);
                    var transform = new Matrix4x4();
                    for (var component = 0; component < 16; component++) { transform[component] = commandTransforms[index * 16 + component]; }
                    var owner = OwnerSurface.ElementOwner(paint.element) ?? throw new InvalidOperationException("绘制器所属文档已失效。");
                    var context = paintContexts.Open(owner.WrapElement(paint.element), null,
                        transform * Matrix4x4.Translate(new Vector3(command.tx, command.ty, 0)),
                        new Vector4(command.clip_x, command.clip_y, command.clip_x + command.clip_w, command.clip_y + command.clip_h),
                        command.ClipEnabled, maskSourceLayers.Contains(layer), recording);
                    var original = false;
                    try
                    {
                        if (paint.reserved == 0) { owner.InvokePainter(paint.element, PaintName(paint.shader), context); }
                        else
                        {
                            original = !owner.RecordEffect(paint.element, context, SourceMesh(ReadGeometry(preparedSourceGeometry[index])),
                                owner.EffectSubmissionSink, owner.EffectKindForName(PaintName(paint.shader)), imageEffectInputKeys[index]);
                        }
                    }
                    finally { context.Close(); }
                    externalBindings[count++] = new RenderPlanExternalBinding {
                        Command = (uint)index, Flags = (recording.WritesParent ? 1u : 0) | (original ? 2u : 0) | (recording.WritesSurface ? 4u : 0)
                    };
                }
            }
            finally
            {
                preRecordingPainter=false;
                for(var i=recordingCount;i<externalRecordingPool.Count;i++)externalRecordingPool[i].Begin(default,default,Matrix4x4.identity);
            }
            return count;
        }

        void DetachRecordedBuffers()
        {
            if (targetCamera) { foreach (var commands in attachedBuffers) { targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, commands); } }
            attachedBuffers.Clear();
        }
        void ClearNativeRecording()
        {
            DetachRecordedBuffers();ClearLayerRecording();
            foreach (var commands in mainBuffers) { commands.Clear(); }
            recordedBuffers.Clear();
            parameterPages?.EndRecording();planResources?.EndRecording();externalGeometryInputs?.Programs.EndRecording();
        }
        void BeginNativeRecording()
        {
            DetachRecordedBuffers();ClearLayerRecording();
            if (mainBuffers.Count == 0) { mainBuffers.Add(buffer); }
            foreach (var commands in mainBuffers) { commands.Clear(); }
            recordedBuffers.Clear(); mainBufferCursor = 0; buffer = mainBuffers[0]; recordedBuffers.Add(buffer);
            buffer.SetGlobalFloat(EncodedTarget,0);
        }
        void AppendExternal(ExternalCommandRecording recording)
        {
            if (recording.WritesParent) { EndEncodedRaster(); }
            for (var i = 0; i < recording.Count; i++) { recordedBuffers.Add(recording[i].Buffer); }
            ContinueNativeRecording();
            // 外部缓冲不继承调用者状态，后续原生操作同样显式恢复自己的目标。
            if (encodedRasterActive)
            {
                var region = Region(activeLayer); var target = LayerTexture(activeLayer, -1);
                buffer.SetRenderTarget(target); buffer.SetViewport(new Rect(0, 0, target.width, target.height));
                buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(region.xMin, region.xMax, region.yMax, region.yMin, -1, 1));
                buffer.SetGlobalFloat(EncodedTarget, 1);
            }
            else { SelectLayer(activeLayer); buffer.SetGlobalFloat(EncodedTarget, 0); }
        }
        void ContinueNativeRecording()
        {
            if (++mainBufferCursor == mainBuffers.Count) { mainBuffers.Add(new CommandBuffer { name = "URUI UI continuation" }); }
            buffer = mainBuffers[mainBufferCursor]; recordedBuffers.Add(buffer);
        }
        void AttachRecordedBuffers()
        {
            if (recordingSerial == ulong.MaxValue) { throw new InvalidOperationException("绘制记录代际已耗尽。"); }
            recordingSerial++;
            buffer = mainBuffers[0];
            // SRP 由 RendererFeature 通过显式上下文提交；相机挂载接口只适用于 Built-in。
            if (GraphicsSettings.currentRenderPipeline) { return; }
            foreach (var commands in recordedBuffers)
            { targetCamera.AddCommandBuffer(CameraEvent.AfterEverything, commands); attachedBuffers.Add(commands); }
        }

        void ReleaseNativePlanRendering()
        {
            contextValidationBuffer?.Release();contextValidationBuffer=null;rootTargetResolver=null;
#if UNITY_EDITOR
            NativePlanFrame=0;NativePlanCommandCount=0;NativePlanGeometryBytes=NativePlanFrameBytes=NativePlanAllocations=NativePlanFrees=NativePlanAllocatedBytes=0;
#endif
            planResources?.Dispose(); planResources=null;
            externalGeometryInputs?.Dispose();externalGeometryInputs=null;geometryProviders.Clear();
            parameterPages?.Dispose(); parameterPages=null;
            if (feedbackTransforms.IsCreated) { feedbackTransforms.Dispose(); }
            foreach (var recording in externalRecordingPool) { recording.Dispose(); }
            externalRecordings.Clear();externalRecordingPool.Clear();
            foreach (var commands in mainBuffers) { if (!ReferenceEquals(commands,buffer)) { commands.Release(); } }
            mainBuffers.Clear();
        }

        unsafe void DrawNativeGeometry(in RenderPlanOperation operation)
        {
            var texture = LoadTexture(operation.Texture(0));
            if(colorPassActive&&!encodedRasterActive&&!(texture is RenderTexture)){SetColorConversionRegion(operation);}
            var fetch = !(texture is RenderTexture) && CanFetchOrdinary(activeLayer);
            if (fetch) { if(!EndEncodedRaster())SelectLayer(activeLayer); }
            else SelectEncodedRaster(texture);
            var properties = reusableProperties; properties.Clear();
            if (fetch)
            {
                var css = !(texture is RenderTexture);
                properties.SetFloat(EncodedTarget, css ? 1 : 0);
                properties.SetFloat(LayerReadRootColor, activeLayer == 0 && IsScreenOutput ? 1 : 0);
            }
            properties.SetTexture(mainTextureId, texture ? texture : white);
            TextureShaderState.Apply(properties, texture ? texture : white, operation.Texture(0));
            for (var index = 1; index < operation.TextureCount; index++)
            {
                var id = operation.Texture(index); var source = LoadTexture(id); if (!source) { source = white; }
                properties.SetTexture(paletteTextureIds[index - 1], source);
                properties.SetVector(paletteSourceIds[index - 1], TextureShaderState.SourceInfo(source, id));
            }
            var maskTexture=LoadTexture(operation.MaskTexture);if(!maskTexture)maskTexture=white;
            properties.SetTexture("_UruiMaskTexture",maskTexture);
            properties.SetVector("_UruiMaskSource",TextureShaderState.SourceInfo(maskTexture,operation.MaskTexture));
            var page = parameterPages.Current.Texture;
            properties.SetTexture(parametersId, page);
            var width=page.width;var height=page.height;
            properties.SetVector(parameterSizeId, new Vector4(1f / width, 1f / height, width, height));
            buffer.DrawMesh(planResources.Mesh(operation.Geometry), Matrix4x4.identity, fetch ? framebufferRasterMaterial : material, 0, (operation.Reason&0x40000000u)!=0&&ShaderContractActive?1:0, properties);
            RecordedGeometryDraws++;
        }

        void DrawNativeImageProgram(in RenderPlanOperation operation)
        {
            if(!EndEncodedRaster())SelectLayer(activeLayer);
            var properties=reusableProperties;properties.Clear();
            for(var i=0;i<operation.TextureCount;i++)
                properties.SetTexture(i==0?mainTextureId:paletteTextureIds[i-1],LoadTexture(operation.Texture(i))??white);
            var page=parameterPages.Current.Texture;properties.SetTexture(parametersId,page);
            var width=page.width;var height=page.height;
            properties.SetVector(parameterSizeId,new Vector4(1f/width,1f/height,width,height));
            buffer.DrawMesh(planResources.Mesh(operation.Geometry),Matrix4x4.identity,imagePrograms[operation.Program],0,0,properties);
            RecordedGeometryDraws++;
        }

        uint RootPixelGrid()
        {
            var viewport=submissionTarget.HasValue?submissionViewport:targetCamera.pixelRect;
            if(viewport.width!=UiWidth||viewport.height!=UiHeight||viewport.x!=Mathf.Floor(viewport.x)||viewport.y!=Mathf.Floor(viewport.y))return 0;
            if(targetCamera.targetTexture)
            {
                if(submissionTarget.HasValue&&!submissionTarget.Value.Equals(new RenderTargetIdentifier(targetCamera.targetTexture)))return 0;
                return targetCamera.targetTexture.antiAliasing==1?1u:0;
            }
            if(submissionTarget.HasValue&&!submissionTarget.Value.Equals(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget)))return 0;
            return !targetCamera.allowMSAA||QualitySettings.antiAliasing<=1?1u:0;
        }
        int ReadNativeResources(in RenderPlanInputPacket input)
        {
            using var profile = readResourcesPhase.Auto();
            if(resourceSerial==ulong.MaxValue)throw new InvalidOperationException("资源输入代际已耗尽。");
            resourceSerial++;var count=0;planTextures.Clear();
            if(planTextureBindings.Length<input.CommandCount){planTextureBindings=new RenderPlanTextureBinding[input.CommandCount];}
            for(var index=0;index<input.CommandCount;index++)
            {
                var id=commands[index].texture;if(!planTextures.Add(id))continue;
                var texture=LoadTexture(id);var isLive=texture is RenderTexture;
                var format=texture?UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetLinearFormat(texture.graphicsFormat):default;
                var normalized=texture&&UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsUNormFormat(format);
                var version=resourceSerial;
                if(!isLive&&texture)
                {
                    if(textureVersions.TryGetValue(id,out var previous)&&Alive(previous.Dependency))version=previous.Version;
                    else textureVersions[id]=(Depend(texture),version);
                }
                planTextureBindings[count++]=new RenderPlanTextureBinding {Texture=id,Version=version,Domain=isLive?1u:0,Flags=normalized?1u:0};
            }
            removedTextureVersions.Clear();
            foreach(var id in textureVersions.Keys)if(!planTextures.Contains(id))removedTextureVersions.Add(id);
            foreach(var id in removedTextureVersions)textureVersions.Remove(id);
            return count;
        }

        NativeRenderPlanLease CompileNativePlan(NativeRenderPlanLease input, in RenderPlanBindings bindings, out RenderPlanPacket packet)
        {
            using var profile=planCompilePhase.Auto();var lease=input.Compile(bindings,out packet);
#if UNITY_EDITOR
            NativePlanFrame=Time.frameCount;NativePlanGeometryBytes=packet.GeometryBytes;NativePlanFrameBytes=packet.FrameBytes;
            NativePlanAllocations=packet.AllocationCount;NativePlanFrees=packet.FreeCount;NativePlanAllocatedBytes=packet.AllocatedBytes;
#endif
            return lease;
        }

        unsafe void DrawFrameCore(ref PhaseFrame phase)
        {
            using var profile = recordingPhase.Auto();
            using (recordStatePhase.Auto())
            {
                RequireRecordingReady(); CollectImageSubmissions();ClearNativeRecording();
                RecordedGeometryDraws = RecordedRasterResolves = RecordedExternalGeometryDraws = 0;
                borrowedTextures.Validate(); shaderPropertyState.Reset();
                ShaderContractActive = propertyContract && propertyContract.Supports(material);
                framebufferFetchActive = CanUseFramebufferFetch();
                framebufferRootHighPrecision = framebufferFetchActive && CurrentTargetInfo.IsHighPrecision;
                colorPassCapable=recordingForContext&&CanUseColorPass();
            }
            using (effectParametersPhase.Auto())
            {
                foreach (var owner in OwnerSurface.DocumentsSnapshot())
                { if (owner && owner.IsLoaded) { owner.UpdateEffectParameters(); owner.BeginEffectPreparation(); } }
            }
            externalGeometryInputs??=new ExternalGeometryInputs();
            using var geometryWindow=externalGeometryInputs.Begin();
            NativeRenderPlanLease acquired;RenderPlanInputPacket input;
            using(nativeRenderPhase.Auto()){acquired=NativeRenderPlanLease.Begin(out input);}
            using var inputLease=acquired;
            planInputLease = inputLease;
            try
            {
                ReadNativeFrame(input);
                foreach (var owner in OwnerSurface.DocumentsSnapshot()) { if (owner && owner.IsLoaded) { owner.FreezeEffectParameters(); } }
                FreezeEffectClocks();
                imagePrograms.Clear();parameterImageCommands.Clear();
                usedEffectGeometries.Clear(); frameUsedGeometry.Clear(); usedCaptures.Clear(); preparedSourceGeometry.Clear(); imageEffectSlots.Clear(); imageEffectInputKeys.Clear();
                var textureCount=ReadNativeResources(input);var resourceFlags=RootPixelGrid()|(QualitySettings.activeColorSpace==ColorSpace.Linear?2u:0)|(targetCamera.allowHDR?4u:0)|(UsesEncodedRaster?8u:0)|(IsScreenOutput?16u:0)|32u|(framebufferFetchActive?64u:0)|(framebufferRootHighPrecision?128u:0)|(colorPassCapable?256u:0)|(CanBoundColorConversions()?512u:0)|(CanShareFinalRootColorPass()?1024u:0);
                var providerCount=PrepareGeometryProviders(input);
                RenderPlanPreparePacket ready;
                fixed(RenderPlanExternalGeometry* geometry=externalGeometryInputs.Geometry)
                fixed(RenderPlanExternalDraw* geometryDraws=externalGeometryInputs.Draws)
                fixed(RenderPlanExternalBinding* providers=externalBindings)
                fixed(RenderPlanTextureBinding* resources=planTextureBindings)
                {
                    var bindings=new RenderPlanBindings {Textures=(IntPtr)resources,TextureCount=(uint)textureCount,FrameFlags=resourceFlags,
                        Externals=(IntPtr)providers,ExternalCount=(uint)providerCount,ExternalGeometry=(IntPtr)geometry,
                        ExternalGeometryCount=(uint)externalGeometryInputs.GeometryCount,ExternalDraws=(IntPtr)geometryDraws,ExternalDrawCount=(uint)externalGeometryInputs.DrawCount};
                    using(planPreparePhase.Auto()){RenderPlanPreparation.Resolve(inputLease,input,this,out ready,bindings);}
                }
                ApplyNativeRegions(ready);
                foreach (var owner in OwnerSurface.DocumentsSnapshot()) { if (owner && owner.IsLoaded) { owner.EndEffectPreparation(); } }
                removedCaptures.Clear();
                foreach (var pair in captures) { if (!usedCaptures.Contains(pair.Key)) { removedCaptures.Add(pair.Key); } }
                foreach (var key in removedCaptures) { RetirePaintResource(captures[key].Lease); captures.Remove(key); }
                PruneEffectGeometries();
                var externalCount = RecordExternalCommands(input, ref phase);
                var layerBindingCount=ReadLayerBindings();
                fixed(RenderPlanLayerBinding* layerCache=layerBindings)
                fixed (RenderPlanExternalBinding* externals = externalBindings)
                fixed (RenderPlanTextureBinding* textures = planTextureBindings)
                {
                    var bindings = new RenderPlanBindings { Textures = (IntPtr)textures, TextureCount = (uint)textureCount, FrameFlags=resourceFlags, Externals = (IntPtr)externals, ExternalCount = (uint)externalCount,LayerBindings=(IntPtr)layerCache,LayerBindingCount=(uint)layerBindingCount };
                    using var plan = CompileNativePlan(inputLease, bindings, out var packet);
                    PrepareFinalLayerTargets(packet);
                    planResources ??= new RenderPlanResources(RetirePaintResource);
                    var before = planResources.GeometryUploadBytes;
                    using(planUploadPhase.Auto()){planResources.Apply(plan, packet, externalGeometryInputs);}
                    GeometryUploadBytes += planResources.GeometryUploadBytes - before;
                    var operations = new ReadOnlySpan<RenderPlanOperation>((void*)packet.Operations, checked((int)packet.OperationCount));
                    var usesParameters=false;
                    foreach (var operation in operations) { if (operation.Kind==1||operation.Kind==6||operation.Kind==8||operation.Program!=0) { usesParameters=true;break; } }
                    if (usesParameters) { parameterPages ??= new RenderParameterPages(); using(planUploadPhase.Auto()){parameterPages.Apply(plan, packet);} }
                    else { parameterPages?.EndRecording(); }
                    BeginNativeRecording(); BeginLayers();
                    RetireReleasedTextures(ref phase);
                    if (submissionTarget.HasValue) { buffer.SetRenderTarget(submissionTarget.Value); buffer.SetViewport(submissionViewport); }
                    buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, UiWidth, UiHeight, 0, -1, 1));
                    using(commandPhase.Auto())
                    foreach (var operation in operations)
                    {
                        if((operation.Reason&RasterBegin)!=0){BeginColorPass(operation);}
                        switch (operation.Kind)
                        {
                            case 9: CopyScreenRoot();break;
                            case 10: PresentScreenRoot();break;
                            case 1: DrawNativeGeometry(operation); break;
                            case 6: DrawNativeExternalGeometry(operation);break;
                            case 8: DrawNativeImageProgram(operation);break;
                            case 7: DrawNativeRawGeometry(operation,((RenderPlanParameter*)packet.Parameters)[checked((int)(input.CommandCount+operation.LayerIndex))]);break;
                            case 2:
                            case 3: AppendExternal(externalRecordings[checked((int)operation.CommandIndex)]); break;
                            case 5: ApplyNativeLayer(checked((int)operation.LayerIndex),(operation.Reason&1)!=0,operation); break;
                            default: throw new InvalidOperationException("未知原生绘制操作。");
                        }
                        if((operation.Reason&RasterEnd)!=0){EndColorPass();}
                    }
                    FinishLayers(checked((int)input.CommandCount), ref phase); EndEncodedRaster();
                    externalGeometryInputs.Programs.CommitRecording();AttachRecordedBuffers();
                }
                frameStaleGeometry.Clear();
                foreach (var pair in meshes) { if (!frameUsedGeometry.Contains(pair.Key)) { frameStaleGeometry.Add(pair.Key); } }
                foreach (var key in frameStaleGeometry) { RecycleGeometry(meshes[key]); meshes.Remove(key); }

            }
            finally { planInputLease=default;nativeInputUniforms=IntPtr.Zero;frozenEffectClocks.Clear(); }
        }
    }
}
