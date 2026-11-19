using L = URUI.Editor.Localization.EditorText;
using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace URUI.Authoring.Animation
{
    internal static class CurveView
    {
        static readonly string[] names = { "linear", "ease", "ease-in", "ease-out", "ease-in-out", "自定义三次贝塞尔" };
        internal static string Draw(string easing)
        {
            int current = Array.IndexOf(names, easing);
            if (current < 0)
            {
                current = 5;
            }
            int next = EditorGUILayout.Popup(L.Tr("缓动"), current, Array.ConvertAll(names, L.Tr));
            if (next != current)
            {
                return next == 5 ? "cubic-bezier(.25,.1,.25,1)" : names[next];
            }
            if (current != 5)
            {
                return easing;
            }
            string value = EditorGUILayout.DelayedTextField(L.Tr("CSS 曲线"), easing);
            if (!TryBezier(value, out var a, out var b))
            {
                return value;
            }
            EditorGUI.BeginChangeCheck();
            a = EditorGUILayout.Vector2Field(L.Tr("控制点 1"), a);
            b = EditorGUILayout.Vector2Field(L.Tr("控制点 2"), b);
            if (EditorGUI.EndChangeCheck())
            {
                value = string.Format(CultureInfo.InvariantCulture, "cubic-bezier({0:0.####},{1:0.####},{2:0.####},{3:0.####})", Mathf.Clamp01(a.x), a.y, Mathf.Clamp01(b.x), b.y);
            }
            var rect = GUILayoutUtility.GetRect(100, 60);
            EditorGUI.DrawRect(rect, new Color(.12f, .12f, .13f));
            Handles.BeginGUI();
            Handles.color = new Color(.3f, .7f, 1);
            Vector3 previous = Point(rect, Vector2.zero);
            for (int i = 1; i <= 40; i++)
            {
                float t = i / 40f;
                var p = 3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t * Vector2.one;
                var point = Point(rect, p);
                Handles.DrawLine(previous, point);
                previous = point;
            }
            Handles.EndGUI();
            return value;
        }
        static Vector3 Point(Rect rect, Vector2 point) => new Vector3(rect.x + point.x * rect.width, rect.yMax - Mathf.Clamp01(point.y) * rect.height);
        static bool TryBezier(string text, out Vector2 a, out Vector2 b)
        {
            a = b = default;
            if (!text.StartsWith("cubic-bezier(", StringComparison.Ordinal) || !text.EndsWith(")", StringComparison.Ordinal))
            {
                return false;
            }
            var parts = text.Substring(13, text.Length - 14).Split(',');
            if (parts.Length != 4)
            {
                return false;
            }
            float[] values = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !float.IsFinite(values[i]))
                {
                    return false;
                }
            }
            a = new Vector2(values[0], values[1]);
            b = new Vector2(values[2], values[3]);
            return true;
        }
    }
}
