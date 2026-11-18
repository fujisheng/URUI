using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Editor.Inspection;
namespace URUI.Authoring.Windows
{
    /// <summary>统一保存清单；源工作集和原生资产明确分组，资产默认不应用。</summary>
    public sealed class SaveWorksetWindow : EditorWindow
    {
        readonly HashSet<string> selected = new HashSet<string>();
        Vector2 scroll;
        bool saveSources = true;
        string error;
        public static void OpenOrSave()
        {
            if (AuthoringEnvironment.PendingAssets().Count == 0 && !EditorApplication.isPlaying)
            {
                AuthoringEnvironment.Author.Save();
                AssetDatabase.Refresh();
                AuthoringEnvironment.Report("已保存页面、CSS 与序列工作集。");
                return;
            }
            var window = GetWindow<SaveWorksetWindow>(true, L.Tr("保存 URUI 工作集"));
            window.minSize = new Vector2(540, 340);
            window.saveSources = !EditorApplication.isPlaying;
            window.selected.Clear();
            window.error = null;
            window.Show();
        }
        void OnGUI()
        {
            L.Title(this, "保存 URUI 工作集");
            var sources = AuthoringEnvironment.Author.Workspace.Files.Where(f => f.Dirty).ToArray();
            var assets = AuthoringEnvironment.PendingAssets();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || sources.Length == 0))
                saveSources = EditorGUILayout.ToggleLeft(L.Tr("保存页面 / CSS / 序列（同一源工作集）"), saveSources, EditorStyles.boldLabel);
            foreach (var file in sources)
                EditorGUILayout.LabelField(file.Path, EditorStyles.wordWrappedMiniLabel);
            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox(L.Tr("Play 属性默认临时生效；源属性需从运行调试界面明确对比并应用。"), MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(L.Tr("原生资产：逐项选择 Apply，默认不写原资产"), EditorStyles.boldLabel);
            foreach (var asset in assets)
            {
                string key = asset.Provider + "|" + asset.Id;
                bool next = EditorGUILayout.ToggleLeft(asset.Kind + " · " + asset.Path + " · r" + asset.Revision, selected.Contains(key));
                if (next)
                    selected.Add(key);
                else
                    selected.Remove(key);
            }
            if (assets.Count == 0)
                EditorGUILayout.LabelField(L.Tr("没有未应用的资产修改。"));
            EditorGUILayout.EndScrollView();
            if (error != null)
                EditorGUILayout.HelpBox(L.Message(error), MessageType.Warning);
            EditorGUILayout.HelpBox(L.Tr("源文件使用暂存与冲突检查；原生资产按选中项独立 Apply。材质资产单独保存。"), MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L.Tr("取消")))
                    Close();
                using (new EditorGUI.DisabledScope((!saveSources || sources.Length == 0) && selected.Count == 0))
                    if (GUILayout.Button(L.Tr("保存选中项")))
                        try
                        {
                            var chosen = assets.Where(a => selected.Contains(a.Provider + "|" + a.Id)).ToArray();
                            var current = AuthoringEnvironment.PendingAssets();
                            foreach (var asset in chosen)
                                if (!current.Any(a => a.Provider == asset.Provider && a.Id == asset.Id && a.Revision == asset.Revision))
                                    throw new InvalidOperationException("source_conflict: 保存清单已变化，请重新查看。");
                            if (saveSources && !EditorApplication.isPlaying)
                                AuthoringEnvironment.Author.Save();
                            foreach (var asset in chosen)
                                AuthoringEnvironment.ApplyAsset(asset);
                            AssetDatabase.Refresh();
                            AuthoringEnvironment.Report("已保存选中的工作集项。");
                            Close();
                        }
                        catch (Exception exception) { error = exception.Message; }
            }
        }
    }
}
