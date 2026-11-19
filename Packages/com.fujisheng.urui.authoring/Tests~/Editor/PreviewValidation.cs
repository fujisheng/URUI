using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;

namespace URUI.Authoring.Tests
{
    public static class PreviewValidation
    {
        [InitializeOnLoadMethod]
        static void ReopenVisualFixture()
        {
            if (Path.GetFileName(Directory.GetCurrentDirectory()) == "AuthoringVisual")
            {
                EditorApplication.delayCall += () =>
                {
                    Open();
                    EditorUtility.FocusProjectWindow();
                };
            }
        }
        [MenuItem("Tools/URUI Validation/Open Authoring Demo")]
        public static void Open()
        {
            string sample = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AuthoringEnvironment).Assembly).resolvedPath, "Samples~/Authoring Demo");
            Directory.CreateDirectory("Assets/AuthoringDemo");
            foreach (string file in Directory.GetFiles(sample))
            {
                if (!file.EndsWith(".meta"))
                {
                    File.Copy(file, "Assets/AuthoringDemo/" + Path.GetFileName(file), true);
                }
            }
            AssetDatabase.Refresh();
            AuthoringEnvironment.Open("Assets/AuthoringDemo/Inventory.html");
            HierarchyWindow.Open();
            CanvasWindow.Open();
            foreach (var node in AuthoringEnvironment.Author.Model.Nodes)
            {
                if (node.Attribute("id") == "item-details")
                {
                    AuthoringEnvironment.Select(ElementSelectionProxy.ForAuthor(node.Key));
                }
            }
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
            Directory.CreateDirectory("Evidence");
            AuthoringEnvironment.Preview.Capture(Path.GetFullPath("Evidence/inventory.png"));
            if (Path.GetFileName(Directory.GetCurrentDirectory()) == "AuthoringVisual")
            {
                URUI.Authoring.Agent.AgentServer.Start();
            }
        }
        public static void RunBatch()
        {
            try
            {
                Open();
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            }
            catch (System.Exception error)
            {
                UnityEngine.Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
    }
}
