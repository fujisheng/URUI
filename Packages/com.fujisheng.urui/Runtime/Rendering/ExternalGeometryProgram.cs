using System;
using UnityEngine;

namespace URUI.Rendering
{
    /// <summary>
    /// 外部几何程序及渲染约定。材质由调用者持有至注册的 GPU 退役回调。
    /// </summary>
    public sealed class ExternalGeometryProgram
    {
        public Material Material
        {
            get;
        }
        public RenderProgramDescriptor Descriptor
        {
            get;
        }
        public int Pass
        {
            get;
        }
        /// <summary>
        /// 未在 ShaderLab 属性中声明的矩阵、缓冲等资源变化时递增；普通属性同时由宿主冻结。
        /// </summary>
        public ulong ResourceVersion
        {
            get;
        }
        /// <summary>
        /// 已知程序读取的局部位置分量数；二维程序忽略源网格的 Z。
        /// </summary>
        public int PositionComponents
        {
            get;
        }
        /// <summary>
        /// 已知程序使用位置、RGBA8 顶点色、UV0 和统一参数表；未知程序保留独立绘制边界。
        /// </summary>
        public ExternalGeometryProgram(Material material, RenderProgramDescriptor descriptor = default, int pass = 0, int positionComponents = 3, ulong resourceVersion = 1)
        {
            if (resourceVersion == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resourceVersion));
            }
            if (!material)
            {
                throw new ArgumentNullException(nameof(material));
            }
            if (pass < 0 || pass >= material.passCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pass));
            }
            if (positionComponents != 2 && positionComponents != 3)
            {
                throw new ArgumentOutOfRangeException(nameof(positionComponents));
            }
            if (descriptor.IsKnown)
            {
                if (!material.HasProperty("_UruiParameters"))
                {
                    throw new ArgumentException("已知外部程序必须声明 URUI 参数表契约。", nameof(material));
                }
            }
            else if (!material.HasProperty("_UruiPaintOpacity") || !material.HasProperty("_UruiClipEnabled") || !material.HasProperty("_UruiClipRect"))
            {
                throw new ArgumentException("未知外部程序必须声明 URUI 透明度与裁剪契约。", nameof(material));
            }
            Material = material;
            Descriptor = descriptor;
            Pass = pass;
            PositionComponents = positionComponents;
            ResourceVersion = resourceVersion;
        }
    }
}
