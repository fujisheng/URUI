using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Preview;
using URUI.Authoring.Windows;
using URUI.Documents;
using URUI.Editor.Inspection;
using URUI.Hosting;

namespace URUI.Authoring.Sessions
{
    public enum WorkspaceMode
    {
        Layout, Animation, Live
    }
    [InitializeOnLoad]
    public static class AuthoringEnvironment
    {
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static AuthoringSession Author
        {
            get;
        }
        public static PreviewSession Preview
        {
            get; private set;
        }
        public static LiveSession Live
        {
            get; private set;
        }
        public static Document LiveDocument
        {
            get; private set;
        }
        public static string Status { get; private set; } = "Open an HTML document to begin.";
        public static event Action Changed;
        public static WorkspaceMode Mode
        {
            get; private set;
        }
        public static void SetMode(WorkspaceMode mode)
        {
            if (Mode == mode)
                return;
            Animation.AnimationEnvironment.Stop();
            Mode = mode;
            Changed?.Invoke();
        }
        public static IReadOnlyList<IAuthoringExtension> Extensions => extensions;
        static readonly List<IAuthoringExtension> extensions = new List<IAuthoringExtension>();
        static double lastTick, lastPoll;
        static int observers;
        public static bool HasObservers => observers != 0;
        static bool changingSelection;
        static bool recoveryConflict;
        static string RecoveryPath => Path.Combine(ProjectRoot, "Library/URUI/Authoring/working-copy.json");
        static AuthoringEnvironment()
        {
            RuntimeInspectionBridge.EnableSourceMapping();
            Author = new AuthoringSession(ProjectRoot);
            Author.Changed += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    Try(() => { EnsurePreview(); Preview.Refresh(); });
                Changed?.Invoke();
            };
            foreach (var type in TypeCache.GetTypesDerivedFrom<IAuthoringExtension>())
                if (!type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                    try
                    {
                        extensions.Add((IAuthoringExtension)Activator.CreateInstance(type));
                    }
                    catch (Exception error) { Debug.LogException(error); }
            if (EditorApplication.isPlaying)
                Live = new LiveSession();
            EditorApplication.playModeStateChanged += OnPlayState;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting += BeforeReload;
            Selection.selectionChanged += FollowUnitySelection;
            EditorApplication.update += Update;
            EditorApplication.delayCall += () =>
            {
                if (Author.File != null || !File.Exists(RecoveryPath))
                    return;
                try
                {
                    Report("Recovered authoring working copies; source files unchanged.");
                    SourceRecovery.Restore(Author, RecoveryPath);
                }
                catch (Exception error) { recoveryConflict = true; Report(error.Message); }
            };
        }
        public static void Observe(bool add)
        {
            observers = Math.Max(0, observers + (add ? 1 : -1));
        }
        public static void Open(string path)
        {
            if (Author.File != null && Path.GetFullPath(path) != Author.File.Path)
                foreach (var file in Author.Workspace.Files)
                    if (file.Dirty)
                        throw new InvalidOperationException("Save or discard the current working set before opening another document.");
            Status = "Authoring · " + Path.GetFullPath(path);
            Author.Open(path);
            Changed?.Invoke();
        }
        public static void EnsurePreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            Preview ??= new PreviewSession(Author, ProjectRoot);
        }
        public static void Select(ElementSelectionProxy proxy, bool add = false)
        {
            if (changingSelection)
                return;
            changingSelection = true;
            try
            {
                if (!add)
                    Selection.activeObject = proxy;
                else
                {
                    var selected = new List<UnityEngine.Object>(Selection.objects);
                    if (selected.Contains(proxy))
                        selected.Remove(proxy);
                    else
                        selected.Add(proxy);
                    Selection.objects = selected.ToArray();
                }
            }
            finally { changingSelection = false; }
            Changed?.Invoke();
        }
        public static Document[] Documents()
        {
            var result = new List<Document>();
            foreach (var document in UnityEngine.Object.FindObjectsByType<Document>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                try
                {
                    if (document.IsLoaded)
                        result.Add(document);
                }
                catch (InvalidOperationException) { }
            return result.ToArray();
        }
        public static void SetDocument(Document document)
        {
            if (LiveDocument == document)
                return;
            Animation.AnimationEnvironment.DocumentChanged(document);
            LiveDocument = document;
            Changed?.Invoke();
        }
        static void FollowUnitySelection()
        {
            if (changingSelection || !EditorApplication.isPlaying)
                return;
            var gameObject = Selection.activeGameObject;
            if (!gameObject)
                return;
            var document = gameObject.GetComponent<Document>() ?? gameObject.GetComponentInParent<Document>();
            if (document)
            {
                SetDocument(document);
                return;
            }
            var surface = gameObject.GetComponent<Surface>();
            if (surface)
                foreach (var candidate in Documents())
                    if (candidate.Surface == surface)
                    {
                        SetDocument(candidate);
                        break;
                    }
        }
        static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                SaveRecovery();
                foreach (var extension in extensions)
                    extension.EndSession(Author.Id);
                Preview?.Dispose();
                Preview = null;
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EndLive();
                Live = new LiveSession();
                FollowUnitySelection();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EndLive();
                LiveDocument = null;
                SessionUndoBridge.Clear();
                ElementSelectionProxy.ClearLive();
            }
            else if (state == PlayModeStateChange.EnteredEditMode && Author.File != null)
                Try(() => { EnsurePreview(); Preview.Refresh(); });
            Changed?.Invoke();
        }
        static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = (float)Math.Min(.1, Math.Max(0, now - lastTick));
            lastTick = now;
            if (observers == 0)
                return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Preview != null)
                Try(() => Preview.Tick(delta));
            if (now - lastPoll < .5)
                return;
            lastPoll = now;
            if (Author.File != null && !EditorApplication.isPlayingOrWillChangePlaymode)
                foreach (var file in new List<URUI.Authoring.Source.SourceFile>(Author.Workspace.Files))
                    try
                    {
                        if (!file.HasExternalChange)
                            continue;
                        if (file.Dirty)
                        {
                            Report("source_conflict: external change and unsaved edits: " + file.Path);
                            break;
                        }
                        Author.Workspace.Reload(file.Path, false);
                        if (file == Author.File)
                            Author.Reload(false);
                        else
                        {
                            Preview?.Dispose();
                            Preview = null;
                            EnsurePreview();
                            Preview.Refresh();
                        }
                    }
                    catch (Exception error) { Report(error.Message); break; }
            Changed?.Invoke();
        }
        static void BeforeReload()
        {
            SaveRecovery();
            SessionUndoBridge.Clear();
            EndLive();
            Preview?.Dispose();
            Preview = null;
            foreach (var extension in extensions)
                extension.Dispose();
            ElementSelectionProxy.Clear();
        }
        static void EndLive()
        {
            if (Live == null)
                return;
            foreach (var extension in extensions)
                Try(() => extension.EndSession(Live.Id));
            Try(Live.Dispose);
            if (Live.RestoreDiagnostics.Count != 0)
                Report("Some runtime properties could not be safely restored: " + string.Join("; ", Live.RestoreDiagnostics));
            Live = null;
        }
        static void SaveRecovery()
        {
            if (Author.File == null)
                return;
            Try(() =>
            {
                if (recoveryConflict && File.Exists(RecoveryPath))
                    File.Move(RecoveryPath, Path.Combine(Path.GetDirectoryName(RecoveryPath), "working-copy-conflict-" + Guid.NewGuid().ToString("N") + ".json"));
                recoveryConflict = false;
                SourceRecovery.Save(Author, RecoveryPath);
            });
        }
        public static IReadOnlyList<AuthoringAssetChange> PendingAssets()
        {
            var result = new List<AuthoringAssetChange>();
            foreach (var extension in extensions)
                if (extension is IAuthoringAssetWorkspace workspace)
                    result.AddRange(workspace.PendingAssets());
            return result;
        }
        public static void ApplyAsset(AuthoringAssetChange change)
        {
            foreach (var extension in extensions)
                if (extension.Id == change.Provider && extension is IAuthoringAssetWorkspace workspace)
                {
                    workspace.ApplyAsset(change.Id, change.Revision);
                    return;
                }
            throw new InvalidOperationException("asset_extension_missing: 资产扩展已失效。");
        }
        public static void Save() => SaveWorksetWindow.OpenOrSave();
        public static void Report(string message)
        {
            Status = message;
            Changed?.Invoke();
        }
        public static void Try(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error) { Report(error.Message); }
        }
    }
}
