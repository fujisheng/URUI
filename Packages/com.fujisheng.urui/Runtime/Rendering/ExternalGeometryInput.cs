using System;
using UnityEngine;

namespace URUI.Rendering
{
    /// <summary>
    /// 一个子网格的只读输入声明；宿主持有 MeshData 消费租约直到当前原生输入窗口结束。
    /// </summary>
    public readonly struct ExternalGeometryInput
    {
        internal Mesh Mesh
        {
            get;
        }
        public ulong GeometryVersion
        {
            get;
        }
        public int Submesh
        {
            get;
        }
        /// <summary>
        /// 几何内容、索引或布局改变时必须递增 version；变换通过绘制参数独立提供。
        /// </summary>
        public ExternalGeometryInput(Mesh mesh, ulong version, int submesh = 0)
        {
            if (!mesh)
            {
                throw new ArgumentNullException(nameof(mesh));
            }
            if (version == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }
            if (submesh < 0 || submesh >= mesh.subMeshCount)
            {
                throw new ArgumentOutOfRangeException(nameof(submesh));
            }
            Mesh = mesh;
            GeometryVersion = version;
            Submesh = submesh;
        }
    }
}
