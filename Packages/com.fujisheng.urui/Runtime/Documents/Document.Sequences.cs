using System.Collections.Generic;
using URUI.Animation;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        readonly List<SequencePlayer> sequencePlayers = new List<SequencePlayer>();
        SequencePlayer[] sequenceSnapshot;
        internal void RegisterSequence(SequencePlayer player)
        {
            CheckThread();
            sequencePlayers.Add(player);
            sequenceSnapshot = null;
        }
        internal void UnregisterSequence(SequencePlayer player)
        {
            CheckThread();
            sequencePlayers.Remove(player);
            sequenceSnapshot = null;
        }
        internal void AdvanceSequences()
        {
            if (!IsLoaded || sequencePlayers.Count == 0)
                return;
            var snapshot = sequenceSnapshot ??= sequencePlayers.ToArray();
            double now = Surface.UiClock.Now;
            foreach (var player in snapshot)
                player.Tick(now);
        }
    }
}
