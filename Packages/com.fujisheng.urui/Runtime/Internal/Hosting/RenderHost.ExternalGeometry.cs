using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Documents;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        ExternalGeometryInputs externalGeometryInputs;
        bool[] geometryCommands=Array.Empty<bool>();
        readonly Dictionary<ulong,(Document Owner,ulong Element,string Shader)> geometryProviders=new();
        readonly HashSet<ulong> geometryGroupProviders=new();
        internal int RecordedExternalGeometryDraws {get;private set;}
        int PrepareGeometryProviders(in RenderPlanInputPacket input)
        {
            using var profile = geometryProvidersMarker.Auto();
            var commandsCount=checked((int)input.CommandCount);geometryProviders.Clear();
            if(geometryCommands.Length<commandsCount)geometryCommands=new bool[commandsCount];
            Array.Clear(geometryCommands,0,commandsCount);
            if(externalBindings.Length<input.PaintCount)externalBindings=new RenderPlanExternalBinding[input.PaintCount];
            maskSourceLayers.Clear();for(var index=0;index<layerCount;index++)if(layerCommands[index].kind==3)maskSourceLayers.Add(layerCommands[index].source);
            var count=0;
            unsafe
            {
                var layers=(ulong*)input.CommandLayers;
                for(var ordinal=0;ordinal<input.PaintCount;ordinal++)
                {
                    var paint=paintScratch[ordinal];if(paint.reserved!=0)continue;
                    var owner=OwnerSurface.ElementOwner(paint.element);if(!owner)throw new InvalidOperationException("几何准备器所属文档已失效。");
                    var shader=PaintName(paint.shader);if(!owner.HasGeometryProvider(paint.element,shader))continue;
                    var index=paint.command_index;var command=commands[index];
                    var clipped=command.ClipEnabled&&(command.clip_w<=0||command.clip_h<=0);
                    var provider=owner.PrepareGeometry(paint.element,shader,externalGeometryInputs,(uint)index,clipped,maskSourceLayers.Contains(layers[index]));
                    geometryCommands[index]=true;geometryProviders[provider]=(owner,paint.element,shader);
                    externalBindings[count++]=new RenderPlanExternalBinding {Command=(uint)index,Flags=8};
                }
            }
            return count;
        }
        void DrawNativeExternalGeometry(in RenderPlanOperation operation)
        {
            if(!EndEncodedRaster())SelectLayer(activeLayer);
            var program=externalGeometryInputs.Program(operation.Program);var page=parameterPages.Current.Texture;
            var properties=reusableProperties;properties.Clear();
            var width=page.width;var height=page.height;
            properties.SetTexture(parametersId,page);properties.SetVector(parameterSizeId,new Vector4(1f/width,1f/height,width,height));
            buffer.DrawMesh(planResources.Mesh(operation.Geometry),Matrix4x4.identity,program.Material,0,program.Pass,properties);
            RecordedExternalGeometryDraws++;NotifyGeometryGroup(operation);
        }
        void DrawNativeRawGeometry(in RenderPlanOperation operation,in RenderPlanParameter parameter)
        {
            if(!EndEncodedRaster())SelectLayer(activeLayer);
            var program=externalGeometryInputs.Program(operation.Program);var source=externalGeometryInputs.RawGeometry(operation.LayerIndex);
            var properties=reusableProperties;properties.Clear();properties.SetFloat("_UruiPaintOpacity",1);
            properties.SetFloat("_UruiClipEnabled",((int)parameter.Metadata.x&1)!=0?1:0);properties.SetVector("_UruiClipRect",parameter.Clip);
            buffer.DrawMesh(planResources.Mesh(source.Identity),parameter.Matrix,program.Material,source.Submesh,program.Pass,properties);
            RecordedExternalGeometryDraws++;NotifyGeometryGroup(operation);
        }
        void NotifyGeometryGroup(in RenderPlanOperation operation)
        {
            geometryGroupProviders.Clear();
            var end=checked((int)(operation.LayerIndex+operation.Count));
            for(var index=checked((int)operation.LayerIndex);index<end;index++)
            {
                var draw=externalGeometryInputs.Draws[index];
                if(externalGeometryInputs.Geometry[draw.GeometryIndex].IndexCount==0||!geometryGroupProviders.Add(draw.Provider))continue;
                var owner=geometryProviders[draw.Provider];owner.Owner.GeometryRecorded(owner.Element,owner.Shader,1);
            }
        }
    }
}
