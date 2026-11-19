using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;
using URUI.Editor.Localization;

namespace URUI.Authoring.Tests
{
    public static class LocalizationValidation
    {
        static int count;
        static void Check(bool condition, string message)
        {
            count++;
            if (!condition)
            {
                throw new Exception(message);
            }
        }
        public static void Run()
        {
            EditorApplication.delayCall += Exercise;
        }
        static void Exercise()
        {
            try
            {
                var type = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.LocalizationDatabase");
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                Check(type != null && EditorText.CanReadEditorLanguage, "Current Unity language API is unavailable");
                bool enabled = (bool)type.GetProperty("enableEditorLocalization", flags).GetValue(null);
                var native = enabled ? (SystemLanguage)type.GetProperty("currentEditorLanguage", flags).GetValue(null) : SystemLanguage.English;
                Check(EditorText.CurrentLanguage == native, "Text provider does not follow the effective Unity Editor language");
                Check(EditorText.Tr("保存") == (EditorText.IsChinese ? "保存" : "Save"), "Current-language lookup failed");
                Check(EditorText.Translate("布局 Layout", false) == "Layout", "English section label leaked Chinese");
                Check(EditorText.Translate("布局 Layout", true) == "布局", "Chinese section label retained a bilingual suffix");
                Check(EditorText.NodeName("button", "Save") == "Save", "An English binding name was translated");
                Check(EditorText.NodeName("button", "保存") == "保存", "A Chinese binding name was translated");
                Check(EditorText.NodeName("div", null) == (EditorText.IsChinese ? "div（未命名）" : "div (unnamed)"), "Unnamed annotation did not follow language");
                Check(EditorText.Tr("--urui-image-fill-mode") == "--urui-image-fill-mode", "CSS identifiers were translated");
                var menuExists = typeof(Menu).GetMethod("MenuItemExists", flags);
                foreach (string command in new[] { "Window/URUI/Editor", "Window/URUI/Hierarchy", "Window/URUI/Canvas", "Window/URUI/Animation" })
                {
                    Check((bool)menuExists.Invoke(null, new object[] { EditorMenus.Path(command) }), "Localized command is missing: " + command);
                }
                Directory.CreateDirectory("Assets/LocalizationChecks");
                const string path = "Assets/LocalizationChecks/page.html";
                const string original = "<!doctype html><html><head><style>body{margin:0;}</style></head><body><button id='保存' style='width:25%;height:40px;'>Button</button><div id='Save'></div></body></html>";
                File.WriteAllText(path, original);
                AuthoringEnvironment.Open(path);
                var author = AuthoringEnvironment.Author;
                var node = author.Model.Nodes.Single(n => n.Attribute("id") == "保存");
                var proxy = ElementSelectionProxy.ForAuthor(node.Key);
                Selection.activeObject = proxy;
                AuthoringWindow.Open();
                long revision = author.Revision;
                var changed = (Action)typeof(EditorText).GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                changed?.Invoke();
                Check(author.Revision == revision && author.File.Text == original && !author.File.Dirty, "Language refresh edited source files");
                Check(EditorText.CurrentLanguage == native, "Language refresh changed Unity's language preference");
                proxy.Set("style.width", "35%");
                Check(proxy.Read("style.width") == "35%" && proxy.Read("attr.id") == "保存", "Localized fields changed property values or binding names");
                Undo.PerformUndo();
                Check(author.File.Text == original, "Localized edit Undo did not restore source");
                var widgets = typeof(AuthoringWindow).Assembly.GetType("URUI.Authoring.Windows.PropertyWidgets");
                string width = (string)widgets.GetMethod("Label", flags).Invoke(null, new object[] { "width" });
                Check(width == (EditorText.IsChinese ? "宽度" : "Width"), "Cached property labels ignored language");
                var choices = (Dictionary<string, string[]>)widgets.GetField("choices", flags).GetValue(null);
                Check(choices["position"].SequenceEqual(new[] { "static", "relative", "absolute", "fixed" }), "CSS enum values were translated");
                Check(File.ReadAllText(path) == original, "Localization saved source files implicitly");
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/localization.json", "{\"passed\":" + count + ",\"failed\":0,\"nativeLanguage\":\"" + native + "\",\"isChinese\":" + (EditorText.IsChinese ? "true" : "false") + "}");
                Debug.Log("URUI Localization PASS " + count);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/localization-failure.txt", error.ToString());
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
    }
}
