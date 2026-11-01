using System;
using UnityEngine;

namespace URUI.Internal.Rendering.Meshes
{
    /// <summary>批处理共享的变换与边界判定，保持绘制混合顺序。</summary>
    internal static class BatchRules
    {
        public static bool EquivalentAffine2D(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
            {
                if (float.IsNaN(a[i]) || float.IsInfinity(a[i]) || a[i] != b[i])
                {
                    return false;
                }
            }

            if (a.m02 != 0
                || a.m12 != 0
                || a.m20 != 0
                || a.m21 != 0
                || a.m22 != 1
                || a.m23 != 0
                || a.m30 != 0
                || a.m31 != 0
                || a.m32 != 0
                || a.m33 != 1)
            {
                return false;
            }

            return Math.Abs((double)a.m00 * a.m11 - (double)a.m01 * a.m10) > 1e-12;
        }

        // Touching bounds are kept separate so edge pixels cannot change blending.
        public static bool Disjoint(float ax0, float ay0, float ax1, float ay1, float bx0, float by0, float bx1, float by1) => ax1 < bx0 || bx1 < ax0 || ay1 < by0 || by1 < ay0;
    }
}
