using System.Collections.Generic;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    internal static class EffectHtmlFixtureAssets
    {
        internal static float TargetChannel(float encoded) => QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.GammaToLinearSpace(encoded) : encoded;
        internal static Document Create(GameObject host)
        {
            var surface = host.AddComponent<Surface>();
            var document = host.AddComponent<Document>();
            document.Surface = surface;
            return document;
        }

        internal static void Load(Document document, string resourceKey)
        {
            var files = new List<TextResource>(document.TextResources) {
                new TextResource("/urui/base.css", UnityEngine.Resources.Load<TextAsset>("URUI/base")),
                new TextResource("/urui/behaviors.css", UnityEngine.Resources.Load<TextAsset>("URUI/behaviors")),
                new TextResource("/urui-effect/Effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect"))
            };
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf";
                    files.Add(new TextResource("/urui/Fonts/" + name + ".bytes", UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + name)));
                }
            var unique = new Dictionary<string, TextResource>(System.StringComparer.Ordinal);
            foreach (var file in files) { unique[file.Source] = file; }
            document.TextResources = new List<TextResource>(unique.Values).ToArray();
            document.Load(UnityEngine.Resources.Load<TextAsset>(resourceKey), "/" + resourceKey + ".html");
        }
    }
}
