using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace URUI.Editor.Compilation
{
    sealed class CompiledAssetPostprocessor : AssetPostprocessor
    {
        static readonly HashSet<string> RelevantExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".css", ".asset", ".ttf", ".otf", ".bytes"
        };
        static readonly HashSet<string> changed = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<string, HashSet<string>> reverseIndex = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        static readonly HashSet<string> indexedCompiledAssets = new HashSet<string>(StringComparer.Ordinal);
        static bool indexBuilt;
        static bool queued;
        static bool all;

        [InitializeOnLoadMethod]
        static void OnReload()
        {
            indexBuilt = false;
            reverseIndex.Clear();
            indexedCompiledAssets.Clear();
            all = true;
            Queue();
        }

        static bool IsRelevantPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            var ext = Path.GetExtension(path);
            return !string.IsNullOrEmpty(ext) && RelevantExtensions.Contains(ext);
        }

        static void CollectRelevant(string[] paths, ref bool any)
        {
            if (paths == null)
            {
                return;
            }
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (IsRelevantPath(path))
                {
                    changed.Add(path);
                    any = true;
                }
            }
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool any = false;
            CollectRelevant(imported, ref any);
            CollectRelevant(deleted, ref any);
            CollectRelevant(moved, ref any);
            CollectRelevant(movedFrom, ref any);
            if (any)
            {
                Queue();
            }
        }

        static void Queue()
        {
            if (queued || AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }
            queued = true;
            EditorApplication.delayCall += Refresh;
        }

        static void RebuildIndex()
        {
            reverseIndex.Clear();
            indexedCompiledAssets.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:CompiledDocumentAsset", new[] { "Assets" }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<URUI.Resources.CompiledDocumentAsset>(assetPath);
                if (!asset)
                {
                    continue;
                }
                IndexAsset(assetPath, asset);
            }
            indexBuilt = true;
        }

        static void IndexAsset(string compiledPath, URUI.Resources.CompiledDocumentAsset asset)
        {
            indexedCompiledAssets.Add(compiledPath);
            void Link(string dependencyPath)
            {
                if (string.IsNullOrEmpty(dependencyPath))
                {
                    return;
                }
                if (!reverseIndex.TryGetValue(dependencyPath, out var owners))
                {
                    reverseIndex.Add(dependencyPath, owners = new HashSet<string>(StringComparer.Ordinal));
                }
                owners.Add(compiledPath);
            }
            Link(compiledPath);
            if (asset.AuthorSource)
            {
                Link(AssetDatabase.GetAssetPath(asset.AuthorSource));
            }
            var deps = asset.Dependencies;
            for (int i = 0; i < deps.Count; i++)
            {
                if (deps[i].Asset)
                {
                    Link(AssetDatabase.GetAssetPath(deps[i].Asset));
                }
            }
        }

        static void Refresh()
        {
            queued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Queue();
                return;
            }
            var paths = new HashSet<string>(changed, StringComparer.Ordinal);
            changed.Clear();
            var refreshAll = all;
            all = false;

            bool needsIndexRebuild = !indexBuilt || refreshAll;
            if (!needsIndexRebuild)
            {
                foreach (var path in paths)
                {
                    if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && !indexedCompiledAssets.Contains(path))
                    {
                        if (AssetDatabase.LoadAssetAtPath<URUI.Resources.CompiledDocumentAsset>(path))
                        {
                            needsIndexRebuild = true;
                            break;
                        }
                    }
                }
            }
            if (needsIndexRebuild)
            {
                RebuildIndex();
            }

            IEnumerable<string> candidates;
            if (refreshAll)
            {
                candidates = new List<string>(indexedCompiledAssets);
            }
            else
            {
                var affected = new HashSet<string>(StringComparer.Ordinal);
                foreach (var path in paths)
                {
                    if (reverseIndex.TryGetValue(path, out var owners))
                    {
                        affected.UnionWith(owners);
                    }
                }
                if (affected.Count == 0)
                {
                    return;
                }
                candidates = affected;
            }

            bool recompiledAny = false;
            foreach (var compiledPath in candidates)
            {
                var asset = AssetDatabase.LoadAssetAtPath<URUI.Resources.CompiledDocumentAsset>(compiledPath);
                if (!asset || !Affected(asset, paths, refreshAll))
                {
                    continue;
                }
                try
                {
                    try
                    {
                        CompiledDocumentCompiler.ValidateCurrent(asset);
                        continue;
                    }
                    catch (InvalidOperationException) { }
                    CompiledDocumentCompiler.Recompile(asset);
                    recompiledAny = true;
                }
                catch (Exception error)
                {
                    Debug.LogError("URUI 自动编译失败：" + asset.SourceUrl + "：" + error.Message, asset);
                }
            }
            if (recompiledAny)
            {
                RebuildIndex();
            }
        }

        static bool Affected(URUI.Resources.CompiledDocumentAsset asset, HashSet<string> paths, bool refreshAll)
        {
            if (refreshAll || !asset.AuthorSource || paths.Contains(AssetDatabase.GetAssetPath(asset.AuthorSource)))
            {
                return true;
            }
            foreach (var file in asset.Dependencies)
            {
                if (!file.Asset || paths.Contains(AssetDatabase.GetAssetPath(file.Asset)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
