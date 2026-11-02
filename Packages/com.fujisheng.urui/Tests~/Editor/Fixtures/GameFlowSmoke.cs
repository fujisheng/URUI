using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;
using URUI.Samples.GameFlow;

namespace URUI.Tests
{
    /// <summary>Real imported sample and public UI input; Editor only, no Player build.</summary>
    public sealed class GameFlowSmoke : MonoBehaviour
    {
        GameFlowSample app;
        Document doc;
        int assertions;
        readonly List<string> errors = new List<string>();
        string output;
        bool hadPreferences;
        string previousPreferences;
        async void Start()
        {
            output = Path.GetFullPath("Artifacts/GameFlow/Editor"); Directory.CreateDirectory(output);
            hadPreferences = PlayerPrefs.HasKey(GameFlowSample.SettingsKey);
            previousPreferences = PlayerPrefs.GetString(GameFlowSample.SettingsKey, "");
            Application.logMessageReceived += Log;
            try
            {
                app = FindFirstObjectByType<GameFlowSample>(); doc = app.GetComponent<Document>();
                await Until(() => doc.IsLoaded && !app.IsBusy, "initialization must finish or offer retry");
                foreach (string missing in new[] { "item-03", "action-middle" })
                {
                    Need(app.CurrentPage == GamePage.Loading && Status.Contains("Missing") && Status.Contains("Images/" + missing),
                        "missing " + missing + " is reported on loading page");
                    Capture("00-missing-" + missing);
#if UNITY_EDITOR
                    string source = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.fujisheng.urui").resolvedPath;
                    string relative = "Resources/URUI/GameFlow/Images/" + missing + ".png";
                    string scriptPath = UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.MonoScript.FromMonoBehaviour(app));
                    string sampleRoot = Path.GetDirectoryName(Path.GetDirectoryName(scriptPath)).Replace('\\', '/');
                    string image = sampleRoot + "/" + relative;
                    File.Copy(Path.Combine(source, "Samples~/Game Flow/" + relative), image, true);
                    File.Copy(Path.Combine(source, "Samples~/Game Flow/" + relative + ".meta"), image + ".meta", true);
                    UnityEditor.AssetDatabase.ImportAsset(image, UnityEditor.ImportAssetOptions.ForceSynchronousImport | UnityEditor.ImportAssetOptions.ForceUpdate);
#endif
                    Click("retry"); await Until(() => !app.IsBusy, "asset recovery and initialization retry");
                }
                if (app.CurrentPage == GamePage.Loading)
                {
                    Need(doc.Q<Label>("status").Text.Contains("interrupted"), "startup failure feedback: " + Status);
                    Capture("01-loading-retry"); Click("retry");
                }
                await Page(GamePage.Login); Capture("02-login");
                doc.Q<TextField>("call-sign").Value = " "; Click("login-submit");
                await Until(() => !app.IsBusy, "invalid name validation"); Need(Status.Contains("call sign"), "blank name error");
                doc.Q<TextField>("call-sign").Value = "Rhea"; doc.Q<TextField>("access-code").Value = "wrong"; Click("login-submit");
                await Until(() => !app.IsBusy, "invalid demo code validation"); Need(Status.Contains("access code"), "wrong code error");
                doc.Q<TextField>("access-code").Value = "demo"; doc.Q<Dropdown>("connection").Value = "Offline"; Click("login-submit");
                await Until(() => !app.IsBusy, "offline login"); Need(Status.Contains("offline"), "offline login feedback");
                doc.Q<Dropdown>("connection").Value = "Online"; Click("login-submit");
                Need(app.IsBusy, "login exposes busy state");
                await Page(GamePage.Home); Capture("03-home");
                Need(doc.Q<Label>("greeting").Text.Contains("Rhea"), "profile rendered");
                Need(doc.Q("player-hud").Layout.width > 200, "game HUD external RCSS loaded");
                Need(doc.Q("open-expedition").Layout.width > 200, "real layout available");
                VerifyFrameBounds();
                var actor = doc.Q<URUI.Controls.Image>(className: "operator-art");
                Need(actor != null && actor.Source == "gameflow/operator.png" && actor.WorldBound.height > Screen.height * .7f,
                    "approved home uses a separate full-height original operator sprite");
                Click("open-expedition"); await Task.Delay(80);
                Need(!doc.Q<Button>("open-inventory").Focus(), "adventure modal gates background focus");
                Click("confirm-expedition"); Need(doc.Q<Label>("expedition-status").Text.Contains("ready"), "adventure preview state");
                await Task.Delay(80); Capture("03a-expedition"); Click("close-expedition");
                Click("open-profile"); await Task.Delay(80);
                Need(doc.Q<Label>("profile-name").Text == "Rhea", "profile modal renders the current operator");
                Need(!doc.Q<Button>("open-inventory").Focus(), "profile modal gates background focus");
                Click("profile-dismiss");
                var hover = doc.Q<Button>("open-inventory");
                string normalColor = hover.ResolvedStyle.Get("background-color");
                var point = hover.WorldBound.center;
                doc.Input.SendPointer(PointerPhase.Move, point); await Task.Delay(180);
                Need(hover.ResolvedStyle.Get("background-color") != normalColor, "real hover transition feedback");
                float normalTop = hover.WorldBound.yMin;
                doc.Input.SendPointer(PointerPhase.Down, point, pressedButtons: 1); await Task.Delay(160);
                Need(hover.WorldBound.yMin > normalTop + 1, "real press offset feedback");
                doc.Input.SendPointer(PointerPhase.Cancel, point); await Task.Delay(160);
                int power = app.Service.CurrentPlayer.power;
                Click("open-inventory"); await Page(GamePage.Inventory);
                var grid = doc.Q("item-grid");
                Need(grid.Query(className: "item-slot").Count == app.Service.Inventory.Count, "inventory renders real item slots");
                Need(doc.Q("item-scroll").WorldBound.width > Screen.width * .5f && doc.Q("item-detail").WorldBound.width > Screen.width * .25f,
                    "inventory keeps the approved grid and fixed detail proportions");
                var secondItem = app.Service.Inventory[1]; var firstItem = app.Service.Inventory[0];
                var touchPoint = doc.Q<Button>("item-" + secondItem.id).WorldBound.center;
                doc.Input.SendPointer(PointerPhase.Down, touchPoint, 7, true, pressedButtons: 1);
                doc.Input.SendPointer(PointerPhase.Up, touchPoint, 7, true);
                Need(doc.Q<Label>("detail-name").Text == secondItem.name, "touch selects an item slot");
                var scroll = doc.Q<ScrollView>("item-scroll"); var drag = scroll.WorldBound.center;
                // End an uncaptured touch just beyond the edge, below the drag threshold.
                // A different pointer must still be able to begin the next gesture.
                var edge = new Vector2(scroll.WorldBound.xMin + 1, scroll.WorldBound.yMin + 2);
                doc.Input.SendPointer(PointerPhase.Down, edge, 9, true, pressedButtons: 1);
                doc.Input.SendPointer(PointerPhase.Move, edge - new Vector2(2, 0), 9, true, pressedButtons: 1);
                doc.Input.SendPointer(PointerPhase.Up, edge - new Vector2(2, 0), 9, true);
                doc.Q<TextField>("search").Focus();
                doc.Input.SendPointer(PointerPhase.Down, drag, 8, true, pressedButtons: 1);
                doc.Input.SendPointer(PointerPhase.Move, drag + new Vector2(0,-100), 8, true, pressedButtons: 1);
                await Task.Delay(80);
                doc.Input.SendPointer(PointerPhase.Up, drag + new Vector2(0,-100), 8, true);
                await Task.Delay(80);
                Need(scroll.ScrollOffset.y > 0, "new touch scrolls after a below-threshold touch leaves the viewport");
                Need(doc.Q<Label>("detail-name").Text == secondItem.name, "drag does not choose another item");
                doc.Q<TextField>("search").Value = firstItem.name; Need(grid.Query(className: "item-slot").Count == 1, "inventory search");
                Need(scroll.ScrollOffset.y < 1, "filter returns to first item slot");
                Click("item-" + firstItem.id); Click("item-action"); Need(app.Service.CurrentPlayer.power > power, "equip updates profile");
                doc.Q<TextField>("search").Value = "not-present"; Need(grid.Query(className: "item-slot").Count == 0, "empty inventory state");
                Click("clear-search"); Need(grid.Query(className: "item-slot").Count >= 8, "clear filter"); await Task.Delay(500); await VerifyGridClip(); Capture("04-inventory");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--game-flow-checkpoint") >= 0) { RestorePreferences(); Debug.Log("GAME_FLOW_VISUAL_CHECKPOINT_PASS"); Exit(0); return; }
                Click("back"); await Page(GamePage.Home);
                Click("open-settings"); await Page(GamePage.Settings);
                Capture("05a-settings-audio");
                doc.Q<Slider>("volume").Value = 23; doc.Q<Toggle>("sound").Value = false;
                Click("tab-display");
                Need(doc.Q("group-display").Style.Display != DisplayStyle.None && doc.Q("group-audio").Style.Display == DisplayStyle.None, "settings category switches its rows");
                doc.Q<Dropdown>("density").Value = "Compact"; doc.Q<Dropdown>("accent").Value = "Glacier";
                Click("save-settings"); Need(Status.Contains("saved"), "save feedback"); await Task.Delay(100); Capture("05-settings");
                Click("back"); await Page(GamePage.Home); Click("open-settings"); await Page(GamePage.Settings);
                Need(Math.Abs(doc.Q<Slider>("volume").Value - 23) < .1f && !doc.Q<Toggle>("sound").Value, "saved settings persist across pages");
                doc.Q<Slider>("volume").Value = 77; Click("back"); await Page(GamePage.Home);
                Click("open-settings"); await Page(GamePage.Settings); Need(Math.Abs(doc.Q<Slider>("volume").Value - 23) < .1f, "unsaved draft discarded");
                Click("back"); await Page(GamePage.Home); Click("open-leaderboard"); await Page(GamePage.Leaderboard);
                Need(doc.Q<ListView>("rankings").ItemsSource.Count == 40, "ranking data loaded");
                Need(doc.Q<ListView>("rankings").WorldBound.height > Screen.height * .35f, "ranking viewport fills the available game window");
                Capture("06-leaderboard");
                doc.Q<Dropdown>("ranking-scope").Value = "Friends"; await Until(() => !app.IsBusy, "friends request");
                Need(doc.Q<ListView>("rankings").ItemsSource.Count == 0, "empty friends feed");
                doc.Q<Dropdown>("ranking-scope").Value = "Global"; await Until(() => !app.IsBusy, "global request");
                string previous = doc.Q<ListView>("rankings").ContentContainer[0].Q<Label>("score").Text;
                Click("refresh"); await Until(() => !app.IsBusy, "ranking refresh");
                Need(doc.Q<ListView>("rankings").ContentContainer[0].Q<Label>("score").Text != previous, "refresh updates snapshot");
                Click("back"); await Page(GamePage.Home); Click("open-settings"); await Page(GamePage.Settings); Click("tab-network"); doc.Q<Dropdown>("connection").Value = "Offline"; Click("save-settings");
                Click("back"); await Page(GamePage.Home); Click("open-leaderboard"); await Page(GamePage.Leaderboard); Need(Status.Contains("offline"), "ranking service failure"); Capture("07-ranking-error");
                Click("retry"); await Until(() => !app.IsBusy, "offline retry"); Need(Status.Contains("offline"), "retry remains honest while offline");
                Click("back"); await Page(GamePage.Home); Click("open-settings"); await Page(GamePage.Settings); Click("tab-network"); doc.Q<Dropdown>("connection").Value = "Online"; Click("save-settings");
                Click("back"); await Page(GamePage.Home); Click("open-leaderboard"); await Until(() => app.CurrentPage == GamePage.Leaderboard && app.IsBusy, "pending request starts");
                Click("back"); await Page(GamePage.Home); await Task.Delay(500); Need(app.CurrentPage == GamePage.Home, "outgoing request cancelled");
                for (int i = 0; i < 3; i++) { Click("open-inventory"); await Page(GamePage.Inventory); Click("back"); await Page(GamePage.Home); }
#if UNITY_EDITOR
                SetSize(680, 900);
#endif
                await Task.Delay(450); Need(Screen.width == 680, "narrow window applied"); Capture("08-home-narrow");
                Click("open-inventory"); await Page(GamePage.Inventory); Capture("09-inventory-narrow");
                Need(doc.Q("item-detail").Layout.width > 100, "narrow detail remains usable");
                var old = doc.Root; app.enabled = false; Need(!doc.IsLoaded && !old.IsValid, "disable unloads and invalidates old elements");
                app.enabled = true;
                await Until(() => doc.IsLoaded && !app.IsBusy, "reenable startup"); if (app.CurrentPage == GamePage.Loading) Click("retry");
                await Page(GamePage.Login); Need(Camera.main != null && FindFirstObjectByType<AudioListener>() != null, "same-frame reenable retains camera and audio listener"); Need(errors.Count == 0, "no unexpected runtime errors");
                string stale = Path.Combine(output, "failure.txt"); if (File.Exists(stale)) File.Delete(stale);
                File.WriteAllText(Path.Combine(output, "game-flow.json"), "{\"passed\":true,\"assertions\":" + assertions + ",\"sixPageFlow\":true,\"relocatedImport\":true,\"wideAndNarrow\":true,\"mockServices\":true,\"errors\":[]}");
                Debug.Log("GAME_FLOW_EDITOR_PASS assertions=" + assertions);
                RestorePreferences();
                Exit(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "failure.txt"), error + "\n" + string.Join("\n", errors)); Debug.LogException(error); RestorePreferences(); Exit(1);
            }
            finally
            {
                Application.logMessageReceived -= Log;
                RestorePreferences();
            }
        }
        string Status => doc.Q<Label>("status").Text;
        void RestorePreferences()
        {
            if (hadPreferences) PlayerPrefs.SetString(GameFlowSample.SettingsKey, previousPreferences); else PlayerPrefs.DeleteKey(GameFlowSample.SettingsKey);
            PlayerPrefs.Save();
        }
        void Need(bool value, string name) { if (!value) throw new Exception(name); assertions++; }
        void VerifyFrameBounds()
        {
            int checkedParts = 0;
            bool contained = true;
            foreach (var part in doc.Root.Query(className: "frame-part"))
            {
                if (!part.HasLayout || part.Layout.width <= 0 || part.Layout.height <= 0) continue;
                checkedParts++;
                var box = part.WorldBound; var owner = part.Parent.WorldBound;
                contained &= box.xMin >= owner.xMin - 1 && box.yMin >= owner.yMin - 1
                    && box.xMax <= owner.xMax + 1 && box.yMax <= owner.yMax + 1;
            }
            Need(checkedParts > 0 && contained, "scalable panel artwork stays within its owner");
        }
        async Task Until(Func<bool> predicate, string name)
        {
            float start = Time.realtimeSinceStartup;
            while (!predicate()) { if (Time.realtimeSinceStartup - start > 12) throw new Exception("Timeout: " + name); await Task.Delay(20); }
        }
        async Task Page(GamePage page)
        {
            await Until(() => app.CurrentPage == page && doc.IsLoaded && !app.IsBusy, "page " + page);
            doc.Input.UseUnityInput = false; await Task.Delay(300);
        }
        void Click(string name)
        {
            var button = doc.Q<Button>(name); Need(button != null && button.Focus(), "focus button " + name);
            doc.Input.UseUnityInput = false; doc.Input.SendKey(KeyCode.Return, true); doc.Input.SendKey(KeyCode.Return, false);
        }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        void Capture(string name)
        {
            var camera = Camera.main; var target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            var prior = RenderTexture.active; var priorTarget = camera.targetTexture;
            try { camera.targetTexture = target; camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG()); }
            finally { camera.targetTexture = priorTarget; RenderTexture.active = prior; target.Release(); Destroy(target); Destroy(texture); }
            SaveLayout(name);
        }
        [Serializable] sealed class LayoutEntry { public string name; public float x, y, width, height; }
        [Serializable] sealed class LayoutSnapshot { public int width, height; public List<LayoutEntry> elements = new List<LayoutEntry>(); }
        void SaveLayout(string name)
        {
            var snapshot = new LayoutSnapshot { width = Screen.width, height = Screen.height };
            void Add(string key, Element element)
            {
                if (element == null || !element.HasLayout) return;
                var b = element.WorldBound;
                snapshot.elements.Add(new LayoutEntry { name = key, x = b.x, y = b.y, width = b.width, height = b.height });
            }
            foreach (string id in new[] { "player-hud", "open-expedition", "item-scroll", "item-detail", "detail-icon", "login-submit", "content", "rankings", "your-rank" }) Add(id, doc.Q(id));
            foreach (string cls in new[] { "operator-art", "cover-title", "game-dock", "inventory-panel", "detail-panel", "login-window", "personal-rank" }) Add(cls, doc.Q(className: cls));
            File.WriteAllText(Path.Combine(output, name + ".layout.json"), JsonUtility.ToJson(snapshot, true));
        }
        async Task VerifyGridClip()
        {
            var viewport = doc.Q<ScrollView>("item-scroll").WorldBound;
            int x = (int)viewport.xMin + 25, y = Screen.height - (int)viewport.yMax - 7;
            Color before = Pixel(x, y);
            var grid = doc.Q("item-grid"); grid.Style.Display = DisplayStyle.None;
            await Task.Delay(80); Color after = Pixel(x, y);
            grid.Style.Display = DisplayStyle.Flex; await Task.Delay(80);
            Need(Math.Abs(before.r-after.r) < .02f && Math.Abs(before.g-after.g) < .02f && Math.Abs(before.b-after.b) < .02f,
                "item slots paint only inside their scrolling viewport");
        }
        Color Pixel(int x, int y)
        {
            var camera = Camera.main; var target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            var prior = RenderTexture.active; var priorTarget = camera.targetTexture;
            try { camera.targetTexture=target; camera.Render(); RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply(); return texture.GetPixel(x,y); }
            finally { camera.targetTexture=priorTarget; RenderTexture.active=prior; target.Release(); Destroy(target); Destroy(texture); }
        }
#if UNITY_EDITOR
        public static void SetSize(int width, int height)
        {
            var assembly = typeof(UnityEditor.Editor).Assembly; var type = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(type); var sizes = singleton.GetProperty("instance").GetValue(null);
            var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType"); var group = type.GetMethod("GetGroup").Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize"); var kind = assembly.GetType("UnityEditor.GameViewSizeType");
            var size = Activator.CreateInstance(sizeType, new[] { Enum.Parse(kind, "FixedResolution"), (object)width, height, "URUI Game Flow" });
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size }); int index = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null) - 1;
            var viewType = assembly.GetType("UnityEditor.GameView"); var view = UnityEditor.EditorWindow.GetWindow(viewType); viewType.GetProperty("selectedSizeIndex").SetValue(view, index);
        }
#endif
        static void Exit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#endif
        }
    }
}
