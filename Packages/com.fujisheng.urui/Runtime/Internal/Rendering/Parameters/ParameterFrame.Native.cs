using System;
using System.Runtime.InteropServices;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Parameters
{
    internal sealed partial class ParameterFrame
    {
        [StructLayout(LayoutKind.Sequential)]
        struct CoreKey
        {
            internal ulong geometry;
            internal ulong texture;
            internal int enabled;
            internal int x;
            internal int y;
            internal int w;
            internal int h;
            internal uint mode;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CoreSummary
        {
            internal uint ready;
            internal uint fallback;
            internal ulong bytes;
        }

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_core_frame_api_version();

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_core_frame_copy(
            ref UruiFrameStampNative expected,
            IntPtr keys,
            IntPtr values,
            IntPtr worlds,
            int capacity,
            out CoreSummary summary);

        CoreKey[] coreKeys = Array.Empty<CoreKey>();
        bool coreCapturing;
        bool coreChecked;
        bool coreAvailable;

        internal bool NativeCoreReady
        {
            get;
            private set;
        }

        internal void InvalidateNativeCore()
        {
            NativeCoreReady = false;
#if URUI_DEBUG
            NativeCoreReadyCommands = NativeCoreFallbackCommands = 0;
            NativeCoreBytes = 0;
#endif
        }

        // 核心快照写入当前对象的私有数组。固定句柄只覆盖同步原生调用，所有退出路径都要解锁。
        internal bool CaptureFinalCore()
        {
            if (coreCapturing)
            {
                return false;
            }

            coreCapturing = true;
            InvalidateNativeCore();
            InvalidateOwnedSnapshot();
#if URUI_DEBUG
            snapshotReused = nativePayloadReads = decodedCommands = fastCommands = fallbackCommands = 0;
#endif
            GCHandle kp = default;
            GCHandle vp = default;
            GCHandle wp = default;
            try
            {
                if (!coreChecked)
                {
                    coreChecked = true;
                    coreAvailable = Marshal.SizeOf<CoreKey>() == 40
                        && Marshal.SizeOf<AffineParameter>() == 32
                        && Marshal.SizeOf<Matrix4x4>() == 64
                        && urui_core_frame_api_version() == 1;
                }

                if (!coreAvailable)
                {
                    return false;
                }

                if (NativeMethods.urui_render_plan_stamp(out stamp) != 1 || stamp.flags != 1 || stamp.count > 65536)
                {
                    return false;
                }

                count = (int)stamp.count;
                if (coreKeys.Length < count)
                {
                    coreKeys = new CoreKey[Math.Min(65536, Math.Max(count, coreKeys.Length * 2 + 16))];
                }

                if (worlds.Length < count)
                {
                    worlds = new Matrix4x4[Math.Min(65536, Math.Max(count, worlds.Length * 2 + 16))];
                }

                if (parameters.Length < count)
                {
                    parameters = new AffineParameter[Math.Min(65536, Math.Max(count, parameters.Length * 2 + 16))];
                }

                if (keys.Length < count)
                {
                    keys = new ParameterKey[Math.Min(65536, Math.Max(count, keys.Length * 2 + 16))];
                }

                kp = GCHandle.Alloc(coreKeys, GCHandleType.Pinned);
#if URUI_DEBUG
                corePinsForProbe++;
                afterCorePinForProbe?.Invoke();
#endif
                vp = GCHandle.Alloc(parameters, GCHandleType.Pinned);
#if URUI_DEBUG
                corePinsForProbe++;
#endif
                wp = GCHandle.Alloc(worlds, GCHandleType.Pinned);
#if URUI_DEBUG
                corePinsForProbe++;
                nativePayloadReads = 1;
#endif
                int result = urui_core_frame_copy(ref stamp, kp.AddrOfPinnedObject(), vp.AddrOfPinnedObject(), wp.AddrOfPinnedObject(), coreKeys.Length, out var summary);
                if (result != count || summary.ready + summary.fallback != (uint)count || summary.bytes != (ulong)count * 136)
                {
                    return false;
                }

                for (int i = 0; i < count; i++)
                {
                    var k = coreKeys[i];
                    bool affine = true;
                    if (k.mode == 0)
                    {
                        var m = worlds[i];
                        var p = parameters[i];
                        if (!AffineParameter.TryPrepare(m, p.row0.x, p.row0.y, out worlds[i], out parameters[i], out affine))
                        {
                            return false;
                        }
                    }
                    else if (k.mode != 1)
                    {
                        return false;
                    }

                    keys[i] = new ParameterKey
                    {
                        geometry = k.geometry,
                        texture = k.texture,
                        enabled = k.enabled,
                        x = k.x,
                        y = k.y,
                        w = k.w,
                        h = k.h,
                        affine = affine
                    };
                }
#if URUI_DEBUG
                NativeCoreReadyCommands = (int)summary.ready;
                NativeCoreFallbackCommands = (int)summary.fallback;
                NativeCoreBytes = (long)summary.bytes;
                fastCommands = NativeCoreReadyCommands;
                fallbackCommands = NativeCoreFallbackCommands;
                decodedCommands = count;
#endif
                NativeCoreReady = true;
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                coreAvailable = false;
                return false;
            }
            catch (DllNotFoundException)
            {
                coreAvailable = false;
                return false;
            }
            finally
            {
                if (wp.IsAllocated)
                {
                    wp.Free();
#if URUI_DEBUG
                    corePinsForProbe--;
#endif
                }

                if (vp.IsAllocated)
                {
                    vp.Free();
#if URUI_DEBUG
                    corePinsForProbe--;
#endif
                }

                if (kp.IsAllocated)
                {
                    kp.Free();
#if URUI_DEBUG
                    corePinsForProbe--;
#endif
                }

                coreCapturing = false;
                if (!NativeCoreReady)
                {
                    InvalidateNativeCore();
                }
            }
        }
    }
}
