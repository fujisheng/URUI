using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Tests
{
    /// <summary>
    /// 独立验收场景显式提供 Surface 与作者 CSS、原始字体资源。
    /// </summary>
    internal static class AcceptanceDocuments
    {
        internal static Document Create(GameObject owner, Surface surface = null)
        {
            var document = owner.AddComponent<Document>();
            document.Surface = surface ? surface : owner.GetComponent<Surface>() ?? owner.AddComponent<Surface>();
            document.Surface.Viewport = new Vector2Int(640, 480);
            document.Surface.Input.UseUnityInput = false;
            document.SourceUrl = "ui/acceptance.html";
            var files = new List<TextResource>();
            Add("/urui/base.css", "URUI/base");
            Add("/urui/controls.css", "URUI/controls");
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf";
                    Add("/urui/Fonts/" + name + ".bytes", "URUI/Fonts/" + name);
                }
            }
            document.TextResources = files.ToArray();
            return document;

            void Add(string source, string key)
            {
                files.Add(new TextResource(source, UnityEngine.Resources.Load<TextAsset>(key)
                    ?? throw new InvalidOperationException("验收缺少作者资源：" + key)));
            }
        }
    }
}
