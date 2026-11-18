using UnityEditor;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace URUI.Editor.Compilation
{
    sealed class CompiledAssetPostprocessor : AssetPostprocessor
    {
        static readonly HashSet<string> changed = new HashSet<string>(StringComparer.Ordinal);
        static bool queued, all;

        [InitializeOnLoadMethod]
        static void OnReload() { all = true; Queue(); }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var paths in new[] { imported, deleted, moved, movedFrom })
                foreach (var path in paths) changed.Add(path);
            Queue();
        }

        static void Queue()
        {
            if (queued || AssetDatabase.IsAssetImportWorkerProcess()) return;
            // 使用编辑器更新队列，批处理同样会处理导入后的重编译。
            queued = true;
            EditorApplication.update += Refresh;
        }

        static void Refresh()
        {
            // 资产导入结束后可读写编译资产。脚本编译可能被测试运行器推迟到整套测试结束。
            if (EditorApplication.isUpdating) return;
            EditorApplication.update -= Refresh;
            queued = false;
            var paths = new HashSet<string>(changed, StringComparer.Ordinal); changed.Clear();
            var refreshAll = all; all = false;
            foreach (var guid in AssetDatabase.FindAssets("t:CompiledDocumentAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<URUI.Resources.CompiledDocumentAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (!asset || !Affected(asset, paths, refreshAll)) continue;
                try
                {
                    try { CompiledDocumentCompiler.ValidateCurrent(asset); continue; }
                    catch (InvalidOperationException) { }
                    CompiledDocumentCompiler.Recompile(asset);
                }
                catch (Exception error) { Debug.LogError("URUI 自动编译失败：" + asset.SourceUrl + "：" + error.Message, asset); }
            }
        }

        static bool Affected(URUI.Resources.CompiledDocumentAsset asset, HashSet<string> paths, bool refreshAll)
        {
            if (refreshAll || !asset.AuthorSource || paths.Contains(AssetDatabase.GetAssetPath(asset.AuthorSource))) return true;
            foreach (var file in asset.Dependencies)
                if (!file.Asset || paths.Contains(AssetDatabase.GetAssetPath(file.Asset))) return true;
            return false;
        }
    }
}
