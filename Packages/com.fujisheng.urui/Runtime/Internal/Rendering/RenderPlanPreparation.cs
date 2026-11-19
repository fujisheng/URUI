using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering
{
    /// <summary>
    /// Unity 对象与用户回调适配；顺序、捕获范围和依赖由原生请求决定。
    /// </summary>
    internal interface IRenderPlanPreparationBindings
    {
        // Transforms 若非空，必须保持有效直到下一次原生 resolve 已消费反馈。
        RenderPlanFeedback Prepare(in RenderPlanPrepareRequest request);
        RenderTexture ResolveCapture(ulong resource);
    }
    internal static unsafe class RenderPlanPreparation
    {
        internal static int Resolve(NativeRenderPlanLease lease, in RenderPlanInputPacket input, IRenderPlanPreparationBindings bindings)
            => Resolve(lease, input, bindings, out _);

        internal static int Resolve(NativeRenderPlanLease lease, in RenderPlanInputPacket input, IRenderPlanPreparationBindings bindings, out RenderPlanPreparePacket ready, RenderPlanBindings resources = default)
        {
            ready = default;
            if (lease.Token != input.Lease || input.Protocol != NativeRenderPlanLease.ProtocolVersion)
            {
                throw new InvalidOperationException("准备输入的租约世代无效。");
            }
            var maximum = checked(((long)input.CommandCount + input.LayerCount) * 4 + 1);
            RenderPlanFeedback feedback = default;
            var haveFeedback = false;
            ulong previous = 0;
            for (var step = 0; step <= maximum; step++)
            {
                var pointer = haveFeedback ? (IntPtr)(&feedback) : IntPtr.Zero;
                NativeRenderPlanLease.Require(NativeMethods.urui_plan_resolve(input.Lease, pointer, (IntPtr)(&resources), out var packet));
                if (packet.Lease != input.Lease)
                {
                    throw new InvalidOperationException("准备队列的租约世代变化。");
                }
                if (packet.Ready == 1)
                {
                    if (packet.Count != 0)
                    {
                        throw new InvalidOperationException("已完成的准备队列仍含请求。");
                    }
                    ready = packet;
                    return step;
                }
                if (packet.Count != 1 || packet.Requests == IntPtr.Zero || bindings == null)
                {
                    throw new InvalidOperationException("准备队列未前进或缺少资源适配。");
                }
                var request = *(RenderPlanPrepareRequest*)packet.Requests;
                if (request.Request == 0 || request.Request == previous || request.Kind < 1 || request.Kind > 4)
                {
                    throw new InvalidOperationException("准备队列重复或包含未知请求。");
                }
                previous = request.Request;
                feedback = bindings.Prepare(request);
                feedback.Request = request.Request;
                feedback.Kind = request.Kind;
                if (request.Kind == 3)
                {
                    var source = bindings.ResolveCapture(feedback.Resource);
                    if (!source || !source.IsCreated() || source.width != feedback.PixelWidth || source.height != feedback.PixelHeight)
                    {
                        throw new InvalidOperationException("效果捕获必须绑定已创建的真实 RenderTexture 与实际像素尺寸。");
                    }
                }
                haveFeedback = true;
            }
            throw new InvalidOperationException("准备队列超出每个效果槽的有界请求数量。");
        }
    }
}
