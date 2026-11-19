using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace URUI.Effect.Tests
{
    public sealed class EffectPairwiseTests
    {
        [Serializable]
        sealed class Input
        {
            public int pairs;
            public int coveredPairs;
            public Case[] cases;
        }
        [Serializable]
        sealed class Case
        {
            public string id;
            public string css;
            public int[] values;
        }
        [Serializable]
        sealed class Result
        {
            public string id;
            public float maximum;
            public float maximumHdr;
            public double mean;
            public bool finite;
        }
        [Serializable]
        sealed class Report
        {
            public int pairs;
            public int cases;
            public string colorSpace;
            public List<Result> results = new();
        }
        [UnityTest, Timeout(600000)]
        public IEnumerator AllModePairsAgreeBetweenOriginalImageAndCapturedSubtree()
        {
            var input = JsonUtility.FromJson<Input>(UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Pairwise").text);
            Assert.That(input.coveredPairs, Is.EqualTo(input.pairs));
            Assert.That(input.pairs, Is.EqualTo(3135));
            using var fixture = new EffectRenderFixture();
            var report = new Report
            {
                pairs = input.pairs,
                colorSpace = QualitySettings.activeColorSpace.ToString()
            };
            var failures = new List<string>();
            foreach (var item in input.cases)
            {
                // 任意混合按 URUI 的整节点合成契约；其全部因子由独立数值测试验证。
                // 算法先与原插件 AlphaBlend 输出比较，避免把源逐图元混合当作整节点参考。
                fixture.Load(item.css);
                var requested = fixture.Render();
                Assert.That(fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"), out var frame), Is.True);
                Assert.That(frame.SourceIsCaptured, Is.EqualTo(item.values[5] != (int)BlendType.AlphaBlend), item.id);
                var requestedFinite = true;
                foreach (var pixel in requested)
                {
                    requestedFinite &= float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b) && float.IsFinite(pixel.a);
                }
                Assert.That(requestedFinite, Is.True, item.id);
                fixture.Load(item.css + "--urui-fx-blend-type:alpha-blend;");
                var actual = fixture.Render(item.id);
                var data = UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Reference/" + QualitySettings.activeColorSpace + "/" + item.id);
                Assert.That(data, Is.Not.Null, item.id);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                var row = new Result
                {
                    id = item.id,
                    finite = true
                };
                double sum = 0;
                try
                {
                    Assert.That(texture.LoadImage(data.bytes), Is.True);
                    var expected = texture.GetPixels();
                    for (var i = 0; i < actual.Length; i++)
                    {
                        for (var c = 0; c < 4; c++)
                        {
                            row.finite &= float.IsFinite(actual[i][c]);
                            var error = Mathf.Abs(Mathf.Clamp01(actual[i][c]) - expected[i][c]);
                            row.maximum = Mathf.Max(row.maximum, error);
                            sum += error;
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                row.mean = sum / (actual.Length * 4);
                report.results.Add(row);
                report.cases++;
                if (!row.finite || row.maximum > 4f / 255 || row.mean > .5 / 255)
                {
                    failures.Add(item.id + ": max=" + row.maximum + ", mean=" + row.mean + ", values=" + string.Join(",", item.values));
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(fixture.Output, "pairwise-results.json"), JsonUtility.ToJson(report, true));
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }
    }
}
