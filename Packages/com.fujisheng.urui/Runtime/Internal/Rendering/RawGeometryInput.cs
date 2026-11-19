using UnityEngine;
using UnityEngine.Rendering;
namespace URUI.Internal.Rendering
{
    internal readonly struct RawGeometryInput
    {
        internal readonly ulong Identity;
        internal readonly Mesh.MeshData Data;
        internal readonly VertexAttributeDescriptor[] Layout;
        internal readonly int Submesh;
        internal RawGeometryInput(ulong identity, Mesh.MeshData data, VertexAttributeDescriptor[] layout, int submesh)
        {
            Identity = identity;
            Data = data;
            Layout = layout;
            Submesh = submesh;
        }
    }
    internal interface IRawGeometryBindings
    {
        RawGeometryInput RawGeometry(uint draw);
    }
}
