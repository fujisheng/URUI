using L = URUI.Editor.Localization.EditorText;
using System;
using UnityEditor;
using UnityEngine;
namespace URUI.Effect.Editor.Import
{
    static class EffectPresetImportMenu
    {
        [InitializeOnLoadMethod]
        static void RegisterLocalizedMenus()
        {
            EditorApplication.delayCall += () =>
            {
                URUI.Editor.Localization.EditorMenus.Register("Assets/URUI/Import Selected UIEffect Preset", 100, ImportSelected);
            };
        }
        [MenuItem("Assets/URUI/Import Selected UIEffect Preset")]
        static void ImportSelected()
        {
            var source = Selection.activeObject;
            if (!source)
            {
                throw new InvalidOperationException("请选择 UIEffect 5.8.4 预设或组件。");
            }
            var result = UIEffectPresetImporter.Read(source);
            var path = EditorUtility.SaveFilePanelInProject(L.Tr("导入 URUI 预设"), source.name + " URUI", "asset", L.Tr("保存为新资产；不会覆盖原预设。"));
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            Selection.activeObject = UIEffectPresetImporter.Save(result, path);
        }
    }
}
