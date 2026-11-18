using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Animation;
using URUI.Authoring.Agent;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;
using URUI.Editor.Inspection;
namespace URUI.Authoring.Tests
{
    public static class ReviewValidation
    {
        static readonly List<string> failures = new List<string>(); static int passed;
        static void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
        static bool Reject(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException) { return true; }
            return false;
        }
        static void Case(string name, Action action)
        {
            try
            {
                action();
                passed++;
            }
            catch (Exception error) { failures.Add(name + ": " + error); }
        }
        public static void Run()
        {
            Directory.CreateDirectory("Assets/ReviewChecks");
            Directory.CreateDirectory("Evidence");
            Case("relative-layout", RelativeLayout);
            Case("total-key-limit", TotalLimit);
            Case("paused-binding-invalidated", BindingInvalidated);
            Case("keyframe-page-scope", PageScope);
            Case("stale-page-request", StaleRequest);
            Case("nonrecord-preview", Nonrecord);
            Case("gesture-context", GestureContext);
            Case("invalid-sequence", InvalidSequence);
            Case("muted-particle-edit", MutedParticle);
            File.WriteAllText("Evidence/review-cases.txt", "passed=" + passed + "\n" + string.Join("\n", failures));
            Debug.Log("URUI Review cases passed=" + passed + " failed=" + failures.Count);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
        static void RelativeLayout()
        {
            using (var host = RuntimeInspectionBridge.CreatePreview(400, 200))
            {
                host.Document.LoadHtml("<!doctype html><html><head><style>#p{width:100px;height:100px;}#c{width:10px;height:10px;}@keyframes parent{from{width:100px;}to{width:300px;}}@keyframes child{from{width:10px;transform:translateX(0px);}to{width:100%;transform:translateX(100%);}}</style></head><body><div id='p'><div id='c'></div></div></body></html>", "ui/relative.html");
                host.Render();
                var definition = new SequenceDefinition { Duration = 1 };
                definition.Tracks.Add(new SequenceTrack { Id = "c", Target = "c", Keyframes = "child", Duration = 1 });
                definition.Tracks.Add(new SequenceTrack { Id = "p", Target = "p", Keyframes = "parent", Duration = 1 });
                using (var player = new SequencePlayer(host.Document.Body, definition))
                    foreach (double time in new[] { .8, .2, .8, .8 })
                    {
                        player.Sample(time);
                        host.Render();
                        double expected = 10 * (1 - time) + (100 + 200 * time) * time;
                        Check(Math.Abs(host.Document.Q("c").Layout.width - expected) < .01, "Mixed units used previous layout at " + time + ": " + host.Document.Q("c").Layout.width + " expected " + expected);
                        Check(Math.Abs(host.Document.Q("c").WorldBound.x - host.Document.Q("p").WorldBound.x - time * expected) < .02, "Mixed transform used stale self geometry");
                    }
            }
        }
        static void TotalLimit()
        {
            var css = new System.Text.StringBuilder();
            foreach (string name in new[] { "a", "b" })
            {
                css.Append("@keyframes " + name + "{");
                for (int i = 0; i < 2049; i++)
                    css.Append((i * 100.0 / 2048).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%{opacity:1;}");
                css.Append('}');
            }
            using (var host = RuntimeInspectionBridge.CreatePreview(200, 100))
            {
                host.Document.LoadHtml("<!doctype html><html><head><style>" + css + "</style></head><body><div id='a'></div><div id='b'></div></body></html>", "ui/limit.html");
                host.Render();
                var definition = new SequenceDefinition { Duration = 1 };
                foreach (string name in new[] { "a", "b" })
                    definition.Tracks.Add(new SequenceTrack { Id = name, Target = name, Keyframes = name, Duration = 1 });
                Check(Reject(() => { using (var player = new SequencePlayer(host.Document.Body, definition)) { } }), "4098 total keys accepted");
            }
        }
        static void BindingInvalidated()
        {
            using (var host = RuntimeInspectionBridge.CreatePreview(200, 100))
            {
                host.Document.LoadHtml("<!doctype html><html><head><style>div{width:30px;height:30px;}@keyframes a{from{opacity:0;}to{opacity:1;}}</style></head><body><div id='root'><div id='a'></div></div><div id='other'></div></body></html>", "ui/binding.html");
                host.Render();
                var definition = new SequenceDefinition { Duration = 1 };
                definition.Tracks.Add(new SequenceTrack { Id = "a", Target = "a", Keyframes = "a", Duration = 1 });
                using (var player = new SequencePlayer(host.Document.Q("root"), definition))
                {
                    player.Sample(.4);
                    host.Document.Q("other").Add(host.Document.Q("a"));
                    host.Render();
                    Check(player.Finished.IsCanceled, "Paused player retained an out-of-root binding");
                    Check(Reject(() => player.Sample(.5)), "Out-of-root sample accepted");
                }
            }
        }
        static string Open(string name, string head = "", string body = "<div id='node' style='width:40px;height:30px;opacity:1;'></div>")
        {
            AnimationEnvironment.Stop();
            var author = AuthoringEnvironment.Author;
            if (author.File != null)
                author.Save();
            string path = "Assets/ReviewChecks/" + name + ".html";
            File.Delete(Path.ChangeExtension(path, ".urui-sequence.json"));
            File.Delete(Path.ChangeExtension(path, ".animations.css"));
            File.WriteAllText(path, "<!doctype html><html><head>" + head + "</head><body>" + body + "</body></html>");
            AuthoringEnvironment.Open(path);
            AnimationEnvironment.EnsureWorkspace();
            return Path.GetFullPath(path);
        }
        static void PageScope()
        {
            File.WriteAllText("Assets/ReviewChecks/A.css", "@keyframes pop{from{opacity:0;}to{opacity:1;}}");
            Open("A", "<link rel='stylesheet' href='A.css'>");
            string path = Open("B", "<style>@keyframes pop{from{opacity:0;}to{opacity:1;}}</style>");
            var definition = new SequenceDefinition();
            definition.Tracks.Add(new SequenceTrack { Id = "t", Target = "node", Keyframes = "pop" });
            string sequence = Path.ChangeExtension(path, ".urui-sequence.json");
            File.WriteAllText(sequence, definition.ToJson());
            var workspace = new AnimationWorkspace(AuthoringEnvironment.Author);
            workspace.Open(sequence);
            string a = AuthoringEnvironment.Author.Workspace.Open("Assets/ReviewChecks/A.css").Text;
            Check(Reject(() => workspace.SetKey("t", .5, "opacity", ".5", workspace.Revision)), "B's inline animation edited unrelated A.css");
            Check(AuthoringEnvironment.Author.Workspace.Open("Assets/ReviewChecks/A.css").Text == a, "A.css changed while editing B");
        }
        static void StaleRequest()
        {
            string a = Open("staleA");
            Open("staleB");
            var author = AuthoringEnvironment.Author;
            long revision = author.Revision;
            string key = author.Model.Nodes.First(n => n.Tag == "div").Key;
            AuthoringEnvironment.Open(a);
            var request = AgentDispatcher.Obj("op", "properties.set", "mode", "authoring", "session", author.Id, "expectedRevision", revision, "target", key, "property", "style.opacity", "value", ".2");
            Check(Reject(() => new AgentDispatcher("review").Execute(request)), "Old B request edited cached A");
        }
        static void Nonrecord()
        {
            Open("nonrecord");
            var author = AuthoringEnvironment.Author;
            var proxy = ElementSelectionProxy.ForAuthor(author.Model.Nodes.First(n => n.Tag == "div").Key);
            string original = author.File.Text;
            long revision = author.Revision;
            UruiEditorWindow.OpenAnimation();
            try
            {
                proxy.Set("style.opacity", ".3");
                Check(author.File.Text == original && author.Revision == revision, "Non-record animation edit changed base source");
                using (var gesture = new CanvasGesture(proxy))
                {
                    var type = typeof(UruiCanvasWindow).Assembly.GetType("URUI.Authoring.Windows.CanvasView");
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var view = Activator.CreateInstance(type, flags, null, new object[] { (Action)(() => { }) }, null);
                    type.GetField("gesture", flags).SetValue(view, gesture);
                    type.GetField("tool", flags).SetValue(view, "Scale");
                    type.GetMethod("Drag", flags).Invoke(view, new object[] { proxy, new Vector2(20, 0), new Vector2(20, 0) });
                    gesture.Commit();
                    Check(Math.Abs(proxy.Node.Bounds.width - 48) < .01, "Flow-node scale did not preview through the canvas adapter");
                    Check(author.File.Text == original, "Animation canvas scale wrote base source");
                    ((IDisposable)view).Dispose();
                }

            }
            finally { AnimationEnvironment.Stop(); EditorWindow.GetWindow<UruiEditorWindow>().Close(); }
        }
        static void GestureContext()
        {
            Open("gesture");
            var author = AuthoringEnvironment.Author;
            var workspace = AnimationEnvironment.Workspace;
            string id = workspace.AddPreset(author.Model.Nodes.First(n => n.Tag == "div").Key, "empty", 0, .3, workspace.Revision);
            AnimationEnvironment.SelectTrack(id);
            AnimationEnvironment.Seek(.1);
            var proxy = (ElementSelectionProxy)typeof(AuthoringEnvironment)
                .GetProperty("ActiveNode", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            AnimationEnvironment.BeginRecording(proxy);
            string original = author.File.Text;
            long revision = author.Revision;
            using (var gesture = new CanvasGesture(proxy))
            {
                gesture.Preview(new[] { new AuthoringEdit { Node = proxy.SourceKey, Property = "style.opacity", Value = ".3" } });
                AnimationEnvironment.EndRecording();
                Check(Reject(gesture.Commit), "Cancelled recording gesture fell back to base source");
            }
            Check(author.Revision == revision && author.File.Text == original, "Gesture context failure published source");
            AnimationEnvironment.Stop();
        }
        static void InvalidSequence()
        {
            Open("invalid");
            var author = AuthoringEnvironment.Author;
            var workspace = new AnimationWorkspace(author);
            string key = author.Model.Nodes.First(n => n.Tag == "div").Key;
            workspace.AddPreset(key, "fade-in", 0, .3, workspace.Revision);
            string original = workspace.Definition.ToJson();
            key = author.Model.Nodes.First(n => n.Tag == "div").Key;
            Check(Reject(() => workspace.AddPreset(key, "fade-in", 0, .3, workspace.Revision)), "Overlapping opacity tracks published");
            Check(original == workspace.Definition.ToJson(), "Failed preset changed sequence");
            Check(Reject(() => workspace.SetTrack(workspace.Definition.Tracks[0].Id, t => t.Target = "missing", workspace.Revision)), "Missing target published");
        }
        static void MutedParticle()
        {
            File.WriteAllText("Assets/ReviewChecks/muted.css", "@keyframes a{from{opacity:0;}to{opacity:1;}}");
            string path = Open("muted", "<link rel='stylesheet' href='muted.css'>");
            var definition = new SequenceDefinition();
            definition.Tracks.Add(new SequenceTrack { Id = "a", Target = "node", Keyframes = "a" });
            definition.Tracks.Add(new SequenceTrack { Id = "p", Target = "absent", Kind = "particles", Source = "missing", Muted = true });
            string sequence = Path.ChangeExtension(path, ".urui-sequence.json");
            File.WriteAllText(sequence, definition.ToJson());
            var workspace = new AnimationWorkspace(AuthoringEnvironment.Author);
            workspace.Open(sequence);
            workspace.SetTrack("a", t => t.Start = .1, workspace.Revision);
            Check(workspace.Definition.Tracks[0].Start == .1, "Muted particle blocked UI edit");
        }
    }
}
