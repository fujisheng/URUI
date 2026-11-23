using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering
{
    /// <summary>
    /// Unity 对象与用户回调适配；顺序、捕获范围和依赖由原生请求决定。
    /// 支持 C ABI v13 批量请求队列，同一阶段内所有独立请求在单次原生往返内全部回传。
    /// </summary>
    internal interface IRenderPlanPreparationBindings
    {
        // 每批独立请求开始前调用，允许适配层按本批 ElementLayout 数量预留非重叠变换缓冲。
        void BeginFeedbackBatch(int elementLayoutCount);
        // Transforms 若非空，必须保持有效直到下一次原生 resolve 已消费整批反馈。
        RenderPlanFeedback Prepare(in RenderPlanPrepareRequest request);
        RenderTexture ResolveCapture(ulong resource);
    }

    internal static unsafe class RenderPlanPreparation
    {
        [ThreadStatic]
        static RenderPlanFeedback[] feedbackScratch;

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
            var feedbacks = feedbackScratch ??= new RenderPlanFeedback[16];
            uint feedbackCount = 0;
            for (var step = 0; step <= maximum; step++)
            {
                RenderPlanPreparePacket packet;
                fixed (RenderPlanFeedback* fbPtr = feedbacks)
                {
                    var pointer = feedbackCount > 0 ? (IntPtr)fbPtr : IntPtr.Zero;
                    NativeRenderPlanLease.Require(NativeMethods.urui_plan_resolve(
                        lease.Surface, input.Lease, pointer, feedbackCount, (IntPtr)(&resources), out packet));
                }
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
                if (packet.Count == 0 || packet.Requests == IntPtr.Zero || bindings == null)
                {
                    throw new InvalidOperationException("准备队列未前进或缺少资源适配。");
                }
                int batchCount = checked((int)packet.Count);
                if (feedbacks.Length < batchCount)
                {
                    int next = feedbacks.Length;
                    while (next < batchCount)
                    {
                        next *= 2;
                    }
                    feedbacks = feedbackScratch = new RenderPlanFeedback[next];
                }
                var requests = (RenderPlanPrepareRequest*)packet.Requests;
                var elementLayoutCount = 0;
                for (int i = 0; i < batchCount; i++)
                {
                    if (requests[i].Kind == PlanPrepareKind.ElementLayout)
                    {
                        elementLayoutCount++;
                    }
                }
                bindings.BeginFeedbackBatch(elementLayoutCount);
                for (int i = 0; i < batchCount; i++)
                {
                    var request = requests[i];
                    if (request.Request == 0 || request.Kind < PlanPrepareKind.PrepareImage || request.Kind > PlanPrepareKind.PrepareElement)
                    {
                        throw new InvalidOperationException("准备队列包含无效或未知请求。");
                    }
                    var fb = bindings.Prepare(request);
                    fb.Request = request.Request;
                    fb.Kind = request.Kind;
                    if (request.Kind == PlanPrepareKind.AcquireCapture)
                    {
                        var source = bindings.ResolveCapture(fb.Resource);
                        if (!source || !source.IsCreated() || source.width != fb.PixelWidth || source.height != fb.PixelHeight)
                        {
                            throw new InvalidOperationException("效果捕获必须绑定已创建的真实 RenderTexture 与实际像素尺寸。");
                        }
                    }
                    feedbacks[i] = fb;
                }
                feedbackCount = packet.Count;
            }
            throw new InvalidOperationException("准备队列超出每个效果槽的有界请求数量。");
        }
    }
}
