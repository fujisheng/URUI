using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Authoring.Preview;
using URUI.Authoring.Windows;
using UnityEngine.UIElements;

namespace URUI.Authoring.Tests
{
    public static class WorkbenchValidation
    {
        public static void Run()
        {
            try
            {
                EmptySaveKeepsOpenPagePrompt();
                InsertReturnsNewIdentity();
                DiscardSwitchRestoresWholeWorkset();
                ClipboardRemapsConflictingIdsAndReferences();
                QuickAddUsesOuterButtonAsAnchor();
                PreviewUsesProjectDesignSize();
                Debug.Log("Authoring workbench checks: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void DiscardSwitchRestoresWholeWorkset()
        {
            const string original = "<!doctype html><html><head></head><body><button>Original</button></body></html>";
            string folder = Path.GetFullPath("Assets/WorkbenchSwitch");
            Directory.CreateDirectory(folder);
            string first = Path.Combine(folder, "first.html");
            string second = Path.Combine(folder, "second.html");
            string styles = Path.Combine(folder, "first.css");
            string generated = Path.Combine(folder, "generated.css");
            File.WriteAllText(first, original);
            File.WriteAllText(second, original);
            File.WriteAllText(styles, "body { width:100px; }");
            var session = new AuthoringSession(Directory.GetCurrentDirectory());
            session.Open(first);
            session.EditFile(first, original.Replace("Original", "Local"), session.Revision);
            session.EditFile(styles, "body { width:200px; }", session.Revision);
            session.Workspace.Create(generated, "body { color:red; }", session.Revision);
            var open = typeof(AuthoringSession).GetMethod("Open", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool) }, null);
            Check(open != null, "Page switch must offer a whole-workset discard.");
            try
            {
                open.Invoke(session, new object[] { Path.Combine(folder, "missing.html"), true });
                throw new Exception("A missing target was opened.");
            }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is IOException)
            {
                Check(session.Workspace.Files.Count(file => file.Dirty) == 3, "A failed page switch must preserve current edits.");
            }
            open.Invoke(session, new object[] { second, true });
            Check(session.File.Path == second && !session.Workspace.Files.Any(file => file.Dirty), "Discard and switch must restore the whole workset.");
            Check(session.Workspace.Open(first).Text == original && session.Workspace.Open(styles).Text == "body { width:100px; }", "Discard must restore the saved HTML and CSS.");
            Check(!File.Exists(generated) && session.Workspace.Open(generated).Text == null, "Discard must drop an unsaved generated file.");
            Check(!session.Workspace.CanUndo && !session.Workspace.CanRedo, "Discarded changes must not come back through Undo or Redo.");
        }

