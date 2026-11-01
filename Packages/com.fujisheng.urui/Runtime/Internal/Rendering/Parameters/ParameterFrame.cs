using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Parameters
{
    // 帧快照归当前实例所有；只有内容戳与调用模式一致时才允许复用，不能跨上下文共享。
    internal sealed partial class ParameterFrame
    {
        public int count;
        public UruiFrameStampNative stamp;
        public CommandNative[] commands = Array.Empty<CommandNative>();
        public float[] matrixData = Array.Empty<float>();
        public Matrix4x4[] matrices = Array.Empty<Matrix4x4>();
        public Matrix4x4[] worlds = Array.Empty<Matrix4x4>();
        public AffineParameter[] parameters = Array.Empty<AffineParameter>();
        public ParameterKey[] keys = Array.Empty<ParameterKey>();

        // Optional cache is valid only for the Host's private owned frame arrays.
        // Ordinary Capture never reuses: callers retaining/mutating its public arrays keep old semantics.
        bool snapshotReady;
        bool snapshotOptimized;
        UruiFrameStampNative snapshotStamp;
        CommandNative[] snapshotCommands;
        float[] snapshotMatrixData;
        Matrix4x4[] snapshotMatrices;
        Matrix4x4[] snapshotWorlds;
        AffineParameter[] snapshotParameters;
        ParameterKey[] snapshotKeys;

        internal void InvalidateOwnedSnapshot()
        {
            snapshotReady = false;
            snapshotStamp = default;
            snapshotCommands = null;
            snapshotMatrixData = null;
            snapshotMatrices = null;
            snapshotWorlds = null;
            snapshotParameters = null;
            snapshotKeys = null;
        }

        bool SameOwnedSnapshot(bool optimized) => snapshotOptimized == optimized
            && snapshotStamp.epoch == stamp.epoch
            && snapshotStamp.commands == stamp.commands
            && snapshotStamp.resources == stamp.resources
            && snapshotStamp.count == stamp.count
            && stamp.count == (uint)count
            && ReferenceEquals(snapshotCommands, commands)
            && ReferenceEquals(snapshotMatrixData, matrixData)
            && ReferenceEquals(snapshotMatrices, matrices)
            && ReferenceEquals(snapshotWorlds, worlds)
            && ReferenceEquals(snapshotParameters, parameters)
            && ReferenceEquals(snapshotKeys, keys);

        bool CompletedCapture(bool owned, bool optimized)
        {
#if URUI_DEBUG
            decodedCommands = count;
#endif
            if (owned)
            {
                snapshotStamp = stamp;
                snapshotOptimized = optimized;
                snapshotCommands = commands;
                snapshotMatrixData = matrixData;
                snapshotMatrices = matrices;
                snapshotWorlds = worlds;
                snapshotParameters = parameters;
                snapshotKeys = keys;
                snapshotReady = true;
            }

            return true;
        }

        public bool Capture(bool optimized = false) => CaptureCore(optimized, false);
        internal bool CaptureOwned(bool optimized, bool reuse) => CaptureCore(optimized, reuse);

        bool CaptureCore(bool optimized, bool owned)
        {
            InvalidateNativeCore();
            bool mayReuse = owned && snapshotReady;
            snapshotReady = false;
#if URUI_DEBUG
            snapshotReused = nativePayloadReads = decodedCommands = 0;
#endif
            try
            {
#if URUI_DEBUG
                fastCommands = fallbackCommands = 0;
                using var captureMarker = captureMarkerDefinition.Auto();
                using (nativeReadMarkerDefinition.Auto())
                {
#endif
                    count = NativeMethods.urui_command_count();
                    if (count < 0 || count > 65536 || NativeMethods.urui_render_plan_stamp(out stamp) != 1 || stamp.flags != 1)
                    {
                        return false;
                    }

                    if (mayReuse && SameOwnedSnapshot(optimized))
                    {
                        snapshotReady = true;
#if URUI_DEBUG
                        snapshotReused = 1;
#endif
                        return true;
                    }

                    InvalidateOwnedSnapshot();
                    if (commands.Length < count)
                    {
                        int cap = Math.Max(count, commands.Length * 2 + 16);
                        commands = new CommandNative[cap];
                        matrixData = new float[cap * 16];
                        matrices = new Matrix4x4[cap];
                        worlds = new Matrix4x4[cap];
                        parameters = new AffineParameter[cap];
                        keys = new ParameterKey[cap];
                    }
#if URUI_DEBUG
                    nativePayloadReads++;
#endif
                    if (NativeMethods.urui_get_commands(commands, count) != count)
                    {
                        return false;
                    }
#if URUI_DEBUG
                    nativePayloadReads++;
#endif
                    if (NativeMethods.urui_get_command_transforms(matrixData, matrixData.Length) != count)
                    {
                        return false;
                    }
#if URUI_DEBUG
                }

                using var prepareLoopMarker = prepareLoopMarkerDefinition.Auto();
#endif
                if (optimized)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int o = i * 16;
                        var m = new Matrix4x4(new Vector4(matrixData[o], matrixData[o + 1], matrixData[o + 2], matrixData[o + 3]), new Vector4(matrixData[o + 4], matrixData[o + 5], matrixData[o + 6], matrixData[o + 7]), new Vector4(matrixData[o + 8], matrixData[o + 9], matrixData[o + 10], matrixData[o + 11]), new Vector4(matrixData[o + 12], matrixData[o + 13], matrixData[o + 14], matrixData[o + 15]));
                        matrices[i] = m;
                        if (!AffineParameter.TryPrepareOptimized(m, commands[i].tx, commands[i].ty, out worlds[i], out parameters[i], out bool affine, out bool fast))
                        {
                            return false;
                        }

                        if (fast)
                        {
#if URUI_DEBUG
                            fastCommands++;
#endif
                        }
                        else
                        {
#if URUI_DEBUG
                            fallbackCommands++;
#endif
                        }

                        keys[i] = ParameterKey.From(commands[i], affine);
                    }

                    return CompletedCapture(owned, optimized);
                }

                for (int i = 0; i < count; i++)
                {
                    if (float.IsNaN(commands[i].tx)
                        || float.IsInfinity(commands[i].tx)
                        || float.IsNaN(commands[i].ty)
                        || float.IsInfinity(commands[i].ty))
                    {
                        return false;
                    }

                    var m = new Matrix4x4();
                    for (int c = 0; c < 4; c++)
                    {
                        for (int r = 0; r < 4; r++)
                        {
                            float v = matrixData[i * 16 + c * 4 + r];
                            if (float.IsNaN(v) || float.IsInfinity(v))
                            {
                                return false;
                            }

                            m[r, c] = v;
                        }
                    }

                    matrices[i] = m;
                    if (!AffineParameter.TryPrepare(m, commands[i].tx, commands[i].ty, out worlds[i], out parameters[i], out bool affine))
                    {
                        return false;
                    }

                    keys[i] = ParameterKey.From(commands[i], affine);
                }

                return CompletedCapture(owned, optimized);
            }
            finally
            {
                if (!snapshotReady)
                {
                    InvalidateOwnedSnapshot();
                }
            }
        }
    }
}
