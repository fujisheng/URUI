using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Authoring.Windows;

namespace URUI.Authoring.Tests
{
    public static class FamiliarityReviewValidation
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
        static void Call(object target, string name, params object[] values) => target.GetType().GetMethod(name, Hidden).Invoke(target, values);
        public static void Run()
        {
            string name = Environment.GetEnvironmentVariable("URUI_FAMILIARITY_CASE") ?? "reveal";
            try
            {
                Directory.CreateDirectory("Assets/FamiliarityReview");
                const string path = "Assets/FamiliarityReview/page.html";
                File.Delete(Path.ChangeExtension(path, ".urui-sequence.json"));
                File.Delete(Path.ChangeExtension(path, ".animations.css"));
                File.WriteAllText(path, "<!doctype html><html><head><style>body{margin:0;width:100%;height:100%;}</style></head><body><div id='A' style='width:20px;height:40px;opacity:1;padding-left:2px;padding-top:4px !important;'></div><img id='Picture' style='width:30% !important;height:40px;' /></body></html>");
                AuthoringEnvironment.Open(path);
                var author = AuthoringEnvironment.Author;
                var a = ElementSelectionProxy.ForAuthor(author.Model.Nodes.First(n => n.Attribute("id") == "A").Key);
                var b = ElementSelectionProxy.ForAuthor(author.Model.Nodes.First(n => n.Attribute("id") == "Picture").Key);
                var inspector = UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { a, b }, typeof(ElementInspector));
                try
                {
                    string before = author.File.Text;
                    if (name == "numeric")
                    {
                        Call(inspector, "Apply", "style.width", "21px");
                        Check(author.File.Text == before, "Numeric multi-edit destroyed another target's important declaration");
                        var raw = (System.Collections.Generic.HashSet<string>)inspector.GetType().GetField("expressions", Hidden).GetValue(inspector);
                        raw.Add("style.width");
                        Call(inspector, "Apply", "style.width", "21px");
                        Check(a.Read("style.width") == "21px" && b.Read("style.width") == "21px", "Explicit raw replacement was blocked");
                    }
                    else if (name == "units")
                    {
                        b.Set("style.width", "30%");
                        Call(inspector, "Apply", "style.width", "21px");
                        Check(a.Read("style.width") == "21px" && b.Read("style.width") == "21%", "Multi-edit lost per-target units");
                    }
                    else if (name == "linked")
                    {
                        UnityEngine.Object.DestroyImmediate(inspector);
                        inspector = UnityEditor.Editor.CreateEditor(a, typeof(ElementInspector));
                        inspector.GetType().GetField("linkPadding", Hidden).SetValue(inspector, true);
                        Call(inspector, "Apply", "style.padding-left", "5px");
                        Check(author.File.Text == before, "Linked numeric edit destroyed a sibling important declaration");
                    }
                    else if (name == "reveal")
                    {
                        var type = typeof(UruiHierarchyWindow).GetNestedType("NodeTree", BindingFlags.NonPublic);
                        var tree = (TreeView)Activator.CreateInstance(type, new object[] { new TreeViewState() });
                        tree.searchString = "Picture";
                        Call(tree, "Reveal", a);
                        Check(tree.GetRows().Any(row => row.displayName == "A"), "Search still excludes externally selected node");
                        Check(tree.searchString == "", "Tree retained hidden filtering state");
                        tree.searchString = "Picture";
                        Call(tree, "Reveal", b);
                        Check(tree.searchString == "Picture", "Selecting a matching search result cleared the user's search");
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(inspector);
                        bool root = name == "root" || name == "root-menu";
                        var proxy = root ? ElementSelectionProxy.ForAuthor(author.Model.Nodes.First(n => n.Tag == "body").Key) : a;
                        inspector = UnityEditor.Editor.CreateEditor(proxy, typeof(ElementInspector));
                        AnimationEnvironment.EnsureWorkspace();
                        var workspace = AnimationEnvironment.Workspace;
                        string track = workspace.AddPreset(proxy.SourceKey, "popup", 0, .3, author.Revision);
                        if (root)
                            workspace.SetTrack(track, t => t.Target = "$root", workspace.Revision);
                        AnimationEnvironment.SelectTrack(track);
                        AuthoringEnvironment.SetMode(WorkspaceMode.Animation);
                        AnimationEnvironment.Seek(.1);
                        Selection.activeObject = proxy;
                        if (name == "root-menu")
                        {
                            var type = typeof(UruiEditorWindow).Assembly.GetType("URUI.Authoring.Animation.TimelineView");
                            var timeline = Activator.CreateInstance(type, Hidden, null, new object[] { (Action)(() => { }) }, null);
                            type.GetField("keyValue", Hidden).SetValue(timeline, "stale");
                            Call(timeline, "SelectProperty", "width", workspace.Definition.Tracks.First());
                            string value = (string)type.GetField("keyValue", Hidden).GetValue(timeline);
                            Check(value != "stale" && value.Length > 0, "Root property menu retained previous value");
                        }
                        else
                        {
                            string sampled = proxy.Node.Computed("opacity");
                            Check(Math.Abs(double.Parse(sampled, CultureInfo.InvariantCulture) - 1) > .01, "Fixture did not sample an intermediate pose");
                            Call(inspector, "AddKey", proxy, "style.opacity", "1");
                            var keys = workspace.Keys(track).Where(k => Math.Abs(k.Position - 1d / 3) < 1e-5).ToArray();
                            Check(keys.Length > 0, "Root/current pose key was not added");
                            string stored = CssSource.GetDeclaration(keys[0].Declarations, "opacity");
                            Check(Math.Abs(double.Parse(stored, CultureInfo.InvariantCulture) - double.Parse(sampled, CultureInfo.InvariantCulture)) < .001, "Key button stored base value instead of sampled pose");
                        }
                        AnimationEnvironment.Stop();
                    }
                }
                finally { if (inspector) UnityEngine.Object.DestroyImmediate(inspector); }
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/familiarity-review-" + name + ".json", "{\"passed\":true}");
                Debug.Log("URUI Familiarity review PASS " + name);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/familiarity-review-" + name + "-failure.txt", error.ToString());
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
    }
}
