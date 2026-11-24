using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using URUI.Editor.Inspection;
using URUI.Effect.Editor.Authoring;
using URUI.Effect.Rendering;
using URUI.Resources;

namespace URUI.Effect.Tests.Editor
{
    public sealed class EffectMemoryLeakRegressionTests
    {
        static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void EffectModuleUnloadClearsBoundSurfaces()
        {
            var shader = UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter");
            var sourceMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var surfaceOutput = new TextureEffectSurface(16, 16);
            EffectModule module = null;
            try
            {
                using var preview = RuntimeInspectionBridge.CreatePreview(64, 64);
                var document = preview.Document;
                module = new EffectModule(document);
                document.ImageResolver = _ => new ImageLease(Texture2D.whiteTexture);
                document.LoadHtml("<!doctype html><html><head></head><body><img id='fx' src='a.png'></body></html>", "/module-unload.html");
                // Deliberately keep binding undisposed across Document.Unload().
                var binding = module.BindSurface(document.Q("fx"), new SurfaceEffectInput(surfaceOutput, Texture2D.whiteTexture, sourceMat, () => 1));
                GC.KeepAlive(binding);

                var surfacesField = typeof(EffectModule).GetField("surfaces", InstancePrivate);
                Assert.That(((IDictionary)surfacesField.GetValue(module)).Count, Is.EqualTo(1));

                document.Unload();
                Assert.That(((IDictionary)surfacesField.GetValue(module)).Count, Is.Zero,
                    "EffectModule.Unloaded must clear stale SurfaceEffectInput bindings.");
            }
            finally
            {
                module?.Dispose();
                surfaceOutput.Dispose();
                UnityEngine.Object.DestroyImmediate(sourceMat);
                GpuRetirement.Collect();
            }
        }

        [Test]
        public void EffectPlaybackDisposeClearsDelegatesAfterNaturalCompletion()
        {
            var def = new EffectTweenDefinition
            {
                Duration = 0.5f,
                WrapMode = EffectWrapMode.Once,
                Channels = EffectTweenChannels.Event
            };
            int completedCalls = 0;
            var playback = new EffectPlayback(def);
            playback.ProgressChanged += _ => { };
            playback.Completed += () => completedCalls++;
            playback.Advance(1.0);
            Assert.That(completedCalls, Is.EqualTo(1));

            playback.Dispose();

            var progressField = typeof(EffectPlayback).GetField("ProgressChanged", InstancePrivate);
            var completedField = typeof(EffectPlayback).GetField("Completed", InstancePrivate);
            Assert.That(progressField.GetValue(playback), Is.Null, "Dispose() must clear ProgressChanged even after natural completion.");
            Assert.That(completedField.GetValue(playback), Is.Null, "Dispose() must clear Completed even after natural completion.");
        }

        [Test]
        public void ParticlePainterOverrideMaterialCleansUpTransientPrograms()
        {
            var shader = UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle");
            Assert.That(shader, Is.Not.Null);
            var matA = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var matUnused = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var matReplacement = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var prefab = new GameObject("Particle override leak prefab") { hideFlags = HideFlags.HideAndDontSave };
            prefab.AddComponent<ParticleSystem>();
            prefab.GetComponent<ParticleSystemRenderer>().sharedMaterial = matA;
            ParticlePainter painter = null;
            try
            {
                using var preview = RuntimeInspectionBridge.CreatePreview(64, 64);
                var document = preview.Document;
                document.LoadHtml("<!doctype html><html><head></head><body><div id='p'></div></body></html>", "/particle-override-leak.html");
                painter = new ParticlePainter(document.Q("p"), prefab);

                var stateField = typeof(ParticlePainter).GetField("state", InstancePrivate);
                var stateObj = stateField.GetValue(painter);
                var programsField = stateObj.GetType().GetField("programs", InstancePrivate);
                var programs = (IDictionary)programsField.GetValue(stateObj);
                Assert.That(programs.Count, Is.EqualTo(1));

                // Invalid override must not pollute programs dictionary.
                Assert.Throws<InvalidOperationException>(() => painter.OverrideMaterial(matUnused, matReplacement));
                Assert.That(programs.Count, Is.EqualTo(1));

                using (painter.OverrideMaterial(matA, matReplacement))
                {
                    Assert.That(programs.Contains(matReplacement), Is.True);
                }
                Assert.That(programs.Contains(matReplacement), Is.False,
                    "Disposing OverrideMaterial lease must remove transient replacement material from SourceState.programs.");
                Assert.That(programs.Count, Is.EqualTo(1));
                painter.Dispose();
                painter = null;
                document.Unload();
            }
            finally
            {
                painter?.Dispose();
                UnityEngine.Object.DestroyImmediate(prefab);
                UnityEngine.Object.DestroyImmediate(matA);
                UnityEngine.Object.DestroyImmediate(matUnused);
                UnityEngine.Object.DestroyImmediate(matReplacement);
                GpuRetirement.Collect();
            }
        }

