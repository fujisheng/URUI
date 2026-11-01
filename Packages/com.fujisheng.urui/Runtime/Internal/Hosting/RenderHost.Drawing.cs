using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Meshes;
using URUI.Internal.Rendering.Retention;

namespace URUI.Internal.Hosting
{
    // 逐帧录制绘制命令；先处理安全退役和路径选择，再按文档命令顺序提交。
    internal sealed partial class RenderHost
    {
        // A partial recording is never reusable. Preserve this invalidation even when
        // debug instrumentation is compiled out or a resource upload throws.
        void DrawFrame(ref PhaseFrame phase)
        {
            bool completed = false;
            try
            {
                DrawFrameCore(ref phase);
                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    InvalidateRecording(true);
                }
            }
        }

        /// <summary>
        /// 优先尝试满足资源/相机约定的记录路径，其余情况重新读取原生有序命令。
        /// 快路径失败后，只有明确取得回退许可才能重绘，避免一次帧内重复提交。
        /// </summary>
        void DrawFrameCore(ref PhaseFrame phase)
        {
#if URUI_DEBUG
            using var drawScope = drawMarker.Auto();
            ResetParameterRootMetrics();
#endif
            RequireNativeStopPermission();
            shaderPropertyState.Reset();
#if URUI_DEBUG
            PropertyCalls = PropertyWriteCalls = PropertyReuseHits = 0;
            recordCallsThisFrame = 0;
#endif
            recordingWarmupPending = false;
            recordingFinite = true;
#if URUI_DEBUG
            LastRecording = default;
#endif
            if (retentionSchedule.HasValue && retentionSchedule.Value != deferRetainedStateCapture)
            {
                InvalidateRecording(false);
            }

            retentionSchedule = deferRetainedStateCapture;
            bool requested = retainUnchangedCommands && recordingOwnership == RecordingOwnership.HostExclusive;
            RecordingKey recordingKey = default;
            bool eligible = !deferRetainedStateCapture
                && requested
                && RecordingKey.TryCapture(this, material, targetCamera, recordedPassName, out recordingKey);
            bool parameterRequested = ParameterRootEligible();
            if (!parameterRequested)
            {
                StopParameterRoot();
                RequireNativeStopPermission();
            }

            bool tracking = ConfigureNativeRetention(parameterRequested || (deferRetainedStateCapture ? requested : eligible));
            if (!tracking)
            {
                retainedRecording.Invalidate();
                planStability.Reset();
            }

            ShaderContractActive = reuseEquivalentPropertyState && propertyContract && propertyContract.Supports(material);
#if URUI_DEBUG
            if (propertyDependencyDiagnostics)
            {
                propertyDependencies.Reset();
            }
#endif
            if (meshBatchMode.HasValue && meshBatchMode.Value != batchDisjointCommands)
            {
                ClearBatches();
                foreach (var geometry in meshes.Values)
                {
                    if (geometry.mesh)
                    {
                        Destroy(geometry.mesh);
                    }
                }

                meshes.Clear();
            }

            meshBatchMode = batchDisjointCommands;
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
            using (renderMarker.Auto())
#endif
            {
                NativeMethods.urui_render();
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.nativeRenderMs = Millis(Now() - t);
                phase.nativeCalls++;
                t = Now();
            }
#endif
            parameterPrepared = false;
            if (parameterRequested && tracking && PrepareParameterRoot(ref phase))
            {
                return;
            }

            RequireNativeStopPermission();
            UruiFrameStampNative stamp = default;
            if (tracking)
            {
                if (NativeMethods.urui_render_plan_stamp(out stamp) != 1)
                {
                    retainedRecording.Invalidate();
                }
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.nativeCalls++;
                }
#endif
                bool repeated = planStability.Observe(stamp);
                if (deferRetainedStateCapture)
                {
                    eligible = repeated && RecordingKey.TryCapture(this, material, targetCamera, recordedPassName, out recordingKey);
                }
#if URUI_DEBUG
                LastRecording = new RecordingStats
                {
                    capability = eligible && stamp.flags == 1,
                    nativeCommands = (int)stamp.count,
                    nativeExtraBytes = stamp.retained_bytes,
                    compareBytes = stamp.compare_bytes
                };
#endif
                if (eligible && retainedRecording.CanReuse(stamp, recordingKey) && RetainedResourcesAlive())
                {
#if URUI_DEBUG
                    DrawCommands = (int)stamp.count;
                    LastRecording = new RecordingStats
                    {
                        reused = true,
                        capability = true,
                        nativeCommands = DrawCommands,
                        recordCalls = 0,
                        retainedDraws = retainedDraws,
                        nativeExtraBytes = stamp.retained_bytes,
                        compareBytes = stamp.compare_bytes
                    };
#endif
                    return;
                }
            }

