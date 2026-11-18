using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanVertex
    {
        internal Vector3 Position;
        internal Color32 Color;
        internal Vector2 Uv;
        internal float Texture, Parameter;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanParameter
    {
        internal Matrix4x4 Matrix;
        internal Vector4 Clip, Tint, Uv, Metadata;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RenderPlanOperation
    {
        internal ulong Geometry;
        fixed ulong textures[8];
        internal uint Kind, CommandIndex, Count, TextureCount, LayerIndex, Reason;
        internal Vector4 Bounds;
        internal ulong MaskTexture, Program;
        internal ulong Texture(int slot)
        {
            if ((uint)slot >= TextureCount) { throw new ArgumentOutOfRangeException(nameof(slot)); }
            fixed (ulong* values = textures) { return values[slot]; }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanInputPacket
    {
        internal ulong Lease, Epoch;
        internal IntPtr Commands, Matrices, Paints, Layers;
        internal uint CommandCount, PaintCount, LayerCount, Protocol;
        internal IntPtr Uniforms, CommandLayers;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanPrepareRequest
    {
        internal ulong Request, Document, Element, Program, Geometry, Layer, SourceVersion, Resource;
        internal uint Kind, Slot, PixelWidth, PixelHeight;
        internal Vector4 Capture, Uv, SourceInk;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanFeedback
    {
        internal ulong Request, Resource, SourceVersion;
        internal uint Kind, PixelWidth, PixelHeight, TransformCount;
        internal Vector4 Ink, Expansion, Uv;
        internal IntPtr Transforms;
        internal uint Capabilities,Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanPreparePacket
    {
        internal ulong Lease;
        internal IntPtr Requests;
        internal uint Count, Ready;
        internal IntPtr Regions;
        internal uint RegionCount, TargetFlags;
        internal IntPtr LayerIndices;
        internal uint LayerIndexCount, LayerReserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanLayerRegion
    {
        internal ulong Layer;
        internal Vector4 Bounds;
        internal uint Flags, Reserved;
        internal ulong Version;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanTextureBinding
    {
        internal ulong Texture, Version;
        internal uint Domain, Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanBindings
    {
        internal IntPtr Textures;
        internal uint TextureCount, FrameFlags;
        internal IntPtr Externals;
        internal uint ExternalCount, ExternalReserved;
        internal IntPtr LayerBindings;
        internal uint LayerBindingCount, LayerReserved;
        internal IntPtr ExternalGeometry;
        internal uint ExternalGeometryCount, GeometryReserved;
        internal IntPtr ExternalDraws;
        internal uint ExternalDrawCount, DrawReserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanLayerBinding
    {
        internal ulong Layer, Version;
        internal uint Flags, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanExternalBinding
    {
        internal uint Command, Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanTargetRequest
    {
        internal ulong Layer,RelatedLayer;
        internal int Slot;
        internal uint Kind,FirstOperation,LastOperation;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanPacket
    {
        internal ulong Lease, Epoch, Version, ParameterVersion;
        internal IntPtr Operations, Parameters;
        internal uint OperationCount, ParameterCount, Protocol, ExtraParameterCount;
        internal ulong GeometryBytes, FrameBytes, AllocationCount, FreeCount, AllocatedBytes;
        internal IntPtr ParameterPages;
        internal uint ParameterPageCount, PageReserved;
        internal IntPtr ExtraParameters;
        internal IntPtr Targets;
        internal uint TargetCount,TargetReserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanGeometryView
    {
        internal IntPtr Vertices, Indices;
        internal uint VertexCount, IndexCount;
        internal ulong Version;
    }
    internal static partial class NativeMethods
    {
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int urui_plan_image_geometry(ulong input,ulong request,in RenderPlanExternalGeometry geometry,in Matrix4x4 transform);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int urui_plan_image_program(ulong input,ulong request,ulong program,ulong owner,ulong layout,IntPtr runtime,uint runtimeRows,uint fieldRows,IntPtr boundRows,ulong clock);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_begin(out RenderPlanInputPacket output);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_resolve(ulong input, IntPtr feedback, IntPtr resources, out RenderPlanPreparePacket output);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_compile(ulong input, in RenderPlanBindings bindings, out RenderPlanPacket output);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_geometry(ulong plan, ulong geometry, out RenderPlanGeometryView output);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_release(ulong lease);
    }
}
