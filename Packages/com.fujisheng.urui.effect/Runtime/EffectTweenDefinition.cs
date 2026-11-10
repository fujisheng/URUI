using System;
using UnityEngine;
namespace URUI.Effect
{
    [Flags]
    public enum EffectTweenChannels : uint
    {
        None=0,Tone=1,Color=2,Sampling=4,Transition=8,GradationOffset=1<<5,GradationRotation=1<<6,EdgeShiny=1<<8,Event=1u<<31,
        Visual=Tone|Color|Sampling|Transition|GradationOffset|GradationRotation|EdgeShiny
    }
    public enum EffectUpdateMode { Normal,Unscaled,Manual }
    public enum EffectWrapMode { Once,Loop,PingPongOnce,PingPongLoop }
    public enum EffectDirection { Forward,Reverse }
    public enum EffectPlayOnEnable { None,Forward,Reverse,KeepDirection }
    [Serializable]
    public sealed class EffectTweenDefinition
    {
        public EffectTweenChannels Channels=EffectTweenChannels.Visual;
        public EffectDirection Direction=EffectDirection.Forward;
        public AnimationCurve Curve=AnimationCurve.Linear(0,0,1,1);
        public bool SeparateReverseCurve;
        public AnimationCurve ReverseCurve=AnimationCurve.Linear(0,0,1,1);
        public double Delay,Duration=1,Interval;
        public EffectPlayOnEnable PlayOnEnable=EffectPlayOnEnable.Forward;
        public bool ResetTimeOnEnable=true;
        public EffectWrapMode WrapMode=EffectWrapMode.Loop;
        public EffectUpdateMode UpdateMode=EffectUpdateMode.Normal;
        internal EffectTweenDefinition Snapshot()
        {
            if(!double.IsFinite(Delay)||!double.IsFinite(Duration)||!double.IsFinite(Interval))throw new ArgumentOutOfRangeException("播放时间必须有限。");
            if(!Enum.IsDefined(typeof(EffectDirection),Direction)||!Enum.IsDefined(typeof(EffectWrapMode),WrapMode)||!Enum.IsDefined(typeof(EffectUpdateMode),UpdateMode)||!Enum.IsDefined(typeof(EffectPlayOnEnable),PlayOnEnable))throw new ArgumentException("播放枚举无效。");
            var clone=(EffectTweenDefinition)MemberwiseClone();clone.Delay=Math.Max(0,Delay);clone.Duration=Math.Max(.001,Duration);clone.Interval=Math.Max(0,Interval);
            clone.Curve=Copy(Curve);clone.ReverseCurve=Copy(ReverseCurve);return clone;
        }
        static AnimationCurve Copy(AnimationCurve value)
        {if(value==null)throw new ArgumentNullException(nameof(value));return new AnimationCurve(value.keys){preWrapMode=value.preWrapMode,postWrapMode=value.postWrapMode};}
    }
}
