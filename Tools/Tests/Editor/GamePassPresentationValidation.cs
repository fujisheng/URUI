// 在 Client 中临时编译此夹具；只验证当前工作副本，不启动业务或写入页面。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Preview;
using URUI.Elements;

public static class GamePassPresentationValidation
{
    [Serializable]
    sealed class Observation
    {
        public int width, height;
        public float logicalWidth, logicalHeight;
        public Rect root, title, freeTicket, goldenTicket, freeLabel, freeText, goldenLabel, goldenText;
        public string capture;
        public List<string> failures = new List<string>();
    }
    [Serializable]
    sealed class Report
    {
        public bool passed, sourcesUnchanged, partialMarkupTracksViewport, runtimeRejectsMissingTitle;
        public List<Observation> observations = new List<Observation>();
        public string error;
        public List<string> errors = new List<string>();
    }
    static readonly Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2160, 1080), new Vector2Int(2532, 1170), new Vector2Int(1280, 720) };
    static readonly Vector2[] logical = { new Vector2(2294.8987f, 1290.8805f), new Vector2(2294.8987f, 1290.8805f), new Vector2(2434.1076f, 1217.0538f), new Vector2(2532, 1170), new Vector2(2294.8987f, 1290.8805f) };
    static readonly Dictionary<string, string> sources = new Dictionary<string, string>();
    static Report report;
    static Vector2Int original;
    static int index;
    static string output;
    static PreviewSession checkedPreview;
    static AuthoringSession checkedAuthor;

    [MenuItem("Tools/URUI Validation/Check Pass Presentation")]
    public static void Run()
    {
        if (report != null)
            throw new InvalidOperationException("Validation is already running.");
        if (EditorApplication.isPlayingOrWillChangePlaymode || AuthoringEnvironment.Author.File == null || !AuthoringEnvironment.Author.File.Path.EndsWith("/Pass/Pass.html", StringComparison.Ordinal))
            throw new InvalidOperationException("Open Pass.html in Edit Mode first.");
        output = Path.GetFullPath("../artifacts/urui-pass-presentation-20261111/current-run");
        Directory.CreateDirectory(output);
        sources.Clear();
        foreach (var file in AuthoringEnvironment.Author.Workspace.Files)
            sources[file.Path] = file.Text;
        AuthoringEnvironment.EnsurePreview();
        original = new Vector2Int(AuthoringEnvironment.Preview.Width, AuthoringEnvironment.Preview.Height);
        checkedAuthor = new AuthoringSession(Directory.GetCurrentDirectory());
        checkedAuthor.Open(AuthoringEnvironment.Author.File.Path);
        // 验证磁盘上的默认页面；用户当前工作副本可能含有故意放到画布外的编辑，必须保留。
        AuthoringEnvironment.Preview.Dispose();
        checkedPreview = new PreviewSession(checkedAuthor, Directory.GetCurrentDirectory());
        report = new Report();
        Application.logMessageReceived += CaptureError;
        index = 0;
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (EditorApplication.isCompiling)
            return;
        try
        {
            var size = sizes[index];
            var preview = checkedPreview;
            preview.Resize(size.x, size.y);
            var item = new Observation { width = size.x, height = size.y, capture = Path.Combine(output, "pass-" + index + "-" + size.x + "x" + size.y + ".png") };
            preview.Capture(item.capture);
            var canvas = preview.Document.Q(className: "pass-canvas");
            item.logicalWidth = canvas.Layout.width;
            item.logicalHeight = canvas.Layout.height;
            item.root = canvas.WorldBound;
            item.title = canvas.Q(className: "pass-title").WorldBound;
            item.freeTicket = canvas.Q(className: "pass-free-ticket").WorldBound;
            item.goldenTicket = canvas.Q(className: "pass-golden-ticket").WorldBound;
            item.freeLabel = canvas.Q(className: "pass-free-ticket").Q(className: "pass-ticket-label").WorldBound;
            item.freeText = canvas.Q(className: "pass-free-ticket").Q(className: "pass-ticket-label-text").WorldBound;
            item.goldenLabel = canvas.Q(className: "pass-golden-ticket").Q(className: "pass-ticket-label").WorldBound;
            item.goldenText = canvas.Q(className: "pass-golden-ticket").Q(className: "pass-ticket-label-text").WorldBound;
            if (Mathf.Abs(item.logicalWidth - logical[index].x) > .1f || Mathf.Abs(item.logicalHeight - logical[index].y) > .1f)
                item.failures.Add("Reference canvas logical size differs from the production 2532x1170 / match .5 policy.");
            if (Mathf.Abs(item.root.width - size.x) > 2 || Mathf.Abs(item.root.height - size.y) > 2)
                item.failures.Add("Reference canvas world size does not cover its own viewport.");
            Inside(item.title, size, "title", item);
            Inside(item.freeTicket, size, "free ticket", item);
            Inside(item.goldenTicket, size, "golden ticket", item);
            if (!item.freeTicket.Contains(item.freeText.center) || !item.goldenTicket.Contains(item.goldenText.center))
                item.failures.Add("Ticket text is laid out outside its owning ticket.");
            var title = canvas.Q(className: "pass-title");
            if (!title.ClassListContains("align-middle") || !title.ClassListContains("align-left"))
                item.failures.Add("Title alignment differs from runtime TextLayout.");
            report.observations.Add(item);
            if (++index == sizes.Length)
            {
                CheckPartialMarkup();
                Finish();
            }
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            Finish();
        }
    }
    static void CaptureError(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
            report?.errors.Add(message + "\n" + stack);
    }
    static void CheckPartialMarkup()
    {
        var title = checkedAuthor.Model.Nodes.First(n => n.Tag == "div" && (n.Attribute("class") ?? "").Split(' ').Contains("pass-title"));
        checkedAuthor.EditFile(checkedAuthor.File.Path, checkedAuthor.Model.Remove(title.Key), checkedAuthor.Revision);
        checkedPreview.Refresh();
        checkedPreview.Document.Surface.Viewport = new Vector2Int(2160, 1080);
        checkedPreview.Refresh();
        var world = checkedPreview.Document.Q(className: "pass-canvas").WorldBound;
        report.partialMarkupTracksViewport = Mathf.Abs(world.width - 2160) < 2 && Mathf.Abs(world.height - 1080) < 2;
        try
        {
            using (Game.UI.PassUruiView.ConfigurePresentation(checkedPreview.Document.Q(className: "pass-canvas"))) { }
        }
        catch (ArgumentException) { report.runtimeRejectsMissingTitle = true; }
    }
    static void Inside(Rect rect, Vector2Int size, string name, Observation item)
    {
        if (rect.width <= 0 || rect.height <= 0 || rect.xMin < -2 || rect.yMin < -2 || rect.xMax > size.x + 2 || rect.yMax > size.y + 2)
            item.failures.Add(name + " is outside the preview viewport: " + rect);
    }
    static void Finish()
    {
        EditorApplication.update -= Tick;
        try
        {
            checkedPreview?.Dispose();
            checkedPreview = null;
            AuthoringEnvironment.Preview.Resize(original.x, original.y);
            AuthoringEnvironment.Preview.Refresh();
            report.sourcesUnchanged = true;
            foreach (var file in AuthoringEnvironment.Author.Workspace.Files)
                if (sources.TryGetValue(file.Path, out string before) && before != file.Text)
                    report.sourcesUnchanged = false;
            report.passed = report.error == null && report.errors.Count == 0 && report.partialMarkupTracksViewport && report.runtimeRejectsMissingTitle && report.sourcesUnchanged && report.observations.Count == sizes.Length;
            foreach (var item in report.observations)
                report.passed &= item.failures.Count == 0;
            File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(report, true));
            Debug.Log("Pass presentation validation: " + (report.passed ? "PASS" : "FAIL") + " — " + output);
        }
        finally { Application.logMessageReceived -= CaptureError; report = null; checkedAuthor = null; }
    }
}
