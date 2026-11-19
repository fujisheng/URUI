using System;
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanVertexStream
    {
        internal IntPtr Data;
        internal ulong Bytes;
        internal uint Stride;
        internal uint Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanAttribute
    {
        internal uint Stream;
        internal uint Offset;
        internal uint Format;
        internal uint Dimension;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanExternalGeometry
    {
        internal ulong Geometry;
        internal ulong Version;
        internal RenderPlanVertexStream Stream0;
        internal RenderPlanVertexStream Stream1;
        internal RenderPlanVertexStream Stream2;
        internal RenderPlanVertexStream Stream3;
        internal IntPtr Indices;
        internal ulong IndexBytes;
        internal uint VertexCount;
        internal uint IndexCount;
        internal uint IndexStart;
        internal int BaseVertex;
        internal uint StreamCount;
        internal uint IndexStride;
        internal RenderPlanAttribute Position;
        internal RenderPlanAttribute Color;
        internal RenderPlanAttribute Uv;
        internal void SetStream(int index, RenderPlanVertexStream value)
        {
            switch (index)
            {
                case 0:
                    Stream0 = value;
                    break;
                case 1:
                    Stream1 = value;
                    break;
                case 2:
                    Stream2 = value;
                    break;
                case 3:
                    Stream3 = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanExternalDraw
    {
        internal uint Command;
        internal uint GeometryIndex;
        internal ulong Provider;
        internal ulong Program;
        internal RenderPlanParameter Parameter;
        internal uint Flags;
        internal uint Reserved;
    }
}
