using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Solstice.Tests
{
    public sealed class SolsticeVisualCaptureTests
    {
        readonly List<string> errors = new();

        [SetUp]
        public void SetUp()
        {
            errors.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
            {
                if (!string.IsNullOrEmpty(condition) && condition.Contains("Access token is unavailable", StringComparison.Ordinal))
                {
                    return;
                }
                errors.Add($"[{type}] {condition}\n{stackTrace}");
            }
        }

        [UnityTest]
        public IEnumerator CaptureAllScreensAndVerifyRenderingHealth()
        {
            var captureDir = Environment.GetEnvironmentVariable("URUI_SOLSTICE_CAPTURE_DIR");
            if (string.IsNullOrEmpty(captureDir))
            {
                captureDir = Path.Combine(Application.temporaryCachePath, "solstice-captures");
            }
            Directory.CreateDirectory(captureDir);

            SceneManager.LoadScene("Solstice");
            for (var i = 0; i < 6; i++) yield return null;

            var app = UnityEngine.Object.FindFirstObjectByType<SolsticeApp>();
            Assert.IsNotNull(app, "SolsticeApp not found in Solstice scene.");
            app.InitializeIfNeeded();

            var cam = Camera.main;
            Assert.IsNotNull(cam, "MainCamera not found in Solstice scene.");

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1
            };
            rt.Create();
            var tex = new Texture2D(1280, 720, TextureFormat.RGBA32, false);

            try
            {
                IEnumerator Snap(string fileName, int frames = 6)
                {
                    for (var f = 0; f < frames; f++) yield return null;
                    var prevTarget = cam.targetTexture;
                    var prevActive = RenderTexture.active;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    tex.Apply(false);
                    cam.targetTexture = prevTarget;
                    RenderTexture.active = prevActive;
                    File.WriteAllBytes(Path.Combine(captureDir, fileName), tex.EncodeToPNG());
                }

                // 1. Default Lobby Obsidian
                yield return Snap("01-lobby-obsidian.png", 8);

                // 2. Lobby Switch Lead -> Lyra
                app.Data.ActiveHeroId = "lyra";
                app.Lobby?.Refresh();
                yield return Snap("02-lobby-lead-lyra.png", 6);
                app.Data.ActiveHeroId = "aurelia";

                // 3. Combat Ready (Radial360 cooldown sweeps)
                app.LoadScreenImmediate("combat");
                yield return Snap("03-combat-ready.png", 8);

                // 4. Combat Skill Cast + Crit Callout + Particles
                app.Combat?.CastSkill(0);
                yield return Snap("04-combat-skill-crit.png", 4);

                // 5-8. Heroes Roster + Effect Shader Modes + ui-wave + ArcTextLayout
                app.LoadScreenImmediate("heroes");
                app.Heroes?.SelectFxMode("fx-foil");
                yield return Snap("05-heroes-foil.png", 8);

                app.Data.ActiveHeroId = "veyra";
                app.LoadScreenImmediate("heroes");
                app.Heroes?.SelectFxMode("fx-shine");
                yield return Snap("06-heroes-shine.png", 6);

                app.Data.ActiveHeroId = "lyra";
                app.LoadScreenImmediate("heroes");
                app.Heroes?.SelectFxMode("fx-halo");
                yield return Snap("07-heroes-halo.png", 6);

                app.Heroes?.SelectFxMode("fx-dissolve");
                yield return Snap("08-heroes-dissolve.png", 6);
                app.Data.ActiveHeroId = "aurelia";

                // 9-10. Summon Astrolabe Ready & SSR Reveal Sequence
                app.LoadScreenImmediate("summon");
                yield return Snap("09-summon-ready.png", 8);

                app.Summon?.TriggerSummon();
                app.Summon?.SeekDemoSample(0.88);
                yield return Snap("10-summon-ssr.png", 6);

                // 11. Armory 10k Virtualized ListView (AOT CompiledDocumentAsset)
                app.LoadScreenImmediate("armory");
                yield return Snap("11-armory-10k.png", 8);

                // 12-13. Settings Modal + Crimson & Alabaster Themes
                app.LoadScreenImmediate("lobby");
                app.SetSettingsOpen(true);
                app.ApplyTheme("theme-crimson");
                yield return Snap("12-settings-crimson.png", 6);

                app.ApplyTheme("theme-alabaster");
                yield return Snap("13-settings-alabaster.png", 6);

                // 14. Dev Telemetry Drawer + Obsidian + Render Stats Verification
                app.ApplyTheme("theme-obsidian");
                app.SetSettingsOpen(false);
                app.SetDevOpen(true);
                yield return Snap("14-dev-telemetry.png", 10);

                var stats = app.Surface.GetRenderingStatistics();
                Assert.IsTrue(stats.Ready, "Surface should report Ready == true.");
                Assert.GreaterOrEqual(stats.LoadedDocuments, 3, "Expected persistent TopHud + Page + Overlay docs.");
                Assert.Greater(stats.RecordedGeometryDraws, 0, "Expected recorded geometry draw calls.");
                Assert.Greater(stats.NativePlanCommandCount, 0, "Expected native plan commands.");

                Assert.IsEmpty(errors, "Unexpected Unity Console errors/exceptions:\n" + string.Join("\n", errors));
                Debug.Log($"SOLSTICE_CAPTURE_PASS docs={stats.LoadedDocuments} draws={stats.RecordedGeometryDraws} cmds={stats.NativePlanCommandCount} planBytes={stats.NativePlanFrameBytes}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
