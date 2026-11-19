using System.Collections.Generic;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using UnityEngine;

namespace URUI.Tests
{
    internal static class HtmlFixtureAssets
    {
        internal static Document Create(GameObject root)
        {
            var surface = root.AddComponent<Surface>();
            var document = root.AddComponent<Document>();
            document.Surface = surface;
            var resources = new List<TextResource> {
                new TextResource("/urui/base.css", UnityEngine.Resources.Load<TextAsset>("URUI/base"))
            };
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf";
                    resources.Add(new TextResource("/urui/Fonts/" + name + ".bytes", UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + name)));
                }
            }
            document.TextResources = resources.ToArray();
            return document;
        }
    }
}
