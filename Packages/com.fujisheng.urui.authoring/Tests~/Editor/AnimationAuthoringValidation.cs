using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Animation;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Authoring.Windows;
using URUI.Editor.Inspection;
namespace URUI.Authoring.Tests
{
    public static class AnimationAuthoringValidation
    {
        static int count;
        static void Check(bool condition, string message)
        {
            count++;
            if (!condition)
                throw new Exception(message);
        }
        static void Reject(Action action)
        {
            count++;
            try
            {
                action();
            }
            catch (InvalidOperationException) { return; }
            throw new Exception("Expected rejection");
        }
        public static void Run()
        {
            try
            {
                const string path = "Assets/AnimationChecks/page.html";
                Directory.CreateDirectory("Assets/AnimationChecks");
                foreach (string suffix in new[] { ".animations.css", ".urui-sequence.json" })
                    File.Delete(Path.ChangeExtension(path, suffix));
                string original = "<!doctype html><html><head><style>body{margin:0;}div{position:absolute;left:10px;top:20px;width:100px;height:50px;opacity:.7;background-color:red;}</style></head><body><div></div><div></div><section></section></body></html>";
                File.WriteAllText(path, original);
                AuthoringEnvironment.Open(path);
                AnimationEnvironment.EnsureWorkspace();
                var author = AuthoringEnvironment.Author;
                string key = author.Model.Nodes.First(n => n.Tag == "div").Key;
                var proxy = ElementSelectionProxy.ForAuthor(key);
                string identity = proxy.TargetId, runtimeId = proxy.Node.Id;
                proxy.Set("style.width", "120px");
                Check(proxy.Node.Id == runtimeId, "Ordinary edit reloaded the DOM");
                long revision = author.Revision;
                string before = author.File.Text;
                int history = author.Workspace.HistoryPosition;
                using (var gesture = new CanvasGesture(proxy))
                {
                    gesture.Preview(new[] { new AuthoringEdit { Node = proxy.SourceKey, Property = "style.left", Value = "30px" } });
                    gesture.Preview(new[] { new AuthoringEdit { Node = proxy.SourceKey, Property = "style.left", Value = "40px" } });
                    Check(author.Revision == revision && author.File.Text == before, "Gesture preview published source");
                }
                Check(proxy.Node.Computed("left") == "10px", "Cancel did not restore initial preview");
                using (var gesture = new CanvasGesture(proxy))
                {
                    gesture.Preview(new[] { new AuthoringEdit { Node = proxy.SourceKey, Property = "style.left", Value = "40px" } });
                    gesture.Commit();
                }
                Check(author.Workspace.HistoryPosition == history + 1, "Gesture did not commit exactly once");
                author.Undo();
                Check(author.File.Text == before && proxy.IsTargetValid, "Gesture undo lost source or identity");
                string section = author.Model.Nodes.First(n => n.Tag == "section").Key;
                author.Execute(new[] { new AuthoringEdit { Operation = "move", Node = proxy.SourceKey, Parent = section } }, author.Revision);
                Check(proxy.IsTargetValid && proxy.TargetId == identity && proxy.SourceNode.Parent.Tag == "section", "Known move lost selected node");
                author.Undo();
                Check(proxy.IsTargetValid && proxy.SourceNode.Parent.Tag == "body", "Move undo identity mismatch");
                author.Redo();
                Check(proxy.IsTargetValid && proxy.SourceNode.Parent.Tag == "section", "Move redo identity mismatch");
                author.Undo();
                var workspace = AnimationEnvironment.Workspace;
                before = author.File.Text;
                history = author.Workspace.HistoryPosition;
                string track = workspace.AddPreset(proxy.SourceKey, "popup", 0, .3, author.Revision);
                Check(proxy.IsTargetValid && proxy.TargetId == identity, "Preset creation lost locked selection identity");
                Check(author.Workspace.HistoryPosition == history + 1, "Preset creation was not one transaction");
                string sequencePath = workspace.Path, cssPath = Path.ChangeExtension(author.File.Path, ".animations.css");
                Check(!File.Exists(sequencePath) && !File.Exists(cssPath), "Creation wrote files before Save");
                Check(author.File.Text.Contains("animations.css") && workspace.Definition.Tracks.Count == 1, "Preset did not link CSS/sequence");
                author.Undo();
                Check(author.File.Text == before && workspace.Definition == null && author.Workspace.Open(cssPath).Text == null, "Preset undo left half a workset");
                author.Redo();
                Check(workspace.Definition.Tracks.Count == 1, "Preset redo failed");
                AnimationEnvironment.SelectTrack(track);
                proxy = (ElementSelectionProxy)typeof(AuthoringEnvironment)
                    .GetProperty("ActiveNode", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                workspace.SetKey(track, .5, "opacity", ".5", workspace.Revision);
                var htmlBeforeRecording = author.File.Text;
                AnimationEnvironment.Seek(.15);
                AnimationEnvironment.BeginRecording(proxy);
                proxy.Set("style.opacity", ".33");
                Check(author.File.Text == htmlBeforeRecording, "Recording changed base layout");
                Check(workspace.Keys(track).Any(k => Math.Abs(k.Position - .5) < 1e-8 && k.Declarations.Contains(".33")), "Inspector did not record current key");
                Check(Math.Abs(float.Parse(proxy.Node.Computed("opacity"), System.Globalization.CultureInfo.InvariantCulture) - .33f) < .003f, "Recorded sample did not render");
                AnimationEnvironment.EndRecording();
                AnimationEnvironment.Stop();
                workspace.SetLocked(track, true, workspace.Revision);
                Reject(() => workspace.SetKey(track, .4, "opacity", ".4", workspace.Revision));
                workspace.SetLocked(track, false, workspace.Revision);
                workspace.TransformKeys(new[] { new AnimationWorkspace.KeySelection(track, .5) }, .03, false, false, workspace.Revision);
                Check(workspace.Keys(track).Any(k => Math.Abs(k.Position - .6) < 1e-7), "Key movement did not use track time");
                long stale = workspace.Revision - 1;
                Reject(() => workspace.SetKey(track, .4, "opacity", ".4", stale));
                string recovery = Path.Combine(AuthoringEnvironment.ProjectRoot, "Library/URUI/Authoring/animation-recovery.json");
                SourceRecovery.Save(author, recovery);
                var restored = new AuthoringSession(AuthoringEnvironment.ProjectRoot);
                SourceRecovery.Restore(restored, recovery);
                Check(restored.Workspace.Files.Count(f => f.Dirty) == author.Workspace.Files.Count(f => f.Dirty), "Recovery missed new animation files");
                Check(restored.Workspace.Open(sequencePath).Text == author.Workspace.Open(sequencePath).Text, "Recovered sequence differs");
                author.Save();
                Check(File.Exists(sequencePath) && File.Exists(cssPath), "Save did not publish the whole workset");
                Check(SequenceDefinition.Parse(File.ReadAllText(sequencePath)).Tracks.Count == 1, "Saved sequence cannot reopen");
                Check(File.ReadAllText(path) == author.File.Text, "Saved HTML differs");
                UruiEditorWindow.Open();
                UruiAnimationWindow.Open();
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/animation-authoring.json", "{\"passed\":" + count + ",\"failed\":0}");
                Debug.Log("URUI Animation Authoring PASS " + count);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Directory.CreateDirectory("Evidence"); File.WriteAllText("Evidence/animation-authoring-failure.txt", error.ToString()); Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
