using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class DefaultControlSchemaTests
    {
        [Test]
        public void SharedControlsFullEffectSchemaAndAuthorPropertiesCanLoadTogether()
        {
            using var fixture = new EffectRenderFixture();
            var document = fixture.Document;
            var files = new List<TextResource>(document.TextResources)
            {
                new TextResource("/urui/base.css", UnityEngine.Resources.Load<TextAsset>("URUI/base")),
                new TextResource("/urui/controls.css", UnityEngine.Resources.Load<TextAsset>("URUI/controls")),
                new TextResource("/urui/behaviors.css", UnityEngine.Resources.Load<TextAsset>("URUI/behaviors"))
            };
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf";
                    files.Add(new TextResource("/urui/Fonts/" + name + ".bytes", UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + name)));
                }
            }
            document.TextResources = files.ToArray();
            const string html = "<!doctype html><html><head><link rel='stylesheet' href='/urui/base.css'>" +
                "<link rel='stylesheet' href='/urui/controls.css'><link rel='stylesheet' href='/urui/behaviors.css'>" +
                "<link rel='stylesheet' href='/effect.css'><style>" +
                "@property --preset{syntax:'none | custom';inherits:false;initial-value:none;}" +
                "@property --open{syntax:'<number>';inherits:false;initial-value:0;}" +
                "@property --reveal{syntax:'<number>';inherits:false;initial-value:0;}" +
                "@property --progress{syntax:'<number>';inherits:false;initial-value:0;}" +
                "#icon{width:32px;height:32px;--urui-effect:ui;--urui-fx-tone-filter:grayscale;}" +
                "</style></head><body><button id='button' data-urui-skin='image'><img id='icon' src='input.png' data-urui-graphic></button></body></html>";
            for (int i = 0; i < 3; i++)
            {
                Assert.DoesNotThrow(() => document.LoadHtml(html, "/default-effect-schema.html"));
                document.SynchronizeLayout();
                Assert.That(document.Q("button").Layout.width, Is.EqualTo(160));
                Assert.That(fixture.Module.GetController(document.Q("icon")).CreateParameters().ToneFilter,
                    Is.EqualTo(ToneFilter.Grayscale));
            }
        }
    }
}
