using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Parameters
{
    internal struct ParameterKey : IEquatable<ParameterKey>
    {
        public ulong geometry;
        public ulong texture;
        public int enabled;
        public int x;
        public int y;
        public int w;
        public int h;
        public bool affine;

        public static ParameterKey From(CommandNative c, bool affine) => new ParameterKey
        {
            geometry = c.geometry,
            texture = c.texture,
            enabled = c.clip_enabled,
            x = c.clip_x,
            y = c.clip_y,
            w = c.clip_w,
            h = c.clip_h,
            affine = affine
        };

        public Vector4 ClipVector => new Vector4(x, y, unchecked(x + w), unchecked(y + h));

        public bool Equals(ParameterKey b) => geometry == b.geometry
            && texture == b.texture
            && enabled == b.enabled
            && x == b.x
            && y == b.y
            && w == b.w
            && h == b.h
            && affine == b.affine;
        public static bool CanJoin(ParameterKey a, ParameterKey b) => a.affine
            && b.affine
            && a.texture == b.texture
            && a.enabled == b.enabled
            && a.x == b.x
            && a.y == b.y
            && a.w == b.w
            && a.h == b.h;

        public static bool SameSequence(ParameterKey[] a, int an, ParameterKey[] b, int bn)
        {
            if (an != bn || an < 0 || a == null || b == null || a.Length < an || b.Length < bn)
            {
                return false;
            }

            for (int i = 0; i < an; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
