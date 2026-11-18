using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Editor.Inspection;
using URUI.Events;

namespace URUI.Authoring.Windows
{
    public sealed class UruiCanvasWindow : EditorWindow
    {
        CanvasView view;
        [InitializeOnLoadMethod]
        static void RegisterLocalizedMenus()
        {
            EditorApplication.delayCall += () =>
            {
                URUI.Editor.Localization.EditorMenus.Register("Window/URUI/Canvas", 100, Open);
            };
        }
        [MenuItem("Window/URUI/Canvas")]
        public static void Open() => GetWindow<UruiCanvasWindow>(L.Tr("URUI 画布"));
        void OnEnable()
        {
            view = new CanvasView(Repaint);
            AuthoringEnvironment.Observe(true);
            AuthoringEnvironment.Changed += Repaint;
        }
        void OnDisable()
        {
            view?.Dispose();
            AuthoringEnvironment.Observe(false);
            AuthoringEnvironment.Changed -= Repaint;
        }
        void OnGUI()
        {
            L.Title(this, "URUI 画布");
            view?.Draw();
        }
        void OnLostFocus() => view?.CancelInput();
    }
    internal sealed class CanvasView : IDisposable
    {
        readonly Action repaint;
        CanvasGesture gesture;
        readonly Dictionary<string, string> initialStyles = new Dictionary<string, string>();
        Vector2 containingSize;
        internal CanvasView(Action repaint)
        {
            this.repaint = repaint;
        }
        void Repaint() => repaint?.Invoke();
        public void Dispose()
        {
            gesture?.Dispose();
            gesture = null;
        }

