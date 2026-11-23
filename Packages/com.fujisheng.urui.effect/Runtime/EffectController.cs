using System;
using System.Collections.Generic;
using System.Threading;
using URUI.Animation;
using URUI.Elements;
using URUI.Styles;
using URUI.Rendering.Effects;
using URUI.Effect.Rendering;
namespace URUI.Effect
{
    public enum EffectApplyMode
    {
        Replace,
        MergeActiveGroups
    }
    public enum TransitionInputPolicy
    {
        Standard,
        DisableWhenComplete
    }
    /// <summary>
    /// 作者节点的预设、独立播放与快照联动；不拥有 DOM 或每节点 MonoBehaviour。
    /// </summary>
    public sealed class EffectController : IDisposable, IEffectParameterBinding
    {
        readonly EffectModule module;
        readonly IDisposable binding;
        EffectParameters parameters = new();
        EffectParameters lastSnapshot;
        Dictionary<string, StyleOverride> presetValues;
        Dictionary<string, StyleOverride> linkedValues;
        Dictionary<string, StyleOverride> playbackValues;
        Dictionary<string, StyleOverride> inspectionValues;
        EffectPlayback playback;
        EffectLink link;
        EffectLayout rawLayout;
        EffectLayout resolvedLayout;
        EffectParameters resolvedParameters;
        EffectParameters computedParameters;
        EffectParameters computedBasis;
        EffectParameters lastSnapshotSource;
        ComputedStyleSnapshot computedAuthor;
        long revision;
        long computedRevision = -1;
        long valueVersion;
        long computedSourceVersion;
        (bool capture, bool input)? runtimeState;
        double previousTime;
        IUIClock previousClock;
        bool disposed;
        bool available;
        EffectParameters inspectionParameters;
        readonly List<PresetOverride> inspectionOverrides = new();
        sealed class PresetOverride : IDisposable
        {
            internal EffectParameters Parameters;
            Action release;
            internal PresetOverride(Action release)
            {
                this.release = release;
            }
            public void Dispose()
            {
                var previous = release;
                release = null;
                previous?.Invoke();
            }
        }
        /// <summary>
        /// 临时覆盖预设基础参数，保留原联动、播放与 CSS 级联；释放后恢复。
        /// </summary>
        public IDisposable OverridePreset(EffectPreset preset)
        {
            RequireValid();
            if (!preset)
            {
                throw new ArgumentNullException(nameof(preset));
            }
            PresetOverride lease = null;
            lease = new PresetOverride(() =>
            {
                inspectionOverrides.Remove(lease);
                inspectionParameters = inspectionOverrides.Count == 0 ? null : inspectionOverrides[inspectionOverrides.Count - 1].Parameters;
                if (!disposed)
                {
                    Invalidate();
                }
            })
            {
                Parameters = preset.CreateParameters()
            };
            inspectionOverrides.Add(lease);
            inspectionParameters = lease.Parameters;
            Invalidate();
            return lease;
        }
        public Element Element
        {
            get;
        }
        public TransitionInputPolicy InputPolicy
        {
            get;
            set;
        }
        internal bool Alive => !disposed && Element.IsValid && Element.IsAttached;
        internal EffectController(EffectModule module, Element element)
        {
            this.module = module;
            Element = element;
            available = true;
            binding = element.Document.Effects.BindProgramParameters(element, this);
        }
        void RequireValid()
        {
            if (!Alive)
            {
                throw new ObjectDisposedException(nameof(EffectController));
            }
        }
        public void Apply(EffectPreset preset, EffectApplyMode mode = EffectApplyMode.Replace)
        {
            RequireValid();
            if (!preset)
            {
                throw new ArgumentNullException(nameof(preset));
            }
            if (!Enum.IsDefined(typeof(EffectApplyMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            var next = preset.CreateParameters();
            next = mode == EffectApplyMode.Replace ? next : EffectParameterMerge.Merge(parameters, next);
            var values = EffectParameterStyle.Write(next);
            Unlink();
            parameters = next;
            presetValues = values;
            Invalidate();
        }
        public EffectParameters CreateParameters()
        {
            RequireValid();
            return (resolvedParameters ?? Compute(Element.Document.Styles.GetComputed(Element))).Clone();
        }
        public void SaveTo(EffectPreset preset)
        {
            if (!preset)
            {
                throw new ArgumentNullException(nameof(preset));
            }
            preset.SetParameters(CreateParameters());
        }
        public EffectPlayback Play(EffectTweenDefinition definition, CancellationToken cancellationToken = default)
        {
            RequireValid();
            var next = new EffectPlayback(definition, Invalidate, cancellationToken);
            playback?.Cancel();
            playback = next;
            previousClock = null;
            Invalidate();
            return playback;
        }
        public IDisposable Link(EffectController source, bool useTargetRoot = true)
        {
            RequireValid();
            if (source == null || !source.Alive)
            {
                throw new ArgumentException("联动来源必须有效。", nameof(source));
            }
            if (useTargetRoot && !ReferenceEquals(Element.Document, source.Element.Document))
            {
                throw new ArgumentException("根坐标不能跨文档联动。", nameof(useTargetRoot));
            }
            for (var current = source; current != null; current = current.link?.Source)
            {
                if (ReferenceEquals(current, this))
                {
                    throw new InvalidOperationException("效果联动不能形成循环。");
                }
            }
            Unlink();
            link = new EffectLink(this, source, useTargetRoot);
            Invalidate();
            return link;
        }
        internal void Release(EffectLink value)
        {
            if (!ReferenceEquals(link, value))
            {
                return;
            }
            parameters = (resolvedParameters ?? lastSnapshot ?? parameters).Clone();
            presetValues = EffectParameterStyle.Write(parameters);
            link = null;
            Invalidate();
        }
        void Unlink()
        {
            link?.Dispose();
        }
        void Invalidate()
        {
            revision++;
            resolvedLayout = null;
            resolvedParameters = null;
        }
        internal void Freeze()
        {
            resolvedLayout = null;
            available = Alive;
            if (available)
            {
                rawLayout = Element.Document.Effects.GetLayout(Element);
            }
        }
        internal void Tick()
        {
            if (!Alive)
            {
                Dispose();
                return;
            }
            if (playback != null)
            {
                var surfaceClock = Element.Document.Surface.UIClock;
                var clock = playback.UpdateMode == EffectUpdateMode.Unscaled ? UnityUIClock.Unscaled : UnityUIClock.Scaled;
                // A caller-supplied Surface clock is authoritative in deterministic simulations and previews.
                if (!ReferenceEquals(surfaceClock, UnityUIClock.Scaled) && !ReferenceEquals(surfaceClock, UnityUIClock.Unscaled))
                {
                    clock = surfaceClock;
                }
                var now = clock.Now;
                var delta = ReferenceEquals(clock, previousClock) ? now - previousTime : 0;
                previousClock = clock;
                previousTime = now;
                playback.Advance(playback.UpdateMode == EffectUpdateMode.Manual ? 0 : delta);
                if (!Alive)
                {
                    return;
                }
            }
            var p = Compute(Element.Document.Styles.GetComputed(Element), true);
            var rejecting = p.TransitionFilter is TransitionFilter.Fade or TransitionFilter.Cutoff or TransitionFilter.Dissolve or TransitionFilter.Melt or TransitionFilter.Burn;
            var state = (p.BlendType != BlendType.AlphaBlend, InputPolicy == TransitionInputPolicy.DisableWhenComplete && rejecting && p.TransitionProgress >= 1);
            if (runtimeState != state)
            {
                Element.Document.Effects.SetRuntimeState(Element, state.Item1, state.Item2);
                runtimeState = state;
            }
        }
        EffectParameters Compute(ComputedStyleSnapshot author, bool freshSources = false)
        {
            var basis = parameters;
            if (link != null)
            {
                if (link.Source.disposed || !link.Source.available)
                {
                    Release(link);
                    basis = parameters;
                }
                else
                {
                    basis = freshSources ? link.Source.Compute(link.Source.Element.Document.Styles.GetComputed(link.Source.Element), true) : link.Source.ResolveParameters();
                }
            }
            if (inspectionParameters != null)
            {
                basis = inspectionParameters;
            }
            var sourceVersion = link?.Source.valueVersion ?? 0;
            if (ReferenceEquals(computedAuthor, author) && ReferenceEquals(computedBasis, basis) && computedRevision == revision && computedSourceVersion == sourceVersion)
            {
                return computedParameters;
            }
            var p = computedParameters ??= basis.CopyForRendering();
            p.CopyValuesFrom(basis);
            if (link != null)
            {
                p.SamplingScale = parameters.SamplingScale;
                p.AllowToModifyMeshShape = parameters.AllowToModifyMeshShape;
            }
            var values = inspectionParameters != null ? EffectParameterStyle.Write(p, inspectionValues ??= new(StringComparer.Ordinal)) : link != null ? EffectParameterStyle.Write(p, linkedValues ??= new(StringComparer.Ordinal)) : presetValues;
            var style = new EffectStyleView(author, values);
            EffectParameterReader.Read(style, p);
            if (playback != null && !playback.IsCancelled)
            {
                var rate = playback.EvaluatedProgress;
                var channels = playback.Channels;
                var animated = playbackValues ??= new(StringComparer.Ordinal);
                animated.Clear();
                bool Set(EffectTweenChannels channel, bool active, string key, float value)
                {
                    if (active && (channels & channel) != 0)
                    {
                        animated[key] = StyleOverride.Number(value);
                        return author.GetOrigin(key) <= StyleValueOrigin.Author;
                    }
                    return false;
                }
                if (Set(EffectTweenChannels.Tone, p.ToneFilter != ToneFilter.None, "--urui-fx-tone-intensity", rate))
                {
                    p.ToneIntensity = UnityEngine.Mathf.Clamp01(rate);
                }
                if (Set(EffectTweenChannels.Color, p.ColorFilter != ColorFilter.None, "--urui-fx-color-intensity", rate))
                {
                    p.ColorIntensity = UnityEngine.Mathf.Clamp01(rate);
                }
                if (Set(EffectTweenChannels.Sampling, p.SamplingFilter != SamplingFilter.None, "--urui-fx-sampling-intensity", rate))
                {
                    p.SamplingIntensity = UnityEngine.Mathf.Clamp01(rate);
                }
                if (Set(EffectTweenChannels.Transition, p.TransitionFilter != TransitionFilter.None, "--urui-fx-transition-progress", rate))
                {
                    p.TransitionProgress = UnityEngine.Mathf.Clamp01(rate);
                }
                if (Set(EffectTweenChannels.GradationOffset, p.GradationMode != GradationMode.None, "--urui-fx-gradation-offset", -1 + 2 * rate))
                {
                    p.GradationOffset = UnityEngine.Mathf.Clamp(-1 + 2 * rate, -1, 1);
                }
                if (Set(EffectTweenChannels.GradationRotation, p.GradationMode is GradationMode.Angle or GradationMode.AngleGradient, "--urui-fx-gradation-rotation", 360 * rate))
                {
                    p.GradationRotation = UnityEngine.Mathf.Clamp(360 * rate, 0, 360);
                }
                if (Set(EffectTweenChannels.EdgeShiny, p.EdgeMode == EdgeMode.Shiny, "--urui-fx-edge-shiny-progress", rate))
                {
                    p.EdgeShinyProgress = UnityEngine.Mathf.Clamp01(rate);
                }
                style = new EffectStyleView(author, values, animated);
            }
            // Keep original CSS image declarations in the style view. Direct preset textures stay in the pure snapshot.
            lastStyleView = style;
            lastStyle = null;
            computedAuthor = author;
            computedBasis = basis;
            computedRevision = revision;
            computedSourceVersion = sourceVersion;
            valueVersion++;
            return p;
        }
        ComputedStyleSnapshot lastStyle;
        EffectStyleView lastStyleView;
        internal EffectParameters ResolveParameters()
        {
            if (rawLayout == null)
            {
                return lastSnapshot ?? parameters;
            }
            var author = rawLayout.Style;
            resolvedParameters = Compute(author);
            if (!ReferenceEquals(lastSnapshotSource, resolvedParameters))
            {
                lastSnapshot = resolvedParameters;
                lastSnapshotSource = resolvedParameters;
            }
            return resolvedParameters;
        }
        EffectLayout ResolveLayout(EffectLayout current)
        {
            if (resolvedLayout != null)
            {
                return resolvedLayout;
            }
            ResolveParameters();
            var result = current.WithStyle(lastStyle ??= lastStyleView.Materialize());
            if (link is { UseTargetRoot: true } && link.Source.rawLayout != null)
            {
                result = result.WithRoot(link.Source.ResolveLayout(link.Source.rawLayout));
            }
            return resolvedLayout = result;
        }
        EffectLayout IEffectParameterBinding.ResolveLayout(EffectLayout current) => ResolveLayout(current);
        EffectProgramCoordinates IEffectParameterBinding.ResolveCoordinates(EffectProgramCoordinates coordinates)
        {
            if (link is not { UseTargetRoot: true } || link.Source.rawLayout == null)
            {
                return coordinates;
            }
            var source = link.Source;
            while (source.link is { UseTargetRoot: true } && source.link.Source.rawLayout != null)
            {
                source = source.link.Source;
            }
            return coordinates.WithRoot(source.rawLayout);
        }
        void IEffectParameterBinding.ResolveProgramParameters(string program, Span<UnityEngine.Vector4> values)
        {
            ResolveParameters();
            if (program == "ui")
            {
                UIEffectParameters.ApplyOverrides(values, lastStyleView);
            }
        }
        internal EffectParameters RenderingParameters => resolvedParameters ?? lastSnapshot ?? parameters;
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            playback?.Cancel();
            link?.Dispose();
            binding.Dispose();
            rawLayout = null;
            resolvedLayout = null;
            module.Release(this);
        }
    }
}