        [Test]
        public void EffectAssetSessionEnsureMaterialIsolationReusesCloneInsteadOfLeaking()
        {
            var shader = UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle");
            var originalMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var prefab = new GameObject("Isolation leak prefab") { hideFlags = HideFlags.HideAndDontSave };
            prefab.AddComponent<ParticleSystem>();
            prefab.GetComponent<ParticleSystemRenderer>().sharedMaterial = originalMat;
            try
            {
                using var session = new EffectAssetSession(prefab);
                var working = (GameObject)session.Working;
                var renderer = working.GetComponent<ParticleSystemRenderer>();
                var materialsField = typeof(EffectAssetSession).GetField("materials", InstancePrivate);
                var materials = (IDictionary)materialsField.GetValue(session);
                Assert.That(materials.Count, Is.EqualTo(1));

                var firstClone = renderer.sharedMaterial;
                // Simulate repeated Undo restoring the original material reference onto the renderer.
                for (int i = 0; i < 20; i++)
                {
                    renderer.sharedMaterials = new[] { originalMat };
                    session.EnsureMaterialIsolation();
                }

                Assert.That(materials.Count, Is.EqualTo(1),
                    "EnsureMaterialIsolation must reuse the existing isolated clone for an original material instead of creating new clones.");
                Assert.That(renderer.sharedMaterial, Is.SameAs(firstClone));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
                UnityEngine.Object.DestroyImmediate(originalMat);
                GpuRetirement.Collect();
            }
        }

        sealed class FakeContext : IAuthoringContext
        {
            public string SessionId { get; set; }
            public string TargetId { get; set; }
            public bool IsLive { get; set; }
            public bool IsObserved { get; set; } = true;
            public InspectedNode Node { get; set; }
            public long Revision { get; set; }
            public void Set(string property, string value) { }
            public void SetWithStyleSheet(string property, string value, string source) { }
            public void Refresh() { }
            public void Report(string message) { }
        }

        [Test]
        public void EffectAuthoringExtensionPrunesStaleBindings()
        {
            try
            {
                using var preview = RuntimeInspectionBridge.CreatePreview(64, 64);
                var document = preview.Document;
                using var extension = new EffectAuthoringExtension();
                var bindingsField = typeof(EffectAuthoringExtension).GetField("bindings", InstancePrivate);
                var bindings = (IDictionary)bindingsField.GetValue(extension);

                document.LoadHtml(
                    "<!doctype html><html><head></head><body><div id='a'></div><div id='b'></div></body></html>",
                    "/ext-prune-1.html");
                var staleNode = RuntimeInspectionBridge.Inspect(document.Q("a"));
                for (int i = 0; i < 24; i++)
                {
                    var ctx = new FakeContext
                    {
                        SessionId = "s1",
                        TargetId = "t" + i,
                        Node = staleNode
                    };
                    extension.Execute(ctx, "asset.read", new Dictionary<string, string>());
                }
                Assert.That(bindings.Count, Is.EqualTo(24));

                // Invalidate all prior nodes by unloading & reloading the document.
                document.Unload();
                document.LoadHtml("<!doctype html><html><head></head><body><div id='fresh'></div></body></html>", "/ext-prune-2.html");
                var freshCtx = new FakeContext
                {
                    SessionId = "s2",
                    TargetId = "fresh",
                    Node = RuntimeInspectionBridge.Inspect(document.Q("fresh"))
                };
                extension.Execute(freshCtx, "asset.read", new Dictionary<string, string>());

                Assert.That(bindings.Count, Is.EqualTo(1),
                    "EffectAuthoringExtension must prune stale clean bindings whose inspected nodes are no longer valid.");
                document.Unload();
            }
            finally
            {
                GpuRetirement.Collect();
            }
        }
    }
}
