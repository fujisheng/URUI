using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;
using URUI.Documents;
using URUI.Editor.Inspection;
using URUI.Hosting;

namespace URUI.Authoring.Tests
{
    [InitializeOnLoad]
    public static class PlayValidation
    {
        const string Prefix = "URUI.Authoring.PlayValidation.", Html = "Assets/AuthoringChecks/live.html", ScenePath = "Assets/AuthoringChecks/live.unity";
        static InspectedNode cleanupWidth, cleanupImage, cleanupText, cleanupAnimation;
        static PlayValidation()
        {
            EditorApplication.playModeStateChanged += State;
        }
        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
            SessionState.SetInt(Prefix + "count", SessionState.GetInt(Prefix + "count", 0) + 1);
        }
        public static void Run()
        {
            Directory.CreateDirectory("Assets/AuthoringChecks");
            Directory.CreateDirectory("Evidence");
            SessionState.SetBool(Prefix + "active", true);
            SessionState.SetInt(Prefix + "count", 0);
            SessionState.SetInt(Prefix + "iteration", 0);
            SessionState.SetInt(Prefix + "originalOptions", (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(Prefix + "originalEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            File.WriteAllText(Html, "<!doctype html><html><head><style>body{margin:0;}#live{position:absolute;left:0px;top:0px;width:100px;height:40px;background-color:red;}@keyframes sequence-live{from{opacity:0;}to{opacity:1;}}</style></head><body><div id='live'>runtime</div><div id='animated' style='width:10px;height:10px;opacity:1;'></div></body></html>");
            AssetDatabase.ImportAsset(Html, ImportAssetOptions.ForceSynchronousImport);
            const string imagePath = "Assets/AuthoringChecks/play-image.asset";
            AssetDatabase.DeleteAsset(imagePath);
            var image = new Texture2D(1, 1);
            image.SetPixel(0, 0, Color.blue);
            image.Apply();
            AssetDatabase.CreateAsset(image, imagePath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Real URUI Page");
            var surface = root.AddComponent<Surface>();
            var document = root.AddComponent<Document>();
            var serialized = new SerializedObject(document);
            serialized.FindProperty("surface").objectReferenceValue = surface;
            serialized.FindProperty("source").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(Html);
            serialized.FindProperty("sourceUrl").stringValue = Html;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            File.WriteAllText("Evidence/play-original.html", File.ReadAllText(Html));
            File.WriteAllText("Evidence/play-original.unity", File.ReadAllText(ScenePath));
            var sequence = new URUI.Animation.SequenceDefinition { Duration = 1 };
            sequence.Tracks.Add(new URUI.Animation.SequenceTrack { Id = "live", Target = "animated", Keyframes = "sequence-live", Duration = 1 });
            File.WriteAllText(Path.ChangeExtension(Html, ".urui-sequence.json"), sequence.ToJson());
            AuthoringEnvironment.Open(Html);
            URUI.Authoring.Animation.AnimationEnvironment.EnsureWorkspace();
            string sourceKey = null;
            foreach (var node in AuthoringEnvironment.Author.Model.Nodes)
                if (node.Attribute("id") == "live")
                    sourceKey = node.Key;
            AuthoringEnvironment.Author.Set(sourceKey, "text", "unsaved author draft", AuthoringEnvironment.Author.Revision);
            StartIteration();
        }
        static void StartIteration()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = SessionState.GetInt(Prefix + "iteration", 0) == 0 ? EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload : EnterPlayModeOptions.None;
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
        static void State(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Prefix + "active", false))
                return;
            try
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                    EditorApplication.delayCall += Exercise;
                else if (state == PlayModeStateChange.EnteredEditMode)
                {
                    EditorApplication.delayCall += () => EditorApplication.delayCall += CheckEditMode;
                }
            }
            catch (Exception error) { Fail(error); }
        }
        static void CheckEditMode()
        {
            try
            {
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
                Check(File.ReadAllText(Html) == File.ReadAllText("Evidence/play-original.html"), "HTML changed without apply");
                Check(File.ReadAllText(ScenePath) == File.ReadAllText("Evidence/play-original.unity"), "Scene source changed during temporary editing");
                if (SessionState.GetString(Prefix + "failure", "").Length != 0)
                {
                    Finish(1);
                    return;
                }
                Check(AuthoringEnvironment.Author.File != null && AuthoringEnvironment.Author.File.Dirty && AuthoringEnvironment.Author.File.Text.Contains("unsaved author draft"), "Unsaved author text was lost during Play/domain reload");
                int iteration = SessionState.GetInt(Prefix + "iteration", 0) + 1;
                SessionState.SetInt(Prefix + "iteration", iteration);
                if (iteration < 2)
                    StartIteration();
                else
                    Finish(0);
            }
            catch (Exception error) { Fail(error); }
        }
        static void Exercise()
        {
            try
            {
                var document = UnityEngine.Object.FindFirstObjectByType<Document>();
                Check(document && document.IsLoaded, "Real scene page did not load");
                Check(document.Q("live").Text == "runtime", "Game loaded unsaved author preview instead of actual source");
                Check(AuthoringEnvironment.Preview == null, "Authoring preview retained Surface during Play");
                AuthoringEnvironment.SetDocument(document);
                Selection.activeGameObject = document.gameObject;
                var node = RuntimeInspectionBridge.Inspect(document.Q("live"));
                var proxy = ElementSelectionProxy.ForLive(node, AuthoringEnvironment.Live);
                AuthoringEnvironment.Select(proxy);
                Check(Selection.activeObject == proxy, "Native selection did not use proxy");
                proxy.Set("style.width", "88px");
                Check(document.Q("live").Layout.width == 88, "Live width did not affect real game node");
                EditorApplication.isPaused = true;
                proxy.Set("style.height", "44px");
                Check(document.Q("live").Layout.height == 44, "Paused layout did not refresh");
                using (var pixels = RuntimeInspectionBridge.CaptureLive(document))
                {
                    File.WriteAllBytes("Evidence/live-" + SessionState.GetInt(Prefix + "iteration", 0) + ".png", pixels.Texture.EncodeToPNG());
                    Check(pixels.Texture.GetPixel(70, pixels.Texture.height - 30).r > .9f, "Live capture did not contain actual rendered UI");
                }
                EditorApplication.isPaused = false;
                // Unity 6000.3 禁止 Play 中保存场景；退出后仍显式保存以检查工具没有留下序列化引用。
                try
                {
                    EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
                }
                catch (InvalidOperationException) { }
                Check(File.ReadAllText(ScenePath) == File.ReadAllText("Evidence/play-original.unity"), "Saving the scene persisted temporary node properties");
                int objects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
                for (int i = 0; i < 100; i++)
                    document.Body.Add(document.CreateElement());
                Check(objects == UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length, "DOM nodes created GameObjects");
                var otherRoot = new GameObject("Second business page");
                var other = otherRoot.AddComponent<Document>();
                other.Surface = document.Surface;
                other.LoadHtml("<!doctype html><html><head><style>@keyframes sequence-live{from{opacity:0;}to{opacity:1;}}</style></head><body><div id='live' style='width:25px;'>other</div><div id='animated' style='opacity:1;width:10px;height:10px;'></div></body></html>", "ui/other.html");
                RuntimeInspectionBridge.Refresh(other);
                bool modeRejected = false;
                try
                {
                    new URUI.Authoring.Agent.AgentDispatcher("play-review").Execute(URUI.Authoring.Agent.AgentDispatcher.Obj("op", "animation.sample", "mode", "authoring", "session", AuthoringEnvironment.Author.Id, "expectedRevision", AuthoringEnvironment.Author.Revision, "time", .2));
                }
                catch (InvalidOperationException) { modeRejected = true; }
                Check(modeRejected, "Authoring-mode animation request controlled a live document");
                URUI.Authoring.Animation.AnimationEnvironment.Seek(.2);
                AuthoringEnvironment.SetDocument(other);
                URUI.Authoring.Animation.AnimationEnvironment.Seek(.8);
                Check(Math.Abs(document.Styles.GetComputed(document.Q("animated")).GetNumber("opacity") - 1) < .003, "Switching Live Document retained old sequence overlay");
                Check(Math.Abs(other.Styles.GetComputed(other.Q("animated")).GetNumber("opacity") - .8) < .003, "Sequence sample edited a previously selected document");
                AuthoringEnvironment.SetDocument(document);
                proxy.Set("style.width", "77px");
                Check(other.Q("live").Layout.width == 25, "Duplicate id edited wrong document");
                document.Q("live").Destroy();
                var recreated = document.CreateElement();
                recreated.Name = "live";
                document.Body.Add(recreated);
                Check(!proxy.IsTargetValid, "Destroyed proxy rebound to same-name node");
                Check(RuntimeInspectionBridge.Inspect(recreated).Id != node.Id, "Recreated node reused old identity");
                var widthTarget = document.CreateElement();
                document.Body.Add(widthTarget);
                cleanupWidth = RuntimeInspectionBridge.Inspect(widthTarget);
                var imageTarget = document.CreateElement("img");
                document.Body.Add(imageTarget);
                cleanupImage = RuntimeInspectionBridge.Inspect(imageTarget);
                var textTarget = document.CreateElement();
                textTarget.Text = "original";
                document.Body.Add(textTarget);
                cleanupText = RuntimeInspectionBridge.Inspect(textTarget);
                var live = AuthoringEnvironment.Live;
                live.Set(cleanupWidth, "style.width", "83px", live.Revision);
                live.Set(cleanupImage, "image", "Assets/AuthoringChecks/play-image.asset", live.Revision);
                live.Set(cleanupText, "text", "temporary", live.Revision);
                var businessChild = document.CreateElement("span");
                businessChild.Text = "business";
                textTarget.Add(businessChild);
                URUI.Authoring.Animation.AnimationEnvironment.Seek(.5);
                cleanupAnimation = RuntimeInspectionBridge.Inspect(document.Q("animated"));
                Check(Math.Abs(document.Styles.GetComputed(document.Q("animated")).GetNumber("opacity") - .5) < .003, "Unified live animation did not sample the actual node");
                EditorApplication.playModeStateChanged += VerifyExitingCleanup;
                EditorApplication.isPlaying = false;
            }
            catch (Exception error) { Fail(error); }
        }
        static void VerifyExitingCleanup(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode)
                return;
            EditorApplication.playModeStateChanged -= VerifyExitingCleanup;
            try
            {
                Check(Math.Abs(float.Parse(cleanupAnimation.Computed("opacity"), System.Globalization.CultureInfo.InvariantCulture) - 1) < .003, "Play exit retained sequence overlay");
                Check(AuthoringEnvironment.Live == null, "Live session was retained at exit");
                Check(cleanupWidth.Read("style.width") == null, "A failed text restoration prevented width cleanup");
                Check(cleanupImage.Read("attr.src") == null, "A failed text restoration leaked the image override");
                Check(!cleanupText.CanEditText, "Cleanup destroyed business-owned structured children");
            }
            catch (Exception error) { SessionState.SetString(Prefix + "failure", error.ToString()); File.WriteAllText("Evidence/play-failure.txt", error.ToString()); }
        }
        static void Fail(Exception error)
        {
            SessionState.SetString(Prefix + "failure", error.ToString());
            File.WriteAllText("Evidence/play-failure.txt", error.ToString());
            Debug.LogException(error);
            EditorApplication.isPaused = false;
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
            else
                Finish(1);
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Prefix + "active", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Prefix + "originalOptions", 0);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Prefix + "originalEnabled", false);
            if (code == 0)
            {
                File.WriteAllText("Evidence/play.json", "{\"passed\":" + SessionState.GetInt(Prefix + "count", 0) + ",\"failed\":0,\"reloadModes\":2}");
                Debug.Log("URUI Play PASS " + SessionState.GetInt(Prefix + "count", 0));
            }
            EditorApplication.Exit(code);
        }
    }
}
