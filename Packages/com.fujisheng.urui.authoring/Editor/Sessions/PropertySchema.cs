using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;
using URUI.Documents;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Sessions
{
    public static class PropertySchema
    {
        [Serializable]
        sealed class Metadata
        {
            public Entry[] entries;
        }
        [Serializable]
        sealed class Entry
        {
            public string name, category, status;
        }
        static AuthoringProperty[] cached;
        public static IReadOnlyList<AuthoringProperty> All
        {
            get
            {
                if (cached != null)
                    return cached;
                var result = new List<AuthoringProperty>();
                foreach (string name in new[] { "id", "class", "src", "data-urui-sprite", "title", "disabled", "tabindex" })
                    result.Add(new AuthoringProperty { Name = "attr." + name, Group = "Identity", Description = "HTML attribute; null removes it." });
                foreach (string name in new[] { "name", "placeholder", "min", "max", "step", "maxlength", "readonly", "checked", "selected", "for", "data-urui-label", "data-urui-multiline", "data-urui-orientation" })
                    result.Add(new AuthoringProperty { Name = "attr." + name, Group = "Control", Description = "Supported authored control attribute; null removes it. Boolean HTML attributes use presence, not the string false." });
                result.Add(new AuthoringProperty { Name = "text", Group = "Control", Description = "Plain leaf text; never replaces structured children." });
                result.Add(new AuthoringProperty { Name = "value", Group = "Control", Description = "Control value; live writes do not notify business callbacks." });
                result.Add(new AuthoringProperty { Name = "image", Group = "Control", Type = "asset", Description = "Texture2D asset path; live uses a temporary resource lease." });
                string metadata = Path.Combine(PackageInfo.FindForAssembly(typeof(Document).Assembly).resolvedPath, "Documentation~/HtmlCssSupport.json");
                foreach (var entry in JsonUtility.FromJson<Metadata>(File.ReadAllText(metadata)).entries)
                    if ((entry.category == "property" || entry.category == "shorthand") && entry.status != "remove" && entry.status != "unsupported")
                        result.Add(new AuthoringProperty { Name = "style." + entry.name, Group = "CSS", Description = "Inline CSS declaration; native validation determines valid values." });
                cached = result.ToArray();
                return cached;
            }
        }
        public static void Require(string property)
        {
            foreach (var item in All)
                if (item.Name == property && !item.ReadOnly)
                    return;
            foreach (var extension in AuthoringEnvironment.Extensions)
                foreach (var item in extension.Properties)
                    if (item.Name == property && !item.ReadOnly)
                        return;
            throw new InvalidOperationException("unsupported_property: " + property);
        }
    }
}
