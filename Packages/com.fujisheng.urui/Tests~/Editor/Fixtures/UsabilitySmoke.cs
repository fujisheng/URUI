#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using URUI;
using URUI.Samples;

public sealed class UsabilitySmoke : MonoBehaviour
{
    [DllImport("RmlUnityBridge")] static extern void rui_mouse_move(int x, int y);
    [DllImport("RmlUnityBridge")] static extern void rui_mouse_button(int button, int down);
    [DllImport("RmlUnityBridge")] static extern int rui_get_element_box(string id, [Out] float[] values);
    public Document generatedDocument;
    public TextAsset generatedSource;
    HelloUrui sample;
    int frames, clicks;
    bool finished, failed;
    string error;
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    void OnEnable() { Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) { failed = true; error = message; }
    }
    static void Click()
    {
        var box = new float[6];
        Need(rui_get_element_box("action", box) == 1 && box[2] > 0 && box[3] > 0, "sample button has layout bounds");
        Need(Mathf.Abs(box[2] - 220) < .1f && Mathf.Abs(box[3] - 50) < .1f, "sample button uses documented block size " + string.Join(",", box));
        var x = (int)(box[0] + box[2] * .5f); var y = (int)(box[1] + box[3] * .5f);
        Debug.Log("URUI_SAMPLE_BUTTON_BOX " + string.Join(",", box));
        rui_mouse_move(x, y); rui_mouse_button(0, 1); rui_mouse_button(0, 0);
    }
    void Update()
    {
        if (finished) return;
        frames++;
        try
        {
            if (frames == 3)
            {
                Need(generatedDocument.IsLoaded && generatedDocument.Q("label").IsValid, "generated serialized Document loaded");
                Need(generatedDocument.Source == generatedSource, "TextAsset source visible after Loaded");
                generatedDocument.enabled = false; generatedDocument.enabled = true;
                Need(generatedDocument.IsLoaded && generatedDocument.Source == generatedSource, "serialized source survives enable cycle");
                generatedDocument.Unload(); Destroy(generatedDocument.gameObject);
                sample = new GameObject("Hello URUI").AddComponent<HelloUrui>();
            }
            if (frames == 6)
            {
                var document = sample.GetComponent<Document>();
                Need(document.IsLoaded && document.Q("action").IsValid, "sample loaded via new API");
                document.Q("action").RegisterClickCallback(value => clicks++);
                Click();
            }
            if (frames == 9)
            {
                Need(clicks == 1, "sample native click");
                var target = Camera.main; var previous = RenderTexture.active;
                var rt = new RenderTexture(640, 480, 24); var pixels = new Texture2D(640, 480);
                try
                {
                    target.targetTexture = rt; target.Render(); RenderTexture.active = rt;
                    pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); pixels.Apply();
                    var box = new float[6]; Need(rui_get_element_box("panel", box) == 1, "sample panel bounds");
                    var color = pixels.GetPixel((int)box[0] + 5, 479 - (int)box[1] - 5);
                    Need(color.g > .35f && color.b < .4f, "sample click changed green background " + color);
                    File.WriteAllBytes("Evidence/hello-urui.png", pixels.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; target.targetTexture = null; Destroy(rt); Destroy(pixels); }
                sample.gameObject.SetActive(false); sample.gameObject.SetActive(true);
            }
            if (frames == 12)
            {
                sample.GetComponent<Document>().Q("action").RegisterClickCallback(value => clicks++);
                Click();
            }
            if (frames == 15)
            {
                Need(clicks == 2, "sample reenable has one current subscription");
                Destroy(sample.gameObject);
            }
            if (frames == 18)
            {
                Need(!sample && generatedSource, "sample cleanup preserves caller asset");
                Finish(!failed, error);
            }
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }
    void Finish(bool success, string message)
    {
        finished = true;
        File.WriteAllText("Evidence/usability-smoke.json", "{\"passed\":" + (success ? "true" : "false") + ",\"frames\":" + frames + ",\"clicks\":" + clicks + ",\"error\":\"" + (message ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"}");
        Debug.Log(success ? "URUI_USABILITY_SMOKE_PASS" : "URUI_USABILITY_SMOKE_FAIL " + message);
        UnityEditor.EditorApplication.Exit(success ? 0 : 1);
    }
}

#endif
