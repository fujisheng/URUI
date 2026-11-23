using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Hosting
{
    // Tracks active CommandBuffer target, viewport, projection, and scissor state so
    // redundant layer selections do not split GPU render passes, CopyTexture/Blit
    // always invalidates the bound target, and scissor never leaks into offscreen passes.
    internal struct CommandRenderState
    {
        CommandBuffer activeBuffer;
        bool targetValid;
        RenderTargetIdentifier currentTarget;
        Rect currentViewport;
        Matrix4x4 currentProjection;
        bool scissorActive;
        Rect currentScissor;

        internal void Reset(CommandBuffer buffer)
        {
            activeBuffer = buffer;
            targetValid = false;
            currentTarget = default;
            currentViewport = default;
            currentProjection = default;
            scissorActive = false;
            currentScissor = default;
        }

        internal void AssumeBound(RenderTargetIdentifier target, Rect viewport, Matrix4x4 projection)
        {
            targetValid = true;
            currentTarget = target;
            currentViewport = viewport;
            currentProjection = projection;
            scissorActive = false;
            currentScissor = default;
        }

        internal void InvalidateTarget()
        {
            targetValid = false;
        }

        internal void DisableScissor()
        {
            if (scissorActive && activeBuffer != null)
            {
                activeBuffer.DisableScissorRect();
            }
            scissorActive = false;
        }

        internal void EnableScissor(Rect rect)
        {
            if (activeBuffer == null)
            {
                return;
            }
            if (!scissorActive || currentScissor != rect)
            {
                activeBuffer.EnableScissorRect(rect);
                scissorActive = true;
                currentScissor = rect;
            }
        }

        internal void Bind(RenderTargetIdentifier target, Rect viewport, Matrix4x4 projection, bool wantScissor, Rect scissor)
        {
            if (activeBuffer == null)
            {
                return;
            }
            if (!targetValid || !currentTarget.Equals(target))
            {
                if (scissorActive)
                {
                    activeBuffer.DisableScissorRect();
                    scissorActive = false;
                }
                activeBuffer.SetRenderTarget(target);
                activeBuffer.SetViewport(viewport);
                activeBuffer.SetViewProjectionMatrices(Matrix4x4.identity, projection);
                targetValid = true;
                currentTarget = target;
                currentViewport = viewport;
                currentProjection = projection;
            }
            else
            {
                if (currentViewport != viewport)
                {
                    activeBuffer.SetViewport(viewport);
                    currentViewport = viewport;
                }
                if (currentProjection != projection)
                {
                    activeBuffer.SetViewProjectionMatrices(Matrix4x4.identity, projection);
                    currentProjection = projection;
                }
            }
            if (wantScissor)
            {
                EnableScissor(scissor);
            }
            else
            {
                DisableScissor();
            }
        }

        internal void BindOffscreen(RenderTargetIdentifier target, Rect viewport, Matrix4x4 projection)
        {
            DisableScissor();
            Bind(target, viewport, projection, false, default);
        }
    }
}
