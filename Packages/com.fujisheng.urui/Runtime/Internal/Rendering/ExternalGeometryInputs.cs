using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Rendering;

namespace URUI.Internal.Rendering
{
    // 原生输入窗口持有 MeshData；参数与流描述复制到原生后，网格数据仍只读借用到编译结束。
    internal sealed unsafe class ExternalGeometryInputs : IExternalGeometrySink,IRawGeometryBindings,IDisposable
    {
        static readonly Unity.Profiling.ProfilerMarker CaptureMarker=new("URUI.ExternalGeometry.Capture");
        struct View {internal int Submesh,Components;internal bool Raw;internal ulong Identity,Version,GpuIdentity;}
        sealed class MeshIdentity
        {
            internal readonly List<View> Views=new();
            internal VertexAttributeDescriptor[] Layout;
        }
        internal readonly ExternalProgramResources Programs=new();
        internal readonly struct Window : IDisposable
        {
            readonly ExternalGeometryInputs owner;readonly ulong generation;
            internal Window(ExternalGeometryInputs owner,ulong generation){this.owner=owner;this.generation=generation;}
            public void Dispose()=>owner?.End(generation);
        }
        static ulong nextGeometry,nextRaw;
        ConditionalWeakTable<Mesh,MeshIdentity> identities=new();
        readonly Dictionary<(ulong,ulong),int> captured=new();
        readonly List<Mesh.MeshDataArray> meshes=new();
        readonly Dictionary<ulong,ExternalProgramResources.Binding> programs=new();
        internal RenderPlanExternalGeometry[] Geometry=Array.Empty<RenderPlanExternalGeometry>();
        internal RenderPlanExternalDraw[] Draws=Array.Empty<RenderPlanExternalDraw>();
        RawGeometryInput[] rawGeometry=Array.Empty<RawGeometryInput>();
        internal int GeometryCount {get;private set;}
        internal int DrawCount {get;private set;}
        ulong generation;bool active,disposed;
        Mesh rectangleMesh;Material rectangleMaterial;ExternalGeometryProgram rectangleProgram;
        static readonly Unity.Profiling.ProfilerMarker BeginMarker=new("URUI.ExternalGeometry.Begin");
        static readonly Unity.Profiling.ProfilerMarker EndMarker=new("URUI.ExternalGeometry.End");
        internal Window Begin()
        {
            using var profile=BeginMarker.Auto();
            if(disposed)throw new ObjectDisposedException(nameof(ExternalGeometryInputs));
            if(active)throw new InvalidOperationException("外部几何输入窗口不能重入。");
            if(generation==ulong.MaxValue)throw new InvalidOperationException("外部几何输入窗口已耗尽。");
            active=true;generation++;GeometryCount=DrawCount=0;captured.Clear();programs.Clear();Programs.Begin();
            return new Window(this,generation);
        }
        static ulong Next(ref ulong value)
        {
            if(value==ulong.MaxValue)throw new InvalidOperationException("外部几何资源身份已耗尽。");return ++value;
        }
        static void Grow<T>(ref T[] array,int count)
        {if(array.Length<count)Array.Resize(ref array,Math.Max(count,array.Length*2+16));}
        (ulong Geometry,ulong Gpu) Identity(in ExternalGeometryInput input,int components,bool raw)
        {
            var metadata=identities.GetValue(input.Mesh,static mesh=>new MeshIdentity());
            for(var index=0;index<metadata.Views.Count;index++)
            {
                var view=metadata.Views[index];if(view.Submesh!=input.Submesh||view.Components!=components||view.Raw!=raw)continue;
                if(input.GeometryVersion<view.Version)throw new ArgumentOutOfRangeException(nameof(input),"几何内容版本不能倒退。");
                if(input.GeometryVersion!=view.Version)
                {view.Version=input.GeometryVersion;if(raw)view.GpuIdentity=RawIdentity();metadata.Views[index]=view;}
                return (view.Identity,view.GpuIdentity);
            }
            var id=Next(ref nextGeometry);var gpu=raw?RawIdentity():0;
            metadata.Views.Add(new View {Submesh=input.Submesh,Components=components,Raw=raw,Identity=id,Version=input.GeometryVersion,GpuIdentity=gpu});return (id,gpu);
        }
        static ulong RawIdentity()
        {
            if(nextRaw==long.MaxValue)throw new InvalidOperationException("外部原始网格身份已耗尽。");
            return Next(ref nextRaw)|(1ul<<63);
        }
        VertexAttributeDescriptor[] Layout(Mesh source,Mesh.MeshData data)
        {
            var metadata=identities.GetValue(source,static mesh=>new MeshIdentity());var count=0;
            for(var kind=0;kind<=13;kind++)if(data.HasVertexAttribute((VertexAttribute)kind))count++;
            var layout=metadata.Layout;var same=layout!=null&&layout.Length==count;var index=0;
            for(var kind=0;same&&kind<=13;kind++)
            {
                var attribute=(VertexAttribute)kind;if(!data.HasVertexAttribute(attribute))continue;
                var value=layout[index++];same=value.attribute==attribute&&value.format==data.GetVertexAttributeFormat(attribute)&&
                    value.dimension==data.GetVertexAttributeDimension(attribute)&&value.stream==data.GetVertexAttributeStream(attribute);
            }
            if(same)return layout;
            layout=new VertexAttributeDescriptor[count];index=0;
            for(var kind=0;kind<=13;kind++)
            {
                var attribute=(VertexAttribute)kind;if(!data.HasVertexAttribute(attribute))continue;
                layout[index++]=new VertexAttributeDescriptor(attribute,data.GetVertexAttributeFormat(attribute),data.GetVertexAttributeDimension(attribute),data.GetVertexAttributeStream(attribute));
            }
            metadata.Layout=layout;return layout;
        }
        static RenderPlanAttribute Attribute(Mesh.MeshData mesh,VertexAttribute name,int components=0)
        {
            if(!mesh.HasVertexAttribute(name))return default;
            var format=mesh.GetVertexAttributeFormat(name);
            uint type=format==VertexAttributeFormat.Float32?1u:format==VertexAttributeFormat.UNorm8?2u:format==VertexAttributeFormat.Float16?3u:0;
            if(type==0)throw new NotSupportedException("已知外部程序不支持该顶点格式："+format);
            var dimension=mesh.GetVertexAttributeDimension(name);if(components!=0)dimension=Math.Min(dimension,components);
            return new RenderPlanAttribute {Stream=(uint)mesh.GetVertexAttributeStream(name),Offset=(uint)mesh.GetVertexAttributeOffset(name),Format=type,Dimension=(uint)dimension};
        }
        int Capture(in ExternalGeometryInput input,int components,bool raw)
        {
            using var sample=CaptureMarker.Auto();
            var identity=Identity(input,components,raw);var key=(identity.Geometry,input.GeometryVersion);
            if(captured.TryGetValue(key,out var existing))return existing;
            if(!input.Mesh.isReadable)throw new ArgumentException("外部几何输入必须提供 CPU 可读 MeshData。",nameof(input));
            var lease=Mesh.AcquireReadOnlyMeshData(input.Mesh);var retained=false;
            try
            {
                var mesh=lease[0];var part=mesh.GetSubMesh(input.Submesh);
                if(part.topology!=MeshTopology.Triangles)throw new NotSupportedException("已知外部几何程序使用三角形索引。");
                var geometry=new RenderPlanExternalGeometry {Geometry=identity.Geometry,Version=input.GeometryVersion,VertexCount=(uint)mesh.vertexCount,
                    IndexCount=(uint)part.indexCount,IndexStart=(uint)part.indexStart,BaseVertex=part.baseVertex,StreamCount=(uint)mesh.vertexBufferCount,
                    Position=Attribute(mesh,VertexAttribute.Position,components),Color=raw?default:Attribute(mesh,VertexAttribute.Color),Uv=raw?default:Attribute(mesh,VertexAttribute.TexCoord0)};
                if(geometry.StreamCount>4||geometry.Position.Dimension<2||geometry.Position.Format!=1||
                    geometry.Color.Dimension!=0&&(geometry.Color.Format!=2||geometry.Color.Dimension!=4)||geometry.Uv.Dimension==1||geometry.Uv.Dimension!=0&&(geometry.Uv.Format!=1&&geometry.Uv.Format!=3))
                    throw new NotSupportedException("已知外部程序需要 float 位置、RGBA8 颜色与至少二维 UV0。");
                for(var stream=0;stream<mesh.vertexBufferCount;stream++)
                {
                    var bytes=mesh.GetVertexData<byte>(stream);
                    geometry.SetStream(stream,new RenderPlanVertexStream {Data=(IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytes),Bytes=(ulong)bytes.Length,Stride=(uint)mesh.GetVertexBufferStride(stream)});
                }
                if(mesh.indexFormat==IndexFormat.UInt16)
                {var indices=mesh.GetIndexData<ushort>();geometry.Indices=(IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(indices);geometry.IndexBytes=(ulong)indices.Length*2;geometry.IndexStride=2;}
                else
                {var indices=mesh.GetIndexData<uint>();geometry.Indices=(IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(indices);geometry.IndexBytes=(ulong)indices.Length*4;geometry.IndexStride=4;}
                Grow(ref Geometry,GeometryCount+1);Grow(ref rawGeometry,GeometryCount+1);var index=GeometryCount++;Geometry[index]=geometry;
                rawGeometry[index]=raw?new RawGeometryInput(identity.Gpu,mesh,Layout(input.Mesh,mesh),input.Submesh):default;
                meshes.Add(lease);retained=true;captured.Add(key,index);return index;
            }
            finally{if(!retained)lease.Dispose();}
        }
        void IExternalGeometrySink.RecordFeatheredRectangle(Rect rectangle,Vector4 feather,ulong provider,uint command)
        {
            if(!active||disposed)throw new InvalidOperationException("矩形准备窗口已结束。");
            if(rectangleProgram==null)
            {
                var shader=UnityEngine.Resources.Load<Shader>("URUI/RectangleMask");if(!shader||!shader.isSupported)throw new NotSupportedException("缺少解析矩形遮罩 Shader。");
                rectangleMaterial=new Material(shader){name="URUI rectangle mask source",hideFlags=HideFlags.HideAndDontSave};
                rectangleMesh=new Mesh{name="URUI analytic unit rectangle"};
                rectangleMesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1),Vector3.up};rectangleMesh.triangles=new[]{0,1,2,0,2,3};
                var descriptor=new RenderProgramDescriptor(RenderTextureEncoding.TextureDeclared,RenderColorSpace.Active,RenderColorSpace.Active,
                    RenderAlpha.Premultiplied,RenderBlendDomain.Active,BlendMode.One,BlendMode.OneMinusSrcAlpha,RenderTargetCapability.Hdr,RenderSampling.BilinearClamp,RenderGeometry.Local);
                rectangleProgram=new ExternalGeometryProgram(rectangleMaterial,descriptor);
            }
            var transform=Matrix4x4.TRS(new Vector3(rectangle.x,rectangle.y),Quaternion.identity,new Vector3(rectangle.width,rectangle.height,1));
            ((IExternalGeometrySink)this).RecordExternalGeometry(new ExternalGeometryInput(rectangleMesh,1),rectangleProgram,transform,provider,command);
            ref var draw=ref Draws[DrawCount-1];draw.Flags=4;draw.Parameter.Tint=feather;draw.Parameter.Uv=new Vector4(rectangle.width,rectangle.height,0,0);
        }
        internal RenderPlanExternalGeometry CaptureImageGeometry(in ExternalGeometryInput input)
        {
            if(!active||disposed)throw new InvalidOperationException("图像几何输入窗口已结束。");
            var index=Capture(input,3,false);return Geometry[index];
        }
        void IExternalGeometrySink.RecordExternalGeometry(in ExternalGeometryInput input,ExternalGeometryProgram program,in Matrix4x4 transform,ulong provider,uint command)
        {
            if(!active||disposed)throw new InvalidOperationException("外部几何输入窗口已结束。");
            if(!program.Material)throw new ArgumentException("外部程序材质已失效。",nameof(program));
            if(input.Mesh.vertexCount==0||input.Mesh.GetIndexCount(input.Submesh)==0)return;
            var geometry=Capture(input,program.PositionComponents,!program.Descriptor.IsKnown);
            var binding=Programs.Capture(program);var token=binding.Identity;programs[token]=binding;Grow(ref Draws,DrawCount+1);
            Draws[DrawCount++]=new RenderPlanExternalDraw {Command=command,GeometryIndex=(uint)geometry,Provider=provider,Program=token,Flags=(program.Descriptor.IsKnown?0u:1u)|(binding.Dynamic?2u:0u),
                Parameter=new RenderPlanParameter {Matrix=transform,Tint=Vector4.one,Uv=new Vector4(1,1,0,0)}};
        }
        public RawGeometryInput RawGeometry(uint draw)
        {
            if(!active||draw>=DrawCount||(Draws[draw].Flags&1)==0)throw new InvalidOperationException("原始几何不属于当前消费窗口。");
            return rawGeometry[Draws[draw].GeometryIndex];
        }
        internal ExternalProgramResources.Binding Program(ulong token)=>programs.TryGetValue(token,out var value)?value:throw new InvalidOperationException("外部几何程序不属于当前输入窗口。");
        void End(ulong expected)
        {
            using var profile=EndMarker.Auto();
            if(!active||generation!=expected)return;active=false;
            foreach(var mesh in meshes)mesh.Dispose();meshes.Clear();
            programs.Clear();
        }
        public void Dispose()
        {
            if(disposed)return;End(generation);disposed=true;identities=null;programs.Clear();Programs.Dispose();
            Geometry=Array.Empty<RenderPlanExternalGeometry>();Draws=Array.Empty<RenderPlanExternalDraw>();rawGeometry=Array.Empty<RawGeometryInput>();
            if(rectangleMesh)URUI.Resources.GpuRetirement.DestroyOwnedObject(rectangleMesh);rectangleMesh=null;
            if(rectangleMaterial)URUI.Resources.GpuRetirement.DestroyOwnedObject(rectangleMaterial);rectangleMaterial=null;rectangleProgram=null;
        }
    }
}
