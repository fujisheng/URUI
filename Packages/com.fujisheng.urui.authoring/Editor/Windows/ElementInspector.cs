using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Authoring.Presentation;

namespace URUI.Authoring.Windows
{
    [CustomEditor(typeof(ElementSelectionProxy)), CanEditMultipleObjects]
    public sealed class ElementInspector : UnityEditor.Editor
    {
        internal bool AllowSharedRules { get; set; } = true;
        string search = "";
        bool advanced, computed, drawingAdvanced, drawingWorkbench, linkPadding, linkMargin;
        readonly HashSet<string> expressions = new HashSet<string>();
        readonly Dictionary<string, bool> panels = new Dictionary<string, bool>();
        sealed class RuleTarget
        {
            internal SourceFile File;
            internal string Text, Css, Label;
            internal int CssStart, CssEnd;
            internal long FileRevision;
            internal CssSource.Rule Rule;
        }
        readonly List<RuleTarget> ruleTargets = new List<RuleTarget>();
        long ruleRevision = -1;
        int selectedRule;
        void OnEnable()
        {
            AuthoringEnvironment.Observe(true);
            AuthoringEnvironment.Changed += Repaint;
        }
        void OnDisable()
        {
            AuthoringEnvironment.Observe(false);
            AuthoringEnvironment.Changed -= Repaint;
        }
        protected override void OnHeaderGUI()
        {
            var proxy = target as ElementSelectionProxy;
            if (proxy && proxy.IsTargetValid)
            {
                string tag = proxy.IsLive ? proxy.Node.Tag : proxy.SourceNode.Tag;
                EditorGUILayout.LabelField(L.NodeName(tag, proxy.Read("attr.id")), EditorStyles.largeLabel);
                EditorGUILayout.LabelField(L.Tr(NodePresentation.TypeLabel(tag)) + " · URUI", EditorStyles.miniLabel);
            }
        }
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        internal void DrawWorkbenchProperties()
        {
            if (target is not ElementSelectionProxy proxy || !proxy || !proxy.IsTargetValid)
                return;

            string tag = proxy.SourceNode.Tag;
            string type = proxy.Read("attr.type");
            bool text = !string.IsNullOrWhiteSpace(proxy.Read("text")) || tag is "span" or "p" or "label" or "button";
            bool control = tag is "input" or "textarea" or "select" or "progress";
            drawingWorkbench = true;
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * .36f, 86, 116);
            try
            {
                if (tag is "img" or "urui-particles" || text || control)
                {
                    WorkbenchSection("内容");
                    if (tag is "img" or "urui-particles")
                        DrawResource(proxy);
                    else if (control)
                    {
                        Row("value");
                        if (tag == "textarea" || tag == "input" && type is not ("range" or "checkbox" or "radio"))
                            Row("attr.placeholder");
                    }
                    else if (text)
                    {
                        Row("text");
                        if (tag == "button" && TextChild(proxy) is ElementSelectionProxy child &&
                            GUILayout.Button(L.Tr("选择文字子节点"), EditorStyles.miniButton))
                            AuthoringEnvironment.Select(child);
                    }
                }

                WorkbenchSection("大小与位置");
                Row("style.width");
                Row("style.height");
                Row("style.position");
                string position = DisplayedValue(proxy, "style.position");
                if (position is "absolute" or "fixed" or "relative")
                {
                    Row("style.left");
                    Row("style.top");
                }
                if (proxy.Node?.Computed("display") == "flex")
                {
                    Row("style.flex-direction");
                    Row("style.gap");
                }

                WorkbenchSection("外观");
                if (text)
                {
                    Row("style.font-size");
                    Row("style.color");
                }
                Row("style.background-color");
                Row("style.opacity");
                EditorGUILayout.Space(8);
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                drawingWorkbench = false;
            }
        }

