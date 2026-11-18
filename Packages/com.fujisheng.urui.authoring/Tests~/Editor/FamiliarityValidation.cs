using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using URUI.Authoring.Animation;
using URUI.Authoring.Presentation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Authoring.Windows;

namespace URUI.Authoring.Tests
{
    public static class FamiliarityValidation
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static int assertions;
        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition)
                throw new Exception(message);
        }
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        static void Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Hidden).Invoke(target, arguments);
        public static void Run()
        {
            try
            {
                Exercise();
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/familiarity.json", "{\"passed\":" + assertions + ",\"failed\":0}");
                Debug.Log("URUI Familiarity PASS " + assertions);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/familiarity-failure.txt", error.ToString());
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
        internal static void Exercise()
        {
            const string directory = "Assets/FamiliarityChecks";
            Directory.CreateDirectory(directory);
            const string path = directory + "/page.html", cssPath = directory + "/page.css";
            foreach (string suffix in new[] { ".animations.css", ".urui-sequence.json" })
                File.Delete(Path.ChangeExtension(path, suffix));
            string original = "<!doctype html><html><head><meta name='test' content='keep' /><link rel='stylesheet' href='page.css' /><style>body{margin:0;}button{height:40px;}div{min-height:20px;}</style><style id='BoundStyle'>.keep{color:red;}</style></head><body id='Root'><div><button id='Bound' class='shared' style='width:25%;height:40px;transform:translate(10%,20px) rotate(15deg) scale(1,1);'>领取</button></div><img id='Picture' style='width:80px;height:80px;' /><input id='Input' type='text' /><section id='Custom'></section><button id='Nested'><span id='NestedLabel'>Label</span></button></body></html>";
            File.WriteAllText(path, original);
            File.WriteAllText(cssPath, ".shared{height:40px;color:blue;}");
            AuthoringEnvironment.Open(path);
            var author = AuthoringEnvironment.Author;
            string before = author.File.Text;
            long revision = author.Revision;
            var type = typeof(UruiHierarchyWindow).GetNestedType("NodeTree", BindingFlags.NonPublic);
            var tree = (TreeView)Activator.CreateInstance(type, new object[] { new TreeViewState() });
            Expand(tree);
            var names = tree.GetRows().Select(item => item.displayName).ToArray();
            Check(names.Contains("Bound") && names.Contains("Picture") && names.Contains("Custom"), "Tree lost real binding names");
            Check(names.Contains(URUI.Editor.Localization.EditorText.NodeName("div", null)), "Unnamed layout wrapper disappeared");
            Check(names.Contains("BoundStyle"), "Named metadata was hidden");
            Check(!names.Contains(URUI.Editor.Localization.EditorText.NodeName("html", null)) && !names.Contains(URUI.Editor.Localization.EditorText.NodeName("meta", null)) && !names.Contains(URUI.Editor.Localization.EditorText.NodeName("link", null)), "Default tree exposes document plumbing");
            Check(author.File.Text == before && author.Revision == revision, "Reading tree modified source");
            var nestedButton = ElementSelectionProxy.ForAuthor(author.Model.Nodes.First(n => n.Attribute("id") == "Nested").Key);
            var label = (ElementSelectionProxy)typeof(ElementInspector).GetMethod("TextChild", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { nestedButton });
            Check(label != null && label.Read("attr.id") == "NestedLabel", "Button shortcut did not find its real text child");
            type.GetProperty("ShowFullStructure", Hidden).SetValue(tree, true);
            tree.Reload();
            Expand(tree);
            Check(tree.GetRows().Any(item => item.displayName == URUI.Editor.Localization.EditorText.NodeName("meta", null)), "Complete structure lost metadata");
            type.GetProperty("ShowFullStructure", Hidden).SetValue(tree, false);
            tree.searchString = "meta";
            Check(tree.GetRows().Any(item => item.displayName == URUI.Editor.Localization.EditorText.NodeName("meta", null)), "Search does not reveal hidden structure");
            tree.searchString = "";
            var hidden = author.Model.Nodes.First(n => n.Tag == "meta");
            Invoke(tree, "Reveal", ElementSelectionProxy.ForAuthor(hidden.Key));
            Check((bool)type.GetProperty("ShowFullStructure", Hidden).GetValue(tree), "External selection cannot reveal hidden target");
            var node = author.Model.Nodes.First(n => n.Attribute("id") == "Bound");
            var proxy = ElementSelectionProxy.ForAuthor(node.Key);
            string identity = proxy.TargetId;
            var inspector = UnityEditor.Editor.CreateEditor(proxy, typeof(ElementInspector));
            try
            {
                Invoke(inspector, "Apply", "style.width", "42.5%");
                Check(proxy.Read("style.width") == "42.5%", "Inspector lost percent unit");
                Undo.PerformUndo();
                Check(author.File.Text == before && proxy.TargetId == identity, "Inspector Undo changed other source or identity");
                Invoke(inspector, "Apply", "style.transform", StyleFields.SetTransform(proxy.Read("style.transform"), "rotate", new[] { "30deg" }));
                Check(proxy.Read("style.transform") == "translate(10%,20px) rotate(30deg) scale(1,1)", "Transform edit discarded other functions or units");
                Undo.PerformUndo();
                var file = author.Workspace.Files.First(f => f.Path.EndsWith("page.css", StringComparison.Ordinal));
                var ruleType = typeof(ElementInspector).GetNestedType("RuleTarget", BindingFlags.NonPublic);
                var rule = Activator.CreateInstance(ruleType, true);
                Set(rule, "File", file);
                Set(rule, "Text", file.Text);
                Set(rule, "Css", file.Text);
                Set(rule, "CssStart", 0);
                Set(rule, "CssEnd", file.Text.Length);
                Set(rule, "FileRevision", file.Revision);
                Set(rule, "Rule", CssSource.Rules(file.Text).First());
                var rules = (IList)typeof(ElementInspector).GetField("ruleTargets", Hidden).GetValue(inspector);
                rules.Add(null);
                rules.Add(rule);
                Set(inspector, "selectedRule", 1);
                string cssBefore = file.Text;
                Invoke(inspector, "Apply", "style.width", "80px");
                Check(file.Text == cssBefore && proxy.Read("style.width") == "80px", "Common field unexpectedly edits a selected shared rule");
                Undo.PerformUndo();
                Set(inspector, "drawingAdvanced", true);
                Invoke(inspector, "Apply", "style.height", "64px");
                Check(file.Text.Contains("height:64px") && proxy.Read("style.height") == "40px", "Advanced shared edit wrote local style");
                Undo.PerformUndo();
                Set(inspector, "drawingAdvanced", false);
                Invoke(inspector, "Apply", "style.width", null);
                Check(proxy.Read("style.width") == null, "Remove override kept a local value");
                Undo.PerformUndo();
                Set(inspector, "linkPadding", true);
                int history = author.Workspace.HistoryPosition;
                Invoke(inspector, "Apply", "style.padding-left", "7px");
                Check(new[] { "left", "top", "right", "bottom" }.All(side => proxy.Read("style.padding-" + side) == "7px"), "Linked spacing did not update all sides");
                Check(author.Workspace.HistoryPosition == history + 1, "Linked spacing was multiple transactions");
                Undo.PerformUndo();
                Check(author.File.Text == before, "Linked spacing Undo was partial");
                var image = ElementSelectionProxy.ForAuthor(author.Model.Nodes.First(n => n.Attribute("id") == "Picture").Key);
                history = author.Workspace.HistoryPosition;
                ElementSelectionProxy.SetProperties(new[] { proxy, image }, new Dictionary<string, string> { ["style.opacity"] = ".6", ["style.margin-top"] = "3px" });
                Check(proxy.Read("style.opacity") == ".6" && image.Read("style.opacity") == ".6", "Multi-node edit lost a target");
                Check(author.Workspace.HistoryPosition == history + 1, "Multi-property multi-node edit was not atomic");
                Undo.PerformUndo();
                Check(author.File.Text == before, "Multi-node Undo failed");
                AnimationEnvironment.EnsureWorkspace();
                var workspace = AnimationEnvironment.Workspace;
                string track = workspace.AddPreset(proxy.SourceKey, "popup", 0, .3, author.Revision);
                AnimationEnvironment.SelectTrack(track);
                AuthoringEnvironment.SetMode(WorkspaceMode.Animation);
                AnimationEnvironment.Seek(.15);
                string animationBase = author.File.Text;
                ElementSelectionProxy.SetProperties(new[] { proxy }, new Dictionary<string, string> { ["style.opacity"] = ".3", ["style.width"] = "120px" });
                Check(author.File.Text == animationBase, "Animation preview changed base HTML");
                AnimationEnvironment.BeginRecording(proxy);
                history = author.Workspace.HistoryPosition;
                ElementSelectionProxy.SetProperties(new[] { proxy }, new Dictionary<string, string> { ["style.opacity"] = ".4", ["style.width"] = "130px" });
                Check(author.File.Text == animationBase && author.Workspace.HistoryPosition == history + 1, "Linked recording changed base or made multiple history entries");
                Check(workspace.Keys(track).Any(k => k.Declarations.Contains("130px") && k.Declarations.Contains(".4")), "Recorded values missing from CSS keys");
                AnimationEnvironment.EndRecording();
                string sampled = proxy.Node.Computed("opacity");
                Invoke(inspector, "AddKey", proxy, "style.opacity", sampled);
                Check(workspace.Keys(track).Any(k => CssSource.GetDeclaration(k.Declarations, "opacity") == sampled), "Inspector key button did not publish sampled pose");
                AnimationEnvironment.Stop();
                AuthoringEnvironment.SetMode(WorkspaceMode.Layout);
                Selection.activeObject = ElementSelectionProxy.ForAuthor(author.Model.Nodes.First(n => n.Attribute("id") == "Root").Key);
                history = author.Workspace.HistoryPosition;
                typeof(UruiHierarchyWindow).GetMethod("CreateNode", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "image" });
                Check(author.Model.Nodes.Any(n => n.Attribute("id") == "Image" && n.Tag == "img"), "Creation has no real unique binding name");
                Check(author.Workspace.HistoryPosition == history + 1, "Create was not one transaction");
                Undo.PerformUndo();
                Check(!author.Model.Nodes.Any(n => n.Attribute("id") == "Image"), "Create Undo failed");
                Check(File.ReadAllText(path) == original, "Working-copy operations persisted without Save");
            }
            finally { UnityEngine.Object.DestroyImmediate(inspector); }
        }
        static void Expand(TreeView tree)
        {
            for (int pass = 0; pass < 6; pass++)
                foreach (var item in tree.GetRows().ToArray())
                    tree.SetExpanded(item.id, true);
        }
    }
}
