using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Animation;
using URUI.Events;
namespace URUI.Tests
{
 public sealed class AnimationPlaybackTests
 {
  GameObject root,camera;Document document;
  [SetUp]public void SetUp(){camera=new GameObject("Animation camera",typeof(Camera));camera.tag="MainCamera";root=new GameObject("Named animations");document=HtmlFixtureAssets.Create(root);document.Load(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/Animations"), "URUI/Tests/Animations.html");document.SynchronizeLayout();}
  [TearDown]public void TearDown(){UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera);Time.timeScale=1;}
  sealed class ManualClock:IUiClock {public double Now{get;set;}public float Delta{get;set;}}
  [UnityTest]public IEnumerator InjectedManualClockFreezesNativeCssUntilAdvanced()
  {
   document.Unload();var clock=new ManualClock();document.Surface.UiClock=clock;document.Load(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/Animations"), "URUI/Tests/Animations.html");document.SynchronizeLayout();
   var playback=document.Animations.Play(document.DocumentElement,"same",AnimationGroup.Named(new[]{new AnimationTarget(document.Q("a"),"same-value")}));
   yield return new WaitForSecondsRealtime(.2f);Assert.IsFalse(playback.Finished.IsCompleted,"显式暂停的UI时钟不能被native墙钟推进。");
   for(int frame=0;frame<12;frame++){clock.Delta=.02f;clock.Now+=clock.Delta;yield return null;}
   Assert.IsTrue(playback.Finished.IsCompletedSuccessfully);
  }
  [UnityTest]public IEnumerator ExplicitScaledClockPausesCssAtTimeScaleZero()
  {
   document.Unload();Time.timeScale=0;document.Surface.UiClock=UnityUiClock.Scaled;document.Load(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/Animations"), "URUI/Tests/Animations.html");document.SynchronizeLayout();
   var playback=document.Animations.Play(document.DocumentElement,"same",AnimationGroup.Named(new[]{new AnimationTarget(document.Q("a"),"same-value")}));
   yield return new WaitForSecondsRealtime(.15f);Assert.IsFalse(playback.Finished.IsCompleted);Time.timeScale=1;
   yield return new WaitForSecondsRealtime(.2f);Assert.IsTrue(playback.Finished.IsCompletedSuccessfully);
  }
  AnimationGroup Group()=>AnimationGroup.Named(new[]{new AnimationTarget(document.Q("a"),"short"),new AnimationTarget(document.Q("a"),"slow"),new AnimationTarget(document.Q("b"),"delayed")});
  [UnityTest]public IEnumerator AllNamedTargetsAndDelayedTracksMustFinish()
  {
   var playback=document.Animations.Play(document.DocumentElement,"play",Group());yield return new WaitForSecondsRealtime(.07f);Assert.IsFalse(playback.Finished.IsCompleted);
   yield return new WaitForSecondsRealtime(.3f);Assert.IsTrue(playback.Finished.IsCompletedSuccessfully);
  }
  [UnityTest]public IEnumerator SameClassRestartAndReplayedOldEndCannotFinishTheNewPlayback()
  {
   AnimationEndEvent old=null;using var observe=document.Q("a").RegisterCallback<AnimationEndEvent>(value=>old=value);
   var group=AnimationGroup.Named(new[]{new AnimationTarget(document.Q("a"),"same-value")});
   var first=document.Animations.Play(document.DocumentElement,"same",group);yield return new WaitForSecondsRealtime(.13f);Assert.IsTrue(first.Finished.IsCompletedSuccessfully);Assert.NotNull(old);Assert.That(old.Identity,Is.GreaterThan(0));
   var saved=old;var second=document.Animations.Play(document.DocumentElement,"same",group);for(int i=0;i<3;i++)yield return null;
   first.Dispose();document.Q("a").SendEvent(saved);Assert.IsFalse(second.Finished.IsCompleted,"旧身份不能结束新播放。");
   yield return new WaitForSecondsRealtime(.15f);Assert.IsTrue(second.Finished.IsCompletedSuccessfully);
  }
  [UnityTest]public IEnumerator CancellationHiddenTargetsAndUnloadNeverSucceed()
  {
   using var cancellation=new CancellationTokenSource();var first=document.Animations.Play(document.DocumentElement,"play",Group(),cancellation.Token);yield return null;cancellation.Cancel();Assert.IsTrue(first.Finished.IsCanceled);
   var second=document.Animations.Play(document.DocumentElement,"play",Group());document.Q("a").AddToClassList("hidden");for(int frame=0;frame<5&&!second.Finished.IsCompleted;frame++)yield return null;Assert.IsTrue(second.Finished.IsCanceled);
   document.Q("a").RemoveFromClassList("hidden");var third=document.Animations.Play(document.DocumentElement,"play",Group());document.Unload();Assert.IsTrue(third.Finished.IsCanceled);
  }
  [UnityTest]public IEnumerator NativeAnimationsContinueAtTimeScaleZeroAndSameValueTracksComplete()
  {
   Time.timeScale=0;var group=AnimationGroup.Named(new[]{new AnimationTarget(document.Q("a"),"same-value")});var playback=document.Animations.Play(document.DocumentElement,"same",group);
   yield return new WaitForSecondsRealtime(.14f);Assert.IsTrue(playback.Finished.IsCompletedSuccessfully);Assert.AreEqual(1,document.Styles.GetComputed(document.Q("a")).GetNumber("--anim-same"));
  }
  [UnityTest]public IEnumerator ZeroDurationAndEmptyGroupsComplete()
  {
   var playback=document.Animations.Play(document.DocumentElement,"instant",AnimationGroup.Named(new[]{new AnimationTarget(document.Q("a"),"instant-value")}));
   yield return null;yield return null;yield return null;Assert.IsTrue(playback.Finished.IsCompletedSuccessfully,playback.Finished.Exception?.ToString());
   var empty=document.Animations.Play(document.DocumentElement,"empty",AnimationGroup.FromAuthorTargets(()=>Array.Empty<URUI.Elements.Element>()));yield return null;yield return null;Assert.IsTrue(empty.Finished.IsCompletedSuccessfully);
  }
  [UnityTest]public IEnumerator AuthorTargetWaitsForDelayedStaggerActivation()
  {
   var target=document.Q("b");target.Style.Set("visibility","hidden");document.SynchronizeLayout();
   var playback=document.Animations.Play(document.DocumentElement,"trigger",AnimationGroup.FromAuthorTargets(()=>new[]{target}));
   yield return new WaitForSecondsRealtime(.07f);Assert.IsFalse(playback.Finished.IsCompleted,"等待逐项揭示的作者目标不能在动画启动前被当作隐藏取消。");
   target.Style.Remove("visibility");document.DocumentElement.AddToClassList("activate-second");yield return new WaitForSecondsRealtime(.13f);Assert.IsTrue(playback.Finished.IsCompletedSuccessfully);
  }
  [UnityTest]public IEnumerator AuthorTargetHiddenAfterAnimationStartsIsCanceled()
  {
   document.Unload();document.Surface.UiClock=new ManualClock();document.Load(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/Animations"), "URUI/Tests/Animations.html");document.SynchronizeLayout();
   var target=document.Q("b");document.DocumentElement.AddToClassList("activate-second");
   var playback=document.Animations.Play(document.DocumentElement,"trigger",AnimationGroup.FromAuthorTargets(()=>new[]{target}));
   for(var frame=0;frame<4;frame++){yield return null;}
   Assert.IsFalse(playback.Finished.IsCompleted);
   target.Style.Set("visibility","hidden");
   for(var frame=0;frame<4&&!playback.Finished.IsCompleted;frame++){yield return null;}
   Assert.IsTrue(playback.Finished.IsCanceled,"动画已经启动后隐藏目标仍必须取消，不能报告完成。");
  }

 }
}
