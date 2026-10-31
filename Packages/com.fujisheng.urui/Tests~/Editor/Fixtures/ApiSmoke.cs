#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using URUI;

namespace URUI.Tests
{
public sealed class ApiSmoke : MonoBehaviour
{
    const string Markup = "<rml><head><style>body{font-family:__URUI_FONT__;font-size:20px;color:white;margin:0;}#panel{position:absolute;left:0;top:0;width:200px;height:100px;background-color:red;}#action{position:absolute;left:0;top:110px;width:150px;height:80px;background-color:blue;}</style></head><body><div id='root'><div id='panel'><span id='label'>Hello</span></div><div id='action'>Click</div><img id='image' src='tile.png' style='position:absolute;left:250px;top:0;width:40px;height:40px;'/></div></body></rml>";
    [DllImport("URUIBridge")] static extern void urui_mouse_move(int x, int y);
    [DllImport("URUIBridge")] static extern void urui_mouse_button(int button, int down);
    Document document;
    Element panel, label, action;
    Texture2D image;
    IDisposable subscription;
    int frames, loaded, unloaded, clicks, filtered, skipped;
    bool finished, failed;
    Document explicitlyUnloaded;
    TextAsset callerMarkup;
    string error;
    [Serializable] class Report { public bool passed; public int frames, loaded, unloaded, clicks; public string error; }

    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    void OnEnable() { Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) { failed = true; error = message; }
    }
    void Click() { urui_mouse_move(30, 130); urui_mouse_button(0, 1); urui_mouse_button(0, 0); }
    void CheckInvalidLoadAndDisableCallback()
    {
        var check = new GameObject("Lifecycle edge test").AddComponent<Document>();
        var issues = "";
        var inactiveObject = new GameObject("Inactive configuration");
        inactiveObject.SetActive(false);
        var inactive = inactiveObject.AddComponent<Document>();
        try { inactive.Source = null; inactive.Images = Array.Empty<ImageAsset>(); inactive.FontPath = ""; }
        catch (InvalidOperationException) { issues += "inactive main-thread configuration rejected; "; }
        Destroy(inactiveObject);
        try { check.LoadMarkup("<rml><body>before NUL</body></rml>\0ignored"); issues += "NUL was accepted; "; }
        catch (ArgumentException) { }
        check.Unload();
        try { check.FontPath = "font\0ignored.ttf"; issues += "NUL font path accepted; "; }
        catch (ArgumentException) { }
        check.FontPath = "";
        check.LoadMarkup("<rml><body>old</body></rml>");
        check.Unloaded += value => value.enabled = false;
        try { check.LoadMarkup("<rml><body>new</body></rml>"); }
        catch (InvalidOperationException) { }
        if (check.IsLoaded) issues += "reload created a context after Unloaded disabled the document; ";
        check.Unload();
        Destroy(check.gameObject);
        Need(issues.Length == 0, issues);
    }
    void Update()
    {
        if (finished) return;
        frames++;
        try
        {
            if (frames == 3)
            {
                CheckInvalidLoadAndDisableCallback();
                // Separate assertion recorded before the aggregate edge checks,
                // then verified on a later frame after Unity has called Start.
                explicitlyUnloaded = new GameObject("Explicit unload before Start").AddComponent<Document>();
                callerMarkup = new TextAsset("<rml><body>TextAsset owned by caller</body></rml>");
                explicitlyUnloaded.Load(callerMarkup);
                explicitlyUnloaded.Unload();
            }
            if (frames == 5)
            {
                Need(!explicitlyUnloaded.IsLoaded, "explicit TextAsset unload was undone by Start");
                Need(callerMarkup, "caller TextAsset was destroyed");
                Destroy(explicitlyUnloaded.gameObject); Destroy(callerMarkup);
                document = new GameObject("URUI Document").AddComponent<Document>();
                document.Loaded += value => loaded++;
                document.Unloaded += value => unloaded++;
                Need(document.Q("panel") == null, "query before load");
                Throws<ArgumentException>(() => document.LoadMarkup(" "));
                image = new Texture2D(1, 1); image.SetPixel(0, 0, Color.yellow); image.Apply();
                document.Images = new[] { new ImageAsset("tile.png", image) };
                document.LoadMarkup(Markup);
                Need(document.IsLoaded && loaded == 1, "synchronous load and notification");
                Need(document.Q("missing") == null, "missing query returns null");
                panel = document.Q("panel"); label = document.Q("label"); action = document.Q("action");
                Need(ReferenceEquals(panel, document.Q("panel")), "cached element query");
                panel.Style.SetBackgroundColor(Color.green);
                panel.Style.SetWidth(200); panel.SetAttribute("data-test", "中文"); label.SetText("<b>&中文");
                Throws<ArgumentException>(() => panel.SetAttribute("id", "changed"));
                Throws<ArgumentOutOfRangeException>(() => panel.Style.SetWidth(float.NaN));
                var threadFailure = Task.Run(() => { try { document.Q("panel"); return false; } catch (InvalidOperationException) { return true; } }).Result;
                Need(threadFailure, "worker thread rejected before Unity/native access");
                Action<ClickEvent> handler = value => { Need(value.Target == action, "event target"); clicks++; };
                subscription = action.RegisterClickCallback(handler);
                Need(ReferenceEquals(subscription, action.RegisterClickCallback(handler)), "duplicate callback registration");
                panel.RegisterClickCallback(value => filtered++);
                var other = new GameObject("Other").AddComponent<Document>();
                Throws<InvalidOperationException>(() => other.LoadMarkup(Markup));
                Destroy(other.gameObject);
            }
            if (frames == 7)
            {
                var camera = Camera.main; var old = RenderTexture.active;
                var rt = new RenderTexture(640, 480, 24); var pixels = new Texture2D(640, 480);
                try
                {
                    camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                    pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); pixels.Apply();
                    var color = pixels.GetPixel(30, 430); Need(color.g > .7f && color.r < .3f, "new facade green rendered pixel " + color);
                }
                finally { RenderTexture.active = old; camera.targetTexture = null; Destroy(rt); Destroy(pixels); }
                Click();
            }
            if (frames == 10)
            {
                Need(clicks == 1 && filtered == 0, "actual native click filtering");
                subscription.Dispose(); subscription.Dispose(); Click();
                panel.SetInnerMarkup("<span id='label'>New label</span>");
                Need(!label.IsValid, "inner markup invalidates old object");
                Throws<InvalidOperationException>(() => label.SetText("stale"));
                Need(document.Q("label").IsValid, "same id gets fresh object");
            }
            if (frames == 13)
            {
                Need(clicks == 1, "disposed subscription receives no clicks");
                panel.Remove(); Need(!panel.IsValid, "remove invalidation");
                var replacement = document.Q("root").Append("div", "panel");
                Need(replacement.IsValid && !panel.IsValid, "same id never revives old element");
                IDisposable later = null;
                action.RegisterClickCallback(value => later.Dispose());
                later = action.RegisterClickCallback(value => skipped++);
                Click();
            }
            if (frames == 16)
            {
                Need(skipped == 0, "unsubscribe during dispatch");
                document.enabled = false;
                Need(!document.IsLoaded && !action.IsValid && unloaded == 1, "disable unloads and invalidates");
                document.enabled = true;
                Need(document.IsLoaded && loaded == 2, "enable restores requested markup");
                action = document.Q("action");
                action.RegisterClickCallback(value => document.LoadMarkup(Markup));
                action.RegisterClickCallback(value => skipped++);
            }
            // The newly created native context computes hit boxes on its next
            // Update; content readiness does not imply a rendered layout yet.
            if (frames == 18) Click();
            if (frames == 20)
            {
                Need(loaded == 3 && unloaded == 2 && skipped == 0 && !action.IsValid, "reload in callback stops stale dispatch");
                document.Unload(); document.Unload();
                Need(!document.IsLoaded && unloaded == 3 && image, "unload preserves caller texture");
                document.LoadMarkup(Markup);
                Need(document.IsLoaded && loaded == 4, "reload after explicit unload releases context synchronously");
                Destroy(document.gameObject);
            }
            if (frames == 24)
            {
                Need(!document && unloaded == 4 && image, "destroy releases document, caller texture survives");
                Finish(!failed, error);
            }
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }
    void Finish(bool success, string message)
    {
        finished = true;
        File.WriteAllText("Evidence/api-smoke.json", JsonUtility.ToJson(new Report { passed = success, frames = frames, loaded = loaded, unloaded = unloaded, clicks = clicks, error = message }, true));
        if (image) Destroy(image);
        Debug.Log(success ? "URUI_API_SMOKE_PASS" : "URUI_API_SMOKE_FAIL " + message);
        UnityEditor.EditorApplication.Exit(success ? 0 : 1);
    }
}

}
#endif