            retainedRecording.Invalidate();
            retainedTextures.Clear();
            int count;
            int read;
#if URUI_DEBUG
            using (readMarker.Auto())
#endif
            {
                count = NativeMethods.urui_command_count();
#if URUI_DEBUG
                DrawCommands = count;
#endif
                if (commands.Length < count)
                {
                    commands = new CommandNative[Math.Max(count, commands.Length * 2 + 16)];
                }

                if (commandTransforms.Length < count * 16)
                {
                    commandTransforms = new float[Math.Max(count * 16, commandTransforms.Length * 2 + 256)];
                }

                read = NativeMethods.urui_get_commands(commands, count);
                int transformRead = NativeMethods.urui_get_command_transforms(commandTransforms, commandTransforms.Length);
                if (transformRead != read)
                {
                    throw new Exception("URUI command transform count mismatch: " + transformRead + " / " + read);
                }
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.commandCopyMs = Millis(Now() - t);
                    phase.commandCopyBytes = read * System.Runtime.InteropServices.Marshal.SizeOf(typeof(CommandNative));
                    phase.matrixCopyBytes = read * 16 * sizeof(float);
                    phase.totalCommandCopyBytes = phase.commandCopyBytes + phase.matrixCopyBytes;
                    phase.nativeCalls += 3;
                    phase.commandsRead = read;
                }
#endif
            }
#if URUI_DEBUG
            long unitySetup = phaseDiagnostics ? Now() : 0;
            using (setupMarker.Auto())
#endif
            {
                buffer.Clear();
                // The prior recording is detached before retiring Host-owned Unity textures.
                // Native existence, not this frame's visibility, is the lifetime authority.
                RetireReleasedTextures(ref phase);
                // Construct a screen-space projection; RmlUi has top-left UI coordinates.
                Matrix4x4 projection = Matrix4x4.Ortho(0, UiWidth, UiHeight, 0, -1, 1);
                buffer.SetViewProjectionMatrices(Matrix4x4.identity, projection);
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.commandSetupMs += Millis(Now() - unitySetup);
                }
#endif
            }

            var used = useFrameScratchCollections ? frameUsedGeometry : new HashSet<ulong>();
            used.Clear();
#if URUI_DEBUG
            using (loopMarker.Auto())