        [SerializeField] float zoom = 1;
        [SerializeField] Vector2 pan;
        [SerializeField] int width = 1280, height = 720;
        [SerializeField] Vector4 safeInsets = new Vector4(0, 59, 0, 34);
        bool interact, safeArea, snapping = true, dragging;
        bool marquee;
        Vector2 marqueeStart, marqueeEnd;
        string tool = "Rect", startTransform;
        Vector2 previous, dragStart, resizeDirection;
        Rect initialBounds;
        int pointerButtons;
        public void Draw()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                interact = GUILayout.Toggle(interact, L.Tr("交互"), EditorStyles.toolbarButton, GUILayout.Width(62));
                snapping = GUILayout.Toggle(snapping, L.Tr("吸附"), EditorStyles.toolbarButton, GUILayout.Width(42));
                safeArea = GUILayout.Toggle(safeArea, L.Tr("安全区"), EditorStyles.toolbarButton, GUILayout.Width(68));
                if (GUILayout.Button(L.Tr("适应"), EditorStyles.toolbarButton, GUILayout.Width(30)))
                {
                    zoom = 1;
                    pan = Vector2.zero;
                }
                GUILayout.Label(L.Tr(tool) + " · " + (zoom * 100).ToString("0") + "%", GUILayout.Width(105));
                width = EditorGUILayout.DelayedIntField(width, GUILayout.Width(55));
                GUILayout.Label("×", GUILayout.Width(12));
                height = EditorGUILayout.DelayedIntField(height, GUILayout.Width(55));
                if (GUILayout.Button(L.Tr("应用"), EditorStyles.toolbarButton))
                    AuthoringEnvironment.Try(() => { AuthoringEnvironment.EnsurePreview(); AuthoringEnvironment.Preview?.Resize(width, height); });
                if (GUILayout.Button(L.Tr("尺寸预设"), EditorStyles.toolbarDropDown))
                    ShowViewportPresets();
                if (GUILayout.Button(L.Tr("保存"), EditorStyles.toolbarButton))
                    AuthoringEnvironment.Try(AuthoringEnvironment.Save);
            }
            if (safeArea)
                safeInsets = EditorGUILayout.Vector4Field(new GUIContent(L.Tr("安全区 左/上/右/下（px）"), L.Tr("仅为预览参考线，不写入布局属性。")), safeInsets);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(L.Tr("在 URUI Hierarchy 中选择真实节点并在 Inspector 调参，运行画面在 Game View 查看。"), MessageType.Info);
                return;
            }
            var preview = AuthoringEnvironment.Preview;
            if (preview == null || preview.Target == null)
            {
                EditorGUILayout.HelpBox(L.Tr("从 Project 打开 HTML 页面，或使用工具栏的“打开页面”。"), MessageType.Info);
                return;
            }
            if (!preview.IsReady)
            {
                EditorGUILayout.HelpBox(L.Message(preview.Error ?? AuthoringEnvironment.Status), MessageType.Error);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(preview.Playing ? L.Tr("暂停") : L.Tr("播放页面"), GUILayout.Width(60)))
                    preview.Playing = !preview.Playing;
                if (GUILayout.Button(L.Tr("单步"), GUILayout.Width(45)))
                    AuthoringEnvironment.Try(() => { preview.Playing = true; preview.Tick(1f / 60); preview.Playing = false; });
                string[] tools = { "Move", "Rotate", "Scale", "Rect" };
                int current = Array.IndexOf(tools, tool);
                int next = GUILayout.Toolbar(current, new[] { new GUIContent(L.Tr("移动"), "W"), new GUIContent(L.Tr("旋转"), "E"), new GUIContent(L.Tr("缩放"), "R"), new GUIContent(L.Tr("矩形"), "T") }, EditorStyles.miniButton, GUILayout.Width(200));
                if (next != current)
                {
                    CancelInput();
                    tool = tools[next];
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(L.Tr("F 聚焦 · 中键平移"), EditorStyles.miniLabel);
            }
            var area = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandHeight(true));
            float scale = Mathf.Min((area.width - 40) / preview.Width, (area.height - 40) / preview.Height) * zoom;
            scale = Mathf.Max(.01f, scale);
            var canvas = new Rect(area.center - new Vector2(preview.Width, preview.Height) * scale * .5f + pan, new Vector2(preview.Width, preview.Height) * scale);
            EditorGUI.DrawRect(area, new Color(.12f, .12f, .13f));
            GUI.DrawTexture(canvas, preview.Target, ScaleMode.StretchToFill, false);
            if (safeArea)
            {
                float left = Mathf.Clamp(safeInsets.x, 0, preview.Width), top = Mathf.Clamp(safeInsets.y, 0, preview.Height);
                float right = Mathf.Clamp(safeInsets.z, 0, preview.Width - left), bottom = Mathf.Clamp(safeInsets.w, 0, preview.Height - top);
                Outline(new Rect(canvas.x + left * scale, canvas.y + top * scale, (preview.Width - left - right) * scale, (preview.Height - top - bottom) * scale), new Color(1, .7f, .1f));
            }
            var selected = Selection.activeObject as ElementSelectionProxy;
            Rect selectedRect = default;
            if (selected && !selected.IsLive && selected.IsTargetValid && selected.Node != null && selected.Node.HasLayout)
            {
                Rect bounds = selected.Node.Bounds;
                selectedRect = new Rect(canvas.position + bounds.position * scale, bounds.size * scale);
                Outline(selectedRect, new Color(.2f, .65f, 1));
                if (tool == "Rect")
                    foreach (Vector2 direction in Directions())
                    {
                        var point = Point(selectedRect, direction);
                        EditorGUI.DrawRect(new Rect(point - Vector2.one * 3, Vector2.one * 6), Color.white);
                    }
            }
            HandleInput(area, canvas, scale, selected, selectedRect);
            if (marquee)
                Outline(MarqueeRect(), new Color(.3f, .8f, 1));
            HandleAssetDrop(canvas);
            EditorGUILayout.LabelField(L.Message(AuthoringEnvironment.Status), EditorStyles.wordWrappedMiniLabel);
        }
        void HandleInput(Rect area, Rect canvas, float scale, ElementSelectionProxy selected, Rect selectedRect)
        {
            var evt = Event.current;
            var preview = AuthoringEnvironment.Preview;
            if (evt.type == EventType.ScrollWheel && area.Contains(evt.mousePosition))
            {
                zoom = Mathf.Clamp(zoom * Mathf.Exp(-evt.delta.y * .04f), .05f, 10);
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 2)
            {
                pan += evt.delta;
                evt.Use();
                Repaint();
                return;
            }
            if (!interact && evt.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if (evt.keyCode == KeyCode.F)
                {
                    if (selected && selected.IsTargetValid && !selected.IsLive && selected.Node != null && selected.Node.HasLayout)
                    {
                        Rect bounds = selected.Node.Bounds;
                        float fit = Mathf.Max(.01f, Mathf.Min((area.width - 40) / preview.Width, (area.height - 40) / preview.Height));
                        float focusScale = Mathf.Min((area.width - 80) / Mathf.Max(1, bounds.width), (area.height - 80) / Mathf.Max(1, bounds.height));
                        zoom = Mathf.Clamp(focusScale / fit, .05f, 10);
                        pan = (new Vector2(preview.Width, preview.Height) * .5f - bounds.center) * fit * zoom;
                    }
                    else
                    {
                        zoom = 1;
                        pan = Vector2.zero;
                    }
                    evt.Use();
                    Repaint();
                }
                else if (evt.keyCode == KeyCode.W)
                {
                    tool = "Move";
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.E)
                {
                    tool = "Rotate";
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.R)
                {
                    tool = "Scale";
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.T)
                {
                    tool = "Rect";
                    evt.Use();
                }
                else if ((evt.command || evt.control) && evt.keyCode == KeyCode.S)
                {
                    AuthoringEnvironment.Try(AuthoringEnvironment.Save);
                    evt.Use();
                }
            }
            if (interact)
            {
                if ((canvas.Contains(evt.mousePosition) || pointerButtons != 0) && (evt.type == EventType.MouseDown || evt.type == EventType.MouseUp || evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove))
                {
                    PointerPhase phase = evt.type == EventType.MouseDown ? PointerPhase.Down : evt.type == EventType.MouseUp ? PointerPhase.Up : PointerPhase.Move;
                    int button = Math.Max(0, Math.Min(2, evt.button));
                    if (phase == PointerPhase.Down)
                        pointerButtons |= 1 << button;
                    else if (phase == PointerPhase.Up)
                        pointerButtons &= ~(1 << button);
                    preview.Document.Surface.Input.SendPointer(phase, (evt.mousePosition - canvas.position) / scale, button: button, pressedButtons: pointerButtons);
                    RuntimeInspectionBridge.Refresh(preview.Document);
                    preview.Refresh();
                    evt.Use();
                }
                else if (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp)
                {
                    preview.Document.Surface.Input.SendKey(evt.keyCode, evt.type == EventType.KeyDown);
                    if (evt.type == EventType.KeyDown && !char.IsControl(evt.character))
                        preview.Document.Surface.Input.SendText(evt.character.ToString());
                    preview.Refresh();
                    evt.Use();
                }
                return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && canvas.Contains(evt.mousePosition))
            {
                if (evt.shift)
                {
                    marquee = true;
                    marqueeStart = marqueeEnd = evt.mousePosition;
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    evt.Use();
                    return;
                }
                resizeDirection = Vector2.zero;
                if (selected && tool == "Rect")
                    foreach (Vector2 direction in Directions())
                        if ((Point(selectedRect, direction) - evt.mousePosition).sqrMagnitude < 64)
                        {
                            resizeDirection = direction;
                            break;
                        }
                if (resizeDirection == Vector2.zero)
                {
                    var picked = RuntimeInspectionBridge.Pick(preview.Document, (evt.mousePosition - canvas.position) / scale);
                    string key = preview.Key(picked);
                    if (key != null)
                    {
                        selected = ElementSelectionProxy.ForAuthor(key);
                        AuthoringEnvironment.Select(selected, evt.control || evt.command);
                    }
                }
                if (selected && selected.IsTargetValid && selected.Node != null && selected.Node.HasLayout)
                {
                    dragging = true;
                    previous = dragStart = evt.mousePosition;
                    initialBounds = selected.Node.Layout;
                    startTransform = selected.Node.Computed("transform") ?? selected.Read("style.transform") ?? "";
                    gesture?.Dispose();
                    gesture = new CanvasGesture(selected);
                    initialStyles.Clear();
                    foreach (string name in new[] { "left", "top", "width", "height" })
                        initialStyles[name] = selected.Node.Computed(name);
                    containingSize = RectCommands.ContainingSize(selected.Node);
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    evt.Use();
                }
            }
            if (marquee && evt.type == EventType.MouseDrag)
            {
                marqueeEnd = evt.mousePosition;
                evt.Use();
                Repaint();
                return;
            }
            if (marquee && evt.type == EventType.MouseUp)
            {
                var objects = new List<UnityEngine.Object>();
                Rect box = MarqueeRect();
                foreach (var source in AuthoringEnvironment.Author.Model.Nodes)
                {
                    var node = preview.Node(source.Key);
                    if (node == null || !node.IsValid || !node.HasLayout || node.Bounds.width <= 0 || node.Bounds.height <= 0)
                        continue;
                    var rect = new Rect(canvas.position + node.Bounds.position * scale, node.Bounds.size * scale);
                    if (box.Contains(rect.min) && box.Contains(rect.max))
                        objects.Add(ElementSelectionProxy.ForAuthor(source.Key));
                }
                Selection.objects = objects.ToArray();
                marquee = false;
                GUIUtility.hotControl = 0;
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && (dragging || marquee))
            {
                CancelInput();
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseDrag && dragging && selected && selected.IsTargetValid)
            {
                Vector2 delta = (evt.mousePosition - previous) / scale;
                if (snapping)
                    delta = new Vector2(Mathf.Round(delta.x), Mathf.Round(delta.y));
                previous = evt.mousePosition;
                var total = (evt.mousePosition - dragStart) / scale;
                if (snapping)
                    total = new Vector2(Mathf.Round(total.x), Mathf.Round(total.y));
                AuthoringEnvironment.Try(() => Drag(selected, delta, total));
                evt.Use();
            }
            if (evt.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                GUIUtility.hotControl = 0;
                try
                {
                    AuthoringEnvironment.Try(() => gesture?.Commit());
                }
                finally { gesture?.Dispose(); gesture = null; }
                evt.Use();
            }
        }
        Rect MarqueeRect() => Rect.MinMaxRect(Mathf.Min(marqueeStart.x, marqueeEnd.x), Mathf.Min(marqueeStart.y, marqueeEnd.y), Mathf.Max(marqueeStart.x, marqueeEnd.x), Mathf.Max(marqueeStart.y, marqueeEnd.y));
        void ShowViewportPresets()
        {
            var menu = new GenericMenu();
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1080, 1920), new Vector2Int(1024, 768) })
            {
                var preset = size;
                menu.AddItem(new GUIContent(size.x + " × " + size.y), width == size.x && height == size.y, () => AuthoringEnvironment.Try(() =>
                {
                    width = preset.x;
                    height = preset.y;
                    AuthoringEnvironment.EnsurePreview();
                    AuthoringEnvironment.Preview?.Resize(width, height);
                }));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(L.Tr("Safe area/None")), false, () => { safeInsets = Vector4.zero; safeArea = true; });
            menu.AddItem(new GUIContent(L.Tr("Safe area/Portrait cutout · T59 B34")), false, () => { safeInsets = new Vector4(0, 59, 0, 34); safeArea = true; });
            menu.AddItem(new GUIContent(L.Tr("Safe area/Landscape cutout · L59 R59 B21")), false, () => { safeInsets = new Vector4(59, 0, 59, 21); safeArea = true; });
            menu.ShowAsContext();
        }
        void Drag(ElementSelectionProxy proxy, Vector2 delta, Vector2 total)
        {
            var node = proxy.Node;
            string positionMode = node.Computed("position");
            if (positionMode != "absolute" && positionMode != "fixed" && tool != "Rotate" && tool != "Scale")
                throw new InvalidOperationException("Layout drives this node. Edit flex/order/gap/alignment in Inspector, or explicitly choose an anchor preset.");
            var author = AuthoringEnvironment.Author;
            var edits = new List<AuthoringEdit>();
            void Add(string property, string value) => edits.Add(new AuthoringEdit { Node = proxy.SourceKey, Property = "style." + property, Value = value });
            if (tool == "Rotate")
                Add("transform", (startTransform == "none" ? "" : startTransform) + " rotate(" + total.x.ToString("0.###", CultureInfo.InvariantCulture) + "deg)");
            else if (tool == "Scale")
                Add("transform", (startTransform == "none" ? "" : startTransform) + " scale(" + Mathf.Max(.01f, 1 + total.x / 100).ToString("0.###", CultureInfo.InvariantCulture) + ")");
            else
            {
                var layout = initialBounds;
                var parent = containingSize;
                delta = total;
                bool resize = resizeDirection != Vector2.zero;
                if (!resize || resizeDirection.x < 0)
                    Add("left", RectCommands.Offset(initialStyles["left"], delta.x, parent.x, layout.x));
                if (!resize || resizeDirection.y < 0)
                    Add("top", RectCommands.Offset(initialStyles["top"], delta.y, parent.y, layout.y));
                if (resize && resizeDirection.x != 0)
                    Add("width", Mathf.Max(1, layout.width + delta.x * resizeDirection.x).ToString("0.###", CultureInfo.InvariantCulture) + "px");
                if (resize && resizeDirection.y != 0)
                    Add("height", Mathf.Max(1, layout.height + delta.y * resizeDirection.y).ToString("0.###", CultureInfo.InvariantCulture) + "px");
            }
            gesture.Preview(edits);
        }
        void HandleAssetDrop(Rect canvas)
        {
            var evt = Event.current;
            if (!canvas.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform))
                return;
            if (DragAndDrop.objectReferences.Length != 1 || !(DragAndDrop.objectReferences[0] is Texture2D texture))
                return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AuthoringEnvironment.Try(() =>
                {
                    var proxy = Selection.activeObject as ElementSelectionProxy;
                    if (!proxy || proxy.IsLive || !proxy.IsTargetValid)
                        throw new InvalidOperationException("Select an image node first.");
                    var source = new Uri(AuthoringEnvironment.Author.File.Path);
                    string relative = Uri.UnescapeDataString(source.MakeRelativeUri(new Uri(Path.GetFullPath(AssetDatabase.GetAssetPath(texture)))).ToString());
                    proxy.Set(proxy.SourceNode.Tag == "img" ? "attr.src" : "style.background-image", proxy.SourceNode.Tag == "img" ? relative : "url(\"" + relative + "\")");
                });
            }
            evt.Use();
        }
        static IEnumerable<Vector2> Directions()
        {
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (x != 0 || y != 0)
                        yield return new Vector2(x, y);
        }
        static Vector2 Point(Rect rect, Vector2 direction) => new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, (direction.x + 1) * .5f), Mathf.Lerp(rect.yMin, rect.yMax, (direction.y + 1) * .5f));
        static void Outline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }
        internal void CancelInput()
        {
            gesture?.Dispose();
            gesture = null;
            dragging = marquee = false;
            GUIUtility.hotControl = 0;
            pointerButtons = 0;
            if (AuthoringEnvironment.Preview?.Document && AuthoringEnvironment.Preview.Document.IsLoaded)
                AuthoringEnvironment.Preview.Document.Surface.Input.Reset();
        }
    }
}
