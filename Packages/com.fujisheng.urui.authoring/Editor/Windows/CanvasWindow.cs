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
using URUI.Authoring.Source;

namespace URUI.Authoring.Windows
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "URUI.Authoring.Windows", sourceClassName: "UruiCanvasWindow")]
    public sealed class CanvasWindow : EditorWindow
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
        public static void Open() => GetWindow<CanvasWindow>(L.Tr("URUI 画布"));
        void OnEnable()
        {
            view = new CanvasView(Repaint);
            AuthoringEnvironment.Observe(true);
            AuthoringEnvironment.Changed += Repaint;
            AuthoringEnvironment.NodeSelectionChanged += Repaint;
        }
        void OnDisable()
        {
            view?.Dispose();
            AuthoringEnvironment.Observe(false);
            AuthoringEnvironment.Changed -= Repaint;
            AuthoringEnvironment.NodeSelectionChanged -= Repaint;
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
        readonly bool rememberView;
        WorkbenchViewState.Canvas viewState;
        Vector2Int appliedSize;
        CanvasGesture gesture;
        readonly Dictionary<string, string> initialStyles = new Dictionary<string, string>();
        Vector2 containingSize;
        internal CanvasView(Action repaint) : this(repaint, false) { }

        internal CanvasView(Action repaint, bool rememberView)
        {
            this.repaint = repaint;
            this.rememberView = rememberView;
        }
        void Repaint() => repaint?.Invoke();
        public void Dispose()
        {
            SaveView();
            gesture?.Dispose();
            gesture = null;
        }

        [SerializeField]
        float zoom = 1;
        [SerializeField]
        Vector2 pan;
        [SerializeField]
        int width = 1280;
        [SerializeField]
        int height = 720;
        [SerializeField]
        Vector4 safeInsets = new Vector4(0, 59, 0, 34);
        bool interact;
        bool safeArea;
        bool snapping = true;
        bool dragging;
        bool customSize;
        bool marquee;
        Vector2 marqueeStart;
        Vector2 marqueeEnd;
        string tool = "Rect";
        string startTransform;
        Vector2 previous;
        Vector2 dragStart;
        Vector2 resizeDirection;
        Rect initialBounds;
        int pointerButtons;
        Rect latestCanvas;
        Rect latestArea;
        float latestScale;
        WorkbenchDrag localNodeDrag;
        Vector2 localDragPosition;
        string pendingFlowDrag;
        long pendingFlowRevision;
        public void Draw()
        {
            if (Event.current.type == EventType.Repaint)
            {
                latestArea = default;
            }
            RestoreView();
            var dimensions = AuthoringEnvironment.Preview;
            if (dimensions != null)
            {
                var current = new Vector2Int(dimensions.Width, dimensions.Height);
                if (current != appliedSize)
                {
                    appliedSize = current;
                    width = current.x;
                    height = current.y;
                }
            }
            if (rememberView)
            {
                DrawWorkbenchToolbar(dimensions);
            }
            else
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
                    {
                        AuthoringEnvironment.Try(() =>
                        {
                            AuthoringEnvironment.EnsurePreview();
                            AuthoringEnvironment.Preview?.Resize(width, height);
                        });
                    }
                    if (GUILayout.Button(L.Tr("尺寸预设"), EditorStyles.toolbarDropDown))
                    {
                        ShowViewportPresets();
                    }
                    if (GUILayout.Button(L.Tr("保存"), EditorStyles.toolbarButton))
                    {
                        AuthoringEnvironment.Try(AuthoringEnvironment.Save);
                    }
                }
            }
            if (safeArea)
            {
                safeInsets = EditorGUILayout.Vector4Field(new GUIContent(L.Tr("安全区 左/上/右/下（px）"), L.Tr("仅为预览参考线，不写入布局属性。")), safeInsets);
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(L.Tr("在 URUI 层级中选择节点，在 URUI 属性中调整。运行画面在 Game View 查看。"), MessageType.Info);
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
            if (!rememberView)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(preview.Playing ? L.Tr("暂停") : L.Tr("播放页面"), GUILayout.Width(60)))
                    {
                        preview.Playing = !preview.Playing;
                    }
                    if (GUILayout.Button(L.Tr("单步"), GUILayout.Width(45)))
                    {
                        AuthoringEnvironment.Try(() =>
                        {
                            preview.Playing = true;
                            preview.Tick(1f / 60);
                            preview.Playing = false;
                        });
                    }
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
            }
            var area = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandHeight(true));
            float scale = Mathf.Min((area.width - 40) / preview.Width, (area.height - 40) / preview.Height) * zoom;
            scale = Mathf.Max(.01f, scale);
            var canvas = new Rect(area.center - new Vector2(preview.Width, preview.Height) * scale * .5f + pan, new Vector2(preview.Width, preview.Height) * scale);
            var selected = AuthoringEnvironment.ActiveNode;
            bool hasSelection = selected && !selected.IsLive && selected.IsTargetValid && selected.Node?.HasLayout == true;
            Rect selectedRect = default;
            if (hasSelection)
            {
                Rect bounds = selected.Node.Bounds;
                selectedRect = new Rect(canvas.position + bounds.position * scale, bounds.size * scale);
            }
            if (Event.current.type == EventType.Repaint)
            {
                latestArea = area;
                latestCanvas = canvas;
                latestScale = scale;
                DrawArtboard(area, canvas, scale, selected, hasSelection, selectedRect);
            }
            if (area.Contains(Event.current.mousePosition))
            {
                if (!interact)
                {
                    HandleNodeDrop(canvas, scale);
                }
                HandleAssetDrop(canvas);
            }
            HandleInput(area, canvas, scale, selected, selectedRect);
            if (!rememberView)
            {
                EditorGUILayout.LabelField(L.Message(AuthoringEnvironment.Status), EditorStyles.wordWrappedMiniLabel);
            }
        }

        void DrawArtboard(Rect area, Rect canvas, float scale, ElementSelectionProxy selected, bool hasSelection, Rect selectedRect)
        {
            var pointer = (localNodeDrag == null ? Event.current.mousePosition : localDragPosition) - area.position;
            canvas.position -= area.position;
            selectedRect.position -= area.position;
            var preview = AuthoringEnvironment.Preview;
            GUI.BeginClip(area);
            try
            {
                PaintArtboard(new Rect(Vector2.zero, area.size), canvas, scale, preview, selected, hasSelection, selectedRect, pointer);
            }
            finally
            {
                GUI.EndClip();
            }
        }

        void PaintArtboard(Rect area, Rect canvas, float scale, Preview.PreviewSession preview,
            ElementSelectionProxy selected, bool hasSelection, Rect selectedRect, Vector2 pointer)
        {
            Color backdrop = EditorGUIUtility.isProSkin ? new Color(.125f, .135f, .16f) : new Color(.82f, .84f, .88f);
            EditorGUI.DrawRect(area, rememberView ? backdrop : new Color(.12f, .12f, .13f));
            if (rememberView)
            {
                EditorGUI.DrawRect(new Rect(canvas.x - 1, canvas.y - 1, canvas.width + 2, canvas.height + 2),
                                    EditorGUIUtility.isProSkin ? new Color(.25f, .28f, .34f) : new Color(.56f, .61f, .68f));
            }
            GUI.DrawTexture(canvas, preview.Target, ScaleMode.StretchToFill, false);
            if (safeArea)
            {
                float left = Mathf.Clamp(safeInsets.x, 0, preview.Width);
                float top = Mathf.Clamp(safeInsets.y, 0, preview.Height);
                float right = Mathf.Clamp(safeInsets.z, 0, preview.Width - left);
                float bottom = Mathf.Clamp(safeInsets.w, 0, preview.Height - top);
                Outline(new Rect(canvas.x + left * scale, canvas.y + top * scale, (preview.Width - left - right) * scale, (preview.Height - top - bottom) * scale), new Color(1, .7f, .1f));
            }
            if (hasSelection && !interact)
            {
                Outline(selectedRect, new Color(.2f, .65f, 1));
                if (tool == "Rect" && CanTransform(selected, true))
                {
                    foreach (Vector2 direction in Directions())
                    {
                        var point = Point(selectedRect, direction);
                        EditorGUI.DrawRect(new Rect(point - Vector2.one * 3, Vector2.one * 6), Color.white);
                    }
                }
            }
            if (!interact && area.Contains(pointer))
            {
                var drag = localNodeDrag ?? WorkbenchDrag.Current;
                if (drag == null)
                {
                    DrawHover(canvas, scale, pointer);
                }
                else if (canvas.Contains(pointer) && drag.CanDrop(CanvasPlacement(drag, pointer, canvas, scale, out var marker)))
                {
                    Outline(marker, new Color(.45f, .82f, .99f));
                }
            }
            if (marquee)
            {
                Rect frame = MarqueeRect();
                frame.position -= latestArea.position;
                Outline(frame, new Color(.3f, .8f, 1));
            }
        }

        internal void TrackNodeDrag(WorkbenchDrag drag, Vector2 pointer, bool drop)
        {
            localNodeDrag = drop ? null : drag;
            localDragPosition = pointer;
            if (drop && drag != null && !interact && latestArea.Contains(pointer) && latestCanvas.Contains(pointer) &&
                AuthoringEnvironment.Preview?.IsReady == true)
            {
                AuthoringEnvironment.Try(() => drag.Drop(CanvasPlacement(drag, pointer, latestCanvas, latestScale, out _), nativeDrag: false));
            }
            Repaint();
        }

        internal bool FrameSelection()
        {
            var preview = AuthoringEnvironment.Preview;
            if (interact || preview?.IsReady != true || latestArea.width < 1 || latestArea.height < 1)
            {
                return false;
            }
            var selected = AuthoringEnvironment.ActiveNode;
            if (selected && !selected.IsLive && selected.IsTargetValid && selected.Node?.HasLayout == true)
            {
                Rect bounds = selected.Node.Bounds;
                float fit = Mathf.Max(.01f, Mathf.Min((latestArea.width - 40) / preview.Width, (latestArea.height - 40) / preview.Height));
                float focusScale = Mathf.Min((latestArea.width - 80) / Mathf.Max(1, bounds.width), (latestArea.height - 80) / Mathf.Max(1, bounds.height));
                zoom = Mathf.Clamp(focusScale / fit, .05f, 10);
                pan = (new Vector2(preview.Width, preview.Height) * .5f - bounds.center) * fit * zoom;
            }
            else
            {
                zoom = 1;
                pan = Vector2.zero;
            }
            Repaint();
            return true;
        }

        void DrawWorkbenchToolbar(Preview.PreviewSession preview)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Space(4);
                bool next = GUILayout.Toolbar(interact ? 1 : 0, new[] { L.Tr("编辑"), L.Tr("交互预览") },
                    EditorStyles.toolbarButton, GUILayout.Width(122)) == 1;
                if (interact != next)
                {
                    CancelInput();
                    interact = next;
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(L.Tr("适应"), L.Tr("将页面居中并适应画布")), EditorStyles.toolbarButton))
                {
                    zoom = 1;
                    pan = Vector2.zero;
                }
                GUILayout.Label((zoom * 100).ToString("0") + "%", EditorStyles.miniLabel, GUILayout.Width(32));
                string size = (preview?.Width ?? width) + " × " + (preview?.Height ?? height);
                if (GUILayout.Button(new GUIContent(size, L.Tr("尺寸预设")), EditorStyles.toolbarDropDown, GUILayout.Width(94)))
                {
                    ShowViewportPresets();
                }
                if (GUILayout.Button(new GUIContent("⋯", L.Tr("画布选项")), EditorStyles.toolbarButton, GUILayout.Width(28)))
                {
                    ShowWorkbenchOptions(preview);
                }
            }
            if (customSize)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(L.Tr("自定义尺寸"), GUILayout.Width(96));
                    width = EditorGUILayout.DelayedIntField(width, GUILayout.Width(48));
                    GUILayout.Label("×", GUILayout.Width(12));
                    height = EditorGUILayout.DelayedIntField(height, GUILayout.Width(48));
                    if (GUILayout.Button(L.Tr("应用"), EditorStyles.miniButton))
                    {
                        AuthoringEnvironment.Try(() =>
                                                {
                                                    AuthoringEnvironment.EnsurePreview();
                                                    AuthoringEnvironment.Preview?.Resize(width, height);
                                                    customSize = false;
                                                });
                    }
                }
            }
        }

        void ShowWorkbenchOptions(Preview.PreviewSession preview)
        {
            var menu = new GenericMenu();
            foreach (var entry in new[] { ("Rect", "矩形"), ("Move", "移动"), ("Rotate", "旋转"), ("Scale", "缩放") })
            {
                menu.AddItem(new GUIContent(L.Tr(entry.Item2)), tool == entry.Item1, () =>
                                {
                                    CancelInput();
                                    tool = entry.Item1;
                                    interact = false;
                                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(L.Tr("吸附")), snapping, () => snapping = !snapping);
            menu.AddItem(new GUIContent(L.Tr("安全区")), safeArea, () => safeArea = !safeArea);
            if (preview?.IsReady == true)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(L.Tr(preview.Playing ? "暂停" : "播放页面")), preview.Playing, () => preview.Playing = !preview.Playing);
                menu.AddItem(new GUIContent(L.Tr("单步")), false, () => AuthoringEnvironment.Try(() =>
                {
                    preview.Playing = true;
                    try
                    {
                        preview.Tick(1f / 60);
                    }
                    finally
                    {
                        preview.Playing = false;
                    }
                }));
            }
            menu.ShowAsContext();
        }
        void HandleInput(Rect area, Rect canvas, float scale, ElementSelectionProxy selected, Rect selectedRect)
        {
            var evt = Event.current;
            var preview = AuthoringEnvironment.Preview;
            if (evt.type == EventType.ScrollWheel && area.Contains(evt.mousePosition))
            {
                float before = zoom;
                zoom = Mathf.Clamp(before * Mathf.Exp(-evt.delta.y * .04f), .05f, 10);
                pan += (evt.mousePosition - canvas.center) * (1 - zoom / before);
                evt.Use();
                Repaint();
                return;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 2 && area.Contains(evt.mousePosition))
            {
                pan += evt.delta;
                evt.Use();
                Repaint();
                return;
            }
            if (!interact && evt.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if (evt.keyCode == KeyCode.F && FrameSelection())
                {
                    evt.Use();
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
                if ((area.Contains(evt.mousePosition) && canvas.Contains(evt.mousePosition) || pointerButtons != 0) && (evt.type == EventType.MouseDown || evt.type == EventType.MouseUp || evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove))
                {
                    PointerPhase phase = evt.type == EventType.MouseDown ? PointerPhase.Down : evt.type == EventType.MouseUp ? PointerPhase.Up : PointerPhase.Move;
                    int button = Math.Max(0, Math.Min(2, evt.button));
                    if (phase == PointerPhase.Down)
                    {
                        pointerButtons |= 1 << button;
                    }
                    else if (phase == PointerPhase.Up)
                    {
                        pointerButtons &= ~(1 << button);
                    }
                    preview.Document.Surface.Input.SendPointer(phase, (evt.mousePosition - canvas.position) / scale, button: button, pressedButtons: pointerButtons);
                    RuntimeInspectionBridge.Refresh(preview.Document);
                    preview.Refresh();
                    evt.Use();
                }
                else if (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp)
                {
                    preview.Document.Surface.Input.SendKey(evt.keyCode, evt.type == EventType.KeyDown);
                    if (evt.type == EventType.KeyDown && !char.IsControl(evt.character))
                    {
                        preview.Document.Surface.Input.SendText(evt.character.ToString());
                    }
                    preview.Refresh();
                    evt.Use();
                }
                return;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && area.Contains(evt.mousePosition) && canvas.Contains(evt.mousePosition))
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
                if (selected && tool == "Rect" && CanTransform(selected, true))
                {
                    foreach (Vector2 direction in Directions())
                    {
                        if ((Point(selectedRect, direction) - evt.mousePosition).sqrMagnitude < 64)
                        {
                            resizeDirection = direction;
                            break;
                        }
                    }
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
                    else
                    {
                        AuthoringEnvironment.Select(null);
                        selected = null;
                    }
                }
                if (evt.control || evt.command)
                {
                    return;
                }
                if (selected && selected.IsTargetValid && selected.Node != null && selected.Node.HasLayout)
                {
                    if (!CanTransform(selected, resizeDirection != Vector2.zero))
                    {
                        if (NodePlacement.Editing && selected.SourceNode.Tag != "body" && IsFlow(selected.Node) && (tool == "Move" || tool == "Rect"))
                        {
                            pendingFlowDrag = AuthoringEnvironment.Author.NodeIdentity(selected.SourceKey);
                            pendingFlowRevision = selected.Revision;
                            dragStart = evt.mousePosition;
                            evt.Use();
                        }
                        return;
                    }
                    dragging = true;
                    previous = dragStart = evt.mousePosition;
                    initialBounds = selected.Node.Layout;
                    startTransform = selected.Read("style.transform") ?? selected.Node.Computed("transform") ?? "";
                    gesture?.Dispose();
                    gesture = new CanvasGesture(selected);
                    initialStyles.Clear();
                    foreach (string name in new[] { "left", "top", "width", "height" })
                    {
                        initialStyles[name] = selected.Read("style." + name) ?? selected.Node.Computed(name);
                    }
                    if (tool != "Rotate" && tool != "Scale")
                    {
                        foreach (string axis in new[] { "left", "top" })
                        {
                            if (IsAuto(initialStyles[axis]) && RectCommands.TryAutomaticOffset(selected.Node, axis, out string offset))
                            {
                                initialStyles[axis] = offset;
                            }
                        }
                    }
                    containingSize = tool == "Rotate" || tool == "Scale" ? Vector2.zero : RectCommands.ContainingSize(selected.Node);
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    evt.Use();
                }
            }
            if (pendingFlowDrag != null)
            {
                if (evt.type == EventType.MouseDrag && (evt.mousePosition - dragStart).sqrMagnitude >= 36f)
                {
                    if (pendingFlowRevision == AuthoringEnvironment.Author.Revision)
                    {
                        WorkbenchDrag.Start(L.Tr("移动"), identity: pendingFlowDrag);
                    }
                    pendingFlowDrag = null;
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp || (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape))
                {
                    pendingFlowDrag = null;
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
                var objects = new List<ElementSelectionProxy>();
                Rect box = MarqueeRect();
                foreach (var source in AuthoringEnvironment.Author.Model.Nodes)
                {
                    var node = preview.Node(source.Key);
                    if (node == null || !node.IsValid || !node.HasLayout || node.Bounds.width <= 0 || node.Bounds.height <= 0)
                    {
                        continue;
                    }
                    var rect = new Rect(canvas.position + node.Bounds.position * scale, node.Bounds.size * scale);
                    if (box.Contains(rect.min) && box.Contains(rect.max))
                    {
                        objects.Add(ElementSelectionProxy.ForAuthor(source.Key));
                    }
                }
                AuthoringEnvironment.SetNodeSelection(objects);
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
                {
                    delta = new Vector2(Mathf.Round(delta.x), Mathf.Round(delta.y));
                }
                previous = evt.mousePosition;
                var total = (evt.mousePosition - dragStart) / scale;
                if (snapping)
                {
                    total = new Vector2(Mathf.Round(total.x), Mathf.Round(total.y));
                }
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
                finally
                {
                    gesture?.Dispose();
                    gesture = null;
                }
                evt.Use();
            }
        }
        Rect MarqueeRect() => Rect.MinMaxRect(Mathf.Min(marqueeStart.x, marqueeEnd.x), Mathf.Min(marqueeStart.y, marqueeEnd.y), Mathf.Max(marqueeStart.x, marqueeEnd.x), Mathf.Max(marqueeStart.y, marqueeEnd.y));
        void ShowViewportPresets()
        {
            var menu = new GenericMenu();
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1080, 1920), new Vector2Int(1024, 768), new Vector2Int(2532, 1170) })
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
            if (rememberView)
            {
                menu.AddItem(new GUIContent(L.Tr("自定义尺寸")), customSize, () => customSize = !customSize);
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(L.Tr("Safe area/None")), false, () =>
            {
                safeInsets = Vector4.zero;
                safeArea = true;
            });
            menu.AddItem(new GUIContent(L.Tr("Safe area/Portrait cutout · T59 B34")), false, () =>
            {
                safeInsets = new Vector4(0, 59, 0, 34);
                safeArea = true;
            });
            menu.AddItem(new GUIContent(L.Tr("Safe area/Landscape cutout · L59 R59 B21")), false, () =>
            {
                safeInsets = new Vector4(59, 0, 59, 21);
                safeArea = true;
            });
            menu.ShowAsContext();
        }
        static bool IsFlow(InspectedNode node) => node.Computed("position") is not ("absolute" or "fixed");

        bool CanTransform(ElementSelectionProxy proxy, bool resize)
        {
            var node = proxy.Node;
            if (node == null || !node.HasLayout || proxy.SourceNode?.Tag == "body")
            {
                return false;
            }
            string transform = proxy.Read("style.transform") ?? node.Computed("transform");
            if (!resize && (tool == "Rotate" || tool == "Scale"))
            {
                return node.Origin("transform") is not ("Important" or "Animation" or "Transition") && (string.IsNullOrEmpty(transform) || transform == "none");
            }
            if (IsFlow(node) || (!string.IsNullOrEmpty(transform) && transform != "none"))
            {
                return false;
            }
            for (var parent = node.Parent; parent != null; parent = parent.Parent)
            {
                string value = parent.Computed("transform");
                if (!string.IsNullOrEmpty(value) && value != "none")
                {
                    return false;
                }
            }
            foreach (string name in resize ? new[] { "left", "top", "width", "height" } : new[] { "left", "top" })
            {
                string value = proxy.Read("style." + name) ?? node.Computed(name);
                if (node.Origin(name) is "Important" or "Transition" or "Animation")
                {
                    return false;
                }
                if (IsAuto(value) && name is ("left" or "top") && RectCommands.TryAutomaticOffset(node, name, out value))
                {
                    continue;
                }
                if (!URUI.Authoring.Presentation.StyleFields.TryNumber(value, out _, out string unit) || unit is not ("px" or "%"))
                {
                    return false;
                }
            }
            // Keep opposing constraints intact. These cases are edited explicitly in the Inspector.
            return IsAuto(node.Computed("right")) && IsAuto(node.Computed("bottom"));
        }

        static bool IsAuto(string value) => string.IsNullOrEmpty(value) || value == "auto";

        void DrawHover(Rect canvas, float scale, Vector2 pointer)
        {
            if (dragging || marquee || !canvas.Contains(pointer))
            {
                return;
            }
            var preview = AuthoringEnvironment.Preview;
            var point = (pointer - canvas.position) / scale;
            var hit = RuntimeInspectionBridge.Pick(preview.Document, point);
            var key = preview.Key(hit);
            if (key == null || !hit.HasLayout || key == AuthoringEnvironment.ActiveNode?.SourceKey)
            {
                return;
            }
            var bounds = hit.Bounds;
            var rect = new Rect(canvas.position + bounds.position * scale, bounds.size * scale);
            Outline(rect, new Color(.42f, .75f, .79f, .8f));
            var node = AuthoringEnvironment.Author.Model.Find(key);
            string name = node.Attribute("id") ?? node.Attribute("class") ?? node.Tag;
            var hint = new Rect(rect.x, Mathf.Max(canvas.y, rect.y - 18), Mathf.Min(canvas.width, 320), 18);
            GUI.Label(hint, name, EditorStyles.whiteMiniLabel);
        }

        void HandleNodeDrop(Rect canvas, float scale)
        {
            var drag = WorkbenchDrag.Current;
            if (drag == null || !canvas.Contains(Event.current.mousePosition))
            {
                return;
            }
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            {
                return;
            }
            var destination = CanvasPlacement(drag, evt.mousePosition, canvas, scale, out _);
            if (evt.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = drag.CanDrop(destination)
                    ? drag.Identity == null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Move
                    : DragAndDropVisualMode.Rejected;
                Repaint();
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform)
            {
                AuthoringEnvironment.Try(() => drag.Drop(destination));
                evt.Use();
            }
        }

        static NodePlacement CanvasPlacement(WorkbenchDrag drag, Vector2 pointer, Rect canvas, float scale, out Rect marker)
        {
            var preview = AuthoringEnvironment.Preview;
            Vector2 point = (pointer - canvas.position) / scale;
            var hit = RuntimeInspectionBridge.Pick(preview.Document, point);
            string key = preview.Key(hit);
            var source = key == null ? null : AuthoringEnvironment.Author.Model.Find(key);
            var destination = NodePlacement.ChildOf(source);
            marker = canvas;
            if (source != null && hit.HasLayout)
            {
                var bounds = hit.Bounds;
                marker = new Rect(canvas.position + bounds.position * scale, bounds.size * scale);
                bool horizontal = hit.Parent?.Computed("display") == "flex" &&
                    (hit.Parent.Computed("flex-direction") ?? "row").StartsWith("row", StringComparison.Ordinal);
                float fraction = horizontal ? (point.x - bounds.x) / Mathf.Max(bounds.width, 1) : (point.y - bounds.y) / Mathf.Max(bounds.height, 1);
                bool container = NodePlacement.AcceptsChildren(source);
                if (source.Tag != "body" && (!container || fraction < .25f || fraction > .75f || drag.Identity != null))
                {
                    bool after = fraction >= .5f;
                    string direction = hit.Parent?.Computed("flex-direction");
                    bool reverse = direction == "row-reverse" || direction == "column-reverse";
                    destination = NodePlacement.Beside(source, reverse ? !after : after);
                    marker = horizontal
                        ? new Rect(after ? marker.xMax : marker.xMin, marker.y, 2, marker.height)
                        : new Rect(marker.x, after ? marker.yMax : marker.yMin, marker.width, 2);
                }
            }
            return destination;
        }

        void Drag(ElementSelectionProxy proxy, Vector2 delta, Vector2 total)
        {
            var node = proxy.Node;
            string positionMode = node.Computed("position");
            if (positionMode != "absolute" && positionMode != "fixed" && tool != "Rotate" && tool != "Scale")
            {
                throw new InvalidOperationException("Layout drives this node. Edit flex/order/gap/alignment in Inspector, or explicitly choose an anchor preset.");
            }
            var author = AuthoringEnvironment.Author;
            var edits = new List<AuthoringEdit>();
            void Add(string property, string value) => edits.Add(new AuthoringEdit
            {
                Node = proxy.SourceKey,
                Property = "style." + property,
                Value = value
            });
            if (tool == "Rotate")
            {
                Add("transform", (startTransform == "none" ? "" : startTransform) + " rotate(" + total.x.ToString("0.###", CultureInfo.InvariantCulture) + "deg)");
            }
            else if (tool == "Scale")
            {
                Add("transform", (startTransform == "none" ? "" : startTransform) + " scale(" + Mathf.Max(.01f, 1 + total.x / 100).ToString("0.###", CultureInfo.InvariantCulture) + ")");
            }
            else
            {
                var layout = initialBounds;
                var parent = containingSize;
                delta = total;
                bool resize = resizeDirection != Vector2.zero;
                if (!resize || resizeDirection.x < 0)
                {
                    Add("left", RectCommands.Offset(initialStyles["left"], delta.x, parent.x, layout.x));
                }
                if (!resize || resizeDirection.y < 0)
                {
                    Add("top", RectCommands.Offset(initialStyles["top"], delta.y, parent.y, layout.y));
                }
                if (resize && resizeDirection.x != 0)
                {
                    Add("width", RectCommands.Offset(initialStyles["width"], Mathf.Max(1, layout.width + delta.x * resizeDirection.x) - layout.width, parent.x, layout.width));
                }
                if (resize && resizeDirection.y != 0)
                {
                    Add("height", RectCommands.Offset(initialStyles["height"], Mathf.Max(1, layout.height + delta.y * resizeDirection.y) - layout.height, parent.y, layout.height));
                }
            }
            gesture.Preview(edits);
        }
        void HandleAssetDrop(Rect canvas)
        {
            var evt = Event.current;
            if (!canvas.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform))
            {
                return;
            }
            if (DragAndDrop.objectReferences.Length != 1 || !(DragAndDrop.objectReferences[0] is Texture2D texture))
            {
                return;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AuthoringEnvironment.Try(() =>
                {
                    var proxy = AuthoringEnvironment.ActiveNode;
                    if (!proxy || proxy.IsLive || !proxy.IsTargetValid)
                    {
                        throw new InvalidOperationException("Select an image node first.");
                    }
                    string relative = Preview.AuthoringAssetPaths.Reference(AuthoringEnvironment.ProjectRoot, AuthoringEnvironment.Author.File.Path, AssetDatabase.GetAssetPath(texture));
                    proxy.Set(proxy.SourceNode.Tag == "img" ? "attr.src" : "style.background-image", proxy.SourceNode.Tag == "img" ? relative : "url(\"" + relative + "\")");
                });
            }
            evt.Use();
        }
        static IEnumerable<Vector2> Directions()
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (x != 0 || y != 0)
                    {
                        yield return new Vector2(x, y);
                    }
                }
            }
        }
        static Vector2 Point(Rect rect, Vector2 direction) => new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, (direction.x + 1) * .5f), Mathf.Lerp(rect.yMin, rect.yMax, (direction.y + 1) * .5f));
        static void Outline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }
        void RestoreView()
        {
            if (!rememberView)
            {
                return;
            }
            string path = AuthoringEnvironment.Author.File?.Path;
            if (path == null || path == viewState?.Path)
            {
                return;
            }
            SaveView();
            viewState = WorkbenchViewState.instance.ForPage(path);
            zoom = Mathf.Clamp(viewState.Zoom, .05f, 10);
            pan = viewState.Pan;
            appliedSize = default;
            if (AuthoringEnvironment.Preview is { } preview && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                var state = viewState;
                Vector2Int size = state.Size.x > 0 ? state.Size : preview.DefaultSize;
                if (size.x == preview.Width && size.y == preview.Height)
                {
                    return;
                }
                EditorApplication.delayCall += () =>
                {
                    if (state == viewState && path == AuthoringEnvironment.Author.File?.Path && !EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        AuthoringEnvironment.Try(() => AuthoringEnvironment.Preview?.Resize(size.x, size.y));
                    }
                };
            }
        }

        void SaveView()
        {
            if (viewState == null || (viewState.Zoom == zoom && viewState.Pan == pan && viewState.Size == appliedSize))
            {
                return;
            }
            viewState.Zoom = zoom;
            viewState.Pan = pan;
            viewState.Size = appliedSize;
            WorkbenchViewState.instance.SaveView();
        }

        internal void CancelInput()
        {
            SaveView();
            gesture?.Dispose();
            gesture = null;
            dragging = marquee = false;
            pendingFlowDrag = null;
            GUIUtility.hotControl = 0;
            pointerButtons = 0;
            if (AuthoringEnvironment.Preview?.Document && AuthoringEnvironment.Preview.Document.IsLoaded)
            {
                AuthoringEnvironment.Preview.Document.Surface.Input.Reset();
            }
        }
    }
}
