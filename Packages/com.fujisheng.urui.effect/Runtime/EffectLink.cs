using System;
namespace URUI.Effect
{
    internal sealed class EffectLink : IDisposable
    {
        EffectController owner;
        internal EffectController Source
        {
            get;
        }
        internal bool UseTargetRoot
        {
            get;
        }
        internal EffectLink(EffectController owner, EffectController source, bool useTargetRoot)
        {
            this.owner = owner;
            Source = source;
            UseTargetRoot = useTargetRoot;
        }
        public void Dispose()
        {
            var current = owner;
            owner = null;
            current?.Release(this);
        }
    }
}
