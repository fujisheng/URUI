using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace URUI.Effect
{
    /// <summary>独立的双精度时间线。手动 seek 不触发成功完成；自然到达终点仅通知一次。</summary>
    public sealed class EffectPlayback:IDisposable
    {
        readonly EffectTweenDefinition definition;
        readonly Action changed;
        TaskCompletionSource<bool> finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly CancellationToken cancellationToken;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        CancellationTokenRegistration cancellation;
        bool paused,cancelled,completed,enabled=true;
        int cancellationRequested;
        double time;
        float rawRate,evaluatedRate;
        EffectDirection direction;
        public event Action<float> ProgressChanged;
        public event Action Completed;
        public Task Finished=>finished.Task;
        public long RunIdentity {get;private set;}=1;
        public float Progress=>rawRate;
        public float EvaluatedProgress=>evaluatedRate;
        public double Time=>time;
        public bool IsPlaying=>enabled&&!paused&&!cancelled&&!completed&&(Looping||(direction==EffectDirection.Forward?time<TotalTime:time>0));
        public bool IsCancelled=>cancelled;
        public bool IsPaused=>paused;
        public bool IsDelaying=>time<definition.Delay;
        public EffectDirection Direction=>direction;
        public EffectTweenChannels Channels=>definition.Channels;
        public EffectUpdateMode UpdateMode=>definition.UpdateMode;
        bool Looping=>definition.WrapMode is EffectWrapMode.Loop or EffectWrapMode.PingPongLoop;
        public double TotalTime=>definition.WrapMode switch {
            EffectWrapMode.Once=>definition.Delay+definition.Duration,
            EffectWrapMode.Loop=>definition.Delay+definition.Duration+definition.Interval,
            EffectWrapMode.PingPongOnce=>definition.Delay+2*definition.Duration+definition.Interval,
            _=>definition.Delay+2*(definition.Duration+definition.Interval)};
        public bool Enabled
        {
            get=>enabled;
            set
            {
                CheckThread();
                if(enabled==value)return;enabled=value;
                if(!value)return;
                if(definition.PlayOnEnable==EffectPlayOnEnable.None){paused=true;return;}
                if(definition.PlayOnEnable==EffectPlayOnEnable.Forward)direction=EffectDirection.Forward;
                if(definition.PlayOnEnable==EffectPlayOnEnable.Reverse)direction=EffectDirection.Reverse;
                if(completed){Restart();return;}
                if(definition.ResetTimeOnEnable)ResetPosition();Resume();
            }
        }
        public EffectPlayback(EffectTweenDefinition definition):this(definition,null,default){}
        internal EffectPlayback(EffectTweenDefinition definition,Action changed,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();cancellationToken=token;this.definition=(definition??throw new ArgumentNullException(nameof(definition))).Snapshot();
            this.changed=changed;direction=this.definition.Direction;ResetPosition();
            // Cancellation may arrive on a worker. Native/Unity work stays in the owner's next main-thread tick.
            cancellation=token.Register(()=>Interlocked.Exchange(ref cancellationRequested,1));
        }
        void ResetPosition(){time=direction==EffectDirection.Forward?0:Math.Max(0,TotalTime-.0001);Evaluate(false);}
        static double Repeat(double value,double length)=>value-Math.Floor(value/length)*length;
        static double PingPong(double value,double length)=>length-Math.Abs(Repeat(value,2*length)-length);
        void Evaluate(bool notify)
        {
            var t=time-definition.Delay;var duration=definition.Duration;var interval=definition.Interval;
            if(t<=0)t=0;
            else t=definition.WrapMode switch {
                EffectWrapMode.Once=>Math.Min(t,duration),
                EffectWrapMode.Loop=>Repeat(t,duration+interval),
                EffectWrapMode.PingPongOnce=>PingPong(Math.Min(t,2*duration+interval),duration+interval*.5),
                _=>Repeat(t,2*(duration+interval)) is var q && q<2*duration+interval?PingPong(q,duration+interval*.5):0};
            var rate=(float)Math.Clamp(t/duration,0,1);
            var reverse=definition.WrapMode is EffectWrapMode.Once or EffectWrapMode.Loop ? direction==EffectDirection.Reverse : time>=definition.Delay+duration+interval;
            var curve=definition.SeparateReverseCurve&&reverse?definition.ReverseCurve:definition.Curve;
            var evaluated=curve.Evaluate(rate);if(!float.IsFinite(evaluated))throw new InvalidOperationException("播放曲线输出必须有限。");
            var differs=!Mathf.Approximately(rawRate,rate)||!Mathf.Approximately(evaluatedRate,evaluated);
            rawRate=rate;evaluatedRate=evaluated;if(differs)changed?.Invoke();
            if(notify&&differs&&(definition.Channels&EffectTweenChannels.Event)!=0)ProgressChanged?.Invoke(evaluated);
        }
        public void Advance(double delta)
        {
            CheckThread();
            if(!double.IsFinite(delta))throw new ArgumentOutOfRangeException(nameof(delta));
            if(Interlocked.Exchange(ref cancellationRequested,0)!=0){Cancel();return;}
            if(!IsPlaying)return;
            var signed=direction==EffectDirection.Forward?delta:-delta;var old=time;time+=signed;
            if(Looping)
            {
                if(time<0)time=Repeat(time,TotalTime);
                else if(time>definition.Delay||(signed<0&&old>=definition.Delay))time=Repeat(time-definition.Delay,TotalTime-definition.Delay)+definition.Delay;
            }
            else time=Math.Clamp(time,0,TotalTime);
            var run=RunIdentity;Evaluate(true);
            if(run==RunIdentity&&!cancelled&&!completed&&!Looping&&((direction==EffectDirection.Forward&&time>=TotalTime)||(direction==EffectDirection.Reverse&&time<=0)))
            {completed=true;cancellation.Dispose();finished.TrySetResult(true);Completed?.Invoke();}
        }
        public void SetTime(double seconds)
        {
            CheckThread();
            if(!double.IsFinite(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            time=Looping?(seconds<0?Repeat(seconds,TotalTime):seconds>definition.Delay?Repeat(seconds-definition.Delay,TotalTime-definition.Delay)+definition.Delay:seconds):Math.Clamp(seconds,0,TotalTime);
            Evaluate(true);
        }
        void CheckThread(){if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("播放句柄只能在创建它的线程操作；跨线程取消请使用 CancellationToken。");}
        public void Pause(){CheckThread();paused=true;}
        public void Resume(){CheckThread();if(!cancelled&&!completed)paused=false;}
        public void Stop(){CheckThread();paused=true;time=0;Evaluate(true);}
        public void Restart()
        {
            CheckThread();
            if(cancelled)throw new InvalidOperationException("已取消的播放句柄不能重新使用。");cancellationToken.ThrowIfCancellationRequested();
            if(completed){completed=false;finished=new(TaskCreationOptions.RunContinuationsAsynchronously);cancellation=cancellationToken.Register(()=>Interlocked.Exchange(ref cancellationRequested,1));}
            RunIdentity++;paused=false;ResetPosition();
        }
        public void Cancel(){CheckThread();if(cancelled||completed)return;cancelled=true;cancellation.Dispose();finished.TrySetCanceled();changed?.Invoke();}
        public void Dispose()=>Cancel();
    }
}
