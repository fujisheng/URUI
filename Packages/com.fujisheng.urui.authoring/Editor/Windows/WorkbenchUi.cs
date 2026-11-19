using System;
using UnityEditor;
using URUI.Authoring.Presentation;
using URUI.Authoring.Sessions;
using UnityEngine;
using UnityEngine.UIElements;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    internal static class WorkbenchUI
    {
        internal static void BuildRoot(VisualElement root)
        {
            root.Clear();
            root.focusable = true;
            root.style.flexGrow = 1;
            root.AddToClassList("urui-workbench");
            root.EnableInClassList("urui-workbench-light", !EditorGUIUtility.isProSkin);
            var skin = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.fujisheng.urui.authoring/Editor/Windows/Workbench.uss");
            if (skin && !root.styleSheets.Contains(skin))
            {
                root.styleSheets.Add(skin);
            }
        }

        internal static void FocusWhenEmpty(EditorWindow window)
        {
            var root = window.rootVisualElement;
            root.schedule.Execute(() =>
            {
                if (EditorWindow.focusedWindow == window && root.focusController?.focusedElement == null)
                {
                    root.Focus();
                }
            });
        }

        internal static Label Heading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("urui-heading");
            return label;
        }

        internal static string NodeTitle(string tag, string id, string classes, string text, string type)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                string line = text.Trim().Split('\n', '\r')[0];
                return line.Length > 36 ? line.Substring(0, 36) + "…" : line;
            }
            if (!string.IsNullOrWhiteSpace(classes))
            {
                return classes.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0];
            }
            string kind = NodePresentation.TypeLabel(tag, type);
            return kind == "节点" || NodePresentation.IsMetadata(tag) ? tag : L.Tr(kind);
        }

        internal static string Glyph(string kind) => kind switch
        {
            "panel" or "div" or "body" or "main" or "section" => "□",
            "text" or "span" or "label" or "p" => "T",
            "image" or "img" => "▧",
            "button" => "▰",
            "toggle" or "checkbox" => "✓",
            "slider" or "range" => "↔",
            "input" or "textarea" => "▤",
            "select" => "▾",
            "particles" or "urui-particles" => "✦",
            _ => "◇"
        };

        internal static void SetPlaceholder(UnityEditor.UIElements.ToolbarSearchField search, string text)
        {
            if (search.Q<TextField>() is TextField field)
            {
                field.textEdition.placeholder = text;
            }
        }

        internal delegate bool LocalNodeDrag(WorkbenchDrag drag, Vector2 panelPosition, bool drop);

        internal static void EnableDrag(VisualElement target, Func<WorkbenchDrag> source,
            Action doubleClick = null, Action click = null, LocalNodeDrag local = null)
        {
            Vector2 down = default;
            int pointer = -1;
            int clicks = 0;
            long downRevision = -1;
            WorkbenchDrag localPayload = null;

            void Release()
            {
                int id = pointer;
                pointer = -1;
                if (localPayload != null)
                {
                    localPayload = null;
                    local(null, default, false);
                }
                if (id >= 0 && target.HasPointerCapture(id))
                {
                    target.ReleasePointer(id);
                }
            }

            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }
                pointer = evt.pointerId;
                clicks = evt.clickCount;
                down = evt.position;
                downRevision = AuthoringEnvironment.Author.Revision;
                if (target.focusable)
                {
                    target.Focus();
                }
                target.CapturePointer(pointer);
            });
            target.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (pointer != evt.pointerId || (evt.pressedButtons & 1) == 0 ||
                    (localPayload == null && ((Vector2)evt.position - down).sqrMagnitude < 36f))
                {
                    return;
                }
                if (downRevision != AuthoringEnvironment.Author.Revision)
                {
                    Release();
                    return;
                }
                var payload = localPayload ?? source();
                if (payload != null && local?.Invoke(payload, evt.position, false) == true)
                {
                    localPayload = payload;
                    evt.StopImmediatePropagation();
                    return;
                }
                Release();
                if (payload == null)
                {
                    return;
                }
                payload.Begin(target is Label label ? label.text : L.Tr("移动"));
                evt.StopImmediatePropagation();
            });
            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (pointer != evt.pointerId)
                {
                    return;
                }
                var payload = localPayload;
                Release();
                if (payload != null)
                {
                    local(payload, evt.position, true);
                    evt.StopImmediatePropagation();
                    return;
                }
                if (downRevision != AuthoringEnvironment.Author.Revision || !target.worldBound.Contains(evt.position))
                {
                    return;
                }
                if (clicks == 1)
                {
                    click?.Invoke();
                }
                else if (clicks == 2)
                {
                    doubleClick?.Invoke();
                }
            });
            target.RegisterCallback<PointerCancelEvent>(_ => Release());
            target.RegisterCallback<PointerCaptureOutEvent>(_ => Release());
            target.RegisterCallback<DetachFromPanelEvent>(_ => Release());
        }
    }
}