#endif
            {
                for (int c = 0; c < read; c++)
                {
                    var cmd = commands[c];
#if URUI_DEBUG
                    long detail = DetailBegin(ref phase.matrixDecodeMs, ref phase);
#endif
                    var transform = new Matrix4x4();
                    bool transformed = false;
                    for (int col = 0; col < 4; col++)
                    {
                        for (int row = 0; row < 4; row++)
                        {
                            float value = commandTransforms[c * 16 + col * 4 + row];
                            transform[row, col] = value;
                            if (float.IsNaN(value) || float.IsInfinity(value))
                            {
                                recordingFinite = false;
                            }

                            if (Mathf.Abs(value - (row == col ? 1f : 0f)) > 0.000001f)
                            {
                                transformed = true;
                            }
                        }
                    }
#if URUI_DEBUG
                    DetailEnd(detail, ref phase.matrixDecodeMs);
#endif
                    if (float.IsNaN(cmd.tx) || float.IsInfinity(cmd.tx) || float.IsNaN(cmd.ty) || float.IsInfinity(cmd.ty))
                    {
                        recordingFinite = false;
                    }

                    if (cmd.geometry == 0)
                    {
                        continue;
                    }
#if URUI_DEBUG
                    detail = DetailBegin(ref phase.geometryLookupMs, ref phase);
#endif
                    bool found = meshes.TryGetValue(cmd.geometry, out var entry);
                    if (found && !entry.mesh)
                    {
                        meshes.Remove(cmd.geometry);
                        found = false;
                    }
#if URUI_DEBUG
                    DetailEnd(detail, ref phase.geometryLookupMs);
#endif
                    if (!found)
                    {
#if URUI_DEBUG
                        using var geometryScope = geometryMarker.Auto();
                        t = phaseDiagnostics ? Now() : 0;
#endif
                        if (NativeMethods.urui_get_geometry_info(cmd.geometry, out int nv, out int ni) == 0)
                        {
                            continue;
                        }

                        if (nv == 0 || ni == 0)
                        {
                            continue;
                        }

                        var vs = new VertexNative[nv];
                        var ids = new int[ni];
                        if (NativeMethods.urui_copy_geometry(cmd.geometry, vs, nv, ids, ni) == 0)
                        {
                            continue;
                        }
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.geometryCopyMs += Millis(Now() - t);
                            phase.nativeCalls += 2;
                            phase.geometryCopyBytes += nv * 20 + ni * 4;
                            t = Now();
                        }

                        long convertStart = phaseDiagnostics ? Now() : 0;
#endif
                        var positions = new Vector3[nv];
                        var uv = new Vector2[nv];
                        var colors = new Color32[nv];
                        for (int v = 0; v < nv; v++)
                        {
                            positions[v] = new Vector3(vs[v].x, vs[v].y, 0);
                            uv[v] = new Vector2(vs[v].u, vs[v].v);
                            colors[v] = new Color32(vs[v].r, vs[v].g, vs[v].b, vs[v].a);
                        }
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.meshConversionMs += Millis(Now() - convertStart);
                        }

                        long unityMeshStart = phaseDiagnostics ? Now() : 0;
#endif
                        var mesh = new Mesh
                        {
                            name = "Geometry"
                        };
                        if (nv > 65535)
                        {
                            mesh.indexFormat = IndexFormat.UInt32;
                        }

                        mesh.vertices = positions;
                        mesh.uv = uv;
                        mesh.colors32 = colors;
                        mesh.triangles = ids;
                        mesh.RecalculateBounds();
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.unityMeshApiMs += Millis(Now() - unityMeshStart);
                            phase.meshBuildMs += Millis(Now() - t);
                            phase.meshesCreated++;
                        }
#endif
                        entry = new MeshCache
                        {
                            mesh = mesh,
                            positions = batchDisjointCommands ? positions : null,
                            uv = batchDisjointCommands ? uv : null,
                            colors = batchDisjointCommands ? colors : null,
                            indices = batchDisjointCommands ? ids : null
                        };
                        meshes.Add(cmd.geometry, entry);
                    }
#if URUI_DEBUG
                    detail = DetailBegin(ref phase.entryTrackMs, ref phase);
#endif
                    entry.lastFrame = Time.frameCount;
                    used.Add(cmd.geometry);
#if URUI_DEBUG
                    DetailEnd(detail, ref phase.entryTrackMs);
