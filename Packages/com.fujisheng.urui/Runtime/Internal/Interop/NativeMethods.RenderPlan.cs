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
        internal float Texture;
        internal float Parameter;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanParameter
    {
        internal Matrix4x4 Matrix;
        internal Vector4 Clip;
        internal Vector4 Tint;
        internal Vector4 Uv;
        internal Vector4 Metadata;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RenderPlanOperation
    {
        internal ulong Geometry;
        fixed ulong textures[8];
        internal uint Kind;
        internal uint CommandIndex;
        internal uint Count;
        internal uint TextureCount;
        internal uint LayerIndex;
        internal uint Reason;
        internal Vector4 Bounds;
        internal ulong MaskTexture;
        internal ulong Program;
        internal ulong Texture(int slot)
        {
            if ((uint)slot >= TextureCount)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
            fixed (ulong* values = textures)
            {
                return values[slot];
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanInputPacket
    {
        internal ulong Lease;
        internal ulong Epoch;
        internal IntPtr Commands;
        internal IntPtr Matrices;
        internal IntPtr Paints;
        internal IntPtr Layers;
        internal uint CommandCount;
        internal uint PaintCount;
        internal uint LayerCount;
        internal uint Protocol;
        internal IntPtr Uniforms;
        internal IntPtr CommandLayers;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanPrepareRequest
    {
        internal ulong Request;
        internal ulong Document;
        internal ulong Element;
        internal ulong Program;
        internal ulong Geometry;
        internal ulong Layer;
        internal ulong SourceVersion;
        internal ulong Resource;
        internal uint Kind;
        internal uint Slot;
        internal uint PixelWidth;
        internal uint PixelHeight;
        internal Vector4 Capture;
        internal Vector4 Uv;
        internal Vector4 SourceInk;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanFeedback
    {
        internal ulong Request;
        internal ulong Resource;
        internal ulong SourceVersion;
        internal uint Kind;
        internal uint PixelWidth;
        internal uint PixelHeight;
        internal uint TransformCount;
        internal Vector4 Ink;
        internal Vector4 Expansion;
        internal Vector4 Uv;
        internal IntPtr Transforms;
        internal uint Capabilities;
        internal uint Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanPreparePacket
    {
        internal ulong Lease;
        internal IntPtr Requests;
        internal uint Count;
        internal uint Ready;
        internal IntPtr Regions;
        internal uint RegionCount;
        internal uint TargetFlags;
        internal IntPtr LayerIndices;
        internal uint LayerIndexCount;
        internal uint LayerReserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanLayerRegion
    {
        internal ulong Layer;
        internal Vector4 Bounds;
        internal uint Flags;
        internal uint Reserved;
        internal ulong Version;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanTextureBinding
    {
        internal ulong Texture;
        internal ulong Version;
        internal uint Domain;
        internal uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanBindings
    {
        internal IntPtr Textures;
        internal uint TextureCount;
        internal uint FrameFlags;
        internal IntPtr Externals;
        internal uint ExternalCount;
        internal uint ExternalReserved;
        internal IntPtr LayerBindings;
        internal uint LayerBindingCount;
        internal uint LayerReserved;
        internal IntPtr ExternalGeometry;
        internal uint ExternalGeometryCount;
        internal uint GeometryReserved;
        internal IntPtr ExternalDraws;
        internal uint ExternalDrawCount;
        internal uint DrawReserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanLayerBinding
    {
        internal ulong Layer;
        internal ulong Version;
        internal uint Flags;
        internal uint Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanExternalBinding
    {
        internal uint Command;
        internal uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanTargetRequest
    {
        internal ulong Layer;
        internal ulong RelatedLayer;
        internal int Slot;
        internal uint Kind;
        internal uint FirstOperation;
        internal uint LastOperation;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanPacket
    {
        internal ulong Lease;
        internal ulong Epoch;
        internal ulong Version;
        internal ulong ParameterVersion;
        internal IntPtr Operations;
        internal IntPtr Parameters;
        internal uint OperationCount;
        internal uint ParameterCount;
        internal uint Protocol;
        internal uint ExtraParameterCount;
        internal ulong GeometryBytes;
        internal ulong FrameBytes;
        internal ulong AllocationCount;
        internal ulong FreeCount;
        internal ulong AllocatedBytes;
        internal IntPtr ParameterPages;
        internal uint ParameterPageCount;
        internal uint PageReserved;
        internal IntPtr ExtraParameters;
        internal IntPtr Targets;
        internal uint TargetCount;
        internal uint TargetReserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanGeometryView
    {
        internal IntPtr Vertices;
        internal IntPtr Indices;
        internal uint VertexCount;
        internal uint IndexCount;
        internal ulong Version;
    }
    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_image_geometry(ulong input, ulong request, in RenderPlanExternalGeometry geometry, in Matrix4x4 transform);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_image_program(ulong input, ulong request, ulong program, ulong owner, ulong layout, IntPtr runtime, uint runtimeRows, uint fieldRows, IntPtr boundRows, ulong clock);
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
