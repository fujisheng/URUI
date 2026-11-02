using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering.Parameters
{
    // 共享资源的预算、引用计数与销毁规则；计划退出后才能回收其持有的资源。
    internal sealed partial class ParameterTableRenderer
    {
        void AdmitPayload(long bytes)
        {
            if (shareResources && (bytes < 0 || SharedPayloadBytes + bytes > MaxSharedPayloadBytes))
            {
                throw new InvalidOperationException("Shared immutable payload budget");
            }
        }

        void Release(Packet p)
        {
            p.commands.Clear();
            if (p.plan != null)
            {
                p.plan.references--;
                p.plan = null;
            }

            p.state = State.Free;
        }

        void Release(SharedMesh entry)
        {
            if (entry.references <= 0)
            {
                throw new InvalidOperationException("Unbalanced immutable geometry ownership");
            }

            if (--entry.references != 0)
            {
                return;
            }

            if (!sharedMeshes.TryGetValue(entry.key, out var currentEntry) || !ReferenceEquals(currentEntry, entry))
            {
                throw new InvalidOperationException("Immutable geometry identity mismatch");
            }

            sharedMeshes.Remove(entry.key);
#if URUI_DEBUG
            geometryProbe?.Released(entry.key.epoch, entry.key.id);
#endif
            if (entry.positions != null)
            {
                sourceCpuPayload -= (long)entry.vertices * 24 + (long)entry.indices * 4;
            }

            if (entry.gpuCreated)
            {
                sourcePayload -= (long)entry.vertices * 24 + (long)entry.indices * 4;
            }

            if (entry.value)
            {
                UnityEngine.Object.Destroy(entry.value);
            }
        }

        void Release(SharedTexture entry)
        {
            if (entry.references <= 0)
            {
                throw new InvalidOperationException("Unbalanced immutable texture ownership");
            }

            if (--entry.references != 0)
            {
                return;
            }

            if (!sharedTextures.TryGetValue(entry.key, out var currentEntry) || !ReferenceEquals(currentEntry, entry))
            {
                throw new InvalidOperationException("Immutable texture identity mismatch");
            }

            sharedTextures.Remove(entry.key);
            texturePayload -= entry.bytes;
            if (entry.value)
            {
                UnityEngine.Object.Destroy(entry.value);
            }
        }

        void DestroyPlan(Plan plan)
        {
            if (plan.nativeMeshes != null)
            {
                foreach (var entry in plan.nativeMeshes)
                {
                    ReleaseNative(entry);
                }
            }

            if (reuseBatches && plan.sharedBatches != null)
            {
                foreach (var batch in plan.sharedBatches)
                {
                    Release(batch);
                }
            }

            if (shareResources)
            {
                if (plan.sharedMeshes != null)
                {
                    foreach (var resource in plan.sharedMeshes)
                    {
                        Release(resource);
                    }
                }

                if (plan.sharedTextures != null)
                {
                    foreach (var resource in plan.sharedTextures)
                    {
                        Release(resource);
                    }
                }
            }
            else
            {
                foreach (var mesh in plan.source.Values)
                {
                    if (mesh)
                    {
                        UnityEngine.Object.Destroy(mesh);
                    }
                }

                foreach (var tex in plan.textures)
                {
                    if (!plan.borrowedTextureIds.Contains(tex.Key) && tex.Value)
                        UnityEngine.Object.Destroy(tex.Value);
                }
            }

            plan.textures.Clear();
            plan.borrowedTextureIds.Clear();

            foreach (var b in plan.batches)
            {
                if (b.parameter && b.shared == null && b.nativeShared == null && b.mesh)
                {
                    UnityEngine.Object.Destroy(b.mesh);
                }
            }

            if (shareResources)
            {
                combinedPayload -= plan.combinedBytes;
            }
        }

        static void ChargeGeometry(Plan plan, int n, int k)
        {
            long bytes = (long)n * 24 + (long)k * 4;
            if (n <= 0 || k <= 0 || n > 1048576 || k > 3145728 || plan.geometryBytes + bytes > MaxPlanGeometryBytes)
            {
                throw new InvalidOperationException("Parameter plan geometry budget");
            }

            plan.geometryBytes += bytes;
        }

        static void ChargeTexture(Plan plan, int bytes)
        {
            if (bytes <= 0 || plan.textureBytes + bytes > 32L * 1024 * 1024)
            {
                throw new InvalidOperationException("Parameter texture snapshot budget");
            }

            plan.textureBytes += bytes;
        }

        static Mesh CreateSourceMesh(Vector3[] positions, Vector2[] uv, Color32[] colors, int[] topology)
        {
            var value = new Mesh
            {
                name = "URUI parameter immutable source",
                indexFormat = IndexFormat.UInt32
            };
            try
            {
                value.vertices = positions;
                value.uv = uv;
                value.colors32 = colors;
                value.triangles = topology;
                value.RecalculateBounds();
                return value;
            }
            catch
            {
                UnityEngine.Object.Destroy(value);
                throw;
            }
        }
    }
}
