using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace URUI.Editor.Localization
{
    /// <summary>
    /// Localized URUI menu labels with static English MenuItem fallbacks.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorMenus
    {
        sealed class Binding
        {
            internal string EnglishPath;
            internal string CurrentPath;
            internal int Priority;
            internal Action Execute;
            internal Func<bool> Validate;
        }
        static readonly List<Binding> bindings = new List<Binding>();
        static readonly MethodInfo add = typeof(Menu).GetMethod("AddMenuItem", BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(string), typeof(string), typeof(bool), typeof(int), typeof(Action), typeof(Func<bool>) }, null);
        static readonly MethodInfo remove = typeof(Menu).GetMethod("RemoveMenuItem", BindingFlags.NonPublic | BindingFlags.Static);

        static EditorMenus()
        {
            EditorText.LanguageChanged += Refresh;
            AssemblyReloadEvents.beforeAssemblyReload += RemoveLocalizedEntries;
        }

        public static void Register(string englishPath, int priority, Action execute, Func<bool> validate = null)
        {
            if (add == null || remove == null)
            {
                return;
            }
            var binding = bindings.Find(item => item.EnglishPath == englishPath);
            if (binding == null)
            {
                binding = new Binding { EnglishPath = englishPath };
                bindings.Add(binding);
            }
            binding.Priority = priority;
            binding.Execute = execute;
            binding.Validate = validate;
            Update(binding);
        }

        public static string Path(string englishPath)
        {
            string[] parts = englishPath.Split('/');
            // Unity owns localization of root menus; the command path keeps Window/Assets identifiers.
            for (int i = 2; i < parts.Length; i++)
            {
                parts[i] = EditorText.Tr(parts[i]);
            }
            return string.Join("/", parts);
        }

        static void RemoveLocalizedEntries()
        {
            foreach (var binding in bindings)
            {
                if (binding.CurrentPath != null && binding.CurrentPath != binding.EnglishPath)
                {
                    try
                    {
                        remove.Invoke(null, new object[] { binding.CurrentPath });
                    }
                    catch (Exception) { }
                }
            }
        }

        static void Refresh()
        {
            foreach (var binding in bindings)
            {
                Update(binding);
            }
        }

        static void Update(Binding binding)
        {
            string path = Path(binding.EnglishPath);
            if (binding.CurrentPath == path)
            {
                return;
            }
            if (binding.CurrentPath == null && path == binding.EnglishPath)
            {
                binding.CurrentPath = path;
                return;
            }
            try
            {
                // Install first so an unavailable binding cannot remove the working English fallback.
                add.Invoke(null, new object[] { path, "", false, binding.Priority, binding.Execute, binding.Validate });
                if (binding.CurrentPath != null && binding.CurrentPath != path)
                {
                    remove.Invoke(null, new object[] { binding.CurrentPath });
                }
                if (path != binding.EnglishPath && binding.CurrentPath != binding.EnglishPath)
                {
                    remove.Invoke(null, new object[] { binding.EnglishPath });
                }
                binding.CurrentPath = path;
            }
            catch (Exception)
            {
                // Keep the attribute-declared English entry on unsupported Unity versions.
            }
        }
    }
}
