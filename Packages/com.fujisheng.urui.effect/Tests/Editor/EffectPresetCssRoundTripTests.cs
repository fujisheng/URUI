using System;
using System.Collections;
using UnityEngine.TestTools;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Effect.Editor.Import;
namespace URUI.Effect.Editor.Tests
{
    public sealed class EffectPresetCssRoundTripTests
    {
        [UnityTest]
        public IEnumerator ExportedCssLoadsThroughTheProductionParserAndPreservesParameters()
        {
            yield return new EnterPlayMode();
            var host = new GameObject("CSS preset roundtrip");
            var camera = new GameObject("Camera", typeof(Camera));
            camera.tag = "MainCamera";
            var surface = host.AddComponent<Surface>();
            var document = host.AddComponent<Document>();
            document.Surface = surface;
            var module = new EffectModule(document);
            try
            {
                var expected = new EffectParameters
                {
                    ToneFilter = ToneFilter.Sepia,
                    ToneIntensity = .37f,
                    ColorFilter = ColorFilter.HsvModifier,
                    Color = new Color(-.2f, .3f, .4f, .7f),
                    SamplingFilter = SamplingFilter.RgbShift,
                    SamplingWidth = 3,
                    SamplingScale = 2,
                    AllowToModifyMeshShape = false,
                    TransitionFilter = TransitionFilter.Dissolve,
                    TransitionProgress = .37f,
                    TransitionRange = new EffectRange(.2f, .8f),
                    TransitionTextureScale = new Vector2(2, 3),
                    DetailFilter = DetailFilter.Multiply,
                    DetailIntensity = .6f,
                    ShadowMode = ShadowMode.Shadow3,
                    ShadowDistance = new Vector2(4, -3),
                    ShadowIteration = 3,
                    GradationMode = GradationMode.Horizontal,
                    GradationColor1 = Color.red,
                    GradationColor2 = Color.blue,
                    EdgeMode = EdgeMode.Shiny,
                    EdgeShinyProgress = .7f,
                    PatternArea = PatternArea.Edge,
                    Flip = Flip.Vertical | Flip.Effect
                };
                var css = EffectPresetCssWriter.Write(expected, "#subject");
                document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>" + css + "</style></head><body><div id='subject' style='width:64px;height:64px;'></div></body></html>", "/roundtrip.html");
                document.SynchronizeLayout();
                var actual = module.GetController(document.Q("subject")).CreateParameters();
                foreach (var field in typeof(EffectParameters).GetFields())
                {
                    var a = field.GetValue(actual);
                    var e = field.GetValue(expected);
                    if (e is Gradient || typeof(Texture).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }
                    if (e is Color color)
                    {
                        var got = (Color)a;
                        for (var i = 0; i < 4; i++)
                        {
                            Assert.That(got[i], Is.EqualTo(color[i]).Within(1f / 255), field.Name + "/" + i);
                        }
                    }
                    else
                    {
                        Assert.That(a, Is.EqualTo(e), field.Name);
                    }
                }
            }
            finally
            {
                module.Dispose();
                document.Unload();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(camera);
            }
            yield return new ExitPlayMode();
        }
    }
}
