using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using URUI.Editor.Inspection;
using URUI.Effect;
using URUI.Effect.Editor.Authoring;
using URUI.Resources;
using URUI.Effect.Elements;
using System.Collections.Generic;

namespace URUI.Authoring.Tests
{
    public static class EffectValidation
    {
        static int count;
        sealed class Resolver : IEffectResourceResolver
        {
            internal GameObject Source;
            public GameObject ResolveParticles(string source) => Source;
            public ImageLease AcquireImage(URUI.Styles.ComputedImageValue image) => null;
        }
        sealed class Context : IAuthoringContext
        {
            internal EditorPreviewHost Host;
            public string SessionId { get; } = Guid.NewGuid().ToString("N");
            public string TargetId => Node.Id;
            public bool IsLive => false;
            public bool IsObserved => true;
            public InspectedNode Node
            {
                get; set;
            }
            public long Revision => 0;
            public void Set(string property, string value) => Node.Write(property, value);
            public void SetWithStyleSheet(string property, string value, string source) => Set(property, value);
            public void Refresh() => Host.Render();
            public void Report(string message)
            {
            }
        }
        [Serializable]
        sealed class State
        {
            public long assetRevision; public bool dirty;
        }
        static State ReadState(object result) => JsonUtility.FromJson<State>(JsonUtility.ToJson(result));
        static Dictionary<string, string> Args(params string[] pairs)
        {
            var result = new Dictionary<string, string>();
            for (int i = 0; i < pairs.Length; i += 2)
                result.Add(pairs[i], pairs[i + 1]);
            return result;
        }
        static void Check(bool condition)
        {
            count++;
            if (!condition)
                throw new Exception("Effect Authoring assertion " + count);
        }
        public static int Run()
        {
            var author = URUI.Authoring.Sessions.AuthoringEnvironment.Author;
            string target = null;
            foreach (var node in author.Model.Nodes)
                if (node.Tag == "div")
                {
                    target = node.Key;
                    break;
                }
            string rebound = author.SetWithStyleSheet(target, "style.--urui-effect", "ui", "/urui-effect/Effect.css", author.Revision);
            Check(author.File.Text.Contains("href=\"/urui-effect/Effect.css\""));
            Check(URUI.Authoring.Sessions.AuthoringEnvironment.Preview.Node(rebound).Computed("--urui-fx-tone-filter") != null);
            URUI.Authoring.Sessions.AuthoringEnvironment.Preview.Dispose();
            const string materialPath = "Assets/AuthoringChecks/particles.mat", prefabPath = "Assets/AuthoringChecks/particles.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
            AssetDatabase.DeleteAsset(materialPath);
            var material = new Material(Shader.Find("URUI.Effect/Particle"));
            material.SetFloat("_DstBlend", 10);
            AssetDatabase.CreateAsset(material, materialPath);
            var source = new GameObject("Authored particles", typeof(ParticleSystem));
            var system = source.GetComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.startSpeed = 0;
            main.startLifetime = 10;
            main.startSize = 10;
            var emission = system.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
            var shape = system.shape;
            shape.enabled = false;
            source.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            UnityEngine.Object.DestroyImmediate(source);
            AssetDatabase.SaveAssets();
            string prefabBefore = File.ReadAllText(prefabPath), materialBefore = File.ReadAllText(materialPath);
            using (var working = new EffectAssetSession(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)))
            {
                working.SetParticle("startSize", "30");
                Check(((GameObject)working.Working).GetComponent<ParticleSystem>().main.startSize.constant == 30);
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<ParticleSystem>().main.startSize.constant == 10);
                using (var host = RuntimeInspectionBridge.CreatePreview(480, 320))
                using (var module = new EffectModule(host.Document, new Resolver { Source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) }))
                {
                    host.Document.TextResources = new[] { new TextResource("/urui-effect/Effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                    host.Document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui-effect/Effect.css'><style>body{height:100%;}.has-particles{--urui-decorator:shader(particles);}</style></head><body><div id='particles' style='position:absolute;left:100px;top:100px;width:100px;height:100px;'></div><urui-particles id='declared' src='fixture' style='position:absolute;left:200px;top:100px;width:100px;height:100px;'></urui-particles></body></html>", "ui/particles.html");
                    host.Render();
                    using (var painter = new ParticlePainter(host.Document.Q("particles"), (GameObject)working.Working, false))
                    {
                        painter.Seek(.5f, 7);
                        host.Render();
                        int before = painter.ParticleCount;
                        Check(before > 0);
                        painter.Seek(.5f, 7);
                        host.Render();
                        Check(before == painter.ParticleCount);
                        using (painter.OverridePlayback())
                            painter.Seek(1, 7);
                        Check(painter.SimulationTime == .5f);
                        painter.Step(1f / 60);
                        Check(painter.SimulationTime > .5f && painter.PlaybackState == ParticlePlaybackState.Paused);
                        working.SetParticle("emissionRate", "40");
                        painter.RefreshSource();
                        painter.Seek(.5f, 7);
                        host.Render();
                        Check(painter.ParticleCount > before);
                    }
                    var controller = module.GetController(host.Document.Q("particles"));
                    var preset = ScriptableObject.CreateInstance<EffectPreset>();
                    var parameters = preset.CreateParameters();
                    parameters.ToneIntensity = .6f;
                    preset.SetParameters(parameters);
                    float original = controller.CreateParameters().ToneIntensity;
                    using (controller.OverridePreset(preset))
                        Check(Mathf.Abs(controller.CreateParameters().ToneIntensity - .6f) < .001f);
                    var outer = controller.OverridePreset(preset);
                    parameters.ToneIntensity = .2f;
                    preset.SetParameters(parameters);
                    var inner = controller.OverridePreset(preset);
                    outer.Dispose();
                    Check(Mathf.Abs(controller.CreateParameters().ToneIntensity - .2f) < .001f);
                    inner.Dispose();
                    Check(controller.CreateParameters().ToneIntensity == original);
                    UnityEngine.Object.DestroyImmediate(preset);
                    using (var extension = new EffectAuthoringExtension())
                    {
                        var particles = host.Document.Q<ParticlesElement>("declared");
                        particles.Seek(.5f);
                        var context = new Context { Host = host, Node = RuntimeInspectionBridge.Inspect(particles) };
                        var state = ReadState(extension.Execute(context, "asset.open", Args("path", materialPath)));
                        state = ReadState(extension.Execute(context, "asset.setMaterial", Args("assetRevision", state.assetRevision.ToString(), "property", "_Color", "value", "#ff0000")));
                        using (var pixels = host.Capture())
                            Check(pixels.Texture.GetPixel(201, 219).g < .1f);
                        Check(material.GetColor("_Color") == Color.white);
                        var pending = extension.PendingAssets();
                        Check(pending.Count == 1 && pending[0].Path == materialPath && pending[0].Revision == state.assetRevision);
                        bool staleApply = false;
                        try
                        {
                            extension.ApplyAsset(pending[0].Id, pending[0].Revision - 1);
                        }
                        catch (InvalidOperationException) { staleApply = true; }
                        Check(staleApply && material.GetColor("_Color") == Color.white);
                        long editedRevision = state.assetRevision;
                        Undo.FlushUndoRecordObjects();
                        Undo.PerformUndo();
                        state = ReadState(extension.Execute(context, "asset.read", Args()));
                        Check(state.assetRevision > editedRevision);
                        Check(!state.dirty);
                        using (var pixels = host.Capture())
                            Check(pixels.Texture.GetPixel(201, 219).g > .9f);
                        Undo.PerformRedo();
                        using (var pixels = host.Capture())
                            Check(pixels.Texture.GetPixel(201, 219).g < .1f);
                        Undo.PerformUndo();
                        state = ReadState(extension.Execute(context, "asset.read", Args()));
                        bool rejected = false;
                        try
                        {
                            extension.Execute(context, "asset.setMaterial", Args("assetRevision", editedRevision.ToString(), "property", "_Color", "value", "#00ff00"));
                        }
                        catch (InvalidOperationException) { rejected = true; }
                        Check(rejected);
                        state = ReadState(extension.Execute(context, "asset.setMaterial", Args("assetRevision", state.assetRevision.ToString(), "property", "_Color", "value", "#ff0000")));
                        extension.Execute(context, "asset.discard", Args("assetRevision", state.assetRevision.ToString()));
                        using (var pixels = host.Capture())
                            Check(pixels.Texture.GetPixel(201, 219).g > .9f);
                        state = ReadState(extension.Execute(context, "asset.open", Args("path", prefabPath)));
                        extension.Execute(context, "particles.seek", Args("time", ".5"));
                        host.Render();
                        int originalCount = particles.ParticleCount;
                        state = ReadState(extension.Execute(context, "asset.setParticle", Args("assetRevision", state.assetRevision.ToString(), "property", "emissionRate", "value", "40")));
                        host.Render();
                        Check(particles.ParticleCount > originalCount);
                        editedRevision = state.assetRevision;
                        Undo.FlushUndoRecordObjects();
                        Undo.PerformUndo();
                        host.Render();
                        state = ReadState(extension.Execute(context, "asset.read", Args()));
                        Check(state.assetRevision > editedRevision);
                        Check(particles.ParticleCount == originalCount);
                        extension.Execute(context, "asset.discard", Args("assetRevision", state.assetRevision.ToString()));
                        const string presetPath = "Assets/AuthoringChecks/preview-preset.asset";
                        AssetDatabase.DeleteAsset(presetPath);
                        var presetAsset = ScriptableObject.CreateInstance<EffectPreset>();
                        AssetDatabase.CreateAsset(presetAsset, presetPath);
                        string presetBefore = File.ReadAllText(presetPath);
                        state = ReadState(extension.Execute(context, "asset.open", Args("path", presetPath)));
                        state = ReadState(extension.Execute(context, "asset.setPreset", Args("assetRevision", state.assetRevision.ToString(), "property", "ToneIntensity", "value", "0.35")));
                        Check(Mathf.Abs(module.GetController(particles).CreateParameters().ToneIntensity - .35f) < .001f);
                        editedRevision = state.assetRevision;
                        Undo.FlushUndoRecordObjects();
                        Undo.PerformUndo();
                        state = ReadState(extension.Execute(context, "asset.read", Args()));
                        Check(state.assetRevision > editedRevision);
                        Check(!state.dirty);
                        Check(module.GetController(particles).CreateParameters().ToneIntensity == 1);
                        Undo.PerformRedo();
                        Check(Mathf.Abs(module.GetController(particles).CreateParameters().ToneIntensity - .35f) < .001f);
                        state = ReadState(extension.Execute(context, "asset.read", Args()));
                        extension.Execute(context, "asset.discard", Args("assetRevision", state.assetRevision.ToString()));
                        Check(File.ReadAllText(presetPath) == presetBefore);
                        Check(extension.PendingAssets().Count == 0);
                        state = ReadState(extension.Execute(context, "asset.open", Args("path", presetPath)));
                        state = ReadState(extension.Execute(context, "asset.setPreset", Args("assetRevision", state.assetRevision.ToString(), "property", "ToneIntensity", "value", "0.6")));
                        var selectedAsset = extension.PendingAssets()[0];
                        extension.ApplyAsset(selectedAsset.Id, selectedAsset.Revision);
                        Check(extension.PendingAssets().Count == 0 && Mathf.Abs(presetAsset.CreateParameters().ToneIntensity - .6f) < .001f);
                    }
                }
            }
            Check(File.ReadAllText(prefabPath) == prefabBefore);
            Check(File.ReadAllText(materialPath) == materialBefore);
            using (var working = new EffectAssetSession(material))
            {
                working.SetMaterial("_DstBlend", "1");
                Check(material.GetFloat("_DstBlend") != 1);
            }
            Check(File.ReadAllText(materialPath) == materialBefore);
            const string intShaderPath = "Assets/AuthoringChecks/integer.shader";
            File.WriteAllText(intShaderPath, "Shader \"Hidden/URUI/AuthoringInteger\" { Properties { _Count(\"Count\", Integer) = 0 } SubShader { Pass {} } }");
            AssetDatabase.ImportAsset(intShaderPath, ImportAssetOptions.ForceSynchronousImport);
            var integerMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>(intShaderPath));
            using (var working = new EffectAssetSession(integerMaterial))
            {
                working.SetMaterial("_Count", "16777217");
                Check(((Material)working.Working).GetInteger("_Count") == 16777217);
            }
            UnityEngine.Object.DestroyImmediate(integerMaterial);
            using (var working = new EffectAssetSession(material))
            {
                string originalName = material.name;
                ((Material)working.Working).name = "Preview-only name";
                ((Material)working.Working).SetFloat("_DstBlend", 5);
                working.Apply();
                Check(material.name == originalName);
                ((Material)working.Working).SetFloat("_DstBlend", 1);
                working.Apply();
                Check(material.GetFloat("_DstBlend") == 1);
            }
            var purePreset = ScriptableObject.CreateInstance<EffectPreset>();
            using (var working = new EffectAssetSession(purePreset))
            {
                working.SetPreset("ToneIntensity", "0.35");
                Check(Mathf.Abs(((EffectPreset)working.Working).CreateParameters().ToneIntensity - .35f) < .0001f);
                Check(purePreset.CreateParameters().ToneIntensity == 1);
            }
            UnityEngine.Object.DestroyImmediate(purePreset);
            return count;
        }
    }
}
