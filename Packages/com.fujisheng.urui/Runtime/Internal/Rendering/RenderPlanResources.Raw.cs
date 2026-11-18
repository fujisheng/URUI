using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering
{
    internal sealed unsafe partial class RenderPlanResources
    {
        static int AttributeBytes(VertexAttributeDescriptor descriptor)
        {
            var scalar=descriptor.format switch
            {
                VertexAttributeFormat.Float32 or VertexAttributeFormat.UInt32 or VertexAttributeFormat.SInt32=>4,
                VertexAttributeFormat.Float16 or VertexAttributeFormat.UNorm16 or VertexAttributeFormat.SNorm16 or VertexAttributeFormat.UInt16 or VertexAttributeFormat.SInt16=>2,
                VertexAttributeFormat.UNorm8 or VertexAttributeFormat.SNorm8 or VertexAttributeFormat.UInt8 or VertexAttributeFormat.SInt8=>1,
                _=>throw new NotSupportedException("无法复制该原始顶点格式："+descriptor.format)
            };
            return scalar*descriptor.dimension;
        }
        static void UploadRawStream(Mesh destination,in RawGeometryInput source,int stream,MeshUpdateFlags flags)
        {
            var data=source.Data;var input=data.GetVertexData<byte>(stream);
            var sourceStride=data.GetVertexBufferStride(stream);var destinationStride=destination.GetVertexBufferStride(stream);
            var same=sourceStride==destinationStride;
            foreach(var attribute in source.Layout)
                if(attribute.stream==stream&&data.GetVertexAttributeOffset(attribute.attribute)!=destination.GetVertexAttributeOffset(attribute.attribute))same=false;
            if(same)
            {destination.SetVertexBufferData(input,0,0,checked(data.vertexCount*sourceStride),stream,flags);return;}
            // 仅拷贝格式字节。重排由 Unity 原生 MemCpyStride 完成，保留未知 shader 的所有通道。
            using var packed=new NativeArray<byte>(checked(data.vertexCount*destinationStride),Allocator.Temp,NativeArrayOptions.ClearMemory);
            var output=(byte*)NativeArrayUnsafeUtility.GetUnsafePtr(packed);var original=(byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(input);
            foreach(var attribute in source.Layout)
            {
                if(attribute.stream!=stream)continue;
                UnsafeUtility.MemCpyStride(output+destination.GetVertexAttributeOffset(attribute.attribute),destinationStride,
                    original+data.GetVertexAttributeOffset(attribute.attribute),sourceStride,AttributeBytes(attribute),data.vertexCount);
            }
            destination.SetVertexBufferData(packed,0,0,packed.Length,stream,flags);
        }
        void ApplyRaw(in RawGeometryInput source)
        {
            if(meshes.TryGetValue(source.Identity,out var cached)){cached.Used=++use;return;}
            var data=source.Data;var stride=0;
            for(var stream=0;stream<data.vertexBufferCount;stream++)stride+=data.GetVertexBufferStride(stream);
            var indexCount=data.indexFormat==IndexFormat.UInt16?data.GetIndexData<ushort>().Length:data.GetIndexData<uint>().Length;
            var entry=Acquire(source.Identity,data.vertexCount,indexCount,data.indexFormat,source.Layout,stride,true,out var configure);var mesh=entry.Mesh;
            try
            {
                if(configure){mesh.SetVertexBufferParams(entry.VertexCapacity,source.Layout);mesh.SetIndexBufferParams(entry.IndexCapacity,data.indexFormat);}
                const MeshUpdateFlags flags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices|MeshUpdateFlags.DontNotifyMeshUsers;
                long uploaded=0;
                for(var stream=0;stream<data.vertexBufferCount;stream++)
                {UploadRawStream(mesh,source,stream,flags);uploaded+=(long)data.vertexCount*mesh.GetVertexBufferStride(stream);}
                if(data.indexFormat==IndexFormat.UInt16)
                {var indices=data.GetIndexData<ushort>();mesh.SetIndexBufferData(indices,0,0,indices.Length,flags);uploaded+=(long)indices.Length*2;}
                else
                {var indices=data.GetIndexData<uint>();mesh.SetIndexBufferData(indices,0,0,indices.Length,flags);uploaded+=(long)indices.Length*4;}
                if(uploaded>entry.Bytes)throw new NotSupportedException("原始顶点布局展开超过已保留的几何预算。");
                mesh.subMeshCount=data.subMeshCount;
                for(var submesh=0;submesh<data.subMeshCount;submesh++)mesh.SetSubMesh(submesh,data.GetSubMesh(submesh),flags);
                GeometryUploadBytes+=uploaded;meshes.Add(source.Identity,entry);
            }
            catch{retire(entry);throw;}
        }
    }
}
