using System;
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanVertexStream
    {
        internal IntPtr Data;
        internal ulong Bytes;
        internal uint Stride,Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanAttribute
    {
        internal uint Stream,Offset,Format,Dimension;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanExternalGeometry
    {
        internal ulong Geometry,Version;
        internal RenderPlanVertexStream Stream0,Stream1,Stream2,Stream3;
        internal IntPtr Indices;
        internal ulong IndexBytes;
        internal uint VertexCount,IndexCount,IndexStart;
        internal int BaseVertex;
        internal uint StreamCount,IndexStride;
        internal RenderPlanAttribute Position,Color,Uv;
        internal void SetStream(int index,RenderPlanVertexStream value)
        {
            switch(index){case 0:Stream0=value;break;case 1:Stream1=value;break;case 2:Stream2=value;break;case 3:Stream3=value;break;default:throw new ArgumentOutOfRangeException(nameof(index));}
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderPlanExternalDraw
    {
        internal uint Command,GeometryIndex;
        internal ulong Provider,Program;
        internal RenderPlanParameter Parameter;
        internal uint Flags,Reserved;
    }
}
