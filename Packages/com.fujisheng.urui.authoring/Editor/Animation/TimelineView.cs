using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;
using URUI.Authoring.Presentation;

namespace URUI.Authoring.Animation
{
    internal sealed class TimelineView
    {
        readonly Action repaint;
        readonly List<AnimationWorkspace.KeySelection> selected = new List<AnimationWorkspace.KeySelection>();
        Vector2 scroll;
        float zoom = 1;
        double timeOffset;
        bool dragging, scrubbing;
        float dragStart, dragDelta;
        long dragRevision;
        bool customProperty;
        string keyProperty = "opacity", keyValue = "1";
        internal TimelineView(Action repaint)
        {
            this.repaint = repaint;
        }
        void Edit(Action action) => AuthoringEnvironment.Try(() => SessionUndoBridge.Record(AuthoringEnvironment.Author, action));
        public void Draw()
        {
            AnimationEnvironment.EnsureWorkspace();
            var workspace = AnimationEnvironment.Workspace;
            var definition = workspace.Definition;
            DrawToolbar(definition);
            definition = workspace.Definition;
            if (definition == null)
            {
                EditorGUILayout.HelpBox(L.Tr("选中节点，点击“添加动画”，选择淡入或弹出等预设即可开始。关键帧保存在 CSS，序列文件保存时间与绑定。"), MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.DelayedTextField(L.Tr("序列"), definition.Name);
                double duration = EditorGUILayout.DelayedDoubleField(L.Tr("总时长（秒）"), definition.Duration);
                bool loop = GUILayout.Toggle(definition.Loop, L.Tr("循环"), GUILayout.Width(55));
                if (EditorGUI.EndChangeCheck())
                    Edit(() => workspace.SetSequence(name, duration, loop, workspace.Revision));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("−", GUILayout.Width(26)))
                    zoom = Mathf.Max(1, zoom / 1.5f);
                if (GUILayout.Button("＋", GUILayout.Width(26)))
                    zoom *= 1.5f;
                if (GUILayout.Button(L.Tr("适应全部"), GUILayout.Width(68)))
                {
                    zoom = 1;
                    timeOffset = 0;
                }
                zoom = Mathf.Clamp(zoom, 1, (float)Math.Max(1, definition.Duration * AnimationEnvironment.FrameRate / 10));
                GUILayout.Label(L.Tr("时间缩放 ") + zoom.ToString("0.#") + "×", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(L.Tr("拖入节点可在当前位置建轨"), EditorStyles.miniLabel);
            }
            double visibleDuration = definition.Duration / zoom;
            timeOffset = Math.Max(0, Math.Min(timeOffset, definition.Duration - visibleDuration));
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(110), GUILayout.MaxHeight(260));
            var area = GUILayoutUtility.GetRect(200, Math.Max(100, 28 * (definition.Tracks.Count + 1)), GUILayout.ExpandWidth(true));
            float labelWidth = Mathf.Min(240, area.width * .35f);
            var timeline = new Rect(area.x + labelWidth, area.y, Mathf.Max(20, area.width - labelWidth - 8), area.height);
            EditorGUI.DrawRect(timeline, new Color(.12f, .12f, .13f));
            for (int i = 0; i <= 10; i++)
            {
                float x = timeline.x + timeline.width * i / 10f;
                EditorGUI.DrawRect(new Rect(x, timeline.y, 1, timeline.height), new Color(.25f, .25f, .27f));
                GUI.Label(new Rect(x + 3, timeline.y, 58, 20), (timeOffset + visibleDuration * i / 10).ToString("0.##", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
            }
            for (int i = 0; i < definition.Tracks.Count; i++)
                DrawTrack(definition.Tracks[i], i, area, labelWidth, timeline, visibleDuration);
            float needle = timeline.x + (float)((AnimationEnvironment.Time - timeOffset) / Math.Max(.001, visibleDuration)) * timeline.width;
            if (needle >= timeline.x && needle <= timeline.xMax)
                EditorGUI.DrawRect(new Rect(needle, timeline.y, 2, timeline.height), new Color(1, .45f, .25f));
            HandleDrop(timeline, visibleDuration);
            HandleTimeline(timeline, visibleDuration);
            EditorGUILayout.EndScrollView();
            if (zoom > 1)
                timeOffset = GUILayout.HorizontalSlider((float)timeOffset, 0, (float)Math.Max(0, definition.Duration - visibleDuration));
            var track = definition.Tracks.FirstOrDefault(t => t.Id == AnimationEnvironment.SelectedTrack);
            if (track != null)
                DrawSettings(track);
            if (selected.Count > 0)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(L.Tr("已选择 ") + selected.Count + L.Tr(" 个关键帧"), EditorStyles.miniLabel);
                    if (GUILayout.Button(L.Tr("复制到当前时间")))
                        TransformToCurrent(definition, true);
                    if (GUILayout.Button(L.Tr("移动到当前时间")))
                        TransformToCurrent(definition, false);
                    if (GUILayout.Button(L.Tr("删除关键帧")))
                        Edit(() => workspace.TransformKeys(selected, 0, false, true, workspace.Revision));
                    if (GUILayout.Button(L.Tr("清除选择")))
                        selected.Clear();
                }
            EditorGUILayout.HelpBox(AnimationEnvironment.Recording ? L.Tr("● 录制中：在 Inspector 或画布调整当前节点，写入时间针位置的关键帧。") : L.Tr("拖动上方刻度预览；点击菱形选关键帧，Shift / Cmd 多选。拖动关键帧可整组移动，Option 拖动复制。"), MessageType.None);
        }
        void DrawToolbar(SequenceDefinition definition)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(AnimationEnvironment.Playing ? L.Tr("暂停") : L.Tr("▶ 播放"), EditorStyles.toolbarButton, GUILayout.Width(65)))
                    AuthoringEnvironment.Try(() => { if (AnimationEnvironment.Playing) AnimationEnvironment.Pause(); else AnimationEnvironment.Play(); });
                if (GUILayout.Button(L.Tr("■ 停止"), EditorStyles.toolbarButton, GUILayout.Width(55)))
                    AuthoringEnvironment.Try(AnimationEnvironment.Stop);
                if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(26)))
                    AuthoringEnvironment.Try(() => AnimationEnvironment.Seek(Math.Max(0, AnimationEnvironment.Time - 1 / AnimationEnvironment.FrameRate)));
                if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(26)))
                    AuthoringEnvironment.Try(() => AnimationEnvironment.Seek(AnimationEnvironment.Time + 1 / AnimationEnvironment.FrameRate));
                double time = EditorGUILayout.DelayedDoubleField(GUIContent.none, AnimationEnvironment.Time, GUILayout.Width(65));
                if (time != AnimationEnvironment.Time)
                    AuthoringEnvironment.Try(() => AnimationEnvironment.Seek(time));
                GUILayout.Label(L.Tr("秒"), GUILayout.Width(18));
                AnimationEnvironment.Snap = GUILayout.Toggle(AnimationEnvironment.Snap, L.Tr("帧吸附"), EditorStyles.toolbarButton, GUILayout.Width(85));
                if (GUILayout.Button(AnimationEnvironment.Recording ? L.Tr("● 结束录制") : L.Tr("● 录制"), EditorStyles.toolbarButton, GUILayout.Width(85)))
                    AuthoringEnvironment.Try(() => { if (AnimationEnvironment.Recording) AnimationEnvironment.EndRecording(); else AnimationEnvironment.BeginRecording(Selection.activeObject as ElementSelectionProxy); });
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                {
                    if (GUILayout.Button(L.Tr("添加动画 ▾"), EditorStyles.toolbarDropDown))
                        ShowAdd();
                    if (GUILayout.Button(L.Tr("打开序列…"), EditorStyles.toolbarButton))
                    {
                        string path = EditorUtility.OpenFilePanel(L.Tr("打开 URUI 序列"), Application.dataPath, "json");
                        if (path.Length > 0)
                            AuthoringEnvironment.Try(() => { AnimationEnvironment.Stop(); AnimationEnvironment.EnsureWorkspace(); AnimationEnvironment.Workspace.Open(path); });
                    }
                }
            }
        }
        void ShowAdd()
        {
            var menu = new GenericMenu();
            foreach (var pair in new[] { new[] { L.Tr("弹出"), "popup" }, new[] { L.Tr("淡入"), "fade-in" }, new[] { L.Tr("淡出"), "fade-out" }, new[] { L.Tr("滑入"), "slide-in" }, new[] { L.Tr("按钮脉冲"), "pulse" }, new[] { L.Tr("基础关键帧"), "empty" } })
            {
                string preset = pair[1];
                menu.AddItem(new GUIContent(pair[0]), false, () => Edit(() =>
                {
                    var proxy = Selection.activeObject as ElementSelectionProxy;
                    if (!proxy || proxy.IsLive || !proxy.IsTargetValid)
                        throw new InvalidOperationException(L.Tr("请先在节点树或画布选中一个节点。"));
                    string id = AnimationEnvironment.Workspace.AddPreset(proxy.SourceKey, preset, 0, .3, AnimationEnvironment.Workspace.Revision);
                    AnimationEnvironment.SelectTrack(id);
                    AnimationEnvironment.Seek(0);
                }));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(L.Tr("粒子播放轨道")), false, () => Edit(() =>
            {
                var proxy = Selection.activeObject as ElementSelectionProxy;
                if (!proxy || proxy.IsLive || !proxy.IsTargetValid || proxy.SourceNode.Tag != "urui-particles")
                    throw new InvalidOperationException(L.Tr("请选择 urui-particles 节点，并设置粒子 Prefab 资源路径。"));
                string id = AnimationEnvironment.Workspace.AddParticles(proxy.SourceKey, proxy.SourceNode.Attribute("src") ?? "", 0, 1, AnimationEnvironment.Workspace.Revision);
                AnimationEnvironment.SelectTrack(id);
            }));
            menu.ShowAsContext();
        }
        void DrawTrack(SequenceTrack track, int index, Rect area, float labelWidth, Rect timeline, double duration)
        {
            var row = new Rect(area.x, area.y + 28 * (index + 1), area.width, 26);
            bool current = AnimationEnvironment.SelectedTrack == track.Id;
            if (current)
                EditorGUI.DrawRect(row, new Color(.2f, .3f, .42f, .6f));
            if (GUI.Button(new Rect(row.x, row.y, labelWidth - 78, 24), new GUIContent(track.Target, EditorGUIUtility.FindTexture(track.Kind == "particles" ? "ParticleSystem Icon" : "AnimationClip Icon"), L.Tr("绑定 id: ") + track.Target + L.Tr("\n轨道: ") + track.Id), EditorStyles.label))
                AnimationEnvironment.SelectTrack(track.Id);
            if (GUI.Button(new Rect(row.x + labelWidth - 78, row.y, 24, 24), track.Muted ? "M" : "○"))
                Edit(() => AnimationEnvironment.Workspace.SetTrack(track.Id, t => t.Muted = !t.Muted, AnimationEnvironment.Workspace.Revision));
            if (GUI.Button(new Rect(row.x + labelWidth - 52, row.y, 24, 24), new GUIContent(AnimationEnvironment.SoloTrack == track.Id ? "S" : "s", L.Tr("Solo"))))
                AuthoringEnvironment.Try(() => AnimationEnvironment.SetSolo(track.Id));
            if (GUI.Button(new Rect(row.x + labelWidth - 26, row.y, 24, 24), track.Locked ? L.Tr("锁") : "·"))
                Edit(() => AnimationEnvironment.Workspace.SetLocked(track.Id, !track.Locked, AnimationEnvironment.Workspace.Revision));
            float barX = timeline.x + (float)((track.Start - timeOffset) / Math.Max(.001, duration)) * timeline.width;
            float barWidth = (float)((track.End - track.Start) / Math.Max(.001, duration)) * timeline.width;
            EditorGUI.DrawRect(new Rect(Mathf.Clamp(barX, timeline.x, timeline.xMax), row.y + 5, Mathf.Max(0, Mathf.Clamp(barX + Math.Max(2, barWidth), timeline.x, timeline.xMax) - Mathf.Clamp(barX, timeline.x, timeline.xMax)), 15), track.Kind == "particles" ? new Color(.55f, .35f, .7f) : new Color(.25f, .5f, .65f));
            if (track.Kind != "css")
                return;
            IReadOnlyList<Source.CssKeyframes.Key> keys;
            try
            {
                keys = AnimationEnvironment.Workspace.Keys(track.Id);
            }
            catch (Exception error) { GUI.Label(new Rect(timeline.x, row.y, timeline.width, 24), error.Message); return; }
            foreach (double position in keys.Select(k => k.Position).Distinct())
            {
                var key = new AnimationWorkspace.KeySelection(track.Id, position);
                bool chosen = selected.Contains(key);
                double offset = chosen && dragging ? dragDelta : 0;
                float x = timeline.x + (float)((track.Start + position * track.Duration + offset - timeOffset) / Math.Max(.001, duration)) * timeline.width;
                if (x < timeline.x || x > timeline.xMax)
                    continue;
                var rect = new Rect(x - 7, row.y + 5, 14, 16);
                var old = GUI.color;
                GUI.color = chosen ? Color.yellow : Color.white;
                GUI.Label(rect, "◆");
                GUI.color = old;
                var evt = Event.current;
                if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
                {
                    if (!evt.shift && !evt.command && !evt.control && !chosen)
                        selected.Clear();
                    if ((evt.shift || evt.command || evt.control) && chosen)
                        selected.Remove(key);
                    else if (!chosen)
                        selected.Add(key);
                    AnimationEnvironment.SelectTrack(track.Id);
                    AnimationEnvironment.Pause();
                    dragging = !track.Locked;
                    dragStart = evt.mousePosition.x;
                    dragDelta = 0;
                    dragRevision = AnimationEnvironment.Workspace.Revision;
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    evt.Use();
                }
            }
        }
        void HandleDrop(Rect area, double duration)
        {
            var evt = Event.current;
            if (!area.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform))
                return;
            string key = DragAndDrop.GetGenericData("URUI.SourceKey") as string;
            if (key == null)
                return;
            bool valid = DragAndDrop.GetGenericData("URUI.SourceRevision") is long revision && revision == AnimationEnvironment.Workspace.Revision;
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (evt.type == EventType.DragPerform && valid)
            {
                double start = timeOffset + Mathf.Clamp01((evt.mousePosition.x - area.x) / area.width) * duration;
                if (AnimationEnvironment.Snap)
                    start = Math.Round(start * AnimationEnvironment.FrameRate) / AnimationEnvironment.FrameRate;
                DragAndDrop.AcceptDrag();
                Edit(() =>
                {
                    var node = AuthoringEnvironment.Author.Model.Find(key);
                    var workspace = AnimationEnvironment.Workspace;
                    string id = node.Tag == "urui-particles" ? workspace.AddParticles(key, node.Attribute("src") ?? "", start, 1, workspace.Revision) : workspace.AddPreset(key, "popup", start, .3, workspace.Revision);
                    AnimationEnvironment.SelectTrack(id);
                });
            }
            evt.Use();
        }
        void HandleTimeline(Rect area, double duration)
        {
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && area.Contains(evt.mousePosition))
            {
                scrubbing = true;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                SetTime(evt.mousePosition.x, area, duration);
                evt.Use();
            }
            if (evt.type == EventType.MouseDrag && (dragging || scrubbing))
            {
                if (dragging)
                {
                    dragDelta = (float)((evt.mousePosition.x - dragStart) / area.width * duration);
                    if (AnimationEnvironment.Snap)
                        dragDelta = (float)(Math.Round(dragDelta * AnimationEnvironment.FrameRate) / AnimationEnvironment.FrameRate);
                }
                else
                    SetTime(evt.mousePosition.x, area, duration);
                evt.Use();
                repaint();
            }
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                dragging = scrubbing = false;
                dragDelta = 0;
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            if (evt.type == EventType.MouseUp && (dragging || scrubbing))
            {
                if (dragging && Math.Abs(dragDelta) > 1e-8)
                {
                    bool copy = evt.alt;
                    double delta = dragDelta;
                    var snapshot = selected.ToArray();
                    Edit(() => AnimationEnvironment.Workspace.TransformKeys(snapshot, delta, copy, false, dragRevision));
                    selected.Clear();
                }
                dragging = scrubbing = false;
                dragDelta = 0;
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }
        void SetTime(float x, Rect area, double duration)
        {
            double time = timeOffset + Mathf.Clamp01((x - area.x) / area.width) * duration;
            if (AnimationEnvironment.Snap)
                time = Math.Round(time * AnimationEnvironment.FrameRate) / AnimationEnvironment.FrameRate;
            AuthoringEnvironment.Try(() => AnimationEnvironment.Seek(time));
        }
        void DrawSettings(SequenceTrack track)
        {
            using (new EditorGUI.DisabledScope(track.Locked || EditorApplication.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    double start = EditorGUILayout.DelayedDoubleField(L.Tr("开始（秒）"), track.Start);
                    double duration = EditorGUILayout.DelayedDoubleField(L.Tr("单轮时长"), track.Duration);
                    int count = EditorGUILayout.DelayedIntField(L.Tr("次数"), track.Iterations);
                    bool alternate = GUILayout.Toggle(track.Alternate, L.Tr("往返"), GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck())
                        Edit(() => AnimationEnvironment.Workspace.SetTrack(track.Id, t => { t.Start = start; t.Duration = duration; t.Iterations = count; t.Alternate = alternate; }, AnimationEnvironment.Workspace.Revision));
                }
                if (track.Kind == "css")
                {
                    string easing = CurveView.Draw(track.Easing);
                    if (easing != track.Easing)
                        Edit(() => AnimationEnvironment.Workspace.SetTrack(track.Id, t => t.Easing = easing, AnimationEnvironment.Workspace.Revision));
                    if (AnimationEnvironment.Workspace.IsShared(track.Id))
                        EditorGUILayout.HelpBox(L.Tr("该关键帧被多个轨道引用，修改会影响所有引用。"), MessageType.Info);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (customProperty)
                            keyProperty = EditorGUILayout.TextField(keyProperty, GUILayout.MinWidth(90));
                        else if (GUILayout.Button(PropertyWidgets.Label(keyProperty), EditorStyles.popup, GUILayout.MinWidth(90)))
                            ShowPropertyMenu(track);
                        customProperty = GUILayout.Toggle(customProperty, new GUIContent("CSS", L.Tr("直接输入真实 CSS 属性名。")), EditorStyles.miniButton, GUILayout.Width(34));
                        keyValue = PropertyWidgets.Draw("style." + keyProperty, keyValue, GUIContent.none, !customProperty);
                        if (GUILayout.Button(L.Tr("在当前时间添加关键帧")))
                            Edit(() =>
                        {
                            if (AnimationEnvironment.Time < track.Start || AnimationEnvironment.Time > track.Start + track.Duration)
                                throw new InvalidOperationException(L.Tr("请选择首轮轨道范围内的时间。"));
                            double position = track.Duration == 0 ? 1 : (AnimationEnvironment.Time - track.Start) / track.Duration;
                            AnimationEnvironment.Workspace.SetKey(track.Id, position, keyProperty, keyValue, AnimationEnvironment.Workspace.Revision);
                        });
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    string source = EditorGUILayout.DelayedTextField(L.Tr("粒子资源"), track.Source);
                    double speed = EditorGUILayout.DelayedDoubleField(L.Tr("速度"), track.Speed);
                    string seedText = EditorGUILayout.DelayedTextField(L.Tr("随机种子"), track.Seed.ToString(CultureInfo.InvariantCulture));
                    long seed = long.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0;
                    if (EditorGUI.EndChangeCheck())
                        Edit(() => AnimationEnvironment.Workspace.SetTrack(track.Id, t => { if (seed < 1 || seed > uint.MaxValue) throw new InvalidOperationException(L.Tr("种子必须为非零 uint。")); t.Source = source; t.Speed = speed; t.Seed = (uint)seed; }, AnimationEnvironment.Workspace.Revision));
                }
                if (GUILayout.Button(L.Tr("移除轨道")))
                    Edit(() => AnimationEnvironment.Workspace.RemoveTrack(track.Id, AnimationEnvironment.Workspace.Revision));
            }
        }
        void ShowPropertyMenu(SequenceTrack track)
        {
            var menu = new GenericMenu();
            foreach (string name in new[] { "opacity", "transform", "left", "top", "width", "height", "background-color", "color", "border-color", "border-radius", "font-size" })
            {
                string property = name;
                menu.AddItem(new GUIContent(L.Tr("界面/") + PropertyWidgets.Label(property)), keyProperty == property, () => SelectProperty(property, track));
            }
            foreach (var extension in AuthoringEnvironment.Extensions)
                foreach (var item in extension.Properties)
                    if (item.Name.StartsWith("style.", StringComparison.Ordinal))
                    {
                        string property = item.Name.Substring(6);
                        menu.AddItem(new GUIContent(L.Tr("特效/") + PropertyWidgets.Label(property)), keyProperty == property, () => SelectProperty(property, track));
                    }
            menu.ShowAsContext();
        }
        void SelectProperty(string property, SequenceTrack track)
        {
            keyProperty = property;
            var node = AnimationEnvironment.ResolveSourceTarget(track.Target);
            keyValue = "";
            if (node != null)
            {
                var proxy = ElementSelectionProxy.ForAuthor(node.Key);
                keyValue = proxy.Node?.Computed(property) ?? proxy.Read("style." + property) ?? "";
            }
            repaint?.Invoke();
        }
        void TransformToCurrent(SequenceDefinition definition, bool copy)
        {
            if (selected.Count == 0)
                return;
            double first = selected.Min(k => { var track = definition.Tracks.First(t => t.Id == k.Track); return track.Start + k.Position * track.Duration; });
            double delta = AnimationEnvironment.Time - first;
            Edit(() => AnimationEnvironment.Workspace.TransformKeys(selected, delta, copy, false, AnimationEnvironment.Workspace.Revision));
            selected.Clear();
        }
    }
}
