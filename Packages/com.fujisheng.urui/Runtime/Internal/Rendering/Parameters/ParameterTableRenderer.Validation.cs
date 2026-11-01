using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Parameters
{
    // 验证缓存计划与输入快照、源资源是否仍然匹配；校验失败不能继续复用。
    internal sealed partial class ParameterTableRenderer
    {
        // 可选的直接成员校验仍检查计划所有权；默认使用字典成员校验。
        internal bool DirectSourceValidation
        {
            get;
            set;
        }

        // 验证计划自身持有的资源身份与存活状态，不能仅凭几何 id 存在就接受缓存。
        bool ValidateSources(Plan plan)
        {
#if URUI_DEBUG
            using var sourceValidateMarker = sourceValidateMarkerDefinition.Auto();
#endif
            if (lazySourceMeshes && DirectSourceValidation)
            {
                // Entries were admitted by Source and remain strongly owned by this immutable Plan.
                // Native liveness checks on stamp changes remain in Match, below this helper.
                if (plan == null
                    || !ReferenceEquals(plan.owner, this)
                    || plan.sharedMeshes == null
                    || plan.sharedMeshes.Count != plan.source.Count)
                {
                    return false;
                }

                bool ordered = true;
                int at = 0;
                foreach (var pair in plan.source)
                {
                    var entry = plan.sharedMeshes[at++];
#if URUI_DEBUG
                    if (countSourceValidation)
                    {
                        sourceValidationVisits++;
                    }
#endif
                    if (entry == null
                        || !ReferenceEquals(entry.rendererOwner, this)
                        || entry.references <= 0
                        || entry.key.epoch != plan.epoch
                        || entry.positions == null)
                    {
                        return false;
                    }

                    if (entry.gpuCreated)
                    {
                        if (!entry.value)
                        {
                            return false;
                        }
                    }
                    else if (!ReferenceEquals(entry.value, null))
                    {
                        return false;
                    }

                    if (pair.Key != entry.key.id)
                    {
                        ordered = false;
                        continue;
                    }

                    if (!ReferenceEquals(pair.Value, null) && !ReferenceEquals(pair.Value, entry.value))
                    {
                        return false;
                    }
                }
                // Dictionary enumeration order is not an API promise. Preserve the original
                // membership validation if it differs from the private insertion list.
                return ordered || ValidateSourcesLookup(plan);
            }

            return ValidateSourcesLookup(plan);
        }

        bool ValidateSourcesLookup(Plan plan)
        {
            foreach (var m in plan.source)
            {
#if URUI_DEBUG
                if (countSourceValidation)
                {
                    sourceValidationVisits++;
                }
#endif
                if (m.Value)
                {
                    continue;
                }

                if (!lazySourceMeshes || !ReferenceEquals(m.Value, null))
                {
                    return false;
                }
#if URUI_DEBUG
                if (countSourceValidation)
                {
                    sourceValidationLookups++;
                }
#endif
                if (!sharedMeshes.TryGetValue((plan.epoch, m.Key), out var cpu) || cpu.positions == null || (cpu.gpuCreated && !cpu.value))
                {
                    return false;
                }
            }

            return true;
        }

        bool Match(ParameterFrame frame)
        {
#if URUI_DEBUG
            using var validateMarker = validateMarkerDefinition.Auto();
#endif
            if (current == null
                || current.epoch != frame.stamp.epoch
                || !ParameterKey.SameSequence(current.keys, current.keys.Length, frame.keys, frame.count))
            {
                return false;
            }

            if (!ValidateSources(current))
            {
                return false;
            }

            foreach (var b in current.batches)
            {
                if (!b.mesh)
                {
                    return false;
                }
            }

            foreach (var t in current.textures.Values)
            {
                if (!t)
                {
                    return false;
                }
            }

            if (current.resources != frame.stamp.resources)
            {
                foreach (var key in current.keys)
                {
                    if (key.geometry != 0
                        && (NativeMethods.urui_geometry_exists(key.geometry) == 0
                        || (key.texture != 0
                        && NativeMethods.urui_texture_exists(key.texture) == 0)))
                    {
                        return false;
                    }
                }

                current.resources = frame.stamp.resources;
            }

            return true;
        }
    }
}
