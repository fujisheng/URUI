using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;

namespace URUI.Authoring.Tests
{
    public static class FamiliarityVisualValidation
    {
        static int frames;
        static string original;
        static double readyAt;
        static AuthoringWindow workspace;
        static FamiliarityInspectorHost inspector;
        static readonly List<string> errors = new List<string>();
        static readonly string[] targets = { "claim", "reward", "PreviewImage", "PreviewInput", "claim" };
        public static void Run()
        {
            try
            {
                Application.logMessageReceived += Log;
                string sample = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AuthoringEnvironment).Assembly).resolvedPath, "Samples~/Animation Demo");
                Directory.CreateDirectory("Assets/FamiliarityVisual");
                foreach (string file in Directory.GetFiles(sample))
                {
                    if (!file.EndsWith(".meta"))
                    {
                        File.Copy(file, "Assets/FamiliarityVisual/" + Path.GetFileName(file), true);
                    }
                }
                string path = "Assets/FamiliarityVisual/Reward.html";
                File.WriteAllText(path, File.ReadAllText(path).Replace("</body>", "<img id='PreviewImage' style='width:30px;height:30px;' /><input id='PreviewInput' type='text' /></body>"));
                AssetDatabase.Refresh();
                AuthoringEnvironment.Open(path);
                original = AuthoringEnvironment.Author.File.Text;
                AnimationEnvironment.EnsureWorkspace();
                workspace = ScriptableObject.CreateInstance<AuthoringWindow>();
                workspace.titleContent = new GUIContent("URUI 编辑器");
                workspace.ShowUtility();
                workspace.position = new Rect(10, 50, 1000, 800);
                inspector = ScriptableObject.CreateInstance<FamiliarityInspectorHost>();
                inspector.titleContent = new GUIContent("Inspector");
                inspector.ShowUtility();
                inspector.position = new Rect(1020, 50, 410, 800);
                Select(0);
                Directory.CreateDirectory("Evidence");
                EditorApplication.update += Tick;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }
        static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                errors.Add(message + "\n" + stack);
            }
        }
        static void Select(int index)
        {
            var node = AuthoringEnvironment.Author.Model.Nodes.First(n => n.Attribute("id") == targets[index]);
            AuthoringEnvironment.Select(ElementSelectionProxy.ForAuthor(node.Key));
            inspector.Focus();
        }
        static void Tick()
        {
            try
            {
                frames++;
                workspace.Repaint();
                inspector.Repaint();
                const string selectFile = "Library/URUI/Familiarity/select-target";
                if (File.Exists(selectFile))
                {
                    string binding = File.ReadAllText(selectFile);
                    File.Delete(selectFile);
                    int selected = Array.IndexOf(targets, binding);
                    if (selected >= 0)
                    {
                        Select(selected);
                    }
                }
                if (frames == 25 || frames == 50 || frames == 75 || frames == 100)
                {
                    Select(frames / 25);
                }
                if (frames == 105)
                {
                    AuthoringEnvironment.SetMode(WorkspaceMode.Animation);
                    AnimationEnvironment.SelectTrack("reward-in");
                    AnimationEnvironment.Seek(.2);
                }
                if (frames == 120)
                {
                    if (errors.Count != 0)
                    {
                        throw new Exception(string.Join("\n", errors));
                    }
                    if (FamiliarityInspectorHost.Painted.Count < 4)
                    {
                        throw new Exception("Inspector did not draw all four node types: " + string.Join(",", FamiliarityInspectorHost.Painted));
                    }
                    if (AuthoringEnvironment.Author.File.Text != original)
                    {
                        throw new Exception("GUI observation modified HTML");
                    }
                    AuthoringEnvironment.Preview.Capture(Path.GetFullPath("Evidence/familiarity-canvas.png"));

                    File.WriteAllText("Evidence/familiarity-visual.json", "{\"guiFrames\":120,\"types\":4,\"errors\":0,\"sourceUnchanged\":true}");
                    readyAt = EditorApplication.timeSinceStartup;
                    Directory.CreateDirectory("Library/URUI/Familiarity");
                    File.WriteAllText("Library/URUI/Familiarity/gui-ready", "ready");
                }
                if (readyAt > 0 && (File.Exists("Library/URUI/Familiarity/gui-finish") || EditorApplication.timeSinceStartup - readyAt > 60))
                {
                    if (errors.Count != 0)
                    {
                        throw new Exception(string.Join("\n", errors));
                    }
                    EditorApplication.update -= Tick;
                    AnimationEnvironment.Stop();
                    Application.logMessageReceived -= Log;
                    inspector.Close();
                    workspace.Close();
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }
        static void Fail(Exception error)
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory("Evidence");
            File.WriteAllText("Evidence/familiarity-visual-failure.txt", error.ToString());
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
    public sealed class FamiliarityInspectorHost : EditorWindow
    {
        internal static readonly HashSet<string> Painted = new HashSet<string>();
        static readonly System.Reflection.PropertyInfo activeNode = typeof(AuthoringEnvironment)
            .GetProperty("ActiveNode", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        UnityEditor.Editor inspector;
        Vector2 scroll;
        void OnGUI()
        {
            var proxy = (ElementSelectionProxy)activeNode.GetValue(null);
            if (inspector && inspector.target != proxy)
            {
                DestroyImmediate(inspector);
            }
            if (!inspector && proxy)
            {
                inspector = UnityEditor.Editor.CreateEditor(proxy, typeof(ElementInspector));
            }
            using (var area = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = area.scrollPosition;
                inspector?.OnInspectorGUI();
                if (Event.current.type == EventType.Repaint && inspector && proxy)
                {
                    Painted.Add(proxy.IsLive ? proxy.Node.Tag : proxy.SourceNode.Tag);
                }
            }
        }
        void OnDisable()
        {
            if (inspector)
            {
                DestroyImmediate(inspector);
            }
        }
    }
}
