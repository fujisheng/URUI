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
    /// <summary>Continuous frames from the running Editor game camera, never concept images.</summary>
    public sealed class GameFlowRecording : MonoBehaviour
    {
        [Serializable] sealed class Marker { public string action; public int frame; }
        [Serializable] sealed class Report
        {
            public bool passed, actualEditorPlayMode = true, continuousCameraFrames = true;
            public int width, height, frames, targetFps = 10;
            public float seconds;
            public List<Marker> markers = new List<Marker>();
            public List<float> frameTimes = new List<float>();
        }
        GameFlowSample app;
        Document doc;
        RenderTexture target;
        Texture2D pixels;
        string output;
        bool recording;
        float started, next;
        readonly Report report = new Report();

        async void Start()
        {
            output = Path.GetFullPath("Artifacts/GameFlow/Recording");
            Directory.CreateDirectory(Path.Combine(output, "Frames"));
            try
            {
                Application.runInBackground = true; Application.targetFrameRate = 30;
                app = FindFirstObjectByType<GameFlowSample>(); doc = app.GetComponent<Document>();
                await Until(() => doc.IsLoaded && Camera.main, "loading document");
                doc.Input.UseUnityInput = false;
                await Task.Delay(160);
                report.width = Screen.width; report.height = Screen.height;
                target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
                pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
                started = next = Time.realtimeSinceStartup; recording = true; Mark("loading");
                await Page(GamePage.Login); Mark("login"); await Task.Delay(450);
                var name = doc.Q<TextField>("call-sign"); name.Value = ""; name.Focus();
                foreach (char value in "Rhea") { doc.Input.SendText(value.ToString()); await Task.Delay(110); }
                await Task.Delay(350); await Click("login-submit");
                await Page(GamePage.Home); Mark("home"); await Task.Delay(1100);
                await Click("open-inventory"); await Page(GamePage.Inventory); Mark("inventory");
                await Task.Delay(900); await Click("item-" + app.Service.Inventory[1].id); await Task.Delay(650);
                await Click("item-action");
                if (!app.Service.Inventory[1].equipped) throw new Exception("Recorded equip interaction did not apply.");
                Mark("equip"); await Task.Delay(800);
                await Click("item-" + app.Service.Inventory[0].id); await Task.Delay(600);
                await Click("back"); await Page(GamePage.Home); await Task.Delay(400);
                await Click("open-settings"); await Page(GamePage.Settings); Mark("settings"); await Task.Delay(650);
                var slider = doc.Q<Slider>("volume").WorldBound;
                var point = new Vector2(slider.xMin + slider.width * .65f, slider.center.y);
                doc.Input.SendPointer(PointerPhase.Move, point); await Task.Delay(180);
                doc.Input.SendPointer(PointerPhase.Down, point, pressedButtons: 1);
                for (int i = 0; i <= 8; i++)
                {
                    point.x = slider.xMin + slider.width * (.65f - .035f * i);
                    doc.Input.SendPointer(PointerPhase.Move, point, pressedButtons: 1); await Task.Delay(70);
                }
                doc.Input.SendPointer(PointerPhase.Up, point); await Task.Delay(400);
                if (Math.Abs(doc.Q<Slider>("volume").Value - 65) < 1) throw new Exception("Recorded slider did not move.");
                await Click("tab-display"); await Task.Delay(700);
                await Click("save-settings"); await Task.Delay(650);
                await Click("back"); await Page(GamePage.Home); await Task.Delay(350);
                await Click("open-leaderboard"); await Page(GamePage.Leaderboard); Mark("rankings"); await Task.Delay(1000);
                await Click("refresh"); await Until(() => !app.IsBusy, "ranking refresh"); await Task.Delay(900);
                await Click("back"); await Page(GamePage.Home); Mark("return home"); await Task.Delay(1300);
                recording = false; report.passed = true; report.seconds = Time.realtimeSinceStartup - started;
                File.WriteAllText(Path.Combine(output, "recording.json"), JsonUtility.ToJson(report, true));
                Debug.Log("GAME_FLOW_RECORDING_PASS frames=" + report.frames + " seconds=" + report.seconds);
                Finish(0);
            }
            catch (Exception error)
            {
                recording = false; File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                Debug.LogException(error); Finish(1);
            }
        }

        void LateUpdate()
        {
            if (!recording || Time.realtimeSinceStartup < next || !doc.IsLoaded) return;
            next = Time.realtimeSinceStartup + .1f;
            var camera = Camera.main;
            if (!camera) return;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            float frameTime = Time.realtimeSinceStartup - started;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(output, "Frames/frame-" + report.frames.ToString("D5") + ".png"), pixels.EncodeToPNG());
                report.frames++;
                report.frameTimes.Add(frameTime);
            }
            finally { camera.targetTexture = previousTarget; RenderTexture.active = previousActive; }
        }

        void Mark(string action) => report.markers.Add(new Marker { action = action, frame = report.frames });
        async Task Until(Func<bool> ready, string action)
        {
            float start = Time.realtimeSinceStartup;
            while (!ready()) { if (Time.realtimeSinceStartup - start > 15) throw new Exception("Recording timeout: " + action); await Task.Delay(20); }
        }
        async Task Page(GamePage page)
        {
            await Until(() => app.CurrentPage == page && doc.IsLoaded && !app.IsBusy, page.ToString());
            doc.Input.UseUnityInput = false; await Task.Delay(350);
        }
        async Task Click(string id)
        {
            var element = doc.Q(id);
            if (element == null || !element.HasLayout) throw new Exception("Recording target is absent: " + id);
            var point = element.WorldBound.center;
            doc.Input.SendPointer(PointerPhase.Move, point); await Task.Delay(160);
            doc.Input.SendPointer(PointerPhase.Down, point, pressedButtons: 1); await Task.Delay(120);
            doc.Input.SendPointer(PointerPhase.Up, point); await Task.Delay(150);
        }
        void Finish(int code)
        {
#if UNITY_EDITOR
            const string key = GameFlowSample.SettingsKey;
            if (UnityEditor.SessionState.GetBool("URUI.Recording.HadPreferences", false))
                PlayerPrefs.SetString(key, UnityEditor.SessionState.GetString("URUI.Recording.Preferences", ""));
            else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            UnityEditor.SessionState.EraseBool("URUI.Recording.HadPreferences");
            UnityEditor.SessionState.EraseString("URUI.Recording.Preferences");
            UnityEditor.EditorApplication.Exit(code);
#endif
        }
        void OnDestroy()
        {
            if (target) { target.Release(); Destroy(target); }
            if (pixels) Destroy(pixels);
        }
    }
}
