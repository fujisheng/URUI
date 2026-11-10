using UnityEditor;
namespace URUI.Effect.Editor
{
    [CustomEditor(typeof(EffectPreset))]
    public sealed class EffectPresetEditor:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();EditorGUILayout.PropertyField(serializedObject.FindProperty("parameters"),true);serializedObject.ApplyModifiedProperties();
            if(UnityEngine.GUILayout.Button("在 URUI 中预览"))EffectPreviewWindow.Open((EffectPreset)target);
        }
    }
}
