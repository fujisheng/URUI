using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;
using URUI.Rendering.Effects;
namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        LayerCommandNative[] layerCommands=Array.Empty<LayerCommandNative>();
        LayerRegionNative[] layerRegionScratch=Array.Empty<LayerRegionNative>();
        NativeMethods.EffectInputRegionNative[] effectInputRegionScratch=Array.Empty<NativeMethods.EffectInputRegionNative>();
        readonly Dictionary<ulong,Rect> effectInputRegions=new();
        float[] effectTransformScratch=Array.Empty<float>();
        int layerCount,layerCursor,layerOrderCount;
        int[] layerOrder=Array.Empty<int>();
        ulong activeLayer;
        readonly HashSet<ulong> layerTargets=new();
        readonly HashSet<ulong> maskSourceLayers=new();
        readonly HashSet<ulong> haloLayers=new();
        readonly Dictionary<ulong,RectInt> layerRegions=new();
        readonly Dictionary<ulong,ulong> layerAliases=new();
        readonly Dictionary<ulong,LayerCommandNative> layerEntries=new();
        readonly Dictionary<(ulong,int),EffectTargetPool.Lease> preparedLayerTargets=new();
        readonly HashSet<(ulong,int)> desiredLayerTargets=new();
        readonly Dictionary<(ulong,int),Vector2Int> layerBackdropSizes=new();
        readonly List<(ulong,int)> removedLayerTargets=new();
        readonly List<IDisposable> retiredLayerResources=new();
        readonly Dictionary<int,FilterInvocation> filterInvocations=new();
        readonly Dictionary<ulong,FilterInvocation> frozenFilterInvocations=new();
        readonly Dictionary<int,ulong> elementFilterElements=new();
        readonly Dictionary<ulong,EffectComposite> elementComposites=new();
        readonly Dictionary<(BlendMode,BlendMode),Material> elementCompositeMaterials=new();
        readonly Dictionary<ulong,string> filterNames=new();
        readonly HashSet<ulong> liveFilterNames=new();
        readonly List<ulong> removedFilterNames=new();
        byte[] filterNameBuffer=new byte[128];
        uint elementEffectVersion;
        EffectTargetPool effectTargets;
        Material layerCompositeMaterial;
        Mesh layerCompositeMesh;
        RenderTargetIdentifier? submissionTarget;
        Rect submissionViewport;
        URUI.Rendering.RenderTargetInfo submissionTargetInfo;
        URUI.Rendering.RenderTargetInfo CurrentTargetInfo => submissionTarget.HasValue ? submissionTargetInfo :
            targetCamera && targetCamera.targetTexture ? URUI.Rendering.RenderTargetInfo.FromTexture(targetCamera.targetTexture) :
            URUI.Rendering.RenderTargetInfo.FromScreen(targetCamera?targetCamera.targetDisplay:0,targetCamera&&targetCamera.allowMSAA?Math.Max(1,QualitySettings.antiAliasing):1);
        bool framebufferRootHighPrecision;
        bool rootTextureRequired,rootDirectScreenSource;
        Rect RootOutputViewport=>submissionTarget.HasValue?submissionViewport:targetCamera.pixelRect;
        void CopyScreenRegion(RenderTexture destination,RectInt read)
        {
            // Resolve the non-sampleable screen alias before reading a bounded
            // region. Its screen origin remains distinct from a rendered UI layer.
            var capture=LayerTexture(0,-3);
            buffer.SetRenderTarget(SubmissionOutput);buffer.Blit(SubmissionOutput,capture);
            if(!SystemInfo.graphicsUVStartsAtTop&&(SystemInfo.copyTextureSupport&CopyTextureSupport.Basic)!=0&&capture.graphicsFormat==destination.graphicsFormat&&
                capture.antiAliasing==1&&destination.antiAliasing==1&&destination.width==read.width&&destination.height==read.height&&
                read.x>=0&&read.y>=0&&read.xMax<=capture.width&&read.yMax<=capture.height)
                buffer.CopyTexture(capture,0,0,read.x,read.y,read.width,read.height,destination,0,0,0,0);
            else
            {
                buffer.SetGlobalVector(LayerReadRect,new Vector4(read.x,read.y,read.width,read.height));
                buffer.SetGlobalFloat(LayerReadCameraTarget,SystemInfo.graphicsUVStartsAtTop?1:0);
                buffer.Blit(capture,destination,layerCompositeMaterial,3);
            }
        }
        internal static bool CanCopyLayerData(RenderTexture source,RenderTexture destination,CopyTextureSupport capabilities)
            =>(capabilities&CopyTextureSupport.Basic)!=0&&source.width==destination.width&&source.height==destination.height&&
                source.graphicsFormat==destination.graphicsFormat&&source.antiAliasing==1&&destination.antiAliasing==1;
        void CopyLayerData(RenderTexture source,RenderTexture destination)
        {
            if(CanCopyLayerData(source,destination,SystemInfo.copyTextureSupport))buffer.CopyTexture(source,destination);
            else
            {
                // 同一个颜色域内覆盖写回；不依赖 WebGL 等后端未公开的复制能力。
                buffer.SetGlobalVector(LayerReadRect,new Vector4(0,0,source.width,source.height));
                buffer.SetGlobalFloat(LayerReadCameraTarget,0);
                buffer.Blit(source,destination,layerCompositeMaterial,3);
                SelectLayer(activeLayer);
            }
        }
        Func<RenderTargetIdentifier> rootTargetResolver;
        RenderTextureFormat ScreenRootFormat()
        {
            // Camera HDR describes the intermediate scene target, not the final
            // display buffer. SDR output must retain its normal LDR clamping.
            var displays=HDROutputSettings.displays;var display=submissionTarget.HasValue&&submissionTargetInfo.IsKnown?submissionTargetInfo.Display:targetCamera.targetDisplay;
            return displays!=null&&display>=0&&display<displays.Length&&displays[display].active
                ?displays[display].format:RenderTextureFormat.ARGB32;
        }
        RenderTargetIdentifier PrepareRootTarget()
        {
            if(!rootTextureRequired)throw new InvalidOperationException("原生计划未声明屏幕工作目标。");
            var size=ScreenOutputSize();var format=ScreenRootFormat();
            if(preparedLayerTargets.TryGetValue((0,-2),out var existing))
            {
                var texture=existing.Texture;
                if(texture.width==size.x&&texture.height==size.y&&texture.format==format)return new RenderTargetIdentifier(texture);
            }
            // Native preparation permits a parent write; the callback may still be
            // empty. Rent only when it actually records that write, or in final requests.
            desiredLayerTargets.Add((0,-2));layerBackdropSizes[(0,-2)]=size;
            ApplyLayerTargetRequests();
            return new RenderTargetIdentifier(LayerTexture(0,-2));
        }
        bool encodedRasterActive;
        bool framebufferFetchActive, framebufferProgramsChecked;
        Material framebufferRasterMaterial, framebufferLayerMaterial;
        bool CanUseFramebufferFetch()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear || !HasStandardRasterShader || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            var output=CurrentTargetInfo;
            if (!output.IsKnown || output.Samples!=1) return false;
            if (output.IsScreen && UnityEngine.Display.displays[output.Display].requiresSrgbBlitToBackbuffer) return false;
            if (!framebufferProgramsChecked)
            {
                framebufferProgramsChecked = true;
                var raster = UnityEngine.Resources.Load<Shader>("URUI/PremultipliedFetch");
                var layer = UnityEngine.Resources.Load<Shader>("URUI/LayerCompositeFetch");
                if (raster && layer && raster.isSupported && layer.isSupported && layer.passCount >= 2)
                {
                    framebufferRasterMaterial = new Material(raster) { hideFlags = HideFlags.HideAndDontSave };
                    framebufferLayerMaterial = new Material(layer) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
            return framebufferRasterMaterial && framebufferLayerMaterial;
        }
        RenderTargetIdentifier SubmissionOutput => submissionTarget ?? new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
        bool IsScreenOutput => submissionTarget.HasValue && submissionTargetInfo.IsKnown ? submissionTargetInfo.IsScreen :
            !targetCamera.targetTexture && SubmissionOutput.Equals(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
        bool CanFetchOrdinary(ulong layer) => framebufferFetchActive && (layer==0?framebufferRootHighPrecision:targetCamera&&targetCamera.allowHDR);
        Vector2Int ScreenOutputSize()
        {
            if(submissionTarget.HasValue&&submissionTargetInfo.IsKnown)return new Vector2Int(submissionTargetInfo.Width,submissionTargetInfo.Height);
            var display=targetCamera.targetDisplay;
            if(display==0)return new Vector2Int(Screen.width,Screen.height);
            if(display<0||display>=Display.displays.Length)throw new InvalidOperationException("URUI 输出显示器无效。");
            return new Vector2Int(Display.displays[display].renderingWidth,Display.displays[display].renderingHeight);
        }
        void CopyScreenRoot()
        {
            if(!rootTextureRequired)throw new InvalidOperationException("原生屏幕工作目标未准备。");
            // Unity's default blit resolves the screen target; custom shaders cannot
            // sample that alias on all Player backends. Subsequent reads use a real RT.
            if(rootDirectScreenSource)buffer.Blit(SubmissionOutput,LayerTexture(0,-2));
            else
            {
                // 标准 Blit 保留屏幕别名的解析能力，方向归一化合入同一次复制。
                // 局部屏幕采样仍使用 CopyScreenRegion 的独立解析目标。
                var flip=SystemInfo.graphicsUVStartsAtTop;
                buffer.Blit(SubmissionOutput,LayerTexture(0,-2),new Vector2(1,flip?-1:1),new Vector2(0,flip?1:0));
            }
            SelectLayer(0);
        }
        void PresentScreenRoot()
        {
            // A pending encoded root already contains the complete viewport.
            // Resolve it directly to the output instead of writing and copying
            // an intermediate version of the same pixels.
            if(encodedRasterActive&&activeLayer==0)ResolveEncodedRaster(SubmissionOutput);
            else
            {
                if(rootDirectScreenSource)throw new InvalidOperationException("原生纯几何根目标缺少最终编码解析。");
                EndEncodedRaster();buffer.Blit(LayerTexture(0,-2),SubmissionOutput);
            }
            buffer.SetRenderTarget(SubmissionOutput);
            buffer.SetViewport(submissionTarget.HasValue?submissionViewport:targetCamera.pixelRect);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(0,UiWidth,UiHeight,0,-1,1));
        }
        Shader encodedRasterShader;
        bool encodedRasterCapable;
        bool HasStandardRasterShader
        {
            get
            {
                if (!material) return false;
                var current = material.shader;
                if (current != encodedRasterShader)
                {
                    encodedRasterShader = current;
                    encodedRasterCapable = current && current.name == "URUI/Premultiplied";
                }
                return encodedRasterCapable;
            }
        }
        bool UsesEncodedRaster
        {
            get
            {
                if (QualitySettings.activeColorSpace != ColorSpace.Linear || !material) return false;
                return HasStandardRasterShader;
            }
        }
        static readonly int EncodedTarget=Shader.PropertyToID("_UruiEncodedTarget");
        bool HasLayerResources=>preparedLayerTargets.Count!=0;
        static readonly int LayerMask=Shader.PropertyToID("_UruiLayerMask");
        static readonly int LayerSource=Shader.PropertyToID("_UruiLayerSource");
        static readonly int LayerBackdrop=Shader.PropertyToID("_UruiLayerBackdrop");
        static readonly int LayerBackdropTransform=Shader.PropertyToID("_UruiLayerBackdropTransform");
        static readonly int LayerReadRect=Shader.PropertyToID("_UruiLayerReadRect");
        static readonly int LayerReadCameraTarget=Shader.PropertyToID("_UruiLayerReadCameraTarget");
        static readonly int LayerReadRootColor=Shader.PropertyToID("_UruiLayerReadRootColor");
        static readonly int LayerMaskEnabled=Shader.PropertyToID("_UruiLayerMaskEnabled");
        static readonly int LayerMaskRect=Shader.PropertyToID("_UruiLayerMaskRect");
        static readonly int LayerClip=Shader.PropertyToID("_UruiLayerClip");
        static readonly int LayerClipEnabled=Shader.PropertyToID("_UruiLayerClipEnabled");
        void DrawElementEffect(int index, LayerCommandNative command,in RenderPlanOperation operation)
        {
            var owner = OwnerSurface.FilterOwner(command.mask);
            var region = Region(command.source);
            // 捕获域已在 Prepare 前冻结；输出层扩边不会改变其采样输入。
            var capture = captures[command.mask];
            var captureBounds=capture.Bounds;
            var sourceTexture=LayerTexture(command.source);var captureTexture=capture.Lease.Texture;
            // 相同像素域和格式只需保留快照；缩放/扩边或无复制能力的平台仍执行覆盖复制。
            if((SystemInfo.copyTextureSupport&CopyTextureSupport.Basic)!=0&&
                captureBounds.x==region.x&&captureBounds.y==region.y&&captureBounds.width==region.width&&captureBounds.height==region.height&&
                sourceTexture.width==captureTexture.width&&sourceTexture.height==captureTexture.height&&
                sourceTexture.graphicsFormat==captureTexture.graphicsFormat&&sourceTexture.antiAliasing==captureTexture.antiAliasing)
            {buffer.CopyTexture(sourceTexture,captureTexture);}
            else
            {
                buffer.SetRenderTarget(captureTexture);buffer.SetViewport(new Rect(0,0,captureTexture.width,captureTexture.height));
                buffer.ClearRenderTarget(false,true,Color.clear);
                buffer.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(captureBounds.xMin,captureBounds.xMax,captureBounds.yMax,captureBounds.yMin,-1,1));
                buffer.SetGlobalTexture(LayerSource,sourceTexture);
                buffer.DrawMesh(layerCompositeMesh,Matrix4x4.TRS(new Vector3(region.x,region.y,0),Quaternion.identity,new Vector3(region.width,region.height,1)),layerCompositeMaterial,0,7);
            }
            var destination = LayerTexture(command.source, 2);
            buffer.SetRenderTarget(destination); buffer.SetViewport(new Rect(0, 0, region.width, region.height));
            buffer.ClearRenderTarget(false, true, Color.clear);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(region.xMin, region.xMax, region.yMax, region.yMin, -1, 1));
            var transform = Matrix4x4.TRS(new Vector3(captureBounds.x, captureBounds.y, 0), Quaternion.identity, new Vector3(captureBounds.width, captureBounds.height, 1));
            if(operation.Program!=0) {
                var properties=reusableProperties;properties.Clear();var page=parameterPages.Current.Texture;
                properties.SetTexture(mainTextureId,capture.Lease.Texture);properties.SetTexture(parametersId,page);
                properties.SetVector(parameterSizeId,new Vector4(1f/page.width,1f/page.height,page.width,page.height));
                buffer.DrawMesh(planResources.Mesh(operation.Geometry),Matrix4x4.identity,imagePrograms[operation.Program],0,0,properties);
                RecordedGeometryDraws++;effectSubmitted.Add(new EffectSubmission{ProcessingPasses=1});SelectLayer(activeLayer);
                CopyLayerData(destination,LayerTexture(command.source));return;
            }
            var paint = paintContexts.Open(owner.WrapElement(elementFilterElements[index]), buffer,
                transform, Vector4.zero, false, maskSourceLayers.Contains(activeLayer));
            try
            {
                if (!owner.RecordEffect(elementFilterElements[index], paint, layerCompositeMesh,
                    owner.EffectSubmissionSink, EffectKind.Element))
                { CopyLayerData(LayerTexture(command.source),destination); }
                else { effectSubmitted.Add(new EffectSubmission { ProcessingPasses=1 }); }
            }
            finally { paint.Close(); SelectLayer(activeLayer); }
            CopyLayerData(destination, LayerTexture(command.source));
        }
        void EnsureLayerProgram()
        {
            if(layerCompositeMaterial)return;
            var shader=UnityEngine.Resources.Load<Shader>("URUI/LayerComposite");
            if(!shader)throw new InvalidOperationException("缺少 URUI 图层合成 shader。");
            layerCompositeMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            layerCompositeMesh=new Mesh{name="URUI Layer Composite Quad"};
            layerCompositeMesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1),Vector3.up};
            layerCompositeMesh.uv=new[]{Vector2.up,Vector2.one,Vector2.right,Vector2.zero};
            layerCompositeMesh.triangles=new[]{0,1,2,0,2,3};
        }
        Material ElementCompositeMaterial(EffectComposite composite)
        {
            var key=(composite.Source,composite.Destination);
            if(!elementCompositeMaterials.TryGetValue(key,out var program))
            {
                program=new Material(layerCompositeMaterial){hideFlags=HideFlags.HideAndDontSave};
                program.SetInt("_UruiCompositeSrcBlend",(int)composite.Source);program.SetInt("_UruiCompositeDstBlend",(int)composite.Destination);
                elementCompositeMaterials.Add(key,program);
            }
            return program;
        }
        RectInt Region(ulong id)=>id==0?new RectInt(0,0,UiWidth,UiHeight):layerRegions[id];
        RenderTexture LayerTexture(ulong id,int slot=0)
        {
            if(slot==0)while(layerAliases.TryGetValue(id,out var source))id=source;
            return preparedLayerTargets[(id,slot)].Texture;
        }
        Rect LayerViewport(ulong id)
        {
            if(id==0)return RootOutputViewport;
            var region=Region(id);return new Rect(0,0,region.width,region.height);
        }
        RectInt BackdropReadRect(ulong source,ulong target)
        {
            var bounds=Region(source);var parent=Region(target);var viewport=LayerViewport(target);
            var scaleX=viewport.width/parent.width;var scaleY=viewport.height/parent.height;
            int left=Mathf.FloorToInt(viewport.x+(bounds.x-parent.x)*scaleX);
            int right=Mathf.CeilToInt(viewport.x+(bounds.xMax-parent.x)*scaleX);
            int bottom=Mathf.FloorToInt(viewport.y+viewport.height-(bounds.yMax-parent.y)*scaleY);
            int top=Mathf.CeilToInt(viewport.y+viewport.height-(bounds.y-parent.y)*scaleY);
            return new RectInt(left,bottom,Math.Max(1,right-left),Math.Max(1,top-bottom));
        }
        void CopyBackdrop(ulong source,RenderTexture destination,RectInt read,int pass=3)
        {
            if(source==0&&IsScreenOutput&&!rootTextureRequired&&pass==3)
            {
                buffer.SetGlobalFloat(LayerReadRootColor,1);CopyScreenRegion(destination,read);return;
            }
            buffer.SetGlobalVector(LayerReadRect,new Vector4(read.x,read.y,read.width,read.height));
            // Metal 的最终 CameraTarget 采用屏幕原点，texel size 仍可能为正；
            // 离屏目标和嵌套图层继续使用纹理原点，不能一并翻转。
            bool cameraTarget=source==0&&(rootDirectScreenSource||!rootTextureRequired&&!targetCamera.targetTexture&&
                (!submissionTarget.HasValue||submissionTarget.Value.Equals(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget))));
            buffer.SetGlobalFloat(LayerReadCameraTarget,cameraTarget?1:0);
            buffer.SetGlobalFloat(LayerReadRootColor,source==0&&IsScreenOutput?1:0);
            buffer.Blit(LayerTarget(source),destination,layerCompositeMaterial,pass);
        }
        void BeginEncodedRaster()
        {
            if(encodedRasterActive||!UsesEncodedRaster)return;
            if(colorPassActive){BeginColorEncoding();return;}
            var viewport=LayerViewport(activeLayer);
            var read=new RectInt(Mathf.FloorToInt(viewport.x),Mathf.FloorToInt(viewport.y),
                Math.Max(1,Mathf.CeilToInt(viewport.width)),Math.Max(1,Mathf.CeilToInt(viewport.height)));
            CopyBackdrop(activeLayer,LayerTexture(activeLayer,-1),read,4);
            buffer.SetRenderTarget(LayerTexture(activeLayer,-1));
            buffer.SetViewport(new Rect(0,0,read.width,read.height));
            var region=Region(activeLayer);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(region.xMin,region.xMax,region.yMax,region.yMin,-1,1));
            buffer.SetGlobalFloat(EncodedTarget,1);
            encodedRasterActive=true;
        }
        void SelectEncodedRaster(Texture texture)
        {
            if(texture is RenderTexture)EndEncodedRaster();else BeginEncodedRaster();
        }
        bool EndEncodedRaster()
        {
            if(!encodedRasterActive)return false;
            ResolveEncodedRaster(LayerTarget(activeLayer));
            // 同一缓冲的紧邻绘制可复用解析已恢复的目标、视口和矩阵。
            return true;
        }
        void ResolveEncodedRaster(RenderTargetIdentifier destination)
        {
            if(colorPassActive){ResolveColorEncoding();return;}
            buffer.SetGlobalFloat(EncodedTarget,0);
            SelectLayer(activeLayer,destination);
            buffer.SetGlobalTexture(LayerSource,LayerTexture(activeLayer,-1));
            buffer.SetGlobalFloat(LayerReadRootColor,activeLayer==0&&IsScreenOutput?1:0);
            var region=Region(activeLayer);
            buffer.DrawMesh(layerCompositeMesh,Matrix4x4.TRS(new Vector3(region.x,region.y,0),Quaternion.identity,
                new Vector3(region.width,region.height,1)),layerCompositeMaterial,0,5);
            encodedRasterActive=false;
            RecordedRasterResolves++;
        }
        void PreparePrimaryLayerTargets()
        {
            desiredLayerTargets.Clear();layerBackdropSizes.Clear();
            foreach(var pair in layerRegions)if(!layerAliases.ContainsKey(pair.Key))desiredLayerTargets.Add((pair.Key,0));
            // 已有辅助资源暂存至最终计划就绪；准备回调只需要真实主层目标。
            foreach(var pair in preparedLayerTargets)if(pair.Key.Item2!=0&&desiredLayerTargets.Add(pair.Key))
            {layerBackdropSizes.Add(pair.Key,new Vector2Int(pair.Value.Texture.width,pair.Value.Texture.height));}
            ApplyLayerTargetRequests();
        }
        unsafe void PrepareFinalLayerTargets(in RenderPlanPacket packet)
        {
            using var profile = finalTargetPhase.Auto();
            var operations=new ReadOnlySpan<RenderPlanOperation>((void*)packet.Operations,checked((int)packet.OperationCount));
            if(operations.Length!=0&&operations[0].Kind==9&&(operations[0].Reason&1)!=0)
            {
                var bounds=operations[0].Bounds;
                if(!rootTextureRequired||RootPixelGrid()==0||bounds.x<0||bounds.y<0||bounds.z>UiWidth||bounds.w>UiHeight||bounds.z<=bounds.x||bounds.w<=bounds.y||
                    bounds.x!=Mathf.Floor(bounds.x)||bounds.y!=Mathf.Floor(bounds.y)||bounds.z!=Mathf.Floor(bounds.z)||bounds.w!=Mathf.Floor(bounds.w))
                    throw new InvalidOperationException("原生根区域证明无效。");
                // 原生证明仅含同域几何；屏幕原点转换可与下一次编码读取融合。
                // 工作目标和编码目标保持视口容量，不再随动态 ink 宽高反复租还。
                rootDirectScreenSource=true;
            }
            desiredLayerTargets.Clear();layerBackdropSizes.Clear();memorylessLayerTargets.Clear();
            var requests=new ReadOnlySpan<RenderPlanTargetRequest>((void*)packet.Targets,checked((int)packet.TargetCount));
            foreach(var request in requests)
            {
                if(request.Kind<1||request.Kind>7||request.FirstOperation>request.LastOperation||request.LastOperation>=packet.OperationCount)
                    throw new InvalidOperationException("原生目标请求无效。");
                var key=(request.Layer,request.Slot);
                if(!desiredLayerTargets.Add(key))throw new InvalidOperationException("原生目标请求重复。");
                Vector2Int size;
                switch(request.Kind)
                {
                    case 1:if(request.Slot!=0||request.Layer==0)throw new InvalidOperationException("原生主层目标无效。");size=Region(request.Layer).size;break;
                    case 2:
                        if(request.Slot!=-1)throw new InvalidOperationException("原生编码目标无效。");
                        var viewport=LayerViewport(request.Layer);size=new Vector2Int(Math.Max(1,Mathf.CeilToInt(viewport.width)),Math.Max(1,Mathf.CeilToInt(viewport.height)));break;
                    case 7:
                        if(request.Slot!=-1||!colorPassCapable)throw new InvalidOperationException("原生瞬时颜色目标无效。");
                        memorylessLayerTargets.Add(key);
                        size=request.Layer==0?new Vector2Int(CurrentTargetInfo.Width,CurrentTargetInfo.Height):Region(request.Layer).size;break;
                    case 3:if(request.Slot!=1&&request.Slot!=2)throw new InvalidOperationException("原生临时目标无效。");size=Region(request.Layer).size;break;
                    case 6:
                        if(!IsScreenOutput||request.Layer!=0||request.Slot!=-3)throw new InvalidOperationException("原生屏幕解析目标无效。");
                        size=ScreenOutputSize();break;
                    case 5:
                        if(!rootTextureRequired||request.Layer!=0||request.Slot!=-2)throw new InvalidOperationException("原生屏幕工作目标无效。");
                        size=ScreenOutputSize();break;
                    default:if(request.Slot<3)throw new InvalidOperationException("原生背景目标无效。");size=BackdropReadRect(request.Layer,request.RelatedLayer).size;break;
                }
                if(request.Slot!=0)layerBackdropSizes.Add(key,size);
            }
            var rootInFinalPlan=desiredLayerTargets.Contains((0,-2));
            // Opaque callbacks can record nothing. Only an empty final stream may
            // discard the conservative preparation promise without a parent write.
            if(rootTextureRequired&&!rootInFinalPlan&&packet.OperationCount!=0)
                throw new InvalidOperationException("原生屏幕工作目标准备与最终计划不一致。");
            rootTextureRequired=rootInFinalPlan;
            ApplyLayerTargetRequests();
        }
        void ApplyLayerTargetRequests()
        {
            var desired=desiredLayerTargets;var backdropSizes=layerBackdropSizes;
            var format=targetCamera&&targetCamera.allowHDR?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32;
            var screenFormat=IsScreenOutput?ScreenRootFormat():format;
            var removed=removedLayerTargets;removed.Clear();
            var retired=retiredLayerResources;retired.Clear();
            foreach(var pair in preparedLayerTargets)
            {
                bool keep=desired.Contains(pair.Key);
                if(keep){var size=pair.Key.Item2!=0?backdropSizes[pair.Key]:Region(pair.Key.Item1).size;var texture=pair.Value.Texture;keep=texture.width==size.x&&texture.height==size.y&&texture.format==(pair.Key.Item1==0&&pair.Key.Item2<=-2?screenFormat:format)&&texture.memorylessMode==(memorylessLayerTargets.Contains(pair.Key)?RenderTextureMemoryless.Color:RenderTextureMemoryless.None);}
                if(keep&&pair.Key.Item2==0)
                {
                    var expected=layerVersions[pair.Key.Item1];var content=layerContents[pair.Key.Item1];
                    keep=content.Cacheable==expected.Cacheable&&(!content.Cacheable||content.Version==expected.Version)
                        ||content.TryUpdateUnpublished(expected.Version,expected.Cacheable);
                }
                if(!keep){removed.Add(pair.Key);retired.Add(pair.Key.Item2==0?(IDisposable)layerContents[pair.Key.Item1]:pair.Value);}
            }
            if(retired.Count!=0)
            {
                RetirePaintResources(retired);
                foreach(var key in removed){preparedLayerTargets.Remove(key);if(key.Item2==0)layerContents.Remove(key.Item1);}
                retired.Clear();
            }
            effectTargets??=new EffectTargetPool();
            foreach(var key in desired)if(!preparedLayerTargets.ContainsKey(key)){var size=key.Item2!=0?backdropSizes[key]:Region(key.Item1).size;
                var lease=effectTargets.Rent(size.x,size.y,key.Item1==0&&key.Item2<=-2?screenFormat:format,key.Item2==-1,
                    memorylessLayerTargets.Contains(key)?RenderTextureMemoryless.Color:RenderTextureMemoryless.None);preparedLayerTargets.Add(key,lease);
                if(key.Item2==0){var version=layerVersions[key.Item1];layerContents.Add(key.Item1,LayerContentLease.Rent(lease,version.Version,version.Cacheable));}}
        }
        void BeginLayers(){layerCursor=0;activeLayer=0;layerTargets.Clear();encodedRasterActive=false;colorPassActive=false;colorPassGroups=0;}
        RenderTargetIdentifier LayerTarget(ulong id)
        {
            if(id==0)return rootTextureRequired?new RenderTargetIdentifier(LayerTexture(0,-2)):SubmissionOutput;
            if(!layerTargets.Contains(id))throw new InvalidOperationException("URUI 图层资源不存在："+id);
            return new RenderTargetIdentifier(LayerTexture(id));
        }
        void CreateLayer(ulong id){if(id==0||(!preparedLayerTargets.ContainsKey((id,0))&&!layerAliases.ContainsKey(id))||!layerTargets.Add(id))throw new InvalidOperationException("URUI 图层资源身份无效。");}
        void SelectLayer(ulong id)=>SelectLayer(id,LayerTarget(id));
        void SelectLayer(ulong id,RenderTargetIdentifier destination)
        {
            if(colorPassActive)
            {
                if(id!=activeLayer||!destination.Equals(colorPassTarget))throw new InvalidOperationException("原生共享颜色区间内不能切换目标。");
                SetColorPassProjection();return;
            }
            activeLayer=id;buffer.SetRenderTarget(destination);var region=Region(id);
            buffer.SetViewport(LayerViewport(id));
            buffer.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(region.xMin,region.xMax,region.yMax,region.yMin,-1,1));
        }
        void ReleaseLayer(ulong id){if(id==0||!layerTargets.Remove(id))throw new InvalidOperationException("URUI 图层被重复释放。");}
        CommandNative LayerInputClip(CommandNative command)
        {
            if(activeLayer!=0&&haloLayers.Contains(activeLayer)&&layerEntries.TryGetValue(activeLayer,out var inherited)&&
                (command.flags&1)==inherited.flags&&command.clip_x==inherited.clip_x&&command.clip_y==inherited.clip_y&&command.clip_w==inherited.clip_w&&command.clip_h==inherited.clip_h)
                command.flags&=~1;
            return command;
        }
        void LayerComposite(ulong source,ulong target,ulong mask,int blend,LayerCommandNative command,bool clip=true,int backdropSlot=3)
        {
            if (source == 0)
            {
                // Surface sampling is an explicit extension. Snapshot only the
                // requested destination bounds; filtering never writes the
                // camera target until the bounded final composite.
                CopyBackdrop(0,LayerTexture(target),BackdropReadRect(target,0));
                SelectLayer(activeLayer);
                return;
            }
            var sourceRegion=Region(source);
            EffectComposite effectComposite=default;
            var unityComposite=source!=target&&elementComposites.TryGetValue(source,out effectComposite)&&effectComposite.UsesUnityBlend;
            var fetchMask=framebufferFetchActive&&source==target&&blend==1&&!clip;
            bool fetchOver = framebufferFetchActive && !unityComposite && blend == 0 && source != target &&
                (target == 0 || LayerTexture(source) != LayerTexture(target));
            bool encodedOver=!fetchOver&&!unityComposite&&blend==0&&QualitySettings.activeColorSpace==ColorSpace.Linear;
            if(encodedOver)
            {
                // CSS sRGB 颜色在编码域合成；仅快照当前组边界，租约沿用已有 fence 退役。
                var backdropRegion=Region(target);var viewport=LayerViewport(target);var read=BackdropReadRect(source,target);
                // Shader 使用真正目标的 texel size；目标可能大于或偏移于提交视口。
                CopyBackdrop(target,LayerTexture(source,backdropSlot),read);
                buffer.SetGlobalTexture(LayerBackdrop,LayerTexture(source,backdropSlot));
                var scaleX=viewport.width/backdropRegion.width;var scaleY=viewport.height/backdropRegion.height;
                buffer.SetGlobalVector(LayerBackdropTransform,new Vector4(scaleX/read.width,-scaleY/read.height,
                    (viewport.x-backdropRegion.x*scaleX-read.x)/read.width,
                    (viewport.y+viewport.height+backdropRegion.y*scaleY-read.y)/read.height));
            }
            buffer.SetGlobalFloat(LayerMaskEnabled,mask!=0?1:0);
            if(mask!=0){var region=Region(mask);buffer.SetGlobalTexture(LayerMask,LayerTexture(mask));buffer.SetGlobalVector(LayerMaskRect,new Vector4(region.x,region.y,region.width,region.height));}
            buffer.SetGlobalFloat(LayerClipEnabled,clip?command.flags:0);
            buffer.SetGlobalVector(LayerClip,new Vector4(command.clip_x,command.clip_y,command.clip_x+command.clip_w,command.clip_y+command.clip_h));
            if(!fetchMask)buffer.SetGlobalTexture(LayerSource,LayerTexture(source));
            if (fetchOver) buffer.SetGlobalFloat(LayerReadRootColor, target == 0 && IsScreenOutput ? 1 : 0);
            if(target==source&&!fetchMask){buffer.SetRenderTarget(LayerTexture(source,1));var bounds=Region(source);buffer.SetViewport(new Rect(0,0,bounds.width,bounds.height));buffer.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(bounds.xMin,bounds.xMax,bounds.yMax,bounds.yMin,-1,1));}
            else SelectLayer(target);
            buffer.DrawMesh(layerCompositeMesh,Matrix4x4.TRS(new Vector3(sourceRegion.x,sourceRegion.y,0),Quaternion.identity,new Vector3(sourceRegion.width,sourceRegion.height,1)),
                fetchMask||fetchOver?framebufferLayerMaterial:unityComposite?ElementCompositeMaterial(effectComposite):layerCompositeMaterial,0,fetchMask?1:fetchOver?0:unityComposite?6:encodedOver?2:blend);
            if(target==source&&!fetchMask)CopyLayerData(LayerTexture(source,1),LayerTexture(source));
        }
        void ApplyNativeLayer(int index,bool skipWrites,in RenderPlanOperation operation)
        {
            if(layerCursor>=layerOrderCount||layerOrder[layerCursor++]!=index)throw new InvalidOperationException("原生图层顺序不一致。");
            var command=layerCommands[index];
            if((operation.Reason&2)!=0)
            {
                if(command.kind!=4||command.blend!=0||command.mask!=0||command.source==command.target)
                    throw new InvalidOperationException("原生最终裁剪不是普通组合成。");
                var clip=operation.Bounds;
                var x=checked((int)clip.x);var y=checked((int)clip.y);
                var right=checked((int)clip.z);var bottom=checked((int)clip.w);
                if(x!=clip.x||y!=clip.y||right!=clip.z||bottom!=clip.w||right<x||bottom<y)
                    throw new InvalidOperationException("原生最终裁剪必须是有效整数像素域。");
                // 保留原始输入数组；仅本次合成消费协议4提供的二值遮罩等价裁剪。
                command.flags|=1;command.clip_x=x;command.clip_y=y;
                command.clip_w=checked(right-x);command.clip_h=checked(bottom-y);
            }
            EndEncodedRaster();
            switch(command.kind)
            {
                case 1:
                    CreateLayer(command.target);SelectLayer(command.target);
                    if(!skipWrites){buffer.ClearRenderTarget(false,true,Color.clear);RecordLayerWrite(command.target);}
                    break;
                case 2:SelectLayer(command.target);ReleaseLayer(command.source);break;
                case 3:
                    CreateLayer(command.target);
                    if(!skipWrites&&!layerAliases.ContainsKey(command.target))
                    {CopyLayerData(LayerTexture(command.source),LayerTexture(command.target));RecordLayerWrite(command.target);}
                    break;
                case 4:
                    if(command.blend<0||command.blend>1)throw new NotSupportedException("未支持的图层混合方式。");
                    if(!skipWrites)LayerComposite(command.source,command.target,command.mask,command.blend,command,backdropSlot:3+index);
                    break;
                case 5:ReleaseLayer(command.source);break;
                case 6:
                    if(skipWrites)break;
                    if(elementFilterElements.ContainsKey(index)){DrawElementEffect(index,command,operation);break;}
                    if(command.blend==1)LayerComposite(command.source,command.source,command.mask,1,command,false);
                    else
                    {
                        var context=new FilterRenderContext(buffer,layerCompositeMesh,LayerTexture(command.source),LayerTexture(command.source,1),LayerTexture(command.source,2));
                        OwnerSurface.FilterOwner(command.mask).RecordFilter(context,filterInvocations[index]);
                        effectSubmitted.Add(new EffectSubmission {ProcessingPasses=context.ProcessingPasses});CopyLayerData(LayerTexture(command.source,2),LayerTexture(command.source));
                    }
                    break;
                default:throw new NotSupportedException("未知 URUI 图层操作："+command.kind);
            }
        }
        void FinishLayers(int commandCount,ref PhaseFrame phase)
        {
            using var profile = finishRecordingPhase.Auto();
            if(colorPassActive)throw new InvalidOperationException("原生共享颜色区间未结束。");
            if(layerCursor!=layerOrderCount||activeLayer!=0||layerTargets.Count!=0)throw new InvalidOperationException("URUI 图层或遮罩未平衡释放。");
        }
        sealed class LayerProgramRelease:IDisposable
        {
            Material material;Mesh mesh;
            internal LayerProgramRelease(Material material,Mesh mesh){this.material=material;this.mesh=mesh;}
            public void Dispose(){if(material)URUI.Resources.GpuRetirement.DestroyOwnedObject(material);if(mesh)URUI.Resources.GpuRetirement.DestroyOwnedObject(mesh);material=null;mesh=null;}
        }
        void ReleaseLayerRendering()
        {
            ClearLayerRecording();
            foreach(var capture in captures.Values) { RetirePaintResource(capture.Lease); }
            captures.Clear();
            var retired=new List<IDisposable>();
            foreach(var pair in preparedLayerTargets)retired.Add(pair.Key.Item2==0?(IDisposable)layerContents[pair.Key.Item1]:pair.Value);
            foreach(var program in elementCompositeMaterials.Values)retired.Add(new LayerProgramRelease(program,null));
            elementCompositeMaterials.Clear();elementComposites.Clear();
            if(layerCompositeMaterial||layerCompositeMesh)retired.Add(new LayerProgramRelease(layerCompositeMaterial,layerCompositeMesh));
            if(framebufferRasterMaterial)retired.Add(new LayerProgramRelease(framebufferRasterMaterial,null));
            if(framebufferLayerMaterial)retired.Add(new LayerProgramRelease(framebufferLayerMaterial,null));
            if(colorInputMaterial)retired.Add(new LayerProgramRelease(colorInputMaterial,null));
            framebufferRasterMaterial = framebufferLayerMaterial = null;
            framebufferFetchActive = framebufferProgramsChecked = false;
            effectGeometryCache.Clear(); usedEffectGeometries.Clear(); removedEffectGeometries.Clear();
            if(retired.Count>0)RetirePaintResources(retired);
            preparedLayerTargets.Clear();frozenFilterInvocations.Clear();layerContents.Clear();layerVersions.Clear();textureVersions.Clear();effectTargets?.Dispose();effectTargets=null;layerCompositeMaterial=null;layerCompositeMesh=null;
            ReleaseColorPass();
            layerTargets.Clear();maskSourceLayers.Clear();submissionTarget=null;submissionTargetInfo=default;rootTextureRequired=false;rootDirectScreenSource=false;
        }
    }
}
