using System;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace URUI.Editor.Localization
{
    /// <summary>Follows the effective Unity Editor language, independently of the operating system language.</summary>
    [InitializeOnLoad]
    public static class EditorText
    {
        static readonly Type database = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.LocalizationDatabase");
        static readonly PropertyInfo language = database?.GetProperty("currentEditorLanguage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        static readonly PropertyInfo enabled = database?.GetProperty("enableEditorLocalization", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        static SystemLanguage current;
        static double nextPoll;

        static EditorText()
        {
            current = ReadLanguage();
            EditorApplication.update += Poll;
        }

        public static event Action LanguageChanged;
        public static SystemLanguage CurrentLanguage => current;
        public static bool IsChinese => EditorTextCatalog.IsChineseLanguage(current.ToString());
        public static bool CanReadEditorLanguage => language != null && enabled != null;

        static SystemLanguage ReadLanguage()
        {
            try
            {
                if (!CanReadEditorLanguage || !(bool)enabled.GetValue(null))
                    return SystemLanguage.English;
                return (SystemLanguage)language.GetValue(null);
            }
            catch (Exception)
            {
                // The internal getter is isolated here; unsupported Editor versions retain usable English UI.
                return SystemLanguage.English;
            }
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + .25;
            var next = ReadLanguage();
            if (next == current)
                return;
            current = next;
            LanguageChanged?.Invoke();
            InternalEditorUtility.RepaintAllViews();
        }

        public static string Tr(string key) => EditorTextCatalog.Translate(key, IsChinese);
        public static string Translate(string key, bool chinese) => EditorTextCatalog.Translate(key, chinese);
        public static string Message(string message) => EditorTextCatalog.Message(message, IsChinese);
        public static string Format(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, Tr(key), values);

        /// <summary>Names are user data. Only the absent-name annotation is translated.</summary>
        public static string NodeName(string tag, string id) => string.IsNullOrEmpty(id) ? tag + Tr("（未命名）") : id;

        public static void Title(EditorWindow window, string key)
        {
            string text = Tr(key);
            if (window.titleContent.text != text)
                window.titleContent = new GUIContent(text, window.titleContent.image, window.titleContent.tooltip);
        }
    }
}
