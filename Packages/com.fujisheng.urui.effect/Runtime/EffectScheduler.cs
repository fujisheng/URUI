using System;
using UnityEngine;
namespace URUI.Effect
{
    /// <summary>处理输入的完整身份。时间驱动调用方显式把时间版本放进 Version；视频使用实际呈现版本。</summary>
    public readonly struct EffectInput:IEquatable<EffectInput>
    {
        public readonly object Source;
        public readonly ulong Version,Parameters;
        public readonly Vector2 Size;
        public readonly string Program;
        public readonly RenderTextureFormat Format;
        public EffectInput(object source,ulong version,Vector2 size,ulong parameters,string program,RenderTextureFormat format)
        {
            if(source==null||string.IsNullOrWhiteSpace(program)||!float.IsFinite(size.x)||!float.IsFinite(size.y)||size.x<=0||size.y<=0)throw new ArgumentException("处理输入身份、程序及尺寸必须有效。");
            Source=source;Version=version;Size=size;Parameters=parameters;Program=program;Format=format;
        }
        public bool Equals(EffectInput other)=>ReferenceEquals(Source,other.Source)&&Version==other.Version&&Size==other.Size&&Parameters==other.Parameters&&Program==other.Program&&Format==other.Format;
        public override bool Equals(object value)=>value is EffectInput other&&Equals(other);
        public override int GetHashCode()=>HashCode.Combine(Source,Version,Size,Parameters,Program,Format);
    }
    /// <summary>按实例调度；记录并不等于提交，旧世代和迟到的提交不能覆盖新缓存。</summary>
    public sealed class EffectScheduler
    {
        EffectInput committed;bool valid;ulong generation,serial,committedSerial;
        public readonly struct Update
        {
            readonly EffectScheduler owner;readonly EffectInput input;readonly ulong generation,serial;
            internal Update(EffectScheduler owner,EffectInput input,ulong generation,ulong serial){this.owner=owner;this.input=input;this.generation=generation;this.serial=serial;}
            public void Submitted()
            {if(owner==null)throw new InvalidOperationException("处理任务未准备。");if(owner.generation!=generation||serial<owner.committedSerial)return;owner.committed=input;owner.valid=true;owner.committedSerial=serial;}
        }
        public bool TryPrepare(EffectInput input,out Update update)
        {
            if(input.Source==null||string.IsNullOrWhiteSpace(input.Program))throw new ArgumentException("调度输入不能为空。",nameof(input));
            if(valid&&committed.Equals(input)){update=default;return false;}
            update=new Update(this,input,generation,++serial);return true;
        }
        public void Invalidate(){generation++;valid=false;committed=default;committedSerial=0;}
    }
}
