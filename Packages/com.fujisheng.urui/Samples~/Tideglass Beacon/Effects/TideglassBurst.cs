using System;
using UnityEngine;
using URUI.Animation;
using URUI.Documents;
using URUI.Effect;
using URUI.Effect.Rendering;
using URUI.Elements;
using URUI.Hosting;
using URUI.Resources;
using URUI.Styles;

namespace URUI.Samples.Tideglass
{
    // The Core sample still imports when the optional Effect package is absent.
    internal sealed class TideglassBurst : ITrialBurst, IEffectResourceResolver
    {
        readonly TideglassAssets assets;
        readonly Document document;
        readonly EffectModule module;
        readonly Material material;
        readonly GameObject emitter;
        SequencePlayer sequence;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            TideglassDemo.RegisterInstrumentEffects = doc => doc.Effects.Register(WaveEffect.Define());
            TideglassTrial.CreateBurst = (assets, surface, parent) => new TideglassBurst(assets, surface, parent);
        }

        TideglassBurst(TideglassAssets assets, Surface surface, Transform parent)
        {
            this.assets = assets;
            var shader = Shader.Find("URUI.Effect/Particle");
            if (!shader)
                throw new InvalidOperationException("The URUI Effect particle shader is missing.");

            material = new Material(shader) { name = "Tideglass Sparks", hideFlags = HideFlags.HideAndDontSave };
            material.mainTexture = TideglassAssets.Get<Texture2D>(TideglassAssets.Root + "Images/spark");
            material.SetFloat("_Intensity", 1.4f);

            emitter = new GameObject("Tideglass burst source") { hideFlags = HideFlags.HideAndDontSave };
            emitter.SetActive(false);
            var system = emitter.AddComponent<ParticleSystem>();
            var main = system.main;
            main.duration = .8f;
            main.loop = false;
            main.playOnAwake = false;
            main.startSpeed = new ParticleSystem.MinMaxCurve(93, 265);
            main.startSize = new ParticleSystem.MinMaxCurve(4, 21);
            main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .68f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 80;
            var emission = system.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(.018f, 34) });
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 10;
            var lifetime = system.colorOverLifetime;
            lifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.65f, .27f), new GradientAlphaKey(0, 1) });
            lifetime.color = gradient;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.enabled = false;
            emitter.SetActive(true);

            document = assets.CreateDocument(surface, parent, "Tideglass Effect Layer", withEffects: true);
            try
            {
                module = new EffectModule(document, this);
                TideglassAssets.Load(document, "Particles");
                document.DocumentElement.PickingMode = PickingMode.Ignore;
                document.Body.PickingMode = PickingMode.Ignore;
                document.SetPresentation(4, 0, true, false);
                document.SetInputEnabled(false);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Fire(int skill)
        {
            Stop();
            material.SetColor("_Color", skill switch
            {
                1 => new Color(.61f, .79f, 1),
                2 => new Color(.84f, .59f, 1),
                _ => new Color(.45f, .94f, 1)
            });
            sequence = assets.Play(document, skill == 2 ? "BurstFinisher" : "Burst");
        }

        public void Stop()
        {
            sequence?.Dispose();
            sequence = null;
        }

        public GameObject ResolveParticles(string source) => source == "beacon-burst" ? emitter : null;
        public ImageLease AcquireImage(ComputedImageValue image) => assets.ResolveImage(image.Url);

        public void Dispose()
        {
            Stop();
            document.Unload();
            module?.Dispose();
            UnityEngine.Object.Destroy(document.gameObject);
            UnityEngine.Object.Destroy(emitter);
            UnityEngine.Object.Destroy(material);
        }
    }
}