        static void ClipboardRemapsConflictingIdsAndReferences()
        {
            var document = SourceDocument.Parse("<body><input id='field'><span id='field-copy'></span></body>");
            const string snippet = "<label for='field'>Value</label><input id='field' aria-describedby='field remote'>";
            var placement = typeof(UruiEditorWindow).Assembly.GetType("URUI.Authoring.Windows.NodePlacement");
            var copy = placement.GetMethod("CopyWithUniqueIds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Check(copy != null, "Named controls need a safe copy operation.");
            var fragment = SourceDocument.Parse((string)copy.Invoke(null, new object[] { snippet, document }));
            Check(fragment.Roots[0].Attribute("for") == "field-copy-2", "Copied label should refer to the copied field.");
            Check(fragment.Roots[1].Attribute("id") == "field-copy-2" &&
                fragment.Roots[1].Attribute("aria-describedby") == "field-copy-2 remote", "Copy must update internal references and preserve unrelated targets.");
        }

        static void QuickAddUsesOuterButtonAsAnchor()
        {
            var document = SourceDocument.Parse("<body><header><button id='back'><div><span id='title'>Page</span></div></button></header></body>");
            var library = typeof(UruiEditorWindow).Assembly.GetType("URUI.Authoring.Windows.WorkbenchLibrary");
            var anchor = library.GetMethod("SelectedAnchor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var title = document.Nodes.Single(node => node.Attribute("id") == "title");
            var button = document.Nodes.Single(node => node.Attribute("id") == "back");
            Check(anchor.Invoke(null, new object[] { title }) == button,
                "Quick add from a selected button label must use the outer button as the anchor.");
            Check(anchor.Invoke(null, new object[] { button.Parent }) == button.Parent,
                "Quick add from a normal container must keep that container as the anchor.");
        }

        static void PreviewUsesProjectDesignSize()
        {
            string root = Path.GetFullPath("Evidence/WorkbenchViewportProject");
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            string path = Path.Combine(root, "ProjectSettings/URUIAuthoring.json");
            File.WriteAllText(path, "{\"width\":2532,\"height\":1170}");
            using var preview = new PreviewSession(new AuthoringSession(root), root);
            Check(preview.Width == 2532 && preview.Height == 1170, "The first preview must use the project design size.");
        }

        static void EmptySaveKeepsOpenPagePrompt()
        {
            if (AuthoringEnvironment.Author.File != null)
                return;
            string before = AuthoringEnvironment.Status;
            SaveWorksetWindow.OpenOrSave();
            Check(AuthoringEnvironment.Status == before, "Saving an empty editor must not report a save.");
        }

        public static void RunInteractive()
        {
            try
            {
                const string folder = "Assets/Resources/URUI/WorkbenchCheck";
                Directory.CreateDirectory(folder);
                File.WriteAllText(folder + "/Layout.css", ".frame { display:flex; flex-direction:column; padding:15px; width:400px; height:330px; background-color:#22364d; } .start { width:160px; height:48px; background-color:#50708e; color:white; }");
                File.WriteAllText(folder + "/Layout.html", "<!doctype html><html><head><link rel='stylesheet' href='/urui/base.css'><link rel='stylesheet' href='Layout.css'></head><body><div class='frame'><button class='start'>Start</button><div class='slot'>Inventory slot</div></div></body></html>");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AuthoringEnvironment.Open(folder + "/Layout.html");
                UruiEditorWindow.Open();
                var workbench = UnityEngine.Resources.FindObjectsOfTypeAll<UruiEditorWindow>().Single();
                Directory.CreateDirectory("Evidence");
                EditorApplication.delayCall += () => CheckSurface(workbench);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void CheckSurface(UruiEditorWindow workbench)
        {
            try
            {
                var root = workbench.rootVisualElement;
                Check(root.Q("urui-viewport") != null && root.Q<TreeView>("urui-node-tree") == null && root.Q("urui-inspector") == null,
                    "The canvas must use the separate docked hierarchy and inspector.");
                var windows = UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>();
                var hierarchy = windows.Single(window => window.GetType().Name == "UruiRuntimeHierarchyWindow").rootVisualElement;
                var inspector = windows.Single(window => window.GetType().Name == "UruiRuntimeInspectorWindow").rootVisualElement;
                Check(hierarchy.Q<TreeView>("urui-node-tree") != null && inspector.Q("urui-inspector") != null,
                    "Edit Mode must use the same hierarchy and inspector windows as Play Mode.");
                var drawer = root.Q("urui-library-drawer");
                var libraryToggle = root.Q<ToolbarToggle>("urui-toggle-library");
                Check(hierarchy.Q("urui-library-drawer") == null, "The hierarchy must devote its space to layers and style sheets.");
                Check(drawer != null && libraryToggle != null && drawer.parent == root.Q("urui-viewport").parent,
                    "The editor must place the drawer beside the canvas.");
                libraryToggle.value = false;
                Check(drawer.style.display == DisplayStyle.None, "Collapsing the drawer must free its layout space.");
                libraryToggle.value = true;
                Check(drawer.style.display == DisplayStyle.Flex, "The toolbar must reopen the drawer.");
                foreach (string kind in new[] { "panel", "text", "image", "button", "input", "textarea", "toggle", "radio", "slider", "select", "progress" })
                    Check(drawer.Q("urui-control-" + kind)?.Q("urui-control-preview")?.childCount > 0,
                        "Every standard card must have a visual preview: " + kind);
                var session = AuthoringEnvironment.Author;
                string path = session.File.Path;
                var original = File.ReadAllText(path);
                var start = session.Model.Nodes.Single(node => node.Attribute("class") == "start");
                var asset = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/URUI/WorkbenchCheck/Layout.html");
                Check(asset, "The fixture must provide a native Inspector target.");
                var proxy = ElementSelectionProxy.ForAuthor(start.Key);
                Selection.activeObject = asset;
                AuthoringEnvironment.Select(proxy);
                Check(Selection.objects.Length == 0, "Selecting an author node must clear the native Inspector.");
                Check(!ActiveEditorTracker.sharedTracker.isLocked && ActiveEditorTracker.sharedTracker.activeEditors.Length == 0,
                    "The default Inspector must drop its previous asset editors.");
                Selection.activeObject = asset;
                AuthoringEnvironment.Select(proxy);
                Check(Selection.objects.Length == 0, "Reselecting the same author node must clear the native Inspector.");
                Check(AuthoringEnvironment.Preview.Document.SourceUrl == "/URUI/WorkbenchCheck/Layout.html", "The preview must use the game compiler's canonical mount.");
                Check(AuthoringEnvironment.Preview.Document.Q(className: "start").Layout.width == 160, "Relative stylesheet did not resolve.");
                File.WriteAllText("Evidence/workbench-open.txt", "Workbench is ready. The original source is at " + path + "\nSource bytes: " + original.Length);
                Debug.Log("Authoring workbench graphical fixture ready.");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void InsertReturnsNewIdentity()
        {
            const string markup = "<!doctype html><html><head></head><body><div><button>同级</button><button>同级</button></div></body></html>";
            string path = Path.GetFullPath("Assets/workbench-identity.html");
            File.WriteAllText(path, markup);
            var session = new AuthoringSession(Directory.GetCurrentDirectory());
            session.Open(path);
            var parent = session.Model.Nodes.Single(node => node.Tag == "div");
            string parentIdentity = session.NodeIdentity(parent.Key);
            string first = session.NodeIdentity(parent.Children[0].Key);
            string second = session.NodeIdentity(parent.Children[1].Key);
            long before = session.Revision;
            string inserted = session.InsertNode(parentIdentity, 1, "<button>同级</button>", before);
            Check(inserted != first && inserted != second, "Identical siblings must keep their own identities.");
            Check(session.Model.Find(session.ResolveIdentity(inserted)).Parent.Children[1].Key == session.ResolveIdentity(inserted), "Inserted node must be at the requested position.");
            Check(session.ResolveIdentity(second) == session.Model.Find(session.ResolveIdentity(parentIdentity)).Children[2].Key, "The previous second sibling moved to index 2.");
            session.Undo();
            Check(session.ResolveIdentity(inserted) == null && session.File.Text == markup, "Undo must recover the original source and invalidate the new node.");
            session.Redo();
            Check(session.ResolveIdentity(inserted) != null, "Redo must recover the same inserted identity.");
            Check(File.ReadAllText(path) == markup, "Working copy edits cannot change disk before Save.");
            long revision = session.Revision;
            try
            {
                session.InsertNode(parentIdentity, -1, "<button>过期</button>", before);
                throw new Exception("A stale revision was accepted.");
            }
            catch (InvalidOperationException)
            {
                Check(session.Revision == revision, "Rejecting a stale write must leave revision unchanged.");
            }
            var insertMany = typeof(AuthoringSession).GetMethod("InsertNodes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int history = session.Workspace.HistoryPosition;
            var pasted = (string[])insertMany.Invoke(session, new object[] { parentIdentity, 1, "<span>1</span><span>2</span>", session.Revision, false });
            Check(pasted.Length == 2 && pasted[0] != pasted[1], "Multi-node paste must return each inserted identity.");
            Check(session.Workspace.HistoryPosition == history + 1, "Multi-node paste must record one transaction.");
            session.Undo();
            Check(pasted.All(id => session.ResolveIdentity(id) == null) && session.ResolveIdentity(inserted) != null, "Undo must remove only the pasted nodes.");
        }

        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
