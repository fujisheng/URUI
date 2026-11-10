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
        int layerCount,layerCursor;
        ulong activeLayer;
        readonly HashSet<ulong> layerTargets=new();
        readonly HashSet<ulong> maskSourceLayers=new();
        readonly HashSet<ulong> haloLayers=new();
        readonly Dictionary<ulong,RectInt> layerRegions=new();
        readonly Dictionary<ulong,LayerCommandNative> layerEntries=new();
        readonly Dictionary<(ulong,int),EffectTargetPool.Lease> preparedLayerTargets=new();
        readonly HashSet<(ulong,int)> desiredLayerTargets=new();
        readonly Dictionary<(ulong,int),Vector2Int> layerBackdropSizes=new();
        readonly List<(ulong,int)> removedLayerTargets=new();
        readonly Dictionary<int,FilterInvocation> filterInvocations=new();
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
        bool encodedRasterActive;
        Shader encodedRasterShader;
        bool encodedRasterCapable;
        bool UsesEncodedRaster
        {
            get
            {
                if (QualitySettings.activeColorSpace != ColorSpace.Linear || !material) return false;
                var current = material.shader;
                if (current != encodedRasterShader)
                {
                    encodedRasterShader = current;
                    encodedRasterCapable = current && current.name == "URUI/Premultiplied";
                }
                return encodedRasterCapable;
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
        static readonly int LayerMaskEnabled=Shader.PropertyToID("_UruiLayerMaskEnabled");
        static readonly int LayerMaskRect=Shader.PropertyToID("_UruiLayerMaskRect");
        static readonly int LayerClip=Shader.PropertyToID("_UruiLayerClip");
        static readonly int LayerClipEnabled=Shader.PropertyToID("_UruiLayerClipEnabled");
        void ReadLayerCommands()
        {
            liveFilterNames.Clear();
            layerCount=NativeMethods.urui_get_layer_commands(null,0);maskSourceLayers.Clear();haloLayers.Clear();layerEntries.Clear();filterInvocations.Clear();layerRegions.Clear();elementFilterElements.Clear();elementComposites.Clear();
            if(layerCount<0)throw new InvalidOperationException("URUI 图层命令无效。");
            if(layerCommands.Length<layerCount)layerCommands=new LayerCommandNative[layerCount];
            if(layerCount>0&&NativeMethods.urui_get_layer_commands(layerCommands,layerCount)!=layerCount)throw new InvalidOperationException("URUI 图层命令数量变化。");
            if(layerCount>0)
            {
                if(NativeMethods.urui_filter_api_version()!=3)throw new NotSupportedException("需要 URUI 图层滤镜协议 v3。");
                EnsureLayerProgram();
            }
            for(int index=0;index<layerCount;index++)
            {
                var command=layerCommands[index];
                if(command.command_index<0||index>0&&command.command_index<layerCommands[index-1].command_index)throw new InvalidOperationException("URUI 图层顺序无效。");
                if(command.kind==1)layerEntries.Add(command.target,command);
                // PushLayer precedes URUI applying this node's ancestor clip.
                // The final composite carries the authoritative inherited clip.
                if(command.kind==4)layerEntries[command.source]=command;
                if(command.kind==3)maskSourceLayers.Add(command.source);
                if(command.kind!=6||command.blend!=0)continue;
                var read=NativeMethods.urui_filter_info(command.mask,out var filter,filterNameBuffer,filterNameBuffer.Length);
                if(read<0&&-read>filterNameBuffer.Length){filterNameBuffer=new byte[-read];read=NativeMethods.urui_filter_info(command.mask,out filter,filterNameBuffer,filterNameBuffer.Length);}
                if(read<=0)throw new InvalidOperationException("URUI 滤镜描述读取失败。");
                liveFilterNames.Add(command.mask);
                if(!filterNames.TryGetValue(command.mask,out var key)){key=Encoding.UTF8.GetString(filterNameBuffer,0,read-1);filterNames.Add(command.mask,key);}
                var invocation=new FilterInvocation(key,filter.value,filter.sigma,new Vector2(filter.x,filter.y),new Color(filter.r,filter.g,filter.b,filter.a),default);
                filterInvocations.Add(index,invocation);
                if (filter.element != 0)
                {
                    elementFilterElements.Add(index, filter.element);
                    var expansion = OwnerSurface.FilterOwner(command.mask).ResolveEffectOutput(filter.element, key, out var composite, out var transforms);
                    elementComposites.Add(command.source,composite);
                    if (NativeMethods.urui_element_effect_expansion(command.mask, expansion.x, expansion.y, expansion.z, expansion.w) != 1)
                    { throw new InvalidOperationException("无法提交节点效果扩边。"); }
                    if(transforms.Length>64)throw new InvalidOperationException("节点效果覆盖变换超出上限。");
                    if(effectTransformScratch.Length<transforms.Length*16)effectTransformScratch=new float[transforms.Length*16];
                    for(var matrix=0;matrix<transforms.Length;matrix++)for(var entry=0;entry<16;entry++)effectTransformScratch[matrix*16+entry]=transforms[matrix][entry];
                    if(NativeMethods.urui_element_effect_transforms(command.mask,effectTransformScratch,transforms.Length)!=1)
                        throw new InvalidOperationException("无法提交节点效果覆盖变换。");
                }
                if(key=="blur"||key=="drop-shadow")haloLayers.Add(command.source);
            }
            removedFilterNames.Clear();foreach(var id in filterNames.Keys)if(!liveFilterNames.Contains(id))removedFilterNames.Add(id);
            foreach(var id in removedFilterNames)filterNames.Remove(id);
            if (layerCount > 0)
            {
                var count=NativeMethods.urui_get_layer_regions(null,0);if(count<0)throw new InvalidOperationException("URUI 图层范围读取失败。");
                if (layerRegionScratch.Length < count) { layerRegionScratch = new LayerRegionNative[count]; }
                if(NativeMethods.urui_get_layer_regions(layerRegionScratch,count)!=count)throw new InvalidOperationException("URUI 图层范围过大或无效。");
                for (var index=0;index<count;index++) { var region=layerRegionScratch[index];layerRegions.Add(region.layer,new RectInt(region.x,region.y,region.width,region.height)); }
                for (var index=0;index<layerCount;index++) if (filterInvocations.TryGetValue(index,out var current))
                {
                    var invocation=new FilterInvocation(current.Name,current.Value,current.Sigma,current.Offset,current.Color,Region(layerCommands[index].source));
                    filterInvocations[index]=invocation;
                    if (!elementFilterElements.ContainsKey(index)) { OwnerSurface.FilterOwner(layerCommands[index].mask).PrepareFilter(invocation); }
                }
            }
            PrepareLayerTargets();
            effectInputRegions.Clear();
            if(elementFilterElements.Count>0)
            {
                var count=NativeMethods.urui_get_effect_input_regions(null,0);
                if(count<0)throw new InvalidOperationException("节点效果输入区域无效。");
                if(effectInputRegionScratch.Length<count)effectInputRegionScratch=new NativeMethods.EffectInputRegionNative[count];
                if(NativeMethods.urui_get_effect_input_regions(effectInputRegionScratch,count)!=count)throw new InvalidOperationException("节点效果输入区域读取失败。");
                for(var index=0;index<count;index++)
                {var input=effectInputRegionScratch[index];effectInputRegions.Add(input.Filter,new Rect(input.X,input.Y,input.Width,input.Height));}
            }
            PrepareElementEffects();
        }
        void PrepareElementEffects()
        {
            var grouped=elementEffectInputs;ResetEffectInputs(grouped);
            foreach (var pair in elementFilterElements)
            {
                var command = layerCommands[pair.Key];
                var owner = OwnerSurface.FilterOwner(command.mask);
                var name = filterInvocations[pair.Key].Name;
                if (owner.EffectKindForName(name) != EffectKind.Element) { throw new InvalidOperationException("子树需要 Element 效果：" + name); }
                if (!grouped.TryGetValue(owner, out var uses)) { grouped.Add(owner, uses = new()); }
                var region = Region(command.source);
                uses.Add(new EffectInput(pair.Value, name, LayerTexture(command.source), false, EffectGeometry.CapturedQuad,
                    new Rect(region.x, region.y, region.width, region.height),effectInputRegions[command.mask]));
            }
            var version = unchecked(++elementEffectVersion);
            foreach (var pair in grouped) { pair.Key.PrepareEffects(pair.Value, version); }
            foreach (var owner in OwnerSurface.DocumentsSnapshot()) { if (owner && owner.IsLoaded) { owner.EndEffectPreparation(); } }
        }
        void DrawElementEffect(int index, LayerCommandNative command)
        {
            var owner = OwnerSurface.FilterOwner(command.mask);
            var region = Region(command.source);
            var destination = LayerTexture(command.source, 2);
            buffer.SetRenderTarget(destination); buffer.SetViewport(new Rect(0, 0, region.width, region.height));
            buffer.ClearRenderTarget(false, true, Color.clear);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(region.xMin, region.xMax, region.yMax, region.yMin, -1, 1));
            var transform = Matrix4x4.TRS(new Vector3(region.x, region.y, 0), Quaternion.identity, new Vector3(region.width, region.height, 1));
            var paint = paintContexts.Open(owner.WrapElement(elementFilterElements[index]), buffer,
                transform, Vector4.zero, false, maskSourceLayers.Contains(activeLayer));
            try
            {
                if (!owner.RecordEffect(elementFilterElements[index], paint, layerCompositeMesh,
                    owner.EffectSubmissionSink, EffectKind.Element))
                { buffer.CopyTexture(LayerTexture(command.source), destination); }
                else { effectSubmitted.Add(new EffectSubmission { ProcessingPasses=1 }); }
            }
            finally { paint.Close(); SelectLayer(activeLayer); }
            buffer.CopyTexture(destination, LayerTexture(command.source));
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
        RenderTexture LayerTexture(ulong id,int slot=0)=>preparedLayerTargets[(id,slot)].Texture;
        Rect LayerViewport(ulong id)
        {
            if(id==0)return submissionTarget.HasValue?submissionViewport:targetCamera.pixelRect;
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
        void CopyBackdrop(ulong source,RenderTargetIdentifier destination,RectInt read,int pass=3)
        {
            buffer.SetGlobalVector(LayerReadRect,new Vector4(read.x,read.y,read.width,read.height));
            // Metal 的最终 CameraTarget 采用屏幕原点，texel size 仍可能为正；
            // 离屏目标和嵌套图层继续使用纹理原点，不能一并翻转。
            bool cameraTarget=source==0&&!targetCamera.targetTexture&&
                (!submissionTarget.HasValue||submissionTarget.Value.Equals(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget)));
            buffer.SetGlobalFloat(LayerReadCameraTarget,cameraTarget?1:0);
            buffer.Blit(LayerTarget(source),destination,layerCompositeMaterial,pass);
        }
        void BeginEncodedRaster()
        {
            if(encodedRasterActive||!UsesEncodedRaster)return;
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
        void PrepareOptimizedRaster()
        {
            if(UsesEncodedRaster)ReadLayerCommands();
            BeginLayers();
        }
        void EndEncodedRaster()
        {
            if(!encodedRasterActive)return;
            buffer.SetGlobalFloat(EncodedTarget,0);
            SelectLayer(activeLayer);
            buffer.SetGlobalTexture(LayerSource,LayerTexture(activeLayer,-1));
            var region=Region(activeLayer);
            buffer.DrawMesh(layerCompositeMesh,Matrix4x4.TRS(new Vector3(region.x,region.y,0),Quaternion.identity,
                new Vector3(region.width,region.height,1)),layerCompositeMaterial,0,5);
            encodedRasterActive=false;
            RecordedRasterResolves++;
        }
        void PrepareLayerTargets()
        {
            var desired=desiredLayerTargets;desired.Clear();
            var backdropSizes=layerBackdropSizes;backdropSizes.Clear();
            foreach(var pair in layerRegions)desired.Add((pair.Key,0));
            if(UsesEncodedRaster&&NativeMethods.urui_command_count()>0)
            {
                EnsureLayerProgram();
                var viewport=LayerViewport(0);
                desired.Add((0,-1));backdropSizes.Add((0,-1),new Vector2Int(Math.Max(1,Mathf.CeilToInt(viewport.width)),Math.Max(1,Mathf.CeilToInt(viewport.height))));
                foreach(var pair in layerRegions){desired.Add((pair.Key,-1));backdropSizes.Add((pair.Key,-1),pair.Value.size);}
            }
            for(int i=0;i<layerCount;i++)if(layerCommands[i].kind==6){desired.Add((layerCommands[i].source,1));desired.Add((layerCommands[i].source,2));}
            if(QualitySettings.activeColorSpace==ColorSpace.Linear)
                for(int i=0;i<layerCount;i++)
                    if(layerCommands[i].kind==4&&layerCommands[i].blend==0&&layerCommands[i].source!=0&&
                        !(elementComposites.TryGetValue(layerCommands[i].source,out var composite)&&composite.UsesUnityBlend))
                    {
                        var key=(layerCommands[i].source,3+i);
                        desired.Add(key);backdropSizes.Add(key,BackdropReadRect(layerCommands[i].source,layerCommands[i].target).size);
                        if(layerCommands[i].source==layerCommands[i].target)desired.Add((layerCommands[i].source,1));
                    }
            var format=targetCamera&&targetCamera.allowHDR?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32;
            var removed=removedLayerTargets;removed.Clear();
            List<IDisposable> retired=null;
            foreach(var pair in preparedLayerTargets)
            {
                bool keep=desired.Contains(pair.Key);
                if(keep){var size=pair.Key.Item2>=3||pair.Key.Item2<0?backdropSizes[pair.Key]:Region(pair.Key.Item1).size;var texture=pair.Value.Texture;keep=texture.width==size.x&&texture.height==size.y&&texture.format==format;}
                if(!keep){removed.Add(pair.Key);(retired??=new List<IDisposable>()).Add(pair.Value);}
            }
            if(retired!=null){InvalidateLayerCache();RetirePaintResources(retired);foreach(var key in removed)preparedLayerTargets.Remove(key);}
            effectTargets??=new EffectTargetPool();
            foreach(var key in desired)if(!preparedLayerTargets.ContainsKey(key)){var size=key.Item2>=3||key.Item2<0?backdropSizes[key]:Region(key.Item1).size;InvalidateLayerCache();preparedLayerTargets.Add(key,effectTargets.Rent(size.x,size.y,format,key.Item2<0));}
        }
        void BeginLayers(){layerCursor=0;activeLayer=0;layerTargets.Clear();encodedRasterActive=false;}
        RenderTargetIdentifier LayerTarget(ulong id)
        {
            if(id==0)return submissionTarget??new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            if(!layerTargets.Contains(id))throw new InvalidOperationException("URUI 图层资源不存在："+id);
            return new RenderTargetIdentifier(LayerTexture(id));
        }
        void CreateLayer(ulong id){if(id==0||!preparedLayerTargets.ContainsKey((id,0))||!layerTargets.Add(id))throw new InvalidOperationException("URUI 图层资源身份无效。");}
        void SelectLayer(ulong id)
        {
            activeLayer=id;buffer.SetRenderTarget(LayerTarget(id));var region=Region(id);
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
                CopyBackdrop(0,LayerTarget(target),BackdropReadRect(target,0));
                SelectLayer(activeLayer);
                return;
            }
            var sourceRegion=Region(source);
            EffectComposite effectComposite=default;
            var unityComposite=source!=target&&elementComposites.TryGetValue(source,out effectComposite)&&effectComposite.UsesUnityBlend;
            bool encodedOver=!unityComposite&&blend==0&&QualitySettings.activeColorSpace==ColorSpace.Linear;
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
            buffer.SetGlobalTexture(LayerSource,LayerTexture(source));
            if(target==source){buffer.SetRenderTarget(LayerTexture(source,1));var bounds=Region(source);buffer.SetViewport(new Rect(0,0,bounds.width,bounds.height));buffer.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(bounds.xMin,bounds.xMax,bounds.yMax,bounds.yMin,-1,1));}
            else SelectLayer(target);
            buffer.DrawMesh(layerCompositeMesh,Matrix4x4.TRS(new Vector3(sourceRegion.x,sourceRegion.y,0),Quaternion.identity,new Vector3(sourceRegion.width,sourceRegion.height,1)),
                unityComposite?ElementCompositeMaterial(effectComposite):layerCompositeMaterial,0,unityComposite?6:encodedOver?2:blend);
            if(target==source)buffer.CopyTexture(LayerTexture(source,1),LayerTexture(source));
        }
        void ApplyLayers(int commandIndex,ref PhaseFrame phase)
        {
            if(layerCursor>=layerCount||layerCommands[layerCursor].command_index!=commandIndex)return;
            FlushGroup(ref phase);
            EndEncodedRaster();
            while(layerCursor<layerCount&&layerCommands[layerCursor].command_index==commandIndex)
            {
                var index=layerCursor++;var command=layerCommands[index];
                switch(command.kind)
                {
                    case 1:CreateLayer(command.target);SelectLayer(command.target);if(!reuseLayerContents)buffer.ClearRenderTarget(false,true,Color.clear);break;
                    case 2:SelectLayer(command.target);ReleaseLayer(command.source);break;
                    case 3:CreateLayer(command.target);if(!reuseLayerContents)buffer.CopyTexture(LayerTexture(command.source),LayerTexture(command.target));break;
                    case 4:if(command.blend<0||command.blend>1)throw new NotSupportedException("未支持的图层混合方式。");if(!reuseLayerContents||command.target==0)LayerComposite(command.source,command.target,command.mask,command.blend,command,backdropSlot:3+index);break;
                    case 5:ReleaseLayer(command.source);break;
                    case 6:
                        if(reuseLayerContents)break;
                        if (elementFilterElements.ContainsKey(index)) { DrawElementEffect(index, command); break; }
                        if(command.blend==1)LayerComposite(command.source,command.source,command.mask,1,command,false);
                        else {
                            var context=new FilterRenderContext(buffer,layerCompositeMesh,LayerTexture(command.source),LayerTexture(command.source,1),LayerTexture(command.source,2));
                            OwnerSurface.FilterOwner(command.mask).RecordFilter(context,filterInvocations[index]);effectSubmitted.Add(new EffectSubmission { ProcessingPasses=context.ProcessingPasses });buffer.CopyTexture(LayerTexture(command.source,2),LayerTexture(command.source));
                        }
                        break;
                    default:throw new NotSupportedException("未知 URUI 图层操作："+command.kind);
                }
            }
        }
        void FinishLayers(int commandCount,ref PhaseFrame phase)
        {ApplyLayers(commandCount,ref phase);if(layerCursor!=layerCount||activeLayer!=0||layerTargets.Count!=0)throw new InvalidOperationException("URUI 图层或遮罩未平衡释放。");}
        sealed class LayerProgramRelease:IDisposable
        {
            Material material;Mesh mesh;
            internal LayerProgramRelease(Material material,Mesh mesh){this.material=material;this.mesh=mesh;}
            public void Dispose(){if(material)URUI.Resources.GpuRetirement.DestroyOwnedObject(material);if(mesh)URUI.Resources.GpuRetirement.DestroyOwnedObject(mesh);material=null;mesh=null;}
        }
        void ReleaseLayerRendering()
        {
            InvalidateLayerCache();
            var retired=new List<IDisposable>(preparedLayerTargets.Values);
            foreach(var program in elementCompositeMaterials.Values)retired.Add(new LayerProgramRelease(program,null));
            elementCompositeMaterials.Clear();elementComposites.Clear();
            if(layerCompositeMaterial||layerCompositeMesh)retired.Add(new LayerProgramRelease(layerCompositeMaterial,layerCompositeMesh));
            if (directColorMaterial) { retired.Add(new LayerProgramRelease(directColorMaterial, null)); directColorMaterial = null; }
            directColorFilters.Clear();
            effectGeometryCache.Clear(); usedEffectGeometries.Clear(); removedEffectGeometries.Clear();
            if(retired.Count>0)RetirePaintResources(retired);
            preparedLayerTargets.Clear();effectTargets?.Dispose();effectTargets=null;layerCompositeMaterial=null;layerCompositeMesh=null;
            layerTargets.Clear();maskSourceLayers.Clear();submissionTarget=null;
        }
    }
}
