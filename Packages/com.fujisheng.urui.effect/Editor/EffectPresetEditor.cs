using L = URUI.Editor.Localization.EditorText;
using UnityEditor;
using UnityEngine;
namespace URUI.Effect.Editor
{
    [CustomEditor(typeof(EffectPreset))]
    public sealed class EffectPresetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawParameters(serializedObject.FindProperty("parameters"));
            serializedObject.ApplyModifiedProperties();
            if (UnityEngine.GUILayout.Button(L.Tr("在 URUI 中预览")))
                EffectPreviewWindow.Open((EffectPreset)target);
        }

        static string ParameterLabel(SerializedProperty property) => L.Tr(property.displayName);

        static void DrawParameters(SerializedProperty root)
        {
            int indent = EditorGUI.indentLevel;
            using (var property = root.Copy())
            using (var end = root.GetEndProperty())
            {
                try
                {
                    do
                    {
                        EditorGUI.indentLevel = indent + property.depth - root.depth;
                        // Native controls retain Range, Color, Gradient, vector and enum behavior.
                        EditorGUILayout.PropertyField(property, new GUIContent(ParameterLabel(property), property.tooltip), property.isArray);
                        bool enter = property.propertyType == SerializedPropertyType.Generic && !property.isArray && property.isExpanded;
                        if (!property.NextVisible(enter))
                            break;
                    }
                    while (!SerializedProperty.EqualContents(property, end));
                }
                finally { EditorGUI.indentLevel = indent; }
            }
        }
    }
}
