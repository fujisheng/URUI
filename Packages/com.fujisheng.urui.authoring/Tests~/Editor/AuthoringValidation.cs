using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Editor.Inspection;
using URUI.Authoring.Windows;

namespace URUI.Authoring.Tests
{
    public static class AuthoringValidation
    {
        static int assertions;
        static void Equal<T>(T expected, T actual)
        {
            assertions++;
            if (!Equals(expected, actual))
            {
                throw new Exception($"Expected [{expected}], got [{actual}]");
            }
        }
        static void Rejected(Action action)
        {
            assertions++;
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                return;
            }
            throw new Exception("Expected rejection");
        }

        public static void Run()
        {
            var errors = new System.Collections.Generic.List<string>();
            Application.LogCallback captureError = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception)
                {
                    errors.Add(message);
                }
            };
            Application.logMessageReceived += captureError;
            try
            {
                RuntimeInspectionBridge.EnableSourceMapping();
                Directory.CreateDirectory("Assets/AuthoringChecks");
                string sourcePath = Path.GetFullPath("Assets/AuthoringChecks/page.html");
                string authored = "<!doctype html><html><head></head><body><div id='direct'>hello😀</div></body></html>";
                File.WriteAllText(sourcePath, authored);
                var author = new AuthoringSession(Directory.GetCurrentDirectory());
                author.Open(sourcePath);
                var sourceNode = System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Tag == "div");
                author.Set(sourceNode.Key, "style.width", "75px", author.Revision);
                Equal(authored, File.ReadAllText(sourcePath));
                author.Undo();
                Equal(authored, author.File.Text);
                author.Redo();
                Equal(true, author.File.Text.Contains("75px"));
                author.Undo();
                author.EditFile(sourcePath, author.File.Text.Replace("</body>", "<section><span>second</span></section></body>"), author.Revision);
                string beforeRemove = author.File.Text;
                string sectionKey = System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Tag == "section").Key;
                string spanKey = System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Tag == "span").Key;
                Equal("<section><span>second</span></section>", author.CopyNodes(new[] { spanKey, sectionKey }));
                author.RemoveNodes(new[] { sourceNode.Key, sectionKey, spanKey, sourceNode.Key }, author.Revision);
                Equal(true, author.File.Text.Contains("<body></body>"));
                author.Undo();
                Equal(beforeRemove, author.File.Text);
                author.Undo();
                author.Set(sourceNode.Key, "text", "unsaved recovered 😀", author.Revision);
                string recoveryPath = Path.GetFullPath("Library/URUI/Authoring/check-recovery.json");
                SourceRecovery.Save(author, recoveryPath);
                var restored = new AuthoringSession(Directory.GetCurrentDirectory());
                SourceRecovery.Restore(restored, recoveryPath);
                Equal(true, restored.File.Dirty);
                Equal(true, restored.File.Text.Contains("unsaved recovered 😀"));
                Equal(authored, File.ReadAllText(sourcePath));
                File.Delete(recoveryPath);
                author.Undo();
                using (var preview = RuntimeInspectionBridge.CreatePreview(480, 320))
                {
                    var doc = preview.Document;
                    doc.LoadHtml(author.File.Text, "Assets/AuthoringChecks/page.html");
                    var sourceMap = new SourceMap(doc, author.Model);
                    var direct = RuntimeInspectionBridge.Inspect(doc.Q("direct"));
                    Equal(sourceNode.Key, sourceMap.Key(direct));
                    var generated = doc.CreateElement();
                    generated.Name = "direct";
                    doc.Body.Add(generated);
                    Equal(null, sourceMap.Key(RuntimeInspectionBridge.Inspect(generated)));
                    author.Apply(new[] { new LiveEdit(direct, "style.width", "42px") }, sourceMap, author.Revision);
                    Equal(true, author.File.Text.Contains("42px"));
                    author.Save();
                    Equal(true, File.ReadAllText(sourcePath).Contains("42px"));
                    doc.LoadHtml("<!doctype html><html><head><style>body{margin:0;} #same{width:100px;height:40px;background-color:red;}</style></head><body><div id='same'>中文😀</div></body></html>", "ui/test.html");
                    RuntimeInspectionBridge.Refresh(doc);
                    var node = RuntimeInspectionBridge.Inspect(doc.Q("same"));
                    Equal(node.Id, RuntimeInspectionBridge.Inspect(doc.Q("same")).Id);
                    Equal("100px", node.Computed("width"));
                    using (var live = new LiveSession())
                    {
                        live.Set(node, "style.width", "80px", live.Revision);
                        RuntimeInspectionBridge.Refresh(doc);
                        Equal(80f, doc.Q("same").Layout.width);
                        live.Undo();
                        Equal(null, node.Read("style.width"));
                        live.Redo();
                        Equal("80px", node.Read("style.width"));
                        live.Reset();
                        Equal(null, node.Read("style.width"));
                        var proxy = ElementSelectionProxy.ForLive(node, live);
                        Equal(proxy, ElementSelectionProxy.ForLive(node, live));
                        var another = ElementSelectionProxy.ForLive(RuntimeInspectionBridge.Root(doc), live);
                        Equal(node.Id, proxy.Node.Id);
                        Equal(false, proxy == another);
                        proxy.Set("style.width", "61px");
                        UnityEditor.Undo.PerformUndo();
                        Equal(null, node.Read("style.width"));
                        using (var pixels = preview.Capture())
                        {
                            Equal(true, pixels.Texture.GetPixel(10, 310).r > .9f);
                            Equal(true, pixels.Texture.GetPixel(10, 310).g < .1f);
                            Directory.CreateDirectory("Evidence");
                            File.WriteAllBytes("Evidence/preview.png", pixels.Texture.EncodeToPNG());
                        }
                        const string imagePath = "Assets/AuthoringChecks/live-image.asset";
                        AssetDatabase.DeleteAsset(imagePath);
                        var image = new Texture2D(1, 1);
                        image.SetPixel(0, 0, Color.blue);
                        image.Apply();
                        AssetDatabase.CreateAsset(image, imagePath);
                        var imageElement = doc.CreateElement("img");
                        imageElement.Style.Set("position", "absolute");
                        imageElement.Style.Set("left", "200px");
                        imageElement.Style.Set("top", "0px");
                        imageElement.Style.Set("width", "30px");
                        imageElement.Style.Set("height", "30px");
                        doc.Body.Add(imageElement);
                        var inspectedImage = RuntimeInspectionBridge.Inspect(imageElement);
                        live.Set(inspectedImage, "image", imagePath, live.Revision);
                        using (var pixels = preview.Capture())
                        {
                            Equal(true, pixels.Texture.GetPixel(210, 310).b > .9f);
                        }
                        live.Undo();
                        Equal(null, imageElement.GetAttribute("src"));
                        live.Set(inspectedImage, "image", imagePath, live.Revision);
                        live.Set(node, "style.width", "83px", live.Revision);
                        live.Set(node, "text", "temporary", live.Revision);
                        var structured = doc.CreateElement("span");
                        structured.Text = "game-owned";
                        doc.Q("same").Add(structured);
                        live.Reset();
                        Equal(null, imageElement.GetAttribute("src"));
                        Equal(null, node.Read("style.width"));
                        Equal(true, structured.IsValid);
                        Equal(false, live.CanUndo);
                        Rejected(() => live.Set(node, "text", "<markup>", -1));
                        var old = node;
                        doc.LoadHtml("<!doctype html><html><head></head><body><div id='same'>new</div></body></html>", "ui/test.html");
                        Equal(false, old.IsValid);
                        Equal(false, proxy.IsTargetValid);
                        Rejected(() => live.Set(old, "text", "wrong node", live.Revision));
                        Equal("new", doc.Q("same").Text);
                    }
                    preview.Render();
                    using (var pixels = preview.Capture())
                    {
                        Equal(480, pixels.Texture.width);
                    }
                }
                AuthoringEnvironment.Open(sourcePath);
                VerifyIdentityAndDocumentSwitch();
                VerifyAuthoredControlValues();
                AuthoringEnvironment.Open(sourcePath);
                AuthoringEnvironment.Preview.Resize(480, 320);
                var editable = System.Linq.Enumerable.Single(AuthoringEnvironment.Author.Model.Nodes, n => n.Tag == "div");
                var authoredProxy = ElementSelectionProxy.ForAuthor(editable.Key);
                authoredProxy.Set("style.position", "absolute");
                authoredProxy.Set("style.left", "10px");
                authoredProxy.Set("style.top", "20px");
                authoredProxy.Set("style.width", "100px");
                authoredProxy.Set("style.height", "40px");
                var beforeAnchor = authoredProxy.Node.Bounds;
                var anchorEdits = RectCommands.AnchorEdits(authoredProxy.Node, authoredProxy.SourceKey, 1, 1, false, false);
                AuthoringEnvironment.Author.Execute(anchorEdits, AuthoringEnvironment.Author.Revision);
                Equal(beforeAnchor, authoredProxy.Node.Bounds);
                var stretchEdits = RectCommands.AnchorEdits(authoredProxy.Node, authoredProxy.SourceKey, 0, 0, true, true);
                AuthoringEnvironment.Author.Execute(stretchEdits, AuthoringEnvironment.Author.Revision);
                Equal(beforeAnchor, authoredProxy.Node.Bounds);
                Equal(true, System.Linq.Enumerable.Any(PropertySchema.All, p => p.Name == "style.width"));
                var inspector = UnityEditor.Editor.CreateEditor(authoredProxy);
                Equal(true, inspector is ElementInspector);
                UnityEngine.Object.DestroyImmediate(inspector);
                string cssPath = Path.GetFullPath("Assets/AuthoringChecks/shared.css");
                File.WriteAllText(cssPath, "#direct { background-color:red; }");
                string headKey = null;
                foreach (var node in AuthoringEnvironment.Author.Model.Nodes)
                {
                    if (node.Tag == "head")
                    {
                        headKey = node.Key;
                    }
                }
                AuthoringEnvironment.Author.Execute(new[] { new AuthoringEdit {
Operation = "insert",
Parent = headKey,
Value = "<link rel=\"stylesheet\" href=\"shared.css\">"
} }, AuthoringEnvironment.Author.Revision);
                var rules = URUI.Authoring.Source.CssSource.Rules(AuthoringEnvironment.Author.Workspace.Open(cssPath).Text);
                long beforeCss = AuthoringEnvironment.Author.Revision;
                AuthoringEnvironment.Author.SetSharedRule(cssPath, rules[0].Start, "background-color", "blue", beforeCss);
                Equal(true, AuthoringEnvironment.Author.Revision > beforeCss);
                Equal("#direct { background-color:red; }", File.ReadAllText(cssPath));
                Equal(Color.blue, AuthoringEnvironment.Preview.Document.Styles.GetComputed(AuthoringEnvironment.Preview.Document.Q("direct")).GetColor("background-color"));
                assertions += AgentValidation.RunSync();
                AuthoringEnvironment.Preview.Dispose();
                var effectChecks = typeof(AuthoringValidation).Assembly.GetType("URUI.Authoring.Tests.EffectValidation");
                if (effectChecks != null)
                {
                    assertions += (int)effectChecks.GetMethod("Run").Invoke(null, null);
                }
                Equal(0, errors.Count);
                ElementSelectionProxy.Clear();
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/authoring.json", "{\"passed\":" + assertions + ",\"failed\":0}");
                Debug.Log("URUI Authoring: PASS " + assertions);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/authoring-failure.txt", error.ToString());
                EditorApplication.Exit(1);
            }
            finally
            {
                Application.logMessageReceived -= captureError;
            }
        }
        static void VerifyIdentityAndDocumentSwitch()
        {
            const string path = "Assets/AuthoringChecks/identity.html";
            const string original = "<!doctype html><html><head></head><body><div id='a'>A</div><div id='b'>B</div></body></html>";
            File.WriteAllText(path, original);
            AuthoringEnvironment.Open(path);
            var author = AuthoringEnvironment.Author;
            string a = System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Attribute("id") == "a").Key;
            string body = System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Tag == "body").Key;
            var locked = ElementSelectionProxy.ForAuthor(a);
            author.Execute(new[] { new AuthoringEdit {
Operation = "move",
Node = a,
Parent = body
} }, author.Revision);
            Equal(true, locked.IsTargetValid);
            Equal("a", locked.SourceNode.Attribute("id"));
            var moved = locked;
            author.Undo();
            Equal(true, moved.IsTargetValid);
            var undone = locked;
            author.Redo();
            Equal(true, undone.IsTargetValid);
            var beforeRaw = ElementSelectionProxy.ForAuthor(a);
            author.EditFile(path, original, author.Revision);
            Equal(false, beforeRaw.IsTargetValid);
            const string second = "Assets/AuthoringChecks/second.html";
            File.WriteAllText(second, original.Replace("id='a'", "id='other'"));
            author.Save();
            AuthoringEnvironment.Open(second);
            AuthoringEnvironment.Open(path);
            Equal(true, AuthoringEnvironment.Preview.Document.Q("a") != null);
            Equal(true, AuthoringEnvironment.Preview.Document.Q("other") == null);
            string currentA = System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Attribute("id") == "a").Key;
            Equal(currentA, AuthoringEnvironment.Preview.Key(RuntimeInspectionBridge.Inspect(AuthoringEnvironment.Preview.Document.Q("a"))));
            var undoFailures = new System.Collections.Generic.List<string>();
            foreach (string operation in new[] { "CreateNode", "RenameNode", "MoveNode" })
            {
                SessionUndoBridge.Clear();
                Undo.ClearAll();
                a = System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Attribute("id") == "a").Key;
                body = System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Tag == "body").Key;
                Selection.activeObject = ElementSelectionProxy.ForAuthor(body);
                string before = author.File.Text;
                object[] arguments = operation == "CreateNode" ? new object[] { "span" } : operation == "RenameNode" ? new object[] { a, "renamed" } : new object[] { a, body, -1 };
                typeof(HierarchyWindow).GetMethod(operation, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, arguments);
                string after = author.File.Text;
                Equal(false, before == after);
                Undo.PerformUndo();
                if (author.File.Text != before)
                {
                    undoFailures.Add(operation);
                    author.EditFile(path, before, author.Revision);
                }
                else
                {
                    Undo.PerformRedo();
                    Equal(after, author.File.Text);
                    Undo.PerformUndo();
                }
            }
            Equal("", string.Join(",", undoFailures));
            SessionUndoBridge.Clear();
            Undo.ClearAll();
        }
        static void VerifyAuthoredControlValues()
        {
            const string path = "Assets/AuthoringChecks/control-values.html";
            File.WriteAllText(path, "<!doctype html><html><head></head><body><input id='check' type='checkbox' checked><textarea id='message'>中文😀</textarea></body></html>");
            AuthoringEnvironment.Open(path);
            var author = AuthoringEnvironment.Author;
            var checkbox = ElementSelectionProxy.ForAuthor(System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Attribute("id") == "check").Key);
            var textarea = ElementSelectionProxy.ForAuthor(System.Linq.Enumerable.Single(author.Model.Nodes, n => n.Attribute("id") == "message").Key);
            Equal("true", checkbox.Read("value"));
            Equal("中文😀", textarea.Read("value"));
            checkbox.Set("value", "false");
            textarea.Set("value", "draft 😀");
            Equal(null, checkbox.SourceNode.Attribute("checked"));
            Equal("draft 😀", textarea.Read("value"));
            PropertySchema.Require("attr.placeholder");
            PropertySchema.Require("attr.min");
            PropertySchema.Require("attr.max");
            PropertySchema.Require("attr.step");
            Equal(true, File.ReadAllText(path).Contains("checked"));
            Equal(false, File.ReadAllText(path).Contains("draft"));
            author.Undo();
            author.Undo();
            SessionUndoBridge.Clear();
        }
    }
}
