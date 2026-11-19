using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;
using URUI.Editor.Localization;

namespace URUI.Authoring.Tests
{
    public static class LocalizationReviewValidation
    {
        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
        internal static void WithLanguage(SystemLanguage language, Action action)
        {
            var current = typeof(EditorText).GetField("current", Flags);
            var poll = typeof(EditorText).GetField("nextPoll", Flags);
            object previous = current.GetValue(null);
            object deadline = poll.GetValue(null);
            var changed = (Action)typeof(EditorText).GetField("LanguageChanged", Flags).GetValue(null);
            try
            {
                // Replace only the package's cached input in this isolated process, never Unity preferences.
                current.SetValue(null, language);
                poll.SetValue(null, EditorApplication.timeSinceStartup + 60);
                changed?.Invoke();
                action();
            }
            finally
            {
                current.SetValue(null, previous);
                poll.SetValue(null, deadline);
                changed?.Invoke();
            }
        }
        public static void Run() => EditorApplication.delayCall += Exercise;
        static void Exercise()
        {
            string name = Environment.GetEnvironmentVariable("URUI_LOCALIZATION_UNITY_CASE") ?? "status";
            try
            {
                Directory.CreateDirectory("Assets/LocalizationReview");
                const string path = "Assets/LocalizationReview/page.html";
                const string original = "<!doctype html><html><head></head><body><div id='Save'></div></body></html>";
                File.WriteAllText(path, original);
                AuthoringEnvironment.Open(path);
                foreach (var language in new[] { SystemLanguage.English, SystemLanguage.ChineseSimplified })
                {
                    WithLanguage(language, () =>
                                        {
                                            if (name == "status")
                                            {
                                                SaveWorksetWindow.OpenOrSave();
                                                if (AuthoringEnvironment.Status != "已保存页面、CSS 与序列工作集。")
                                                {
                                                    throw new Exception("Shared Agent diagnostics changed with UI language: " + AuthoringEnvironment.Status);
                                                }
                                            }
                                            else if (name == "menus")
                                            {
                                                var exists = typeof(Menu).GetMethod("MenuItemExists", Flags);
                                                string menu = EditorMenus.Path("Window/URUI/Editor");
                                                string expected = language == SystemLanguage.English ? "Window/URUI/Editor" : "Window/URUI/编辑器";
                                                if (menu != expected || !(bool)exists.Invoke(null, new object[] { menu }))
                                                {
                                                    throw new Exception("Localized menu was not registered: " + menu);
                                                }
                                                if (!EditorApplication.ExecuteMenuItem(menu))
                                                {
                                                    throw new Exception("Localized menu action was not executable");
                                                }
                                                if (File.ReadAllText(path) != original || AuthoringEnvironment.Author.File.Dirty)
                                                {
                                                    throw new Exception("Menu language changed source files");
                                                }
                                            }
                                            else
                                            {
                                                throw new Exception("Unknown review case");
                                            }
                                        });
                }
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/localization-review-" + name + ".json", "{\"passed\":true,\"languages\":2,\"unityPreferencesChanged\":false}");
                Debug.Log("URUI Localization review PASS " + name);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/localization-review-" + name + "-failure.txt", error.ToString());
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
    }
}
