using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace URUI.Internal.Interop
{
    internal static class PlanOperationKind
    {
        internal const uint Geometry = 1;
        internal const uint External = 2;
        internal const uint Image = 3;
        internal const uint Layer = 5;
        internal const uint ExternalGeometry = 6;
        internal const uint ExternalRawGeometry = 7;
        internal const uint ParameterImage = 8;
    }

    internal static class PlanPrepareKind
    {
        internal const uint Ready = 0;
        internal const uint PrepareImage = 1;
        internal const uint ElementLayout = 2;
        internal const uint AcquireCapture = 3;
        internal const uint PrepareElement = 4;
    }

    internal static class PlanTargetKind
    {
        internal const uint Layer = 1;
        internal const uint MaskScratch = 3;
        internal const uint Backdrop = 4;
        internal const uint RootCapture = 6;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanVertex
    {
        internal Vector3 Position;
        internal Color32 Color;
        internal Vector2 Uv;
        internal float Texture;
        // Batch-local parameter offset; shaders add _UruiBaseParameter.
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
        internal uint BaseParameter;
        internal uint Reserved;

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

    // Pointer placed at end so layout matches URUIPlanFeedback identically on 32-bit and 64-bit.
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
        internal uint Capabilities;
        internal uint Reserved;
        internal Vector4 Ink;
        internal Vector4 Expansion;
        internal Vector4 Uv;
        internal IntPtr Transforms;
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
        internal const uint MaskSourceFlag = 1;
        internal const uint HaloFlag = 2;
        internal const uint CaptureFlag = 4;
        internal const uint AliasFlag = 8;
        internal const uint CacheableFlag = 16;
        internal const uint ScalarMaskFlag = 64;
        internal ulong Layer;
        internal Vector4 Bounds;
        internal uint Flags;
        internal uint Reserved;
        internal ulong Version;
        internal ulong AliasLayer;
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
        internal const uint PixelAlignedRoot = 1;
        internal const uint HdrCamera = 4;
        internal const uint ScreenOutput = 16;
        internal const uint ShaderImagePaint = 32;
        internal const uint FramebufferFetch = 64;
        internal const uint ScalarMaskSupported = 8192;
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
        internal static extern int urui_plan_image_geometry(ulong surface, ulong input, ulong request, in RenderPlanExternalGeometry geometry, in Matrix4x4 transform);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_image_program(ulong surface, ulong input, ulong request, ulong program, ulong owner, ulong layout, IntPtr runtime, uint runtimeRows, uint fieldRows, IntPtr boundRows, ulong clock);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_begin(ulong surface, out RenderPlanInputPacket output);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_resolve(ulong surface, ulong input, IntPtr feedbacks, uint feedbackCount, IntPtr resources, out RenderPlanPreparePacket output);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_compile(ulong surface, ulong input, in RenderPlanBindings bindings, out RenderPlanPacket output);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_geometry(ulong surface, ulong plan, ulong geometry, out RenderPlanGeometryView output);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_plan_release(ulong surface, ulong lease);
    }
}
