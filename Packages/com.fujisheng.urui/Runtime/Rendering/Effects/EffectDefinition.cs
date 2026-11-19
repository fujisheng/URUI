using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Styles;
namespace URUI.Rendering.Effects
{
    public enum EffectKind
    {
        Image,
        Element
    }
    /// <summary>
    /// 文档加载前注册的不可变效果声明；参数默认值由作者样式拥有。
    /// </summary>
    public sealed class EffectDefinition
    {
        public string Name
        {
            get;
        }
        public EffectKind Kind
        {
            get;
        }
        public string ProgramKey
        {
            get;
        }
        public string ColorContract
        {
            get;
        }
        /// <summary>
        /// 未知用户程序保留外部操作；不能用 ColorContract 文本推测合批能力。
        /// </summary>
        public RenderProgramDescriptor RenderProgram
        {
            get;
        }
        public RenderParameterLayout RenderParameters
        {
            get;
        }
        public Vector4 Expansion
        {
            get;
        }
        readonly Func<EffectLayout, Vector4> expansionResolver;
        readonly Func<EffectLayout, EffectComposite> compositeResolver;
        readonly Func<EffectLayout, Matrix4x4[]> boundsTransforms;
        internal bool HasLayoutCallbacks => expansionResolver != null;
        internal bool HasCoverageCallbacks => expansionResolver != null || compositeResolver != null || boundsTransforms != null;
        internal bool HasCompositeCallback => compositeResolver != null;
        public EffectImageCondition ImageCondition
        {
            get;
        }
        public bool UsesTime
        {
            get;
        }
        public IReadOnlyList<StylePropertyDefinition> Parameters
        {
            get;
        }
        public IEffectFactory Factory
        {
            get;
        }
        public EffectDefinition(string name, EffectKind kind, IEffectFactory factory, string programKey,
            string colorContract, IEnumerable<StylePropertyDefinition> parameters = null, bool usesTime = false, Vector4 expansion = default,
            Func<EffectLayout, Vector4> expansionResolver = null, RenderProgramDescriptor renderProgram = default, RenderParameterLayout renderParameters = null)
            : this(name, kind, factory, programKey, colorContract, parameters, usesTime, expansion, expansionResolver, null, null, null, renderProgram, renderParameters) { }
        public EffectDefinition(string name, EffectKind kind, IEffectFactory factory, string programKey,
            string colorContract, IEnumerable<StylePropertyDefinition> parameters, bool usesTime, Vector4 expansion,
            Func<EffectLayout, Vector4> expansionResolver, EffectImageCondition imageCondition, Func<EffectLayout, EffectComposite> compositeResolver,
            Func<EffectLayout, Matrix4x4[]> boundsTransforms = null, RenderProgramDescriptor renderProgram = default, RenderParameterLayout renderParameters = null)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "none" || name.IndexOfAny(new[] { ' ', '\0', ';', ':' }) >= 0)
            {
                throw new ArgumentException("效果名必须是非空 CSS 标识符。", nameof(name));
            }
            if (kind != EffectKind.Image && kind != EffectKind.Element)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (string.IsNullOrWhiteSpace(programKey) || string.IsNullOrWhiteSpace(colorContract))
            {
                throw new ArgumentException("效果必须声明程序与颜色约定。");
            }
            for (int i = 0; i < 4; i++)
            {
                if (!float.IsFinite(expansion[i]) || expansion[i] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(expansion));
                }
            }
            var list = parameters == null ? new List<StylePropertyDefinition>() : new List<StylePropertyDefinition>(parameters);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in list)
            {
                if (parameter == null || !names.Add(parameter.Name))
                {
                    throw new ArgumentException("效果参数必须非空且唯一。", nameof(parameters));
                }
            }
            if (renderParameters != null && !renderProgram.IsKnown)
            {
                throw new ArgumentException("固定参数需要已知程序。", nameof(renderParameters));
            }
            RenderParameters = renderParameters;
            RenderProgram = renderProgram;
            Name = name;
            Kind = kind;
            ProgramKey = programKey;
            ColorContract = colorContract;
            UsesTime = usesTime;
            Expansion = expansion;
            Parameters = list.AsReadOnly();
            this.expansionResolver = expansionResolver;
            ImageCondition = imageCondition;
            this.compositeResolver = compositeResolver;
            this.boundsTransforms = boundsTransforms;
        }
        internal EffectComposite ResolveComposite(EffectLayout layout) => compositeResolver?.Invoke(layout) ?? default;
        internal Matrix4x4[] ResolveBoundsTransforms(EffectLayout layout) => boundsTransforms?.Invoke(layout) ?? Array.Empty<Matrix4x4>();
        internal Vector4 ResolveExpansion(EffectLayout layout)
        {
            var result = Expansion + (expansionResolver?.Invoke(layout) ?? Vector4.zero);
            for (var index = 0; index < 4; index++)
            {
                if (!float.IsFinite(result[index]) || result[index] < 0)
                {
                    throw new InvalidOperationException("效果扩边无效：" + Name);
                }
            }
            return result;
        }
    }
}
