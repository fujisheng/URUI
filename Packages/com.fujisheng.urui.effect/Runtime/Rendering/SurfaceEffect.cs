using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Rendering.Effects;
using URUI.Resources;
namespace URUI.Effect.Rendering
{
    /// <summary>
    /// 一个明确绑定的源/表面处理任务；布局和帧版本来自调用者，不扫描其他节点。
    /// </summary>
    public sealed class SurfaceEffectInput
    {
        public TextureEffectSurface Surface
        {
            get;
        }
        public Texture Source
        {
            get;
        }
        public Material Material
        {
            get;
        }
        public Func<ulong> Version
        {
            get;
        }
        public Func<bool> Ready
        {
            get;
        }
        public Action<EffectFrame> Prepare
        {
            get;
        }
        public bool UsesTime
        {
            get;
        }
        public SurfaceEffectInput(TextureEffectSurface surface, Texture source, Material material, Func<ulong> version, Func<bool> ready = null, Action<EffectFrame> prepare = null, bool usesTime = false)
        {
            Surface = surface ?? throw new ArgumentNullException(nameof(surface));
            Source = source ? source : throw new ArgumentNullException(nameof(source));
            Material = material ? material : throw new ArgumentNullException(nameof(material));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Ready = ready ?? (() => true);
            Prepare = prepare;
            UsesTime = usesTime;
        }
    }
    internal sealed class SurfaceEffect : IElementEffect
    {
        static readonly int effectTimeId = Shader.PropertyToID("_EffectTime");
        readonly SurfaceEffectInput input;
        readonly EffectScheduler scheduler = new();
        readonly MaterialPropertyBlock properties = new();
        readonly SurfaceMaterialState materialState = new();
        readonly EffectContext owner;
        readonly SurfaceMaterialPool materials;
        readonly string programKey;
        SurfaceMaterialPool.Lease material;
        EffectScheduler.Update update;
        EffectScheduler.Update recordedUpdate;
        readonly Action submitted;
        bool needsUpdate;
        internal SurfaceEffect(SurfaceEffectInput input, EffectContext context)
        {
            this.input = input;
            owner = context;
            context.Own(input.Surface.Output.Acquire());
            materials = context.Own(new SurfaceMaterialPool());
            programKey = "surface:" + input.Material.GetInstanceID();
            submitted = Submitted;
        }
        public void Prepare(EffectFrame frame)
        {
            needsUpdate = false;
            if (!input.Ready())
            {
                return;
            }
            input.Prepare?.Invoke(frame);
            input.Surface.Prepare(frame.Size);
            var changed = materialState.Capture(input.Material, input.UsesTime, frame.Time);
            if (changed)
            {
                var next = owner.Own(materials.Rent(input.Material, materialState.KeywordHandles, materialState.KeywordStates));
                if (material != null)
                {
                    owner.Retire(material);
                }
                material = next;
            }
            needsUpdate = scheduler.TryPrepare(new EffectInput(input.Source, input.Version(), frame.Size, materialState.Version, programKey, input.Surface.Texture.format), out update);
            if (!needsUpdate)
            {
                return;
            }
            properties.Clear();
            properties.SetFloat(effectTimeId, (float)frame.Time);
        }
        public void Record(EffectRenderContext context)
        {
            if (needsUpdate)
            {
                recordedUpdate = update;
                input.Surface.Record(context, input.Source, material.Material, properties);
                context.AfterSubmission(submitted);
            }
            context.DrawOriginal();
        }
        // 宿主只通知当前命令记录；SRP 在 ExecuteCommandBuffer 后同步通知，不延迟到 GPU fence。
        // Prepare 尚未进入 Record 时不能覆盖已录制版本。
        void Submitted()
        {
            recordedUpdate.Submitted();
        }
        public void Dispose()
        {
            scheduler.Invalidate();
            needsUpdate = false;
            update = default;
        }
    }
}
