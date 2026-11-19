using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Preview;
using URUI.Authoring.Sessions;

namespace URUI.Authoring.Tests
{
    public static class PreviewResourcesValidation
    {
        public static void Run()
        {
            var failures = new List<string>();
            Directory.CreateDirectory("Assets/Resources/URUI/PreviewChecks");
            File.WriteAllText("Assets/Resources/URUI/PreviewChecks/style.css", "#visible{width:80px;height:40px;background-color:red;}");
            const string fragment = "<button class='template'>Template</button>";
            File.WriteAllText("Assets/Resources/URUI/PreviewChecks/fragment.html", fragment);
            File.WriteAllText("Assets/preview-resources.html", "<!doctype html><html><head><link rel='stylesheet' href='/urui/base.css'><link rel='stylesheet' href='/urui/controls.css'><link rel='stylesheet' href='/urui/behaviors.css'><link rel='stylesheet' href='/URUI/PreviewChecks/style.css'><link rel='urui-fragment' href='/URUI/PreviewChecks/fragment.html'></head><body><div id='visible'></div></body></html>");
            File.WriteAllText("Assets/preview-missing.html", "<!doctype html><html><head><link rel='stylesheet' href='missing-preview.css'></head><body></body></html>");
            const string independent = "<!doctype html><html><head></head><body>Independent</body></html>";
            File.WriteAllText("Assets/independent-preview.html", independent);
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.blue);
            texture.Apply();
            File.WriteAllBytes("Assets/Resources/URUI/PreviewChecks/图 像.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            File.WriteAllText("Assets/preview-image.html", "<!doctype html><html><head><style>body{margin:0;}</style></head><body><img style='width:64px;height:64px;' src='/Assets/Resources/URUI/PreviewChecks/图 像.png'></body></html>");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            try
            {
                var author = new AuthoringSession(Directory.GetCurrentDirectory());
                author.Open("Assets/preview-resources.html");
                using (var preview = new PreviewSession(author, Directory.GetCurrentDirectory()))
                {
                    preview.Refresh();
                    if (!preview.Document.CompiledSource)
                    {
                        throw new Exception("Authoring preview must load the current compiled working copy.");
                    }
                    if (!preview.Document.IsLoaded || preview.Document.Q("visible").Layout.width != 80)
                    {
                        throw new Exception("Resources stylesheet did not reach the preview document.");
                    }
                    var css = author.Workspace.Open("Assets/Resources/URUI/PreviewChecks/style.css");
                    author.Workspace.Edit(css.Path, css.Text.Replace("80px", "95px"), css.Revision);
                    preview.Refresh();
                    if (preview.Document.Q("visible").Layout.width != 95)
                    {
                        throw new Exception("Resources URL ignored the unsaved stylesheet working copy.");
                    }
                    string recovery = Path.GetFullPath("Library/URUI/Authoring/preview-check-recovery.json");
                    SourceRecovery.Save(author, recovery);
                    var restored = new AuthoringSession(Directory.GetCurrentDirectory());
                    SourceRecovery.Restore(restored, recovery);
                    if (restored.Workspace.Open("Assets/Resources/URUI/PreviewChecks/fragment.html").Text != fragment)
                    {
                        throw new Exception("Linked fragment changed during recovery.");
                    }
                    bool rejected = false;
                    try
                    {
                        restored.EditFile(restored.File.Path, "<div>not a page</div>", restored.Revision);
                    }
                    catch (InvalidOperationException)
                    {
                        rejected = true;
                    }
                    if (!rejected)
                    {
                        throw new Exception("A fragment was accepted as the active full document.");
                    }
                }
            }
            catch (Exception error)
            {
                failures.Add("resource-urls: " + error);
            }
            try
            {
                var author = new AuthoringSession(Directory.GetCurrentDirectory());
                author.Open("Assets/preview-image.html");
                using (var preview = new PreviewSession(author, Directory.GetCurrentDirectory()))
                {
                    string path = preview.Capture(Path.GetFullPath("Evidence/unicode-image.png"));
                    var pixels = new Texture2D(2, 2);
                    try
                    {
                        if (!pixels.LoadImage(File.ReadAllBytes(path)))
                        {
                            throw new Exception("PNG capture could not be read.");
                        }
                        var pixel = pixels.GetPixel(16, pixels.height - 16);
                        if (pixel.b < .7f || pixel.r > .2f || pixel.g > .2f)
                        {
                            throw new Exception("Encoded Unicode image URL was not loaded; pixel=" + pixel);
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(pixels);
                    }
                }
            }
            catch (Exception error)
            {
                failures.Add("unicode-image: " + error);
            }
            try
            {
                var author = new AuthoringSession(Directory.GetCurrentDirectory());
                author.Open("Assets/preview-resources.html");
                bool rejected = false;
                try
                {
                    author.EditFile("Assets/independent-preview.html", "<div>not a document</div>", author.Revision);
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
                if (!rejected || author.Workspace.Open("Assets/independent-preview.html").Text != independent)
                {
                    throw new Exception("An unrelated full document could be replaced by a fragment.");
                }
            }
            catch (Exception error)
            {
                failures.Add("independent-document: " + error);
            }
            try
            {
                AuthoringEnvironment.Open("Assets/preview-resources.html");
                string previousKey = System.Linq.Enumerable.First(AuthoringEnvironment.Author.Model.Nodes, n => n.Attribute("id") == "visible").Key;
                AuthoringEnvironment.Open("Assets/preview-missing.html");
                if (!AuthoringEnvironment.Status.Contains("missing-preview.css"))
                {
                    throw new Exception("Preview failure was hidden by the successful source-open status: " + AuthoringEnvironment.Status);
                }
                if (AuthoringEnvironment.Preview.Node(previousKey) != null)
                {
                    throw new Exception("Failed page switch left the previous page's nodes editable.");
                }
            }
            catch (Exception error)
            {
                failures.Add("failure-status: " + error);
            }
            finally
            {
                AuthoringEnvironment.Preview?.Dispose();
            }
            Directory.CreateDirectory("Evidence");
            File.WriteAllText("Evidence/preview-resources.txt", failures.Count == 0 ? "PASS: Resources URLs, unsaved CSS and preview failure feedback" : string.Join("\n", failures));
            Debug.Log("Preview resource checks: " + (failures.Count == 0 ? "PASS" : string.Join("\n", failures)));
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
    }
}
