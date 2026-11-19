using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class EffectTransitionTests
    {
        [Serializable]
        internal sealed class CaptureSet
        {
            public string colorSpace;
            public List<string> cases = new();
        }
        [UnityTest]
        public IEnumerator EveryTransitionMatchesReference()
        {
            using var fixture = new EffectRenderFixture();
            var report = new CaptureSet { colorSpace = QualitySettings.activeColorSpace.ToString() };
            foreach (var mode in (TransitionFilter[])Enum.GetValues(typeof(TransitionFilter)))
            {
                foreach (var phase in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    var id = "TransitionFilter." + mode + "." + Mathf.RoundToInt(phase * 100).ToString("000");
                    fixture.Load("--urui-fx-transition-filter:" + EffectRenderFixture.Kebab(mode.ToString()) + ";--urui-fx-transition-progress:" +
                        phase.ToString(CultureInfo.InvariantCulture) + ";--urui-fx-transition-texture:url('pattern.png');" +
                        "--urui-fx-transition-width:.25;--urui-fx-transition-softness:.4;");
                    fixture.Compare(id);
                    report.cases.Add(id);
                    yield return null;
                }
            }
            File.WriteAllText(Path.Combine(fixture.Output, "transition-captures.json"), JsonUtility.ToJson(report, true));
            yield return null;
        }
    }
}