        static void WorkbenchSection(string title)
        {
            EditorGUILayout.Space(11);
            EditorGUILayout.LabelField(L.Tr(title), EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
        }

        public override void OnInspectorGUI()
        {
            var proxy = target as ElementSelectionProxy;
            if (!proxy || !proxy.IsTargetValid)
            {
                EditorGUILayout.HelpBox(L.Tr("节点已失效，请从 URUI 节点树重新选择。"), MessageType.Info);
                return;
            }
            string tag = proxy.IsLive ? proxy.Node.Tag : proxy.SourceNode.Tag;
            string bindingName = proxy.Read("attr.id");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (AllowSharedRules)
                    EditorGUILayout.LabelField(L.NodeName(tag, bindingName), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(L.Tr(NodePresentation.TypeLabel(tag, proxy.Read("attr.type"))) + "  ·  <" + tag + ">", EditorStyles.miniLabel);
                Row("attr.id");
                if (!AllowSharedRules && !proxy.IsLive)
                    Row("attr.class");
                if (proxy.IsLive || AllowSharedRules)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(proxy.IsLive ? L.Tr("● Play · 临时修改") : L.Tr("HTML / CSS 工作副本"), EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(proxy.IsLive ? L.Tr("审阅并应用…") : L.Tr("保存"), EditorStyles.miniButton, GUILayout.Width(proxy.IsLive ? 100 : 50)))
                        {
                            if (proxy.IsLive)
                                LiveApplyWindow.Open(proxy);
                            else
                                AuthoringEnvironment.Try(AuthoringEnvironment.Save);
                        }
                    }
                }
            }
            if (!proxy.IsLive && Animation.AnimationEnvironment.Workspace.Definition != null && AuthoringEnvironment.Mode == WorkspaceMode.Animation)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(Animation.AnimationEnvironment.Recording ? L.Tr("● 正在录制关键帧") : L.Tr("动画姿态 · 临时预览"), EditorStyles.miniLabel);
                    if (GUILayout.Button(Animation.AnimationEnvironment.Recording ? L.Tr("结束录制") : L.Tr("录制"), EditorStyles.miniButton))
                        AuthoringEnvironment.Try(() => { if (Animation.AnimationEnvironment.Recording) Animation.AnimationEnvironment.EndRecording(); else Animation.AnimationEnvironment.BeginRecording(proxy); });
                }
            }
            var sections = InspectorPresentation.SectionsForControl(tag, proxy.Read("attr.type"), !string.IsNullOrWhiteSpace(proxy.Read("text")));
            if (!AllowSharedRules)
                sections = sections.OrderBy(section => section.Name == "文字 Text" || section.Name == "图像 Image" ? 0 : 1);
            foreach (var section in sections)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    bool open = panels.TryGetValue(section.Name, out bool expanded) ? expanded : section.Name == "布局 Layout" || section.Name == "变换 Transform" || section.Name == "图像 Image" || section.Name == "文字 Text" || section.Name == "交互 Interaction" || section.Name == "控件 Control" || section.Name == "粒子资源 Particles";
                    open = EditorGUILayout.Foldout(open, L.Tr(section.Name), true, EditorStyles.foldoutHeader);
                    panels[section.Name] = open;
                    if (!open)
                        continue;
                    EditorGUI.indentLevel++;
                    if (section.Name == "变换 Transform")
                        DrawTransform(proxy);
                    else
                    {
                        if (section.Name == "内外边距 Spacing")
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                linkPadding = GUILayout.Toggle(linkPadding, new GUIContent(L.Tr("内边距联动"), L.Tr("调整任意一边时，将同一值和单位写入四边。")), EditorStyles.miniButton);
                                linkMargin = GUILayout.Toggle(linkMargin, L.Tr("外边距联动"), EditorStyles.miniButton);
                            }
                        }
                        foreach (string property in section.Properties)
                        {
                            if (property == "attr.src" && (tag == "img" || tag == "urui-particles") && !proxy.IsLive)
                                DrawResource(proxy);
                            else
                                Row(property);
                        }
                    }
                    if (section.Name == "文字 Text" && tag == "button")
                    {
                        var child = TextChild(proxy);
                        if (child != null && GUILayout.Button(L.Tr("选择文字子节点"), EditorStyles.miniButton))
                            AuthoringEnvironment.Select(child);
                    }
                    if (section.Name == "布局 Layout")
                    {
                        if (proxy.Node != null && proxy.Node.HasLayout)
                        {
                            if (proxy.Node.Computed("position") == "static")
                                EditorGUILayout.HelpBox(L.Tr("位置由文档流或父容器排列控制；可调整排列顺序、间距，或明确选择其他定位方式。"), MessageType.None);
                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.RectField(L.Tr("实际矩形（px）"), proxy.Node.Layout);
                        }
                        if (!proxy.IsLive)
                            RectCommands.DrawPresets(proxy);
                    }
                    if (section.Name == "容器排列 Layout Group" && proxy.Node?.Computed("display") != "flex")
                        EditorGUILayout.HelpBox(L.Tr("这些排列参数用于弹性布局。布局类型可在上方面板中设置。"), MessageType.None);
                    if (section.Name == "图像 Image" && proxy.IsLive)
                    {
                        var current = AssetDatabase.LoadAssetAtPath<Texture2D>(proxy.Read("image") ?? "");
                        EditorGUI.BeginChangeCheck();
                        var texture = (Texture2D)EditorGUILayout.ObjectField(L.Tr("临时图片"), current, typeof(Texture2D), false);
                        if (EditorGUI.EndChangeCheck())
                            Apply("image", texture ? AssetDatabase.GetAssetPath(texture) : null);
                    }
                    EditorGUI.indentLevel--;
                }
            }
            if (!proxy.IsLive)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    if (EditorGUILayout.Foldout(panels.TryGetValue("排版工具", out bool tools) && tools, L.Tr("排版工具"), true))
                    {
                        panels["排版工具"] = true;
                        LayoutCommands.Draw(proxy);
                    }
                    else
                        panels["排版工具"] = false;
            }
            foreach (var extension in AuthoringEnvironment.Extensions)
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    AuthoringEnvironment.Try(() => extension.DrawInspector(proxy));
            advanced = EditorGUILayout.Foldout(advanced, L.Tr("高级 · HTML / CSS 与共享样式"), true);
            if (advanced)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (string property in new[] { "attr.class", "attr.title", "attr.tabindex", "text", "attr.src" })
                        Row(property);
                    if (!proxy.IsLive && AllowSharedRules)
                    {
                        DrawRuleTarget();
                        EditorGUILayout.LabelField(L.Tr("以下属性使用所选写入位置；上方常用面板始终编辑当前节点。"), EditorStyles.wordWrappedMiniLabel);
                    }
                    search = EditorGUILayout.TextField(L.Tr("搜索属性"), search);
                    drawingAdvanced = true;
                    try
                    {
                        foreach (var property in PropertySchema.All)
                            if (property.Name.StartsWith("style.", StringComparison.Ordinal) && (search.Length == 0 || property.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || PropertyWidgets.Label(property.Name.Substring(6)).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                                Row(property.Name);
                    }
                    finally { drawingAdvanced = false; }
                }
            }
            computed = EditorGUILayout.Foldout(computed, L.Tr("计算结果与诊断"), true);
            if (computed && proxy.Node != null && proxy.Node.IsValid)
            {
                EditorGUILayout.SelectableLabel(L.Tr("Identity: ") + proxy.Node.Id + L.Tr("\nSource: ") + proxy.Node.Document.SourceUrl, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(38));
                if (proxy.Node.HasLayout)
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.RectField(L.Tr("Actual Bounds"), proxy.Node.Bounds);
                    foreach (string name in new[] { "display", "position", "width", "height", "opacity", "visibility" })
                        EditorGUILayout.LabelField(name, proxy.Node.Computed(name) + " · " + proxy.Node.Origin(name));
                }
            }
        }
        static ElementSelectionProxy TextChild(ElementSelectionProxy proxy)
        {
            if (proxy.IsLive)
            {
                var node = FindLiveText(proxy.Node, 0);
                return node == null ? null : ElementSelectionProxy.ForLive(node, AuthoringEnvironment.Live);
            }
            var parent = proxy.SourceNode;
            if (parent.Children.Count == 0)
                return null;
            foreach (var node in AuthoringEnvironment.Author.Model.Nodes)
            {
                if (node == parent || node.IsVoid || node.Children.Count != 0 || NodePresentation.IsMetadata(node.Tag))
                    continue;
                bool inside = false;
                for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent)
                    if (ancestor == parent)
                        inside = true;
                if (inside && !string.IsNullOrWhiteSpace(AuthoringEnvironment.Author.File.Text.Substring(node.OpenEnd, node.CloseStart - node.OpenEnd)))
                    return ElementSelectionProxy.ForAuthor(node.Key);
            }
            return null;
        }
        static URUI.Editor.Inspection.InspectedNode FindLiveText(URUI.Editor.Inspection.InspectedNode parent, int depth)
        {
            if (depth > 64)
                return null;
            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent.Child(i);
                if (child.Tag != "#text" && child.CanEditText && !string.IsNullOrWhiteSpace(child.Read("text")))
                    return child;
                var nested = FindLiveText(child, depth + 1);
                if (nested != null)
                    return nested;
            }
            return null;
        }
        void DrawResource(ElementSelectionProxy proxy)
        {
            string source = proxy.Read("attr.src");
            Type type = proxy.SourceNode.Tag == "img" ? typeof(Texture2D) : typeof(GameObject);
            string path = null;
            if (!string.IsNullOrEmpty(source))
                try
                {
                    path = Preview.AuthoringAssetPaths.ResolveAsset(AuthoringEnvironment.ProjectRoot, AuthoringEnvironment.Author.File.Path, source);
                }
                catch (InvalidOperationException) { }
            var current = path == null ? null : AssetDatabase.LoadAssetAtPath(path, type);
            EditorGUI.BeginChangeCheck();
            var asset = EditorGUILayout.ObjectField(type == typeof(Texture2D) ? L.Tr("图片") : L.Tr("粒子 Prefab"), current, type, false);
            if (EditorGUI.EndChangeCheck())
            {
                string value = asset ? Preview.AuthoringAssetPaths.Reference(AuthoringEnvironment.ProjectRoot, AuthoringEnvironment.Author.File.Path, AssetDatabase.GetAssetPath(asset)) : null;
                Apply("attr.src", value);
            }
            Row("attr.src");
        }
        void DrawTransform(ElementSelectionProxy proxy)
        {
            string value = DisplayedValue(proxy, "style.transform") ?? "none";
            if (targets.Length != 1 || !StyleFields.TryTransform(value, out var parts))
            {
                Row("style.transform");
                EditorGUILayout.LabelField(L.Tr("复合变换保留原始表达式，可直接编辑。"), EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                foreach (string part in new[] { "translate", "rotate", "scale" })
                {
                    string[] values = parts.TryGetValue(part, out var saved) ? saved : part == "translate" ? new[] { "0px", "0px" } : part == "rotate" ? new[] { "0deg" } : new[] { "1", "1" };
                    string[] next = (string[])values.Clone();
                    string label = part == "translate" ? L.Tr("位移") : part == "rotate" ? L.Tr("旋转 Z") : L.Tr("缩放");
                    EditorGUI.BeginChangeCheck();
                    for (int i = 0; i < next.Length; i++)
                        using (new EditorGUILayout.HorizontalScope())
                            next[i] = PropertyWidgets.Draw("style." + (part == "scale" ? "scale" : "transform-part"), values[i], new GUIContent(label + (next.Length == 2 ? (i == 0 ? " X" : " Y") : "")), true);
                    if (EditorGUI.EndChangeCheck())
                        Apply("style.transform", StyleFields.SetTransform(value, part, next));
                }
                bool raw = panels.TryGetValue("变换表达式", out bool showRaw) && showRaw;
                raw = EditorGUILayout.Foldout(raw, L.Tr("原始变换与中心"), true);
                panels["变换表达式"] = raw;
                if (raw)
                {
                    Row("style.transform");
                    Row("style.transform-origin");
                }
            }
        }
        void Row(string property)
        {
            var proxy = (ElementSelectionProxy)target;
            string value;
            try
            {
                value = proxy.Read(property);
            }
            catch (InvalidOperationException) { return; }
            if (property == "text" && (proxy.IsLive ? !proxy.Node.CanEditText : proxy.SourceNode.Children.Count != 0 || proxy.SourceNode.IsVoid))
                return;
            if (property == "value" && !proxy.IsLive && proxy.SourceNode.Tag != "input" && proxy.SourceNode.Tag != "textarea" && proxy.SourceNode.Tag != "select" && proxy.SourceNode.Tag != "progress")
                return;
            RuleTarget rule = drawingAdvanced && !proxy.IsLive && selectedRule > 0 && selectedRule < ruleTargets.Count && property.StartsWith("style.", StringComparison.Ordinal) ? ruleTargets[selectedRule] : null;
            if (rule != null)
                value = CssSource.GetDeclaration(rule.Css.Substring(rule.Rule.BodyStart, rule.Rule.BodyEnd - rule.Rule.BodyStart), property.Substring(6));
            string label = property.StartsWith("style.", StringComparison.Ordinal) ? property.Substring(6) : property.StartsWith("attr.", StringComparison.Ordinal) ? property.Substring(5) : property;
            bool mixed = false;
            foreach (var selected in targets)
                try
                {
                    if (rule == null && ((ElementSelectionProxy)selected).Read(property) != value)
                        mixed = true;
                }
                catch { mixed = true; }
            EditorGUI.showMixedValue = mixed;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string shown = rule == null ? DisplayedValue(proxy, property) ?? "" : value ?? "";
                bool presence = property == "attr.disabled" || property == "attr.readonly" || property == "attr.multiple" || property == "attr.checked" || property == "attr.selected";
                bool checkValue = property == "value" && (proxy.Read("attr.type") == "checkbox" || proxy.Read("attr.type") == "radio");
                string next;
                if (presence)
                    next = EditorGUILayout.Toggle(PropertyWidgets.Label(label), value != null) ? "" : null;
                else if (checkValue)
                    next = EditorGUILayout.Toggle(L.Tr("选中"), value == "true") ? "true" : "false";
                else
                    next = PropertyWidgets.Draw(property, shown, new GUIContent(PropertyWidgets.Label(label), (value == null ? L.Tr("当前计算值；修改后写入显式属性。") : L.Tr("已设置的属性。")) + "\n" + property + (property.StartsWith("style.", StringComparison.Ordinal) && proxy.Node != null ? L.Tr("\n来源: ") + proxy.Node.Origin(property.Substring(6)) : "")), !drawingAdvanced && !expressions.Contains(property));
                if (EditorGUI.EndChangeCheck())
                    Apply(property, next);
                if (!proxy.IsLive && !drawingAdvanced && AuthoringEnvironment.Mode == WorkspaceMode.Animation && InspectorPresentation.IsAnimatable(property))
                {
                    if (GUILayout.Button(new GUIContent("◇", L.Tr("将当前值加入所选轨道的当前时间。")), EditorStyles.miniButton, GUILayout.Width(20)))
                        AddKey(proxy, property, shown);
                }
                if (!drawingWorkbench && property.StartsWith("style.", StringComparison.Ordinal))
                {
                    string status = rule != null ? L.Tr("共") : !proxy.IsLive && Animation.AnimationEnvironment.Recording ? L.Tr("录") : proxy.IsLive ? L.Tr("临") : value == null ? L.Tr("继") : L.Tr("改");
                    GUILayout.Label(new GUIContent(status, rule != null ? L.Tr("共享规则") : value == null ? L.Tr("计算或继承值") : L.Tr("当前节点覆盖")), EditorStyles.miniLabel, GUILayout.Width(15));
                }
                if (GUILayout.Button(new GUIContent("⋮", L.Tr("属性菜单：复制、粘贴、移除覆盖和表达式")), EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    string copied = shown;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(L.Tr("复制值")), false, () => EditorGUIUtility.systemCopyBuffer = copied);
                    menu.AddItem(new GUIContent(L.Tr("粘贴值")), false, () => ApplyValue(property, EditorGUIUtility.systemCopyBuffer, rule, true));
                    if (value != null && property != "text" && property != "value")
                        menu.AddItem(new GUIContent(L.Tr("移除当前覆盖")), false, () => ApplyValue(property, null, rule));
                    else
                        menu.AddDisabledItem(new GUIContent(L.Tr("移除当前覆盖")));
                    if (property.StartsWith("style.", StringComparison.Ordinal))
                        menu.AddItem(new GUIContent(L.Tr("编辑原始表达式")), expressions.Contains(property), () => { if (!expressions.Add(property)) expressions.Remove(property); Repaint(); });
                    menu.ShowAsContext();
                }
            }
            EditorGUI.showMixedValue = false;
        }
        void AddKey(ElementSelectionProxy proxy, string property, string value)
        {
            AuthoringEnvironment.Try(() =>
            {
                var workspace = Animation.AnimationEnvironment.Workspace;
                URUI.Animation.SequenceTrack selected = null;
                if (workspace.Definition != null)
                    foreach (var track in workspace.Definition.Tracks)
                        if (track.Id == Animation.AnimationEnvironment.SelectedTrack)
                            selected = track;
                if (selected == null || selected.Kind != "css" || Animation.AnimationEnvironment.ResolveSourceTarget(selected.Target)?.Key != proxy.SourceKey)
                    throw new InvalidOperationException("请先选择此节点对应的界面动画轨道。");
                value = proxy.Node?.Computed(property.Substring(6)) ?? throw new InvalidOperationException("请先预览当前动画姿态。");
                double time = Animation.AnimationEnvironment.Time;
                if (time < selected.Start || time > selected.Start + selected.Duration)
                    throw new InvalidOperationException("请在所选轨道的首轮范围内添加关键帧。");
                if (Animation.AnimationEnvironment.Recording)
                    Animation.AnimationEnvironment.TryRecord(proxy, property, value);
                else
                {
                    double position = selected.Duration == 0 ? 1 : (time - selected.Start) / selected.Duration;
                    SessionUndoBridge.Record(AuthoringEnvironment.Author, () => workspace.SetKey(selected.Id, position, property.Substring(6), value, workspace.Revision));
                    Animation.AnimationEnvironment.Seek(time);
                }
            });
        }
        static string DisplayedValue(ElementSelectionProxy proxy, string property)
        {
            bool sampled = !proxy.IsLive && AuthoringEnvironment.Mode == WorkspaceMode.Animation && property.StartsWith("style.", StringComparison.Ordinal);
            return sampled ? proxy.Node?.Computed(property.Substring(6)) ?? proxy.Read(property) : proxy.Read(property) ?? (property.StartsWith("style.", StringComparison.Ordinal) ? proxy.Node?.Computed(property.Substring(6)) : null);
        }
        void Apply(string property, string value)
        {
            RuleTarget rule = drawingAdvanced && !((ElementSelectionProxy)target).IsLive && selectedRule > 0 && selectedRule < ruleTargets.Count ? ruleTargets[selectedRule] : null;
            ApplyValue(property, value, rule);
        }
        void ApplyValue(string property, string value, RuleTarget rule, bool explicitValue = false)
        {
            AuthoringEnvironment.Try(() =>
            {
                bool linked = rule == null && !drawingAdvanced && value != null && ((linkPadding && property.StartsWith("style.padding-", StringComparison.Ordinal)) || (linkMargin && property.StartsWith("style.margin-", StringComparison.Ordinal)));
                var local = new List<ElementSelectionProxy>();
                foreach (var item in targets)
                    local.Add((ElementSelectionProxy)item);
                bool layout = local[0].IsLive || AuthoringEnvironment.Mode != WorkspaceMode.Animation;
                string primary = DisplayedValue(local[0], property);
                bool numeric = StyleFields.TryNumber(primary, out _, out string oldUnit) && StyleFields.TryNumber(value, out _, out _);
                bool protect = rule == null && !drawingAdvanced && !expressions.Contains(property) && !explicitValue && value != null && property.StartsWith("style.", StringComparison.Ordinal) && property != "style.transform" && layout && (linked || local.Count > 1 || !StyleFields.RequiresExpressionEdit(primary));
                if (linked || (protect && (numeric || local.Count > 1)))
                {
                    string[] properties = linked ? new[] { "left", "top", "right", "bottom" } : new[] { "" };
                    string prefix = property.StartsWith("style.padding-", StringComparison.Ordinal) ? "style.padding-" : "style.margin-";
                    var maps = new List<IReadOnlyDictionary<string, string>>();
                    foreach (var proxy in local)
                    {
                        var edits = new Dictionary<string, string>();
                        foreach (string side in properties)
                        {
                            string field = linked ? prefix + side : property;
                            string original = DisplayedValue(proxy, field);
                            if (protect && (StyleFields.RequiresExpressionEdit(original) || (numeric && !StyleFields.TryNumber(original, out _, out _))))
                                throw new InvalidOperationException("选择中包含表达式、自动值或重要声明；请在属性菜单选择“编辑原始表达式”，明确输入替换值。");
                            string next = value;
                            if (protect && numeric && !linked && StyleFields.TryNumber(value, out double number, out string unit) && unit == oldUnit)
                                next = StyleFields.WithNumber(original, number);
                            edits.Add(field, next);
                        }
                        maps.Add(edits);
                    }
                    ElementSelectionProxy.SetPerTarget(local, maps);
                    return;
                }
                if (!((ElementSelectionProxy)target).IsLive && Animation.AnimationEnvironment.Recording)
                {
                    if (targets.Length != 1)
                        throw new InvalidOperationException("录制时请只选择当前轨道节点。");
                    Animation.AnimationEnvironment.TryRecord((ElementSelectionProxy)target, property, value);
                    return;
                }
                if (!((ElementSelectionProxy)target).IsLive && AuthoringEnvironment.Mode == WorkspaceMode.Animation)
                {
                    if (targets.Length != 1)
                        throw new InvalidOperationException("临时动画预览请一次编辑一个节点。");
                    Animation.AnimationEnvironment.PreviewEdits((ElementSelectionProxy)target, new[] { new AuthoringEdit { Node = ((ElementSelectionProxy)target).SourceKey, Property = property, Value = value } });
                    return;
                }
                if (!((ElementSelectionProxy)target).IsLive && property.StartsWith("style.", StringComparison.Ordinal) && rule != null)
                {
                    var author = AuthoringEnvironment.Author;
                    if (rule.File.Revision != rule.FileRevision)
                        throw new InvalidOperationException("source_conflict: CSS rule changed; reselect its current version");
                    SessionUndoBridge.Record(author, () =>
                    {
                        if (rule.CssStart == 0 && rule.File.Path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                            author.SetSharedRule(rule.File.Path, rule.Rule.Start, property.Substring(6), value, author.Revision);
                        else
                            author.EditFile(rule.File.Path, rule.Text.Substring(0, rule.CssStart) + CssSource.SetRuleDeclaration(rule.Css, rule.Rule.Start, property.Substring(6), value) + rule.Text.Substring(rule.CssEnd), author.Revision);
                    });
                    return;
                }
                var selected = new List<ElementSelectionProxy>();
                foreach (var obj in targets)
                    selected.Add((ElementSelectionProxy)obj);
                ElementSelectionProxy.SetMany(selected, property, value);
            });
        }
        void DrawRuleTarget()
        {
            var author = AuthoringEnvironment.Author;
            if (ruleRevision != author.Revision)
            {
                string previous = selectedRule > 0 && selectedRule < ruleTargets.Count ? ruleTargets[selectedRule].Label : null;
                ruleTargets.Clear();
                ruleTargets.Add(null);
                foreach (var file in author.Workspace.Files)
                {
                    if (file.Text != null && file.Path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                        AddRules(file, file.Text, 0, file.Text.Length);
                    else if (file == author.File)
                        foreach (var node in author.Model.Nodes)
                            if (node.Tag == "style")
                                AddRules(file, file.Text.Substring(node.OpenEnd, node.CloseStart - node.OpenEnd), node.OpenEnd, node.CloseStart);
                }
                ruleRevision = author.Revision;
                selectedRule = 0;
                if (previous != null)
                    for (int i = 1; i < ruleTargets.Count; i++)
                        if (ruleTargets[i].Label == previous)
                        {
                            selectedRule = i;
                            break;
                        }
            }
            var names = new string[ruleTargets.Count];
            names[0] = L.Tr("当前节点");
            for (int i = 1; i < names.Length; i++)
                names[i] = ruleTargets[i].Label;
            selectedRule = EditorGUILayout.Popup(L.Tr("写入位置"), selectedRule, names);
            if (selectedRule > 0)
                EditorGUILayout.HelpBox(L.Tr("Shared rule: changes affect all matching nodes, including other instances. This selection does not claim to be the winning rule."), MessageType.Info);
        }
        void AddRules(SourceFile file, string css, int start, int end)
        {
            foreach (var rule in CssSource.Rules(css))
                if (!rule.Prelude.StartsWith("@", StringComparison.Ordinal) && !System.Text.RegularExpressions.Regex.IsMatch(rule.Prelude, @"^(from|to|[\d.]+%)") && ruleTargets.Count < 1000)
                    ruleTargets.Add(new RuleTarget
                    {
                        File = file,
                        Text = file.Text,
                        Css = css,
                        CssStart = start,
                        CssEnd = end,
                        FileRevision = file.Revision,
                        Rule = rule,
                        Label = System.IO.Path.GetFileName(file.Path) + ":" + (start + rule.Start) + " · " + rule.Prelude
                    });
        }
    }

    sealed class LiveApplyWindow : EditorWindow
    {
        ElementSelectionProxy proxy;
        readonly List<LiveEdit> changes = new List<LiveEdit>();
        readonly List<bool> selected = new List<bool>();
        AuthoringSession author;
        SourceMap map;
        string error;
        public static void Open(ElementSelectionProxy target)
        {
            var window = CreateInstance<LiveApplyWindow>();
            window.titleContent = new GUIContent(L.Tr("Apply URUI Changes"));
            window.proxy = target;
            try
            {
                string path = target.Node.Document.Source ? AssetDatabase.GetAssetPath(target.Node.Document.Source) : target.Node.Document.SourceUrl.TrimStart('/');
                window.author = new AuthoringSession(AuthoringEnvironment.ProjectRoot);
                window.author.Open(path);
                if (AuthoringEnvironment.Author.File?.Path == window.author.File.Path && AuthoringEnvironment.Author.File.Dirty)
                    throw new InvalidOperationException("source_conflict: save/discard authoring edits first");
                window.map = new SourceMap(target.Node.Document, window.author.Model);
                foreach (var change in AuthoringEnvironment.Live.Changes())
                    if (change.Node.Id == target.Node.Id)
                    {
                        window.changes.Add(change);
                        window.selected.Add(false);
                    }
            }
            catch (Exception exception) { window.error = exception.Message; }
            window.minSize = new Vector2(500, 260);
            window.ShowUtility();
        }
        void OnGUI()
        {
            L.Title(this, "Apply URUI Changes");
            if (error != null)
            {
                EditorGUILayout.HelpBox(L.Message(error), MessageType.Warning);
                return;
            }
            if (!proxy || !proxy.IsTargetValid)
            {
                EditorGUILayout.HelpBox(L.Tr("Runtime node expired."), MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField(author.File.Path, EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox(L.Tr("Select properties to write to the source file. Runtime state and generated/internal nodes cannot be applied."), MessageType.Info);
            for (int i = 0; i < changes.Count; i++)
                selected[i] = EditorGUILayout.ToggleLeft(changes[i].Property + " → " + (changes[i].Value ?? "<unset>"), selected[i]);
            if (GUILayout.Button(L.Tr("Apply Selected to Source")))
            {
                try
                {
                    var apply = new List<LiveEdit>();
                    for (int i = 0; i < changes.Count; i++)
                        if (selected[i])
                            apply.Add(changes[i]);
                    if (apply.Count == 0)
                        return;
                    author.Apply(apply, map, author.Revision);
                    author.Save();
                    AssetDatabase.Refresh();
                    Close();
                }
                catch (Exception exception) { error = exception.Message; }
            }
        }
    }
}
