using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class EffectShadowTests
    {
        [UnityTest]
        public IEnumerator ShadowOutlineMirrorBounds()
        {
            using var fixture = new EffectRenderFixture();
            var report = new EffectTransitionTests.CaptureSet { colorSpace = QualitySettings.activeColorSpace.ToString() };
            foreach (var mode in (ShadowMode[])Enum.GetValues(typeof(ShadowMode)))
            {
                var id = "ShadowMode." + mode + ".050";
                fixture.Load("--urui-fx-shadow-mode:" + EffectRenderFixture.Kebab(mode.ToString()) + ";--urui-fx-shadow-distance-x:5;--urui-fx-shadow-distance-y:-4;--urui-fx-shadow-iteration:2;");
                fixture.Compare(id);
                report.cases.Add(id);
                yield return null;
            }
            File.WriteAllText(Path.Combine(fixture.Output, "shadow-captures.json"), JsonUtility.ToJson(report, true));
            yield return null;
        }
        [UnityTest]
        public IEnumerator AllSixteenFlipCombinations()
        {
            using var fixture = new EffectRenderFixture();
            var report = new EffectTransitionTests.CaptureSet { colorSpace = QualitySettings.activeColorSpace.ToString() };
            for (var flip = 0; flip < 16; flip++)
            {
                var id = "Flip." + flip + ".050";
                fixture.Load("--urui-fx-flip:" + flip + ";--urui-fx-transition-filter:shiny;--urui-fx-transition-texture:url('pattern.png');--urui-fx-transition-width:.25;--urui-fx-transition-softness:.4;" +
                    "--urui-fx-gradation-mode:diagonal;--urui-fx-gradation-intensity:.5;--urui-fx-gradation-color1:#e61a33;--urui-fx-gradation-color2:#1acc4db3;--urui-fx-gradation-color3:#334de6;--urui-fx-gradation-color4:#e6b33366;");
                fixture.Compare(id);
                report.cases.Add(id);
                yield return null;
            }
            File.WriteAllText(Path.Combine(fixture.Output, "flip-captures.json"), JsonUtility.ToJson(report, true));
            yield return null;
        }
        [UnityTest]
        public IEnumerator GradientShadowFlipAndMeltDirectionMatchReference()
        {
            using var fixture = new EffectRenderFixture();
            var report = new EffectTransitionTests.CaptureSet { colorSpace = QualitySettings.activeColorSpace.ToString() };
            var cases = new[]{
                ("Combination.MeltFlipV","--urui-fx-transition-filter:melt;--urui-fx-flip:2;"),
                ("Combination.MeltFlipVEffect","--urui-fx-transition-filter:melt;--urui-fx-flip:6;"),
                ("Combination.GradientShadowFlip","--urui-fx-gradation-mode:diagonal;--urui-fx-shadow-mode:outline8;--urui-fx-flip:13;")};
            const string shared = "--urui-fx-transition-texture:url('pattern.png');--urui-fx-transition-width:.25;--urui-fx-transition-softness:.4;--urui-fx-shadow-distance-x:5;--urui-fx-shadow-distance-y:-4;--urui-fx-shadow-iteration:2;" +
                "--urui-fx-gradation-intensity:.5;--urui-fx-gradation-color1:#e61a33;--urui-fx-gradation-color2:#1acc4db3;--urui-fx-gradation-color3:#334de6;--urui-fx-gradation-color4:#e6b33366;";
            foreach (var item in cases)
            {
                fixture.Load(shared + item.Item2);
                fixture.Compare(item.Item1);
                report.cases.Add(item.Item1);
            }
            File.WriteAllText(Path.Combine(fixture.Output, "geometry-combination-captures.json"), JsonUtility.ToJson(report, true));
            yield return null;
        }
    }
}
