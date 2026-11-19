using System;
namespace URUI.Effect
{
    [Serializable]
    public sealed class ParticlePlaybackOptions
    {
        public bool PlayOnLoad = true;
        public bool PauseWhenHidden = true;
        public bool RestartOnShow = true;
        internal ParticlePlaybackOptions Clone() => (ParticlePlaybackOptions)MemberwiseClone();
    }
    public enum ParticlePlaybackState
    {
        Playing,
        Paused,
        Stopped
    }
    public readonly struct ParticlePainterStatistics
    {
        public long BodyBakes
        {
            get;
        }
        public long TrailBakes
        {
            get;
        }
        public long ParticleReads
        {
            get;
        }
        public long DrawCalls
        {
            get;
        }
        public double ReadMilliseconds
        {
            get;
        }
        public double BodyBakeMilliseconds
        {
            get;
        }
        public double TrailBakeMilliseconds
        {
            get;
        }
        public double RecordMilliseconds
        {
            get;
        }
        internal ParticlePainterStatistics(long body, long trail, long reads, long draws, double readMs, double bodyMs, double trailMs, double recordMs)
        {
            BodyBakes = body;
            TrailBakes = trail;
            ParticleReads = reads;
            DrawCalls = draws;
            ReadMilliseconds = readMs;
            BodyBakeMilliseconds = bodyMs;
            TrailBakeMilliseconds = trailMs;
            RecordMilliseconds = recordMs;
        }
    }
}
