using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Animation;
using URUI.Editor.Inspection;
using URUI.Effect;
using URUI.Effect.Elements;
using URUI.Resources;
namespace URUI.Authoring.Tests
{
    public static class AnimationEffectValidation
    {
        static int count;
        sealed class Resolver : IEffectResourceResolver
        {
            internal GameObject Source;
            public GameObject ResolveParticles(string source) => source == "base" || source == "fixture" ? Source : null;
            public ImageLease AcquireImage(URUI.Styles.ComputedImageValue image) => null;
        }
        static void Check(bool condition, string message)
        {
            count++;
            if (!condition)
            {
                throw new Exception(message);
            }
        }
        static void Reject(Action action)
        {
            count++;
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
            GameObject original = null;
            try
            {
                Directory.CreateDirectory("Assets/AnimationEffectChecks");
                const string materialPath = "Assets/AnimationEffectChecks/particle.mat";
                const string prefabPath = "Assets/AnimationEffectChecks/particle.prefab";
                AssetDatabase.DeleteAsset(materialPath);
                AssetDatabase.DeleteAsset(prefabPath);
                var material = new Material(Shader.Find("URUI.Effect/Particle"));
                material.SetFloat("_DstBlend", 10);
                AssetDatabase.CreateAsset(material, materialPath);
                original = new GameObject("Sequence particles", typeof(ParticleSystem));
                var system = original.GetComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.startLifetime = 5;
                main.startSpeed = 0;
                main.startSize = 12;
                var emission = system.emission;
                emission.rateOverTime = 20;
                var shape = system.shape;
                shape.enabled = false;
                original.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(original, prefabPath);
                UnityEngine.Object.DestroyImmediate(original);
                original = null;
                AssetDatabase.SaveAssets();
                var resolver = new Resolver { Source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) };
                byte[] prefabBefore = File.ReadAllBytes(prefabPath);
                byte[] materialBefore = File.ReadAllBytes(materialPath);
                using (var host = RuntimeInspectionBridge.CreatePreview(480, 320))
                {
                    using (var module = new EffectModule(host.Document, resolver))
                    {
                        host.Document.TextResources = new[] { new TextResource("/urui-effect/Effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                        host.Document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui-effect/Effect.css'><style>body{margin:0;height:100%;}#panel{position:absolute;left:10px;top:20px;width:80px;height:40px;background-color:red;opacity:.4;}#p{position:absolute;left:150px;top:80px;width:100px;height:100px;}@keyframes ui{from{opacity:0;}to{opacity:1;}}@keyframes effect{from{--urui-fx-tone-intensity:0;}to{--urui-fx-tone-intensity:1;}}</style></head><body><div id='panel'></div><urui-particles id='p' src='base'></urui-particles></body></html>", "ui/sequence-effect.html");
                        host.Render();
                        var node = host.Document.Q<ParticlesElement>("p");
                        node.Seek(.1f, 9);
                        host.Render();
                        var initialState = node.PlaybackState;
                        float initialTime = node.SimulationTime;
                        var sequence = new SequenceDefinition { Duration = 2 };
                        sequence.Tracks.Add(new SequenceTrack
                        {
                            Id = "ui",
                            Target = "panel",
                            Keyframes = "ui",
                            Duration = 1
                        });
                        sequence.Tracks.Add(new SequenceTrack
                        {
                            Id = "fx",
                            Target = "panel",
                            Keyframes = "effect",
                            Duration = 1
                        });
                        sequence.Tracks.Add(new SequenceTrack
                        {
                            Id = "particles",
                            Target = "p",
                            Kind = "particles",
                            Source = "fixture",
                            Start = .2,
                            Duration = .8,
                            Speed = 2,
                            Seed = 7
                        });
                        using (var player = new SequencePlayer(host.Document.Body, sequence))
                        {
                            Check(node.SequenceControlled, "Particle driver did not acquire ownership");
                            byte[] pose = null;
                            int particles = 0;
                            foreach (double time in new[] { .8, .1, .8 })
                            {
                                player.Sample(time);
                                host.Render();
                                Check(Math.Abs(host.Document.Styles.GetComputed(host.Document.Q("panel")).GetNumber("opacity") - time) < .003, "UI did not use master time");
                                Check(Math.Abs(host.Document.Styles.GetComputed(host.Document.Q("panel")).GetNumber("--urui-fx-tone-intensity") - time) < .003, "Effect did not use master time");
                                if (time > .5)
                                {
                                    Check(Math.Abs(node.SimulationTime - 1.2) < .003 && node.ParticleCount > 0, "Particle delay/speed sampling failed");
                                    using (var capture = host.Capture())
                                    {
                                        var pixels = capture.Texture.EncodeToPNG();
                                        if (pose == null)
                                        {
                                            pose = pixels;
                                            particles = node.ParticleCount;
                                        }
                                        else
                                        {
                                            Check(pose.SequenceEqual(pixels) && particles == node.ParticleCount, "Backward seek was not deterministic");
                                        }
                                    }
                                }
                                else
                                {
                                    Check(node.ParticleCount == 0, "Particles rendered before the clip starts");
                                }
                            }
                            player.Stop();
                            host.Render();
                            Check(!node.SequenceControlled && node.PlaybackState == initialState && Math.Abs(node.SimulationTime - initialTime) < .003, "Stop failed to restore particle playback");
                            Check(node.Source == "base" && node.CurrentSource == "base", "Sequence rewrote the authored particle source");
                        }
                        var invalid = SequenceDefinition.Parse(sequence.ToJson());
                        invalid.Tracks[2].Source = "missing";
                        Reject(() => { using (var failed = new SequencePlayer(host.Document.Body, invalid)) { } });
                        host.Render();
                        Check(!node.SequenceControlled, "Failed construction leaked particle ownership");
                        Check(Math.Abs(host.Document.Styles.GetComputed(host.Document.Q("panel")).GetNumber("opacity") - .4) < .003, "Failed construction leaked CSS sampling layer");
                        using (var player = new SequencePlayer(host.Document.Body, sequence))
                        {
                            player.Play();
                            host.Advance(.5f);
                            host.Render();
                            Check(Math.Abs(node.SimulationTime - .6) < .003, "Runtime master clock did not drive particles");
                            host.Document.LoadHtml("<!doctype html><html><head></head><body></body></html>", "ui/reloaded.html");
                            Check(!node.IsValid, "Unload kept particle node alive");
                        }
                    }
                }
                Check(prefabBefore.SequenceEqual(File.ReadAllBytes(prefabPath)) && materialBefore.SequenceEqual(File.ReadAllBytes(materialPath)), "Animation preview changed source assets");
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/animation-effect.json", "{\"passed\":" + count + ",\"failed\":0}");
                Debug.Log("URUI Animation Effect PASS " + count);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/animation-effect-failure.txt", error.ToString());
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
            finally
            {
                if (original)
                {
                    UnityEngine.Object.DestroyImmediate(original);
                }
            }
        }
    }
}
