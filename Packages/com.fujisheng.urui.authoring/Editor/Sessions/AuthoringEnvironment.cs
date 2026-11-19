using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        Layout,
        Animation,
        Live
    }
    [InitializeOnLoad]
    public static class AuthoringEnvironment
    {
        internal const string SourcesSaved = "已保存页面、CSS 与序列工作集。";
        internal const string SourcesDirty = "Authoring · unsaved changes";
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static AuthoringSession Author
        {
            get;
        }
        public static PreviewSession Preview
        {
            get;
            private set;
        }
        public static LiveSession Live
        {
            get;
            private set;
        }
        public static Document LiveDocument
        {
            get;
            private set;
        }
        public static string Status
        {
            get;
            private set;
        } = "Open an HTML document to begin.";
        public static event Action Changed;
        internal static event Action NodeSelectionChanged;
        static ElementSelectionProxy[] selectedNodes = Array.Empty<ElementSelectionProxy>();
        internal static IReadOnlyList<ElementSelectionProxy> SelectedNodes => selectedNodes;
        internal static ElementSelectionProxy ActiveNode => selectedNodes.Length == 0 ? null : selectedNodes[0];
        public static WorkspaceMode Mode
        {
            get;
            private set;
        }
        public static void SetMode(WorkspaceMode mode)
        {
            if (Mode == mode)
            {
                return;
            }
            Animation.AnimationEnvironment.Stop();
            Mode = mode;
            Changed?.Invoke();
        }
        public static IReadOnlyList<IAuthoringExtension> Extensions => extensions;
        static readonly List<IAuthoringExtension> extensions = new List<IAuthoringExtension>();
        static double lastTick;
        static double lastPoll;
        static int observers;
        public static bool HasObservers => observers != 0;
        static bool changingSelection;
        static bool recoveryConflict;
        static bool recoveryPending = true;
        static bool previewRefreshPending;
        static string RecoveryPath => Path.Combine(ProjectRoot, "Library/URUI/Authoring/working-copy.json");
        static AuthoringEnvironment()
        {
            RuntimeInspectionBridge.EnableSourceMapping();
            Author = new AuthoringSession(ProjectRoot);
            Author.Changed += () =>
            {
                if (HasUnsavedSources && (Status == SourcesSaved || Status == "已保存选中的工作集项。" || Status.StartsWith("Authoring · /", StringComparison.Ordinal)))
                {
                    Status = SourcesDirty;
                }
                else if (!HasUnsavedSources && Status == SourcesDirty && Author.File != null)
                {
                    Status = "Authoring · " + Author.File.Path;
                }
                if (!EditorApplication.isPlaying)
                {
                    SetNodeSelection(selectedNodes, clearUnitySelection: false);
                }
                RefreshPreview();
                Changed?.Invoke();
            };
            foreach (var type in TypeCache.GetTypesDerivedFrom<IAuthoringExtension>())
            {
                if (!type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                {
                    try
                    {
                        extensions.Add((IAuthoringExtension)Activator.CreateInstance(type));
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error);
                    }
                }
            }
            if (EditorApplication.isPlaying)
            {
                Live = new LiveSession();
            }
            EditorApplication.playModeStateChanged += OnPlayState;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting += BeforeReload;
            Selection.selectionChanged += FollowUnitySelection;
            EditorApplication.update += Update;
        }

        static void RecoverWorkingCopy()
        {
            if (!recoveryPending || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying))
            {
                return;
            }
            recoveryPending = false;
            if (Author.File != null || !File.Exists(RecoveryPath))
            {
                return;
            }
            try
            {
                Status = "Recovered authoring working copies; source files unchanged.";
                SourceRecovery.Restore(Author, RecoveryPath);
            }
            catch (Exception error)
            {
                recoveryConflict = true;
                Report(error.Message);
            }
        }
        public static void Observe(bool add)
        {
            observers = Math.Max(0, observers + (add ? 1 : -1));
        }
        internal static bool HasUnsavedSources => Author.Workspace.Files.Any(file => file.Dirty);

        public static void Open(string path) => Open(path, false);

        internal static void Open(string path, bool discardChanges)
        {
            path = Author.Workspace.CheckPath(path);
            if (!discardChanges && Author.File != null && path != Author.File.Path && HasUnsavedSources)
            {
                throw new InvalidOperationException("Save or discard the current working set before opening another document.");
            }
            string previous = Status;
            Status = "Authoring · " + path;
            try
            {
                Author.Open(path, discardChanges);
            }
            catch
            {
                Status = previous;
                throw;
            }
            if (discardChanges)
            {
                SaveRecovery();
            }
            Changed?.Invoke();
        }

        internal static void SaveSources()
        {
            if (Author.File == null && !HasUnsavedSources)
            {
                return;
            }
            Author.Save();
            AssetDatabase.Refresh();
            Report(SourcesSaved);
            SaveRecovery();
        }
        public static void EnsurePreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            Preview ??= new PreviewSession(Author, ProjectRoot);
        }

        static void RefreshPreview()
        {
            if (Author.File == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            // Unity can report Edit Mode before removing the previous scene's active URUI Surface.
            previewRefreshPending = Preview == null && !RuntimeInspectionBridge.CanCreatePreview;
            if (!previewRefreshPending)
            {
                Try(() =>
                {
                    EnsurePreview();
                    Preview.Refresh();
                });
            }
        }
        public static void Select(ElementSelectionProxy proxy, bool add = false)
        {
            if (!add || !proxy)
            {
                SetNodeSelection(proxy ? new[] { proxy } : Array.Empty<ElementSelectionProxy>());
                return;
            }
            var selection = new List<ElementSelectionProxy>(selectedNodes);
            if (!selection.Remove(proxy))
            {
                selection.Insert(0, proxy);
            }
            SetNodeSelection(selection);
        }

        internal static void SetNodeSelection(IEnumerable<ElementSelectionProxy> nodes, bool clearUnitySelection = true)
        {
            var valid = new List<ElementSelectionProxy>();
            foreach (var node in nodes)
            {
                if (node && node.IsTargetValid && node.IsLive == EditorApplication.isPlaying &&
                                    (!node.IsLive || node.Node.Document == LiveDocument) && !valid.Contains(node))
                {
                    valid.Add(node);
                }
            }
            if (clearUnitySelection && (valid.Count != 0 || Selection.activeObject is ElementSelectionProxy))
            {
                ClearUnitySelection();
            }
            if (valid.SequenceEqual(selectedNodes))
            {
                return;
            }
            selectedNodes = valid.ToArray();
            NodeSelectionChanged?.Invoke();
        }

        static void ClearUnitySelection()
        {
            if (Selection.objects.Length == 0)
            {
                return;
            }
            changingSelection = true;
            try
            {
                Selection.objects = Array.Empty<UnityEngine.Object>();
                var tracker = ActiveEditorTracker.sharedTracker;
                // The Inspector can still hold its previous editors until Unity dispatches the selection change.
                if (!tracker.isLocked)
                {
                    tracker.ForceRebuild();
                }
            }
            finally
            {
                changingSelection = false;
            }
        }
        public static Document[] Documents()
        {
            var result = new List<Document>();
            foreach (var document in UnityEngine.Object.FindObjectsByType<Document>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                try
                {
                    if (document.IsLoaded)
                    {
                        result.Add(document);
                    }
                }
                catch (InvalidOperationException) { }
            }
            return result.ToArray();
        }
        public static void SetDocument(Document document)
        {
            if (LiveDocument == document)
            {
                return;
            }
            Animation.AnimationEnvironment.DocumentChanged(document);
            LiveDocument = document;
            SetNodeSelection(selectedNodes, clearUnitySelection: false);
            Changed?.Invoke();
        }
        static void FollowUnitySelection()
        {
            if (changingSelection)
            {
                return;
            }
            if (Selection.activeObject is ElementSelectionProxy active)
            {
                var selection = new List<ElementSelectionProxy> { active };
                foreach (var item in Selection.objects)
                {
                    if (item is ElementSelectionProxy proxy && proxy != active)
                    {
                        selection.Add(proxy);
                    }
                }
                SetNodeSelection(selection);
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            var gameObject = Selection.activeGameObject;
            if (!gameObject)
            {
                return;
            }
            var document = gameObject.GetComponent<Document>() ?? gameObject.GetComponentInParent<Document>();
            if (document)
            {
                SetDocument(document);
                return;
            }
            var surface = gameObject.GetComponent<Surface>();
            if (surface)
            {
                foreach (var candidate in Documents())
                {
                    if (candidate.Surface == surface)
                    {
                        SetDocument(candidate);
                        break;
                    }
                }
            }
        }
        static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                previewRefreshPending = false;
                SetNodeSelection(Array.Empty<ElementSelectionProxy>());
                SaveRecovery();
                foreach (var extension in extensions)
                {
                    extension.EndSession(Author.Id);
                }
                Preview?.Dispose();
                Preview = null;
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EndLive();
                Live = new LiveSession();
                // Live animation can use the authoring sequence after a domain reload.
                RecoverWorkingCopy();
                FollowUnitySelection();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EndLive();
                LiveDocument = null;
                SetNodeSelection(Array.Empty<ElementSelectionProxy>());
                SessionUndoBridge.Clear();
                ElementSelectionProxy.ClearLive();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                RecoverWorkingCopy();
                RefreshPreview();
            }
            Changed?.Invoke();
        }
        static void Update()
        {
            RecoverWorkingCopy();
            if (previewRefreshPending && RuntimeInspectionBridge.CanCreatePreview)
            {
                RefreshPreview();
            }
            double now = EditorApplication.timeSinceStartup;
            float delta = (float)Math.Min(.1, Math.Max(0, now - lastTick));
            lastTick = now;
            if (observers == 0)
            {
                return;
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Preview != null)
            {
                Try(() => Preview.Tick(delta));
            }
            if (now - lastPoll < .5)
            {
                return;
            }
            lastPoll = now;
            if (Author.File != null && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                foreach (var file in new List<URUI.Authoring.Source.SourceFile>(Author.Workspace.Files))
                {
                    try
                    {
                        if (!file.HasExternalChange)
                        {
                            continue;
                        }
                        if (file.Dirty)
                        {
                            Report("source_conflict: external change and unsaved edits: " + file.Path);
                            break;
                        }
                        Author.Workspace.Reload(file.Path, false);
                        if (file == Author.File)
                        {
                            Author.Reload(false);
                        }
                        else
                        {
                            Preview?.Dispose();
                            Preview = null;
                            RefreshPreview();
                        }
                    }
                    catch (Exception error)
                    {
                        Report(error.Message);
                        break;
                    }
                }
            }
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
            {
                extension.Dispose();
            }
            ElementSelectionProxy.Clear();
        }
        static void EndLive()
        {
            if (Live == null)
            {
                return;
            }
            foreach (var extension in extensions)
            {
                Try(() => extension.EndSession(Live.Id));
            }
            Try(Live.Dispose);
            if (Live.RestoreDiagnostics.Count != 0)
            {
                Report("Some runtime properties could not be safely restored: " + string.Join("; ", Live.RestoreDiagnostics));
            }
            Live = null;
        }
        static void SaveRecovery()
        {
            if (Author.File == null)
            {
                return;
            }
            Try(() =>
            {
                if (recoveryConflict && File.Exists(RecoveryPath))
                {
                    File.Move(RecoveryPath, Path.Combine(Path.GetDirectoryName(RecoveryPath), "working-copy-conflict-" + Guid.NewGuid().ToString("N") + ".json"));
                }
                recoveryConflict = false;
                SourceRecovery.Save(Author, RecoveryPath);
            });
        }
        public static IReadOnlyList<AuthoringAssetChange> PendingAssets()
        {
            var result = new List<AuthoringAssetChange>();
            foreach (var extension in extensions)
            {
                if (extension is IAuthoringAssetWorkspace workspace)
                {
                    result.AddRange(workspace.PendingAssets());
                }
            }
            return result;
        }
        public static void ApplyAsset(AuthoringAssetChange change)
        {
            foreach (var extension in extensions)
            {
                if (extension.Id == change.Provider && extension is IAuthoringAssetWorkspace workspace)
                {
                    workspace.ApplyAsset(change.Id, change.Revision);
                    return;
                }
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
            catch (Exception error)
            {
                Report(error.Message);
            }
        }
    }
}
