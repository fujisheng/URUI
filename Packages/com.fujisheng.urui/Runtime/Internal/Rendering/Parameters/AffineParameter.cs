using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace URUI.Internal.Rendering.Parameters
{
    [StructLayout(LayoutKind.Sequential)]
    // 与原生和 Shader 共享的参数布局；非仿射变换必须保留原矩阵供安全路径使用。
    internal struct AffineParameter : IEquatable<AffineParameter>
    {
        public Vector4 row0;
        public Vector4 row1;

        public Vector2 Apply(Vector2 p) => new Vector2(row0.x * p.x + row0.y * p.y + row0.z, row1.x * p.x + row1.y * p.y + row1.z);
        public bool Equals(AffineParameter p) => row0.Equals(p.row0) && row1.Equals(p.row1);
        public static bool TryCreate(Matrix4x4 matrix, float tx, float ty, out AffineParameter value) => TryPrepare(matrix, tx, ty, out _, out value, out bool affine) && affine;

        static bool FastValue(float value)
        {
            uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
            uint a = bits & 0x7fffffff;
            return bits == 0 || (a >= 0x00800000 && a < 0x7f800000);
        }

        internal static bool TryPrepareOptimized(
            Matrix4x4 matrix,
            float tx,
            float ty,
            out Matrix4x4 world,
            out AffineParameter value,
            out bool affine,
            out bool fast)
        {
            fast = false;
            // Fresh value snapshot only: no validation result survives this call.
            if (!(matrix.m02 == 0
                && matrix.m12 == 0
                && matrix.m20 == 0
                && matrix.m21 == 0
                && matrix.m22 == 1
                && matrix.m23 == 0
                && matrix.m30 == 0
                && matrix.m31 == 0
                && matrix.m32 == 0
                && matrix.m33 == 1))
            {
                return TryPrepare(matrix, tx, ty, out world, out value, out affine);
            }

            for (int i = 0; i < 16; i++)
            {
                if (!FastValue(matrix[i]))
                {
                    return TryPrepare(matrix, tx, ty, out world, out value, out affine);
                }
            }

            if (!FastValue(tx) || !FastValue(ty))
            {
                return TryPrepare(matrix, tx, ty, out world, out value, out affine);
            }
            // Use Unity's public dot-product implementation, preserving its operation order.
            Vector4 column = matrix * new Vector4(tx, ty, 0, 1);
            if (!FastValue(column.x) || !FastValue(column.y) || BitConverter.SingleToInt32Bits(column.z) != 0 || column.w != 1)
            {
                return TryPrepare(matrix, tx, ty, out world, out value, out affine);
            }

            world = matrix;
            world.m03 = column.x;
            world.m13 = column.y;
            world.m23 = column.z;
            world.m33 = column.w;
            affine = fast = true;
            value = new AffineParameter
            {
                row0 = new Vector4(world.m00, world.m01, world.m03, 0),
                row1 = new Vector4(world.m10, world.m11, world.m13, 0)
            };
            return true;
        }

        internal static bool TryPrepare(Matrix4x4 matrix, float tx, float ty, out Matrix4x4 world, out AffineParameter value, out bool affine)
        {
            world = default;
            value = default;
            affine = false;
            for (int i = 0; i < 16; i++)
            {
                if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i]))
                {
                    return false;
                }
            }

            if (float.IsNaN(tx) || float.IsInfinity(tx) || float.IsNaN(ty) || float.IsInfinity(ty))
            {
                return false;
            }

            world = matrix * Matrix4x4.Translate(new Vector3(tx, ty, 0));
            for (int i = 0; i < 16; i++)
            {
                if (float.IsNaN(world[i]) || float.IsInfinity(world[i]))
                {
                    return false;
                }
            }

            affine = world.m02 == 0
                && world.m12 == 0
                && world.m20 == 0
                && world.m21 == 0
                && world.m22 == 1
                && world.m23 == 0
                && world.m30 == 0
                && world.m31 == 0
                && world.m32 == 0
                && world.m33 == 1;
            if (affine)
            {
                value = new AffineParameter
                {
                    row0 = new Vector4(world.m00, world.m01, world.m03, 0),
                    row1 = new Vector4(world.m10, world.m11, world.m13, 0)
                };
            }

            return true;
        }
    }
}
