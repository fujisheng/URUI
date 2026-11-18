using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using URUI.Documents;
using URUI.Editor.Inspection;
using URUI.Effect.Elements;
using URUI.Resources;
using URUI.Styles;

namespace URUI.Effect.Editor.Authoring
{
    /// <summary>通过 Core Editor 契约发现的可选扩展，不引用 Authoring 程序集。</summary>
    public sealed class EffectAuthoringExtension : IAuthoringExtension, IAuthoringAssetWorkspace
    {
        sealed class PreviewBinding : IDisposable
        {
            internal Document Document;
            internal EffectModule Module;
            internal readonly List<ParticlesElement> Particles = new List<ParticlesElement>();
            internal Action Released;
            bool disposed;
            internal void Loaded(Document document)
            {
                Particles.Clear();
                foreach (var node in document.Query<ParticlesElement>().ToList())
                {
                    Particles.Add(node);
                    if (node.Source.Length != 0)
                    {
                        node.Seek(0);
                        node.Play();
                    }
                }
            }
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                Document.Loaded -= Loaded;
                Module.Dispose();
                Particles.Clear();
                Released?.Invoke();
            }
        }
        sealed class Resources : IEffectResourceResolver
        {
            internal Func<string, Type, UnityEngine.Object> Load;
            public GameObject ResolveParticles(string source) => Load(source, typeof(GameObject)) as GameObject;
            public ImageLease AcquireImage(ComputedImageValue image)
            {
                if (image.Kind != ComputedImageKind.Url)
                    return null;
                var texture = Load(RuntimeInspectionBridge.ResolveResourceUrl(image.SourceUrl, image.Url), typeof(Texture2D)) as Texture2D;
                return texture ? new ImageLease(texture) : null;
            }
        }
        sealed class Binding : IDisposable
        {
            internal EffectAssetSession Asset;
            internal UnityEditor.Editor Editor;
            internal IDisposable Override;
            internal IDisposable PlaybackOverride;
            internal IAuthoringContext Context;
            internal string LastNode;
            internal bool Playing, OwnsPlayback;
            internal string PropertyFilter = "";
            internal bool? InspectorExpanded;
            internal float Time, Speed = 1;
            internal long Revision;
            internal string WorkingVersion;
            internal readonly Dictionary<ParticleSystem, UnityEditor.Editor> ParticleEditors = new Dictionary<ParticleSystem, UnityEditor.Editor>();
            public void Dispose()
            {
                Override?.Dispose();
                Override = null;
                PlaybackOverride?.Dispose();
                PlaybackOverride = null;
                Playing = OwnsPlayback = false;
                LastNode = null;
                if (Editor)
                    UnityEngine.Object.DestroyImmediate(Editor);
                Editor = null;
                foreach (var editor in ParticleEditors.Values)
                    if (editor)
                        UnityEngine.Object.DestroyImmediate(editor);
                ParticleEditors.Clear();
                Asset?.Dispose();
                Asset = null;
            }
        }
        [Serializable]
        sealed class AssetState
        {
            public string path, type; public bool dirty; public long assetRevision; public float time; public int particleCount; public object properties;
        }
        readonly Dictionary<Document, PreviewBinding> previews = new Dictionary<Document, PreviewBinding>();
        readonly Dictionary<string, Binding> bindings = new Dictionary<string, Binding>();
        readonly List<AuthoringProperty> properties = new List<AuthoringProperty>();
        double lastTick;
        public IReadOnlyList<AuthoringAssetChange> PendingAssets()
        {
            var result = new List<AuthoringAssetChange>();
            foreach (var pair in bindings)
                if (pair.Value.Asset?.Dirty == true)
                    result.Add(new AuthoringAssetChange { Provider = Id, Id = pair.Key, Path = pair.Value.Asset.SourcePath, Kind = pair.Value.Asset.Working.GetType().Name, Revision = pair.Value.Revision });
            return result;
        }
        public void ApplyAsset(string id, long expectedRevision)
        {
            if (!bindings.TryGetValue(id, out var binding) || binding.Asset == null || binding.Revision != expectedRevision)
                throw new InvalidOperationException("source_conflict: 资产工作副本已变化，请重新查看保存清单。");
            binding.Asset.Apply();
            binding.Revision++;
        }
        public string Id => "effect";
        public IReadOnlyList<AuthoringProperty> Properties => properties;
        public TextAsset ResolveTextResource(string uri) => uri == "/urui-effect/Effect.css" ? UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect") : null;
        public EffectAuthoringExtension()
        {
            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += UpdateBindings;
            Undo.undoRedoPerformed += OnUndoRedo;
            properties.Add(new AuthoringProperty { Name = "style.--urui-effect", Group = "Effect", Choices = new[] { "none", "ui" } });
            foreach (var property in EffectModule.PropertyDefinitions)
                properties.Add(new AuthoringProperty { Name = "style." + property.Name, Group = "Effect", Type = property.Type.ToString().ToLowerInvariant() });
        }
        void OnUndoRedo()
        {
            foreach (var binding in bindings.Values)
            {
                if (binding.Asset?.Working == null)
                    continue;
                try
                {
                    binding.Asset.EnsureMaterialIsolation();
                    string version = binding.Asset.ContentVersion;
                    if (binding.WorkingVersion == version)
                        continue;
                    binding.WorkingVersion = version;
                    binding.Revision++;
                    ApplyPreview(binding, binding.Context);
                }
                catch (Exception error) { binding.Context?.Report(error.Message); }
            }
        }
        public IDisposable AttachPreview(Document document, Func<string, Type, UnityEngine.Object> load)
        {
            var binding = new PreviewBinding { Document = document, Module = new EffectModule(document, new Resources { Load = load }) };
            binding.Released = () => previews.Remove(document);
            previews.Add(document, binding);
            document.Loaded += binding.Loaded;
            return binding;
        }
        public void AdvancePreview(Document document, float delta)
        {
            if (!previews.TryGetValue(document, out var preview))
                return;
            foreach (var binding in bindings.Values)
                if (binding.Context?.Node != null && binding.Context.Node.IsValid && binding.Context.Node.Document == document && binding.LastNode != binding.Context.Node.Id)
                    ApplyPreview(binding, binding.Context, false);
            foreach (var node in preview.Particles)
            {
                if (!node.IsValid || node.SequenceControlled || node.Source.Length == 0 || node.PlaybackState != ParticlePlaybackState.Playing)
                    continue;
                bool owned = false;
                foreach (var binding in bindings.Values)
                    if (binding.OwnsPlayback && binding.Context?.Node?.Id == RuntimeInspectionBridge.Inspect(node).Id)
                    {
                        owned = true;
                        break;
                    }
                if (!owned && delta > 0)
                    node.Step(delta);
            }
        }
        void UpdateBindings()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = (float)Math.Min(.1, Math.Max(0, now - lastTick));
            lastTick = now;
            foreach (var binding in bindings.Values)
            {
                if (!binding.Playing || binding.Context == null || (!binding.Context.IsLive && !binding.Context.IsObserved) || binding.Context.Node == null || !binding.Context.Node.IsValid)
                    continue;
                try
                {
                    if (binding.LastNode != binding.Context.Node.Id)
                        ApplyPreview(binding, binding.Context, false);
                    if (!(RuntimeInspectionBridge.GetElement(binding.Context.Node) is ParticlesElement particles) || particles.SequenceControlled)
                        continue;
                    particles.Step(delta * binding.Speed);
                    binding.Time = particles.SimulationTime;
                    binding.Context.Refresh();
                }
                catch (Exception error) { binding.Playing = false; binding.Context.Report(error.Message); }
            }
        }
        Binding Get(IAuthoringContext context)
        {
            string key = context.SessionId + "/" + context.TargetId;
            if (!bindings.TryGetValue(key, out var binding))
                bindings.Add(key, binding = new Binding());
            binding.Context = context;
            return binding;
        }
        public void DrawInspector(IAuthoringContext context)
        {
            if (context.Node == null || !context.Node.IsValid)
                return;
            var binding = Get(context);
            bool defaultExpanded = context.Node.Tag == "urui-particles" || context.Node.Computed("--urui-effect") == "ui";
            binding.InspectorExpanded = EditorGUILayout.Foldout(binding.InspectorExpanded ?? defaultExpanded, L.Tr("特效与粒子 Effect"), true, EditorStyles.foldoutHeader);
            if (binding.InspectorExpanded != true)
                return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L.Tr("启用特效")))
                {
                    context.SetWithStyleSheet("style.--urui-effect", "ui", "/urui-effect/Effect.css");
                    return;
                }
                if (GUILayout.Button(L.Tr("移除特效覆盖")))
                    context.Set("style.--urui-effect", null);
            }
            binding.PropertyFilter = EditorGUILayout.TextField(L.Tr("搜索特效参数"), binding.PropertyFilter);
            if (binding.PropertyFilter.Length != 0)
                foreach (var property in properties)
                    if (property.Name.IndexOf(binding.PropertyFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string value = context.Node.Read(property.Name);
                        EditorGUI.BeginChangeCheck();
                        string next = EditorGUILayout.DelayedTextField(property.Name.Substring(6), value ?? "");
                        if (EditorGUI.EndChangeCheck())
                            context.Set(property.Name, next);
                    }
            var selected = EditorGUILayout.ObjectField(L.Tr("资源工作副本"), binding.Asset?.Source, typeof(UnityEngine.Object), false);
            if (selected != binding.Asset?.Source)
                OpenAsset(binding, selected, context);
            if (binding.Asset != null)
            {
                EditorGUILayout.HelpBox(L.Tr("正在编辑临时资源副本；应用或另存后才写入资产。Prefab 材质需单独应用。"), MessageType.None);
                DrawAsset(binding, context);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L.Tr("应用到源资产")))
                    {
                        binding.Asset.Apply();
                        context.Report("Applied asset: " + binding.Asset.SourcePath);
                    }
                    if (GUILayout.Button(L.Tr("另存为…")))
                    {
                        string extension = binding.Asset.Working is GameObject ? "prefab" : binding.Asset.Working is Material ? "mat" : "asset";
                        string path = EditorUtility.SaveFilePanelInProject(L.Tr("Save URUI Asset"), binding.Asset.Source.name + " Copy", extension, L.Tr("Choose an Assets path."));
                        if (!string.IsNullOrEmpty(path))
                            binding.Asset.SaveAs(path);
                    }
                    if (GUILayout.Button(L.Tr("放弃修改")))
                        binding.Dispose();
                }
            }
            if (RuntimeInspectionBridge.GetElement(context.Node) is ParticlesElement particles && particles.Source.Length != 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L.Tr("播放")))
                    {
                        OwnPlayback(binding, particles);
                        particles.Seek(binding.Time);
                        particles.Play();
                        binding.Playing = true;
                    }
                    if (GUILayout.Button(L.Tr("暂停")))
                    {
                        OwnPlayback(binding, particles);
                        particles.Pause();
                        binding.Playing = false;
                    }
                    if (GUILayout.Button(L.Tr("重新播放")))
                    {
                        OwnPlayback(binding, particles);
                        particles.Seek(0);
                        particles.Play();
                        binding.Time = 0;
                        binding.Playing = true;
                    }
                    if (GUILayout.Button(L.Tr("Step")))
                    {
                        OwnPlayback(binding, particles);
                        binding.Playing = false;
                        particles.Pause();
                        particles.Step(1f / 60);
                        binding.Time = particles.SimulationTime;
                        context.Refresh();
                    }
                }
                binding.Speed = EditorGUILayout.Slider(L.Tr("Playback speed"), binding.Speed, .1f, 4);
                EditorGUI.BeginChangeCheck();
                binding.Time = EditorGUILayout.Slider(L.Tr("Time (s)"), binding.Time, 0, 30);
                if (EditorGUI.EndChangeCheck())
                {
                    OwnPlayback(binding, particles);
                    binding.Playing = false;
                    particles.Seek(binding.Time);
                    context.Refresh();
                }
                EditorGUILayout.LabelField(L.Tr("Particles"), particles.ParticleCount.ToString(CultureInfo.InvariantCulture));
            }
        }
        void OpenAsset(Binding binding, UnityEngine.Object source, IAuthoringContext context)
        {
            if (binding.Asset?.Dirty == true)
                throw new InvalidOperationException("source_conflict: apply or discard the current asset working copy first");
            binding.Dispose();
            if (!source)
                return;
            binding.Asset = new EffectAssetSession(source);
            binding.WorkingVersion = binding.Asset.ContentVersion;
            binding.Revision++;
            ApplyPreview(binding, context);
        }
        void DrawAsset(Binding binding, IAuthoringContext context)
        {
            var working = binding.Asset.Working;
            if (working is GameObject root)
            {
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    EditorGUILayout.LabelField(system.name, EditorStyles.miniBoldLabel);
                    if (!binding.ParticleEditors.TryGetValue(system, out var editor) || !editor)
                        binding.ParticleEditors[system] = editor = UnityEditor.Editor.CreateEditor(system);
                    EditorGUI.BeginChangeCheck();
                    editor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck())
                    {
                        binding.Asset.MarkChanged();
                        binding.WorkingVersion = binding.Asset.ContentVersion;
                        binding.Revision++;
                        ApplyPreview(binding, context);
                    }
                }
            }
            else
            {
                if (!binding.Editor)
                    binding.Editor = UnityEditor.Editor.CreateEditor(working);
                EditorGUI.BeginChangeCheck();
                binding.Editor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck())
                {
                    binding.Asset.MarkChanged();
                    binding.WorkingVersion = binding.Asset.ContentVersion;
                    binding.Revision++;
                    ApplyPreview(binding, context);
                }
            }
            if (working is EffectPreset preset && GUILayout.Button(L.Tr("打开特效工作台")))
                EffectPreviewWindow.Open(preset);
        }
        static void OwnPlayback(Binding binding, ParticlesElement particles)
        {
            if (particles.SequenceControlled)
                throw new InvalidOperationException("请先停止统一动画预览，再独立编辑粒子播放。");
            binding.PlaybackOverride ??= particles.OverridePlayback();
            binding.OwnsPlayback = true;
        }
        void ApplyPreview(Binding binding, IAuthoringContext context, bool refresh = true)
        {
            binding.Override?.Dispose();
            binding.Override = null;
            if (context.Node == null || !context.Node.IsValid)
                return;
            if (binding.LastNode != context.Node.Id)
            {
                binding.PlaybackOverride?.Dispose();
                binding.PlaybackOverride = null;
            }
            binding.LastNode = context.Node.Id;
            var element = RuntimeInspectionBridge.GetElement(context.Node);
            if (binding.Asset?.Working is EffectPreset preset && EffectModule.TryGet(context.Node.Document, out var module))
            {
                if (context.Node.Computed("--urui-fx-tone-filter") == null)
                    throw new InvalidOperationException("effect_styles_missing: add /urui-effect/Effect.css before preset preview");
                binding.Override = module.GetController(element).OverridePreset(preset);
            }
            else if (binding.Asset?.Working is GameObject source && element is ParticlesElement particles)
            {
                OwnPlayback(binding, particles);
                binding.Override = particles.OverrideSource(source);
                particles.Seek(binding.Time);
                if (binding.Playing)
                    particles.Play();
            }
            else if (binding.Asset?.Working is Material material)
            {
                if (!(element is ParticlesElement materialTarget))
                    throw new InvalidOperationException("Material preview requires a particle node using this material.");
                binding.Override = materialTarget.OverrideMaterial((Material)binding.Asset.Source, material);
            }
            if (binding.OwnsPlayback && element is ParticlesElement animated && binding.Asset?.Working is not GameObject)
            {
                OwnPlayback(binding, animated);
                animated.Seek(binding.Time);
                if (binding.Playing)
                    animated.Play();
            }
            if (refresh)
                context.Refresh();
        }
        public void DrawWorkbench(IAuthoringContext context) => DrawInspector(context);
        public object Execute(IAuthoringContext context, string operation, IDictionary<string, string> arguments)
        {
            var binding = Get(context);
            string Arg(string name) => arguments.TryGetValue(name, out var value) ? value : throw new InvalidOperationException("Missing argument: " + name);
            if (operation.StartsWith("asset.", StringComparison.Ordinal) && operation != "asset.open" && operation != "asset.read")
            {
                if (binding.Asset == null)
                    throw new InvalidOperationException("read_only_asset: open a working copy first");
                if (long.Parse(Arg("assetRevision"), CultureInfo.InvariantCulture) != binding.Revision)
                    throw new InvalidOperationException("source_conflict: stale asset revision");
            }
            if (operation == "asset.open")
                OpenAsset(binding, AssetDatabase.LoadMainAssetAtPath(Arg("path")), context);
            else if (operation == "asset.setParticle")
            {
                if (long.Parse(Arg("assetRevision"), CultureInfo.InvariantCulture) != binding.Revision)
                    throw new InvalidOperationException("source_conflict: stale asset revision");
                binding.Asset.SetParticle(Arg("property"), Arg("value"));
                binding.Revision++;
                ApplyPreview(binding, context);
            }
            else if (operation == "asset.setMaterial")
            {
                binding.Asset.SetMaterial(Arg("property"), Arg("value"));
                binding.Revision++;
                ApplyPreview(binding, context);
            }
            else if (operation == "asset.setPreset")
            {
                binding.Asset.SetPreset(Arg("property"), Arg("value"));
                binding.Revision++;
                ApplyPreview(binding, context);
            }
            else if (operation == "asset.apply")
            {
                binding.Asset.Apply();
                binding.Revision++;
            }
            else if (operation == "asset.saveAs")
            {
                binding.Asset.SaveAs(Arg("path"));
                binding.Revision++;
            }
            else if (operation == "asset.discard")
            {
                binding.Dispose();
                binding.Revision++;
            }
            else if (operation.StartsWith("particles.", StringComparison.Ordinal))
            {
                if (!(RuntimeInspectionBridge.GetElement(context.Node) is ParticlesElement particles))
                    throw new InvalidOperationException("Select a urui-particles node.");
                OwnPlayback(binding, particles);
                switch (operation)
                {
                    case "particles.play":
                        particles.Seek(binding.Time);
                        particles.Play();
                        binding.Playing = true;
                        break;
                    case "particles.pause":
                        particles.Pause();
                        binding.Playing = false;
                        break;
                    case "particles.restart":
                        particles.Seek(0);
                        particles.Play();
                        binding.Playing = true;
                        break;
                    case "particles.seek":
                        binding.Playing = false;
                        particles.Seek(float.Parse(Arg("time"), CultureInfo.InvariantCulture));
                        break;
                    case "particles.step":
                        binding.Playing = false;
                        particles.Pause();
                        particles.Step(float.Parse(Arg("delta"), CultureInfo.InvariantCulture));
                        break;
                    case "particles.refresh":
                        particles.RefreshSource();
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported operation: " + operation);
                }
                context.Refresh();
                binding.Time = particles.SimulationTime;
            }
            else if (operation != "asset.read")
                throw new InvalidOperationException("Unsupported operation: " + operation);
            binding.WorkingVersion = binding.Asset?.ContentVersion;
            return new AssetState
            {
                path = binding.Asset?.SourcePath,
                type = binding.Asset?.Working?.GetType().Name,
                dirty = binding.Asset?.Dirty ?? false,
                assetRevision = binding.Revision,
                time = binding.Time,
                particleCount = context.Node != null && context.Node.IsValid && RuntimeInspectionBridge.GetElement(context.Node) is ParticlesElement selectedParticles ? selectedParticles.ParticleCount : 0,
                properties = binding.Asset?.Describe()
            };
        }
        public void Dispose()
        {
            EditorApplication.update -= UpdateBindings;
            Undo.undoRedoPerformed -= OnUndoRedo;
            foreach (var binding in bindings.Values)
                binding.Dispose();
            bindings.Clear();
            foreach (var preview in new List<PreviewBinding>(previews.Values))
                preview.Dispose();
            previews.Clear();
        }
        public void EndSession(string sessionId)
        {
            var remove = new List<string>();
            foreach (var pair in bindings)
                if (pair.Value.Context?.SessionId == sessionId)
                {
                    pair.Value.Dispose();
                    remove.Add(pair.Key);
                }
            foreach (string key in remove)
                bindings.Remove(key);
        }
    }
}
