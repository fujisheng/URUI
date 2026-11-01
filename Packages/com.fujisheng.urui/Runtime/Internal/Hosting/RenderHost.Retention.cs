using System;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Retention;

namespace URUI.Internal.Hosting
{
    // 只有版本、资源与独占所有权仍满足约束时才复用绘制记录。
    internal sealed partial class RenderHost
    {
        // Retained commands depend on the exact meshes, textures and camera state.
        // Clearing revokes parameter recordings before dropping those dependencies.
        void InvalidateRecording(bool clear)
        {
            if (clear)
            {
                parameterPrepared = false;
                StopParameterRoot();
            }

            retainedRecording.Invalidate();
            planStability.Reset();
            retainedTextures.Clear();
#if URUI_DEBUG
            retainedDraws = 0;
#endif
            if (clear)
            {
                retainedTextures.TrimExcess();
#if URUI_DEBUG
                LastRecording = default;
#endif
                TryClearNativeHostRecording();
            }
        }

        // Explicit notification for consumers managing external render state. Existing
        // current-frame Unity snapshots stay alive; next Host Update records anew.
        public void NotifyExternalRenderResourcesChanged()
        {
            InvalidateRecording(false);
        }

        // Camera.GetCommandBuffers exposes mutable references. Exclusive ownership
        // cannot be inferred from private fields or byte size; unknown is fallback.
        public void SetRecordingOwnership(RecordingOwnership ownership)
        {
            if (ownership != RecordingOwnership.ExternalMutable && ownership != RecordingOwnership.HostExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(ownership));
            }

            if (recordingOwnershipValue == ownership)
            {
                return;
            }

            recordingOwnershipValue = ownership;
            InvalidateRecording(false);
        }

        // This validation is production behavior: stale GPU resources must never be
        // accepted merely because the native command sequence has not changed.
        bool RetainedResourcesAlive()
        {
#if URUI_DEBUG
            using (retainedResourceMarker.Auto())
#endif
            {
                return CheckRetainedResources();
            }
        }

        bool CheckRetainedResources()
        {
            if (buffer == null || buffer.sizeInBytes != retainedBufferBytes)
            {
                return false;
            }

            foreach (var pair in meshes)
            {
                if (!pair.Value.mesh)
                {
                    return false;
                }
            }

            foreach (var pair in batches)
            {
                if (!pair.Value.mesh)
                {
                    return false;
                }
            }

            foreach (var d in retainedTextures)
            {
                var t = d.texture;
                if (!t
                    || t.updateCount != d.update
                    || t.width != d.width
                    || t.height != d.height
                    || t.filterMode != d.filter
                    || t.wrapModeU != d.u
                    || t.wrapModeV != d.v
                    || t.wrapModeW != d.w
                    || t.mipMapBias != d.bias
                    || t.anisoLevel != d.aniso)
                {
                    return false;
                }
            }

            return true;
        }

        void AddTextureDependency(Texture texture)
        {
            if (!texture)
            {
                return;
            }

            retainedTextures.Add(new TextureDependency { texture = texture, update = texture.updateCount, width = texture.width, height = texture.height, filter = texture.filterMode, u = texture.wrapModeU, v = texture.wrapModeV, w = texture.wrapModeW, bias = texture.mipMapBias, aniso = texture.anisoLevel });
        }

        void CaptureTextureDependencies()
        {
#if URUI_DEBUG
#if URUI_DEBUG
            using var dependencyScope = dependencyCaptureMarker.Auto();
#endif
#endif
            retainedTextures.Clear();
            AddTextureDependency(white);
            foreach (var pair in textures)
            {
                AddTextureDependency(pair.Value);
            }
        }

        bool ConfigureNativeRetention(bool enable)
        {
            if (retentionApiMissing)
            {
                return false;
            }

            if (enable == nativeRetention)
            {
                return enable;
            }

            try
            {
                if (enable && NativeMethods.urui_render_plan_version() != 1)
                {
                    retentionApiMissing = true;
                    return false;
                }

                if (NativeMethods.urui_render_plan_enable(enable ? 1 : 0) != 1)
                {
                    return false;
                }

                nativeRetention = enable;
                InvalidateRecording(false);
                return enable;
            }
            catch (EntryPointNotFoundException)
            {
                retentionApiMissing = true;
                nativeRetention = false;
                InvalidateRecording(false);
                return false;
            }
        }
    }
}
