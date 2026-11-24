using System;
using System.Collections.Generic;
using System.IO;
using Solstice.Rendering;
using Solstice.Scene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using URUI.Editor.Compilation;
using URUI.Hosting;
using URUI.Resources;

namespace Solstice.Editor
{
    /// <summary>
    /// Generates authentic Unity 6 URP pipeline assets, pure URUI-compatible particle prefabs,
    /// AOT CompiledDocumentAsset for Backpack, crisp texture import settings, and Solstice.unity.
    /// </summary>
    public static class SolsticeProjectSetup
    {
        const string RootAssetDir = "Assets/Solstice";
        const string ResRoot = RootAssetDir + "/Resources/URUI/Solstice";
        const string PrefabDir = ResRoot + "/Prefabs";
        const string PipelineDir = RootAssetDir + "/Settings";
        const string ScenePath = RootAssetDir + "/Scenes/Solstice.unity";

        [MenuItem("Tools/SOLSTICE/Rebuild Showcase Assets")]
        public static void BuildProjectAssets()
        {
            EnsureFolder(PipelineDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(RootAssetDir + "/Scenes");

            ConfigureImages();
            BuildParticlePrefabs();
            ConfigureUrpPipeline();
            CompileBackpackDocument();
            BuildMainScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("SOLSTICE_SETUP_PASS");
        }

        static void EnsureFolder(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized)) return;
            var parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            var name = Path.GetFileName(normalized);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            if (!string.IsNullOrEmpty(parent))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        static void ConfigureImages()
        {
            var imgDir = ResRoot + "/Images";
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { imgDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer) continue;

                var file = Path.GetFileNameWithoutExtension(path);
                var isDissolve = string.Equals(file, "fx-dissolve-noise", StringComparison.Ordinal);
                var isLut = string.Equals(file, "fx-holo-lut", StringComparison.Ordinal);

                var changed = false;
                if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
                if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
                if (importer.streamingMipmaps) { importer.streamingMipmaps = false; changed = true; }
                if (importer.ignoreMipmapLimit != true) { importer.ignoreMipmapLimit = true; changed = true; }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    changed = true;
                }
                var desiredWrap = isDissolve ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                if (importer.wrapMode != desiredWrap) { importer.wrapMode = desiredWrap; changed = true; }
                if (isLut && importer.filterMode != FilterMode.Bilinear) { importer.filterMode = FilterMode.Bilinear; changed = true; }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        static void BuildParticlePrefabs()
        {
            var shader = Shader.Find("URUI.Effect/Particle");
            if (!shader)
            {
                throw new InvalidOperationException("Missing URUI.Effect/Particle shader.");
            }

            CreateParticlePrefab(
                PrefabDir + "/ParticleStar.prefab",
                PrefabDir + "/ParticleStarMat.mat",
                ResRoot + "/Images/fx-particle-star.png",
                shader,
                new Color(1.0f, 0.88f, 0.48f, 1.0f),
                new Color(0.24f, 0.92f, 0.86f, 1.0f),
                startSize: 22f,
                speed: 90f,
                burstCount: 36,
                rateOverTime: 18f,
                maxParticles: 90,
                life: 1.55f);

            CreateParticlePrefab(
                PrefabDir + "/ParticleEmber.prefab",
                PrefabDir + "/ParticleEmberMat.mat",
                ResRoot + "/Images/fx-particle-ember.png",
                shader,
                new Color(1.0f, 0.42f, 0.24f, 1.0f),
                new Color(1.0f, 0.85f, 0.38f, 1.0f),
                startSize: 18f,
                speed: 140f,
                burstCount: 44,
                rateOverTime: 0f,
                maxParticles: 80,
                life: 0.9f);
        }

        static void CreateParticlePrefab(
            string prefabPath,
            string matPath,
            string texPath,
            Shader shader,
            Color colorA,
            Color colorB,
            float startSize,
            float speed,
            short burstCount,
            float rateOverTime,
            int maxParticles,
            float life)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.shader = shader;
            mat.mainTexture = tex;
            mat.SetFloat("_Intensity", 1.35f);
            EditorUtility.SetDirty(mat);

