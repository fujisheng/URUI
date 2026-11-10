using System;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Meshes;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        bool DrawFrame(ref PhaseFrame phase)
        {
            if(!RestoreSceneCamera())return false;
            var completed=false;
            effectSubmitted.Clear();
            OwnerSurface.BeginDispatch();
            try{DrawFrameCore(ref phase);completed=true;}
            finally
            {
                if(!completed)InvalidateRecording(true);
                OwnerSurface.EndDispatch();OwnerSurface.FinishNativeCall();
            }
            return true;
        }

        void DrawFrameCore(ref PhaseFrame phase)
        {
            using var profile = recordingPhase.Auto();
            RequireRecordingReady();
            RecordedGeometryDraws=0;
            RecordedRasterResolves=0;
            borrowedTextures.Validate();shaderPropertyState.Reset();
            ShaderContractActive=propertyContract&&propertyContract.Supports(material);
#if URUI_DEBUG
            PropertyCalls=PropertyWriteCalls=PropertyReuseHits=0;recordCallsThisFrame=0;
#endif
            foreach(var owner in OwnerSurface.DocumentsSnapshot())if(owner&&owner.IsLoaded)owner.UpdateEffectParameters();
            using(nativeRenderPhase.Auto()){NativeMethods.urui_render();}
            var count=NativeMethods.urui_command_count();
            if(count<0)throw new InvalidOperationException("URUI 绘制命令数量无效。");
            var unchanged=OrdinaryCommandsUnchanged(count);
            if(commands.Length<count)commands=new CommandNative[Math.Max(count,commands.Length*2+16)];
            if(commandTransforms.Length<count*16)commandTransforms=new float[Math.Max(count*16,commandTransforms.Length*2+256)];
            var read=unchanged?count:NativeMethods.urui_get_commands(commands,count);
            if(read!=count)throw new InvalidOperationException("URUI 绘制命令数量变化。");
            ReadPaintCommands();
            foreach(var owner in OwnerSurface.DocumentsSnapshot())if(owner&&owner.IsLoaded)owner.FreezeEffectParameters();
            PrepareImageEffects();ReadLayerCommands();
            if(!unchanged&&NativeMethods.urui_get_command_transforms(commandTransforms,commandTransforms.Length)!=read)
                throw new InvalidOperationException("URUI 绘制变换数量变化。");
#if URUI_DEBUG
            DrawCommands=read;
#endif
            buffer.Clear();
            PrepareLayerCache(read);BeginLayers();batchCursor=0;
            batchGroup.Clear();batchVertexCount=batchIndexCount=batchTextureCount=0;
            pendingGeometry.Clear();segmentCursor=0;
            if(submissionTarget.HasValue){buffer.SetRenderTarget(submissionTarget.Value);buffer.SetViewport(submissionViewport);}
            RetireReleasedTextures(ref phase);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(0,UiWidth,UiHeight,0,-1,1));
            if(ReplayOrdinaryGeometry(read,ref phase))return;
            var used=frameUsedGeometry;used.Clear();
            using(commandPhase.Auto())
            {
            for(var index=0;index<read;index++)
            {
                ApplyLayers(index,ref phase);
                var command=LayerInputClip(commands[index]);
                if(reuseLayerContents&&activeLayer!=0){if(command.geometry!=0)used.Add(command.geometry);continue;}
                var transform=new Matrix4x4();
                for(var component=0;component<16;component++)transform[component]=commandTransforms[index*16+component];
                if(paintCommands.TryGetValue(index,out var paint)&&paint.reserved==0)
                {
                    DrawPaint(paint,command,transform,ref phase);continue;
                }
                if(command.geometry==0)continue;
                var geometry=ReadGeometry(command.geometry);
                if(geometry==null)continue;
                used.Add(command.geometry);
                if(paintCommands.TryGetValue(index,out var direct)&&direct.reserved==2)
                {
                    FlushGroup(ref phase);DrawDirectColorFilter(direct,command,transform,SourceMesh(geometry),ref phase);continue;
                }
                if(paintCommands.TryGetValue(index,out var effect)&&effect.reserved==1)
                {
                    if(DrawImageEffect(effect,command,transform,SourceMesh(geometry),ref phase))continue;
                }
                var affine=BatchRules.EquivalentAffine2D(transform,transform)&&float.IsFinite(command.tx)&&float.IsFinite(command.ty);
                var item=new BatchItem{command=command,geometry=geometry,transform=transform,transformed=!affine};
                if(ShaderContractActive&&affine)
                {
                    SetDrawBounds(ref item);
                    if(FullyClipped(item))continue;
                    pendingGeometry.Add(item);
                    if(pendingGeometry.Count==4096)FlushGroup(ref phase);
                }
                else
                {
                    FlushGroup(ref phase);DrawOne(item,ref phase);
                }
            }
            }
            FinishLayers(read,ref phase);FlushGroup(ref phase);EndEncodedRaster();
            for(var index=batches.Count-1;index>=batchCursor;index--)
            {
                if(batches[index].mesh)Destroy(batches[index].mesh);
                batches.RemoveAt(index);
            }
            if(segmentPlans.Count>segmentCursor)segmentPlans.RemoveRange(segmentCursor,segmentPlans.Count-segmentCursor);
            frameStaleGeometry.Clear();
            foreach(var pair in meshes)
                if(!used.Contains(pair.Key)&&NativeMethods.urui_geometry_exists(pair.Key)==0)frameStaleGeometry.Add(pair.Key);
            foreach(var id in frameStaleGeometry){RecycleGeometry(meshes[id]);meshes.Remove(id);}
            CompleteOrdinaryGeometry(read);
        }

        MeshCache ReadGeometry(ulong id)
        {
            if(meshes.TryGetValue(id,out var existing))return existing;
            if(NativeMethods.urui_get_geometry_info(id,out var vertices,out var indices)==0||vertices<=0||indices<=0)return null;
            MeshCache value=null;
            for(var index=idleGeometry.Count-1;index>=0;index--)
                if(idleGeometry[index].positions.Length>=vertices&&idleGeometry[index].indices.Length>=indices)
                {
                    value=idleGeometry[index];idleGeometry.RemoveAt(index);pooledGeometryBytes-=value.CapacityBytes;break;
                }
            value??=new MeshCache{positions=new Vector3[vertices],uv=new Vector2[vertices],colors=new Color32[vertices],indices=new int[indices]};
            if(vertexScratch.Length<vertices)vertexScratch=new VertexNative[Math.Max(vertices,vertexScratch.Length*2+16)];
            if(NativeMethods.urui_copy_geometry(id,vertexScratch,vertices,value.indices,indices)==0){RecycleGeometry(value);return null;}
            value.vertexCount=vertices;value.indexCount=indices;
            var minimum=new Vector3(float.PositiveInfinity,float.PositiveInfinity,0);
            var maximum=new Vector3(float.NegativeInfinity,float.NegativeInfinity,0);
            for(var index=0;index<vertices;index++)
            {
                var source=vertexScratch[index];var position=new Vector3(source.x,source.y,0);
                value.positions[index]=position;value.uv[index]=new Vector2(source.u,source.v);
                value.colors[index]=new Color32(source.r,source.g,source.b,source.a);
                minimum=Vector3.Min(minimum,position);maximum=Vector3.Max(maximum,position);
            }
            value.bounds=new Bounds((minimum+maximum)*.5f,maximum-minimum);
            meshes.Add(id,value);return value;
        }

        void RecycleGeometry(MeshCache value)
        {
            if(value.mesh)Destroy(value.mesh);value.mesh=null;
            // 只复用私有CPU数组；已进入GPU命令的Mesh与公开效果几何均不改写。
            if(idleGeometry.Count>=2048||pooledGeometryBytes+value.CapacityBytes>16*1024*1024)return;
            idleGeometry.Add(value);pooledGeometryBytes+=value.CapacityBytes;
        }
        Mesh SourceMesh(MeshCache value)
        {
            if(!value.mesh)
            {
                if(zeroTextureSlots.Length<value.vertexCount)zeroTextureSlots=new Vector2[Math.Max(value.vertexCount,zeroTextureSlots.Length*2+16)];
                value.mesh=UploadMesh("URUI geometry",value.positions,value.uv,value.colors,value.indices,value.vertexCount,value.indexCount,zeroTextureSlots);
            }
            return value.mesh;
        }
    }
}
