using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;
namespace URUI.Effect.Tests
{
    public sealed class EffectPlaybackTests
    {
        sealed class Clock : IUIClock
        {
            public double Now
            {
                get;
                set;
            }
            public float Delta => 0;
        }
        [TestCase(EffectWrapMode.Once, .25f)]
        [TestCase(EffectWrapMode.Loop, .25f)]
        [TestCase(EffectWrapMode.PingPongOnce, .25f)]
        [TestCase(EffectWrapMode.PingPongLoop, .25f)]
        public void AllTweenChannelsAndWrapModes(EffectWrapMode wrap, float rate)
        {
            using var playback = new EffectPlayback(new EffectTweenDefinition
            {
                WrapMode = wrap,
                Delay = .5,
                Duration = 2,
                Interval = 1,
                Channels = EffectTweenChannels.Visual | EffectTweenChannels.Event
            });
            playback.SetTime(1);
            Assert.That(playback.Progress, Is.EqualTo(rate));
            playback.SetTime(2.75);
            Assert.That(playback.Progress, Is.EqualTo(wrap == EffectWrapMode.Once ? 1 : 1));
            if (wrap is EffectWrapMode.PingPongOnce or EffectWrapMode.PingPongLoop)
            {
                playback.SetTime(4);
                Assert.That(playback.Progress, Is.EqualTo(.75f));
            }
            if (wrap is EffectWrapMode.Loop or EffectWrapMode.PingPongLoop)
            {
                playback.SetTime(1000000000001);
                Assert.That(playback.Progress, Is.InRange(0, 1));
                playback.SetTime(-1);
                Assert.That(playback.Progress, Is.InRange(0, 1));
            }
        }
        [Test]
        public void SeparateReverseCurveAndInterval()
        {
            using var playback = new EffectPlayback(new EffectTweenDefinition
            {
                WrapMode = EffectWrapMode.PingPongLoop,
                Duration = 1,
                Interval = .5,
                SeparateReverseCurve = true,
                ReverseCurve = AnimationCurve.Linear(0, 1, 1, 0)
            });
            playback.SetTime(.25);
            Assert.That(playback.EvaluatedProgress, Is.EqualTo(.25f).Within(.001));
            playback.SetTime(1.25);
            Assert.That(playback.EvaluatedProgress, Is.EqualTo(1));
            playback.SetTime(1.75);
            Assert.That(playback.Progress, Is.EqualTo(.75f));
            Assert.That(playback.EvaluatedProgress, Is.EqualTo(.25f).Within(.001));
            playback.SetTime(2.75);
            Assert.That(playback.Progress, Is.Zero);
        }
        [Test]
        public void ManualSeekAndLargeDelta()
        {
            using var playback = new EffectPlayback(new EffectTweenDefinition
            {
                WrapMode = EffectWrapMode.Once,
                Duration = 2,
                UpdateMode = EffectUpdateMode.Manual
            });
            var count = 0;
            playback.Completed += () => count++;
            playback.SetTime(2);
            Assert.That(count, Is.Zero, "seek 不是自然完成");
            playback.SetTime(.5);
            playback.Pause();
            playback.Advance(10);
            Assert.That(playback.Time, Is.EqualTo(.5));
            playback.Resume();
            playback.Advance(1e12);
            Assert.That(count, Is.EqualTo(1));
            playback.Advance(5);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(playback.Finished.IsCompletedSuccessfully, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => playback.SetTime(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => playback.Advance(double.PositiveInfinity));
        }
        [UnityTest]
        public IEnumerator CompletionOnceAfterCancelReload()
        {
            using var fixture = new EffectRenderFixture();
            var clock = new Clock();
            fixture.Surface.UIClock = clock;
            fixture.Load("");
            fixture.Render();
            var controller = fixture.Module.GetController(fixture.Document.Q("subject"));
            var cancellation = new CancellationTokenSource();
            var playback = controller.Play(new EffectTweenDefinition { WrapMode = EffectWrapMode.Once }, cancellation.Token);
            var count = 0;
            playback.Completed += () => count++;
            fixture.Render();
            clock.Now = .5;
            fixture.Render();
            Assert.That(playback.Progress, Is.EqualTo(.5f).Within(.001));
            cancellation.Cancel();
            clock.Now = 2;
            fixture.Render();
            Assert.That(playback.IsCancelled, Is.True);
            Assert.That(playback.Finished.IsCanceled, Is.True);
            Assert.That(count, Is.Zero);
            var next = controller.Play(new EffectTweenDefinition { WrapMode = EffectWrapMode.Once });
            next.Completed += () => count++;
            fixture.Render();
            clock.Now = 3;
            fixture.Render();
            fixture.Render();
            Assert.That(count, Is.EqualTo(1));
            fixture.Load("");
            fixture.Render();
            Assert.That(count, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => controller.Play(new EffectTweenDefinition()));
            cancellation.Dispose();
            yield return null;
        }
        [Test]
        public void DefinitionIsAnIndependentSnapshotAndEventsAreExplicit()
        {
            var definition = new EffectTweenDefinition
            {
                WrapMode = EffectWrapMode.Once,
                Curve = AnimationCurve.Linear(0, 0, 1, 1),
                Channels = EffectTweenChannels.Tone
            };
            using var playback = new EffectPlayback(definition);
            definition.Curve = AnimationCurve.Linear(0, 1, 1, 0);
            definition.Duration = 4;
            var events = 0;
            playback.ProgressChanged += _ => events++;
            playback.SetTime(.4);
            Assert.That(playback.EvaluatedProgress, Is.EqualTo(.4f).Within(.001));
            Assert.That(events, Is.Zero);
            using var withEvent = new EffectPlayback(new EffectTweenDefinition { Channels = EffectTweenChannels.Event });
            withEvent.ProgressChanged += _ => events++;
            withEvent.SetTime(.4);
            withEvent.SetTime(.4);
            Assert.That(events, Is.EqualTo(1));
        }
        [TestCase(EffectPlayOnEnable.None, EffectDirection.Reverse, false)]
        [TestCase(EffectPlayOnEnable.Forward, EffectDirection.Forward, true)]
        [TestCase(EffectPlayOnEnable.Reverse, EffectDirection.Reverse, true)]
        [TestCase(EffectPlayOnEnable.KeepDirection, EffectDirection.Reverse, true)]
        public void ExplicitEnablePolicy(EffectPlayOnEnable policy, EffectDirection expected, bool playing)
        {
            using var playback = new EffectPlayback(new EffectTweenDefinition
            {
                PlayOnEnable = policy,
                Direction = EffectDirection.Reverse,
                ResetTimeOnEnable = true,
                WrapMode = EffectWrapMode.Once
            });
            playback.Enabled = false;
            playback.Enabled = true;
            Assert.That(playback.Direction, Is.EqualTo(expected));
            Assert.That(playback.IsPlaying, Is.EqualTo(playing));
        }
        [Test]
        public void CancellingFromProgressCannotReportSuccessAndRestartHasNewIdentity()
        {
            using var playback = new EffectPlayback(new EffectTweenDefinition
            {
                WrapMode = EffectWrapMode.Once,
                Channels = EffectTweenChannels.Event
            });
            var count = 0;
            playback.Completed += () => count++;
            playback.ProgressChanged += _ => playback.Cancel();
            playback.Advance(1);
            Assert.That(count, Is.Zero);
            Assert.That(playback.Finished.IsCanceled, Is.True);
            using var next = new EffectPlayback(new EffectTweenDefinition { WrapMode = EffectWrapMode.Once });
            next.Completed += () => count++;
            next.Advance(1);
            Assert.That(count, Is.EqualTo(1));
            var old = next.Finished;
            var identity = next.RunIdentity;
            next.Enabled = false;
            next.Enabled = true;
            Assert.That(next.IsPlaying, Is.True);
            Assert.That(next.RunIdentity, Is.GreaterThan(identity));
            next.Advance(1);
            next.Advance(1);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(old.IsCompletedSuccessfully, Is.True);
        }
        [UnityTest]
        public IEnumerator ManualSeekDoesNotCompleteOnRenderAndCallbacksMayDisposeTheModule()
        {
            using var fixture = new EffectRenderFixture();
            var clock = new Clock();
            fixture.Surface.UIClock = clock;
            fixture.Load("");
            fixture.Render();
            var controller = fixture.Module.GetController(fixture.Document.Q("subject"));
            var seek = controller.Play(new EffectTweenDefinition
            {
                WrapMode = EffectWrapMode.Once,
                UpdateMode = EffectUpdateMode.Manual
            });
            var count = 0;
            seek.Completed += () => count++;
            seek.SetTime(1);
            fixture.Render();
            Assert.That(count, Is.Zero);
            Assert.That(seek.Finished.IsCompleted, Is.False);
            var playback = controller.Play(new EffectTweenDefinition { WrapMode = EffectWrapMode.Once });
            playback.Completed += () => fixture.Module.Dispose();
            fixture.Render();
            clock.Now = 1;
            fixture.Render();
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }
    }
}