            var tempGo = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            try
            {
                var ps = tempGo.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                ps.randomSeed = 2026;

                var main = ps.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = life;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.65f, life);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
                main.startSize = new ParticleSystem.MinMaxCurve(startSize * 0.65f, startSize);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
                main.gravityModifier = 0.08f;
                main.maxParticles = maxParticles;

                var emission = ps.emission;
                emission.rateOverTime = rateOverTime;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burstCount) });

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 28f;

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 0.72f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.14f), new GradientAlphaKey(0f, 1f) });
                colorOverLifetime.color = grad;

                var renderer = tempGo.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = mat;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

                PrefabUtility.SaveAsPrefabAsset(tempGo, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempGo);
            }
        }

        static void ConfigureUrpPipeline()
        {
            var rendererPath = PipelineDir + "/SolsticeRenderer.asset";
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (!rendererData)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            UruiUrpRendererFeature uruiFeature = null;
            foreach (var existing in rendererData.rendererFeatures)
            {
                if (existing is UruiUrpRendererFeature match)
                {
                    uruiFeature = match;
                    break;
                }
            }
            if (!uruiFeature)
            {
                uruiFeature = ScriptableObject.CreateInstance<UruiUrpRendererFeature>();
                uruiFeature.name = nameof(UruiUrpRendererFeature);
                AssetDatabase.AddObjectToAsset(uruiFeature, rendererData);
                rendererData.rendererFeatures.Add(uruiFeature);
                rendererData.SetDirty();
            }

            var urpPath = PipelineDir + "/SolsticeURPAsset.asset";
            var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
            if (!urpAsset)
            {
                urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urpAsset, urpPath);
            }
            urpAsset.supportsHDR = false;
            urpAsset.msaaSampleCount = 4;
            EditorUtility.SetDirty(urpAsset);
            EditorUtility.SetDirty(rendererData);

            GraphicsSettings.defaultRenderPipeline = urpAsset;
            for (var q = 0; q < QualitySettings.count; q++)
            {
                QualitySettings.SetQualityLevel(q, false);
                QualitySettings.renderPipeline = urpAsset;
            }
        }

        static void CompileBackpackDocument()
        {
            var htmlPath = ResRoot + "/Html/Backpack.html";
            var htmlAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(htmlPath);
            if (!htmlAsset)
            {
                throw new InvalidOperationException("Missing Backpack.html at " + htmlPath);
            }

            var deps = new List<TextResource>
            {
                new("/urui/base.css", Resources.Load<TextAsset>("URUI/base")),
                new("/urui/controls.css", Resources.Load<TextAsset>("URUI/controls")),
                new("/urui/behaviors.css", Resources.Load<TextAsset>("URUI/behaviors")),
                new("/urui-effect/Effect.css", Resources.Load<TextAsset>("URUI.Effect/Effect")),
                new("/solstice/Css/tokens.css", AssetDatabase.LoadAssetAtPath<TextAsset>(ResRoot + "/Css/tokens.css")),
                new("/solstice/Css/base.css", AssetDatabase.LoadAssetAtPath<TextAsset>(ResRoot + "/Css/base.css")),
                new("/solstice/Css/solstice.css", AssetDatabase.LoadAssetAtPath<TextAsset>(ResRoot + "/Css/solstice.css")),
                new("/solstice/Fonts/LatoLatin-Regular.ttf.bytes", AssetDatabase.LoadAssetAtPath<TextAsset>(ResRoot + "/Fonts/LatoLatin-Regular.ttf.bytes")),
                new("/solstice/Fonts/Solstice-SC.ttf.bytes", AssetDatabase.LoadAssetAtPath<TextAsset>(ResRoot + "/Fonts/Solstice-SC.ttf.bytes")),
                new("/solstice/Fonts/RobotoMono-Regular.ttf.bytes", AssetDatabase.LoadAssetAtPath<TextAsset>(ResRoot + "/Fonts/RobotoMono-Regular.ttf.bytes")),
            };

            foreach (var family in new[] { "Serif", "Sans", "Mono" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = $"Liberation{family}-{face}.ttf";
                    deps.Add(new TextResource($"/urui/Fonts/{name}.bytes", Resources.Load<TextAsset>("URUI/Fonts/" + name)));
                }
            }

            deps.RemoveAll(r => r.Asset == null);
            CompiledDocumentCompiler.CompileAndSave(
                htmlAsset,
                "/solstice/Html/Backpack.html",
                ResRoot + "/Html/Backpack.compiled.asset",
                deps);
        }

        static void BuildMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.082f, 0.145f, 1f);
            cam.fieldOfView = 52f;
            cam.transform.position = new Vector3(0f, 0.3f, 0f);
            cam.transform.rotation = Quaternion.identity;
            camGo.AddComponent<UniversalAdditionalCameraData>();

            var lightGo = new GameObject("Sanctuary Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.94f, 0.86f);
            lightGo.transform.rotation = Quaternion.Euler(38f, -26f, 0f);
            lightGo.AddComponent<UniversalAdditionalLightData>();

            var stageGo = new GameObject("SolsticeStage");
            var stageCtrl = stageGo.AddComponent<SolsticeStageController>();

            var hostGo = new GameObject("SolsticeHost");
            var surface = hostGo.AddComponent<Surface>();
            var app = hostGo.AddComponent<SolsticeApp>();
            var audio = hostGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;

            var so = new SerializedObject(app);
            so.FindProperty("surface").objectReferenceValue = surface;
            so.FindProperty("stage").objectReferenceValue = stageCtrl;
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }

        public static void BuildCoreSamplePackageAssetsStage1()
        {
            var pkgSampleDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Packages/com.fujisheng.urui/Samples~/Solstice")).Replace('\\', '/');
            var tempAssetDir = "Assets/_TempCoreSolstice";
            var fullTempDir = Path.GetFullPath(Path.Combine(Application.dataPath, "_TempCoreSolstice")).Replace('\\', '/');

            if (Directory.Exists(fullTempDir)) Directory.Delete(fullTempDir, true);
            if (File.Exists(fullTempDir + ".meta")) File.Delete(fullTempDir + ".meta");
            AssetDatabase.Refresh();

            CopyDirectory(pkgSampleDir, fullTempDir);
            AssetDatabase.Refresh();

            var coreImgDir = tempAssetDir + "/Resources/URUI/SolsticeCore/Images";
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { coreImgDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer) continue;
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.streamingMipmaps = false;
                importer.ignoreMipmapLimit = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("CORE_SAMPLE_STAGE1_PASS");
        }

        public static void BuildCoreSamplePackageAssetsStage2()
        {
            var pkgSampleDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Packages/com.fujisheng.urui/Samples~/Solstice")).Replace('\\', '/');
            var tempAssetDir = "Assets/_TempCoreSolstice";
            var fullTempDir = Path.GetFullPath(Path.Combine(Application.dataPath, "_TempCoreSolstice")).Replace('\\', '/');

            try
            {
                Type coreAppType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    coreAppType = asm.GetType("URUI.Samples.Solstice.SolsticeCoreApp");
                    if (coreAppType != null) break;
                }
                if (coreAppType == null)
                {
                    throw new InvalidOperationException("Failed to locate URUI.Samples.Solstice.SolsticeCoreApp type.");
                }

                var coreScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.055f, 0.082f, 0.145f, 1f);

                var hostGo = new GameObject("SolsticeCoreHost");
                var surf = hostGo.AddComponent<Surface>();
                var comp = hostGo.AddComponent(coreAppType);
                var audio = hostGo.AddComponent<AudioSource>();
                audio.playOnAwake = false;

                var so = new SerializedObject(comp);
                so.FindProperty("surface").objectReferenceValue = surf;
                so.FindProperty("audioSource").objectReferenceValue = audio;
                so.ApplyModifiedPropertiesWithoutUndo();

                var tempScenePath = tempAssetDir + "/Solstice.unity";
                EditorSceneManager.SaveScene(coreScene, tempScenePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                SyncBackWithMeta(fullTempDir, pkgSampleDir);
                Debug.Log("CORE_SAMPLE_SCENE_PASS");
            }
            finally
            {
                AssetDatabase.DeleteAsset(tempAssetDir);
                AssetDatabase.Refresh();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(file) == "Solstice.unity")
                    continue;
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            }
            foreach (var dir in Directory.GetDirectories(src))
            {
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
            }
        }

        static void SyncBackWithMeta(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
            {
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            }
            foreach (var dir in Directory.GetDirectories(src))
            {
                SyncBackWithMeta(dir, Path.Combine(dst, Path.GetFileName(dir)));
            }
        }
    }
}
