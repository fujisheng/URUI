using System;
using URUI.Animation;

static partial class Program
{
    static partial void SequenceTests()
    {
        const string text = "{\"version\":1,\"name\":\"弹窗😀\",\"duration\":3,\"loop\":false,\"tracks\":[{\"id\":\"fade\",\"kind\":\"css\",\"target\":\"panel\",\"keyframes\":\"panel-enter\",\"start\":0.5,\"duration\":1,\"iterations\":2,\"alternate\":true,\"easing\":\"linear\"}]}";
        var invalidEasing = new SequenceDefinition();
        invalidEasing.Tracks.Add(new SequenceTrack { Target = "a", Keyframes = "fade", Easing = "ease-out paused" });
        Throws(() => invalidEasing.Validate());
        invalidEasing.Tracks[0].Easing = "cubic-bezier(2,0,1,1)";
        Throws(() => invalidEasing.Validate());
        invalidEasing.Tracks[0].Easing = "cubic-bezier(.2,-.5,.8,1.5)";
        invalidEasing.Validate();
        invalidEasing.Tracks[0].Easing = "linear(0, .5 30% 60%, 1)";
        invalidEasing.Validate();
        var definition = SequenceDefinition.Parse(text);
        Equal("弹窗😀", definition.Name);
        Equal(1, definition.Tracks.Count);
        Equal(text.Contains("弹窗"), definition.ToJson().Contains("弹窗"));
        Equal(definition.ToJson(), SequenceDefinition.Parse(definition.ToJson()).ToJson());
        var track = definition.Tracks[0];
        Equal(false, SequenceTiming.At(track, 0).Active);
        Equal(0f, SequenceTiming.At(track, .5).Progress);
        Equal(.5f, SequenceTiming.At(track, 1).Progress);
        Equal(1f, SequenceTiming.At(track, 1.5).Progress);
        Equal(.5f, SequenceTiming.At(track, 2).Progress);
        Equal(0f, SequenceTiming.At(track, 2.5).Progress);
        Equal(0f, SequenceTiming.At(track, 30).Progress);
        var zero = new SequenceTrack { Id = "zero", Target = "panel", Keyframes = "appear", Duration = 0 };
        Equal(1f, SequenceTiming.At(zero, 0).Progress);
        Throws(() => SequenceDefinition.Parse(text.Replace("\"version\":1", "\"version\":2")));
        Throws(() => SequenceDefinition.Parse(text.Replace("\"version\":1", "\"version\":1,\"version\":1")));
        Throws(() => SequenceDefinition.Parse(text.Replace("\"start\":0.5", "\"start\":-1")));
        Throws(() => SequenceDefinition.Parse(text.Replace("\"duration\":3", "\"duration\":0")));
        Throws(() => SequenceDefinition.Parse(text.Replace("\"loop\":false", "\"unrecognized\":1,\"loop\":false")));
        var longLoop = new SequenceTrack { Id = "loop", Target = "panel", Keyframes = "pulse", Duration = .25, Iterations = 1000, Alternate = true };
        Equal(.5f, SequenceTiming.At(longLoop, 100.125).Progress);
        Throws(() => SequenceTiming.At(track, double.NaN));
        var particle = new SequenceDefinition { Name = "celebrate", Duration = 1 };
        particle.Tracks.Add(new SequenceTrack { Id = "particles", Kind = "particles", Target = "spark", Duration = 1, Seed = 7, Speed = 2 });
        particle.Validate();
        Equal(2d, particle.Tracks[0].Speed);
        particle.Tracks[0].Seed = 0;
        Throws(particle.Validate);
        particle.Tracks[0].Seed = 1;
        particle.Tracks[0].Speed = 5;
        Throws(particle.Validate);
        var overlap = new SequenceTrack { Id = "another", Target = "panel", Keyframes = "fade-out", Start = 1, Duration = 1 };
        Equal(true, SequenceTiming.Overlaps(track, overlap));
        overlap.Start = track.End;
        Equal(false, SequenceTiming.Overlaps(track, overlap));
    }
}
