using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;
using URUI.Effect.Elements;
namespace URUI.Authoring.Tests
{
    public static class AnimationSampleEffectValidation
    {
        static int count;
        static void Check(bool condition, string message)
        {
            count++;
            if (!condition)
                throw new Exception(message);
        }
        static string Copy(string name)
        {
            string root = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AuthoringEnvironment).Assembly).resolvedPath;
            string path = "Assets/" + name.Replace(" ", "");
            Directory.CreateDirectory(path);
            foreach (string file in Directory.GetFiles(Path.Combine(root, "Samples~/" + name)))
                File.Copy(file, Path.Combine(path, Path.GetFileName(file)), true);
            AssetDatabase.Refresh();
            return path;
        }
        public static void Run()
        {
            var errors = new List<string>();
            Application.LogCallback log = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception) errors.Add(message); };
            Application.logMessageReceived += log;
            try
            {
                string basic = Copy("Animation Demo");
                AuthoringEnvironment.Open(basic + "/Reward.html");
                AnimationEnvironment.EnsureWorkspace();
                AnimationEnvironment.Sample(.2);
                var document = AuthoringEnvironment.Preview.Document;
                float opacity = document.Styles.GetComputed(document.Q("reward")).GetNumber("opacity");
                Check(opacity > 0 && opacity < 1, "Basic sample did not animate");
                Directory.CreateDirectory("Evidence");
                AuthoringEnvironment.Preview.Capture(Path.GetFullPath("Evidence/sample-basic.png"));
                AnimationEnvironment.Stop();
                string effect = Copy("Animation Effect Demo");
                string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AuthoringEnvironment).Assembly).resolvedPath;
                byte[] sourceMaterial = File.ReadAllBytes(Path.Combine(package, "Samples~/Animation Effect Demo/particle.mat"));
                AuthoringEnvironment.Open(effect + "/Reward.html");
                AnimationEnvironment.EnsureWorkspace();
                AnimationEnvironment.Sample(.8);
                document = AuthoringEnvironment.Preview.Document;
                var particles = document.Q<ParticlesElement>("celebrate");
                Check(particles.SequenceControlled && particles.ParticleCount > 0, "Effect sample particles did not preview");
                Check(Math.Abs(particles.SimulationTime - .6f) < .003f, "Effect sample not on master time");
                AuthoringEnvironment.Preview.Capture(Path.GetFullPath("Evidence/sample-effect.png"));
                AnimationEnvironment.Stop();
                Check(!particles.SequenceControlled, "Sample stop retained particle ownership");
                string key = AuthoringEnvironment.Author.Model.Nodes.First(n => n.Attribute("id") == "celebrate").Key;
                var proxy = ElementSelectionProxy.ForAuthor(key);
                var extension = AuthoringEnvironment.Extensions.First(e => e.Id == "effect");
                extension.Execute(proxy, "asset.open", new Dictionary<string, string> { { "path", effect + "/particle.mat" } });
                var status = AuthoringEnvironment.PendingAssets();
                Check(status.Count == 0, "Clean asset shown dirty");
                // The extension protocol returns its own revision before the first edit.
                var state = extension.Execute(proxy, "asset.read", new Dictionary<string, string>());
                var parsed = UnityEngine.JsonUtility.FromJson<AssetState>(UnityEngine.JsonUtility.ToJson(state));
                extension.Execute(proxy, "asset.setMaterial", new Dictionary<string, string> { { "assetRevision", parsed.assetRevision.ToString() }, { "property", "_Color" }, { "value", "#ff0000" } });
                status = AuthoringEnvironment.PendingAssets();
                Check(status.Count == 1 && status[0].Path.EndsWith("particle.mat"), "Save list missed dirty native asset");
                byte[] before = File.ReadAllBytes(effect + "/particle.mat");
                SaveWorksetWindow.OpenOrSave();
                Check(before.SequenceEqual(File.ReadAllBytes(effect + "/particle.mat")), "Opening save list applied assets by default");
                EditorWindow.GetWindow<SaveWorksetWindow>().Close();
                AuthoringEnvironment.ApplyAsset(status[0]);
                Check(AuthoringEnvironment.PendingAssets().Count == 0 && !before.SequenceEqual(File.ReadAllBytes(effect + "/particle.mat")), "Explicit selected Apply failed");
                Check(sourceMaterial.SequenceEqual(File.ReadAllBytes(Path.Combine(package, "Samples~/Animation Effect Demo/particle.mat"))), "Sample validation modified package source material");
                Check(errors.Count == 0, "Unexpected Error/Exception: " + string.Join(";", errors));
                File.WriteAllText("Evidence/animation-samples.json", "{\"passed\":" + count + ",\"failed\":0}");
                Debug.Log("URUI Animation Samples PASS " + count);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Directory.CreateDirectory("Evidence"); File.WriteAllText("Evidence/animation-samples-failure.txt", error.ToString()); Debug.LogException(error); EditorApplication.Exit(1); }
            finally { Application.logMessageReceived -= log; }
        }
        [Serializable]
        sealed class AssetState
        {
            public long assetRevision;
        }
    }
}