#endif
                    if (!transformed && cmd.clip_enabled != 0 && cmd.clip_w >= 0 && cmd.clip_h >= 0)
                    {
                        var bounds = GeometryBounds(entry.mesh, ref phase);
                        float minX = bounds.min.x + cmd.tx;
                        float maxX = bounds.max.x + cmd.tx;
                        float minY = bounds.min.y + cmd.ty;
                        float maxY = bounds.max.y + cmd.ty;
                        bool outside = maxX < cmd.clip_x || minX > cmd.clip_x + cmd.clip_w || maxY < cmd.clip_y || minY > cmd.clip_y + cmd.clip_h;
                        if (outside)
                        {
#if URUI_DEBUG
                            if (phaseDiagnostics)
                            {
                                phase.fullyClippedCandidates++;
                            }
#endif
                            if (cullFullyClipped)
                            {
                                continue;
                            }
                        }
                    }

                    var renderBounds = GeometryBounds(entry.mesh, ref phase);
                    var item = new BatchItem
                    {
                        command = cmd,
                        geometry = entry,
                        transform = transform,
                        transformed = transformed,
                        x0 = renderBounds.min.x + cmd.tx,
                        y0 = renderBounds.min.y + cmd.ty,
                        x1 = renderBounds.max.x + cmd.tx,
                        y1 = renderBounds.max.y + cmd.ty
                    };
                    if (batchDisjointCommands
                        && (!transformed || (batchEquivalentTransforms
                        && BatchRules.EquivalentAffine2D(transform, transform))))
                    {
                        if (!CanJoin(item, ref phase))
                        {
                            FlushGroup(ref phase);
                        }

                        batchGroup.Add(item);
                    }
                    else
                    {
                        if (batchDisjointCommands)
                        {
                            FlushGroup(ref phase);
                        }
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.batchGroups++;
                            if (batchDisjointCommands)
                            {
                                phase.breakTransform++;
                            }
                        }
#endif
                        DrawOne(item, ref phase);
                    }
                }
            }

            if (batchDisjointCommands)
            {
                FlushGroup(ref phase);
#if URUI_DEBUG
                long maintenance = DetailBegin(ref phase.batchMaintenanceMs, ref phase);
#endif
                staleBatchKeys.Clear();
                foreach (var pair in batches)
                {
                    if (!currentBatchKeys.Contains(pair.Key))
                    {
                        staleBatchKeys.Add(pair.Key);
                    }
                }

                foreach (var key in staleBatchKeys)
                {
                    Destroy(batches[key].mesh);
                    batches.Remove(key);
                }

                var swap = previousBatchKeys;
                previousBatchKeys = currentBatchKeys;
                currentBatchKeys = swap;
                currentBatchKeys.Clear();
#if URUI_DEBUG
                DetailEnd(maintenance, ref phase.batchMaintenanceMs);
#endif
            }
#if URUI_DEBUG
            using var cleanupScope = cleanupMarker.Auto();
#endif
            // Avoid leaking meshes invalidated by RmlUi.
#if URUI_DEBUG
            t = phaseDiagnostics ? Now() : 0;
#endif
            var stale = useFrameScratchCollections ? frameStaleGeometry : new List<ulong>();
            stale.Clear();
            foreach (var pair in meshes)
            {
                if (!pair.Value.mesh || (!used.Contains(pair.Key) && NativeMethods.urui_geometry_exists(pair.Key) == 0))
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (var id in stale)
            {
                Destroy(meshes[id].mesh);
                meshes.Remove(id);
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.cleanupMs = Millis(Now() - t);
                phase.nativeCalls += stale.Count;
            }

            retainedDraws = recordCallsThisFrame;
            LastRecording = new RecordingStats
            {
                capability = eligible && tracking && stamp.flags == 1,
                nativeCommands = read,
                recordCalls = recordCallsThisFrame,
                retainedDraws = recordCallsThisFrame,
                nativeExtraBytes = stamp.retained_bytes,
                compareBytes = stamp.compare_bytes
            };
#endif
            if (eligible && tracking && stamp.flags == 1 && !recordingWarmupPending && recordingFinite)
            {
                CaptureTextureDependencies();
                retainedBufferBytes = buffer.sizeInBytes;
                retainedRecording.Commit(stamp, recordingKey);
            }
        }
    }
}
