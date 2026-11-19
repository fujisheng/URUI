using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using URUI.Animation;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Tests
{
    public static class AnimationValidation
    {
        static int assertions;
        static void Check(bool value, string message)
        {
            assertions++;
            if (!value)
            {
                throw new Exception(message);
            }
        }
        static void Near(float expected, float actual, string message) => Check(Mathf.Abs(expected - actual) < .003f, message + ": " + expected + " != " + actual);
        static void Rejected(Action action)
        {
            assertions++;
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                return;
            }
            throw new Exception("Expected rejection");
        }
        public static void Run()
        {
            var errors = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception)
                {
                    errors.Add(message);
                }
            };
            Application.logMessageReceived += capture;
            try
            {
                using (var host = RuntimeInspectionBridge.CreatePreview(480, 320))
                {
                    host.Document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#panel{position:absolute;left:10px;top:20px;width:100px;height:50px;background-color:red;opacity:.25;}@keyframes enter{from{opacity:0;transform:translateX(0px);}to{opacity:1;transform:translateX(100px);}}@keyframes fade{from{opacity:1;}to{opacity:0;}}</style></head><body><div id='panel'></div></body></html>", "ui/animation.html");
                    host.Render();
                    var panel = host.Document.Q("panel");
                    var definition = new SequenceDefinition
                    {
                        Name = "弹窗",
                        Duration = 2
                    };
                    definition.Tracks.Add(new SequenceTrack
                    {
                        Id = "enter",
                        Target = "panel",
                        Keyframes = "enter",
                        Duration = 1
                    });
                    definition.Tracks.Add(new SequenceTrack
                    {
                        Id = "exit",
                        Target = "panel",
                        Keyframes = "fade",
                        Start = 1,
                        Duration = 1
                    });
                    using (var player = new SequencePlayer(host.Document.Body, definition))
                    {
                        foreach (float time in new[] { .8f, .2f, .8f })
                        {
                            player.Sample(time);
                            host.Render();
                            Near(time, host.Document.Styles.GetComputed(panel).GetNumber("opacity"), "Opacity sample");
                            Near(10 + time * 100, panel.WorldBound.x, "Transform sample");
                            Check(!player.Finished.IsCompleted, "Scrubbing completed the sequence");
                            using (var pixels = host.Capture())
                            {
                                var color = pixels.Texture.GetPixel(150, 275);
                                Check(time > .5f ? color.r > .7f && color.g < .15f : color.r < .15f, "Rendered pixels did not follow the sampled transform/opacity");
                            }
                        }
                        Check(panel.Style.Get("opacity") == null, "Sampling wrote a persistent inline opacity");
                        player.Sample(1.5);
                        host.Render();
                        Near(.5f, host.Document.Styles.GetComputed(panel).GetNumber("opacity"), "Sequential track takeover");
                        Near(110, panel.WorldBound.x, "Sequential opacity track removed earlier transform hold");
                        panel.Style.Set("opacity", ".6");
                        player.Stop();
                        host.Render();
                        Near(.6f, host.Document.Styles.GetComputed(panel).GetNumber("opacity"), "Stop lost game-authored value");
                        player.Play();
                        host.Advance(2.1f);
                        host.Render();
                        Check(player.Finished.IsCompletedSuccessfully, "Actual finite playback did not complete");
                        Check(!player.IsPlaying, "Finished player kept ticking");
                        player.Stop();
                    }
                    var overlap = SequenceDefinition.Parse(definition.ToJson());
                    overlap.Tracks[1].Start = .5;
                    Rejected(() => { using (var invalid = new SequencePlayer(host.Document.Body, overlap)) { } });
                    var loop = SequenceDefinition.Parse(definition.ToJson());
                    loop.Loop = true;
                    using (var player = new SequencePlayer(host.Document.Body, loop))
                    {
                        player.Play();
                        host.Advance(5.5f);
                        host.Render();
                        Check(!player.Finished.IsCompleted, "Loop completed naturally");
                        player.Stop();
                        Check(player.Finished.IsCanceled, "Loop stop did not cancel");
                    }
                    using (var player = new SequencePlayer(host.Document.Body, definition))
                    {
                        player.Sample(.5);
                        panel.Destroy();
                        Rejected(() => player.Sample(.8));
                    }
                    var first = host.Document.CreateElement();
                    first.Name = "a";
                    host.Document.Body.Add(first);
                    var second = host.Document.CreateElement();
                    second.Name = "a";
                    host.Document.Body.Add(second);
                    host.Render();
                    var ambiguous = new SequenceDefinition { Duration = 1 };
                    ambiguous.Tracks.Add(new SequenceTrack
                    {
                        Id = "a",
                        Target = "a",
                        Keyframes = "enter",
                        Duration = 1
                    });
                    Rejected(() => { using (var invalid = new SequencePlayer(host.Document.Body, ambiguous)) { } });
                }
                Check(errors.Count == 0, "Unexpected Error/Exception: " + string.Join(";", errors));
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/animation.json", "{\"passed\":" + assertions + ",\"failed\":0}");
                Debug.Log("URUI Animation PASS " + assertions);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/animation-failure.txt", error.ToString());
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }
        }
    }
}
