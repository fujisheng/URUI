using System;
using System.Collections.Generic;
using System.Linq;
using URUI.Elements;
namespace URUI.Animation
{
    /// <summary>一个真实节点上的命名 CSS 动画；全部属性轨道结束后才完成。</summary>
    public sealed class AnimationTarget
    {
        public Element Element {get;}
        public string Name {get;}
        public AnimationTarget(Element element,string name)
        {Element=element??throw new ArgumentNullException(nameof(element));if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("动画名不能为空。",nameof(name));Name=name;}
    }
    /// <summary>命名目标组，或在准备布局后由作者目标解析的有限动画组；不包含时长和曲线。</summary>
    public sealed class AnimationGroup
    {
        internal readonly IReadOnlyList<AnimationTarget> Targets;
        internal readonly Func<IReadOnlyList<Element>> Resolve;
        AnimationGroup(IReadOnlyList<AnimationTarget> targets,Func<IReadOnlyList<Element>> resolve){Targets=targets;Resolve=resolve;}
        public static AnimationGroup Named(IEnumerable<AnimationTarget> targets)
        {if(targets==null)throw new ArgumentNullException(nameof(targets));var values=targets.ToArray();if(values.Any(x=>x==null))throw new ArgumentException("动画目标不能为 null。");return new AnimationGroup(Array.AsReadOnly(values),null);}
        /// <summary>等待每个解析目标启动新的有限命名动画，适合延迟入场或序列最后一个实际条目；空列表立即完成。</summary>
        public static AnimationGroup FromAuthorTargets(Func<IReadOnlyList<Element>> targets)
            =>new(null,targets??throw new ArgumentNullException(nameof(targets)));
    }
}
