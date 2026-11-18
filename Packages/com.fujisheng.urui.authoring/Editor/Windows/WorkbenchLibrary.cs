using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using URUI.Authoring.Preview;
using URUI.Authoring.Source;
using UnityEngine.UIElements;
using URUI.Authoring.Sessions;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    internal sealed class WorkbenchLibrary : VisualElement
    {
        static readonly (string Kind, string Label)[] Standard =
        {
            ("panel", "容器 Panel"), ("text", "文字 Text"), ("image", "图片 Image"), ("button", "按钮 Button"),
            ("input", "输入框 Input"), ("textarea", "多行输入框"), ("toggle", "勾选框 Toggle"), ("radio", "单选 Radio"),
            ("slider", "滑动条 Slider"), ("select", "下拉选择"), ("progress", "进度条 Progress"), ("particles", "粒子 Particles")
        };

        readonly ScrollView entries = new ScrollView();
        readonly ToolbarSearchField search;
        readonly Label destination = new Label();
        readonly WorkbenchUi.LocalNodeDrag localDrag;
        Action firstResult;
        string query = "";
        bool project;
        long resourceRevision = -1;

        internal WorkbenchLibrary(Action close, WorkbenchUi.LocalNodeDrag localDrag)
        {
            this.localDrag = localDrag;
            AddToClassList("urui-library");
            var header = new VisualElement();
            header.AddToClassList("urui-library-header");
            header.Add(WorkbenchUi.Heading(L.Tr("控件库")));
            var collapse = new Button(close) { text = "‹", name = "urui-close-library", tooltip = L.Tr("关闭控件库") };
            collapse.AddToClassList("urui-library-collapse");
            header.Add(collapse);
            Add(header);
            RegisterCallback<GeometryChangedEvent>(evt =>
                EnableInClassList("urui-library-compact", evt.newRect.width < 190));
            var tabs = new Toolbar();
            tabs.AddToClassList("urui-tabs");
            var standardTab = new ToolbarToggle { text = L.Tr("标准控件"), value = true };
            var projectTab = new ToolbarToggle { text = L.Tr("项目片段") };
            void SetProject(bool enabled)
            {
                project = enabled;
                standardTab.SetValueWithoutNotify(!enabled);
                projectTab.SetValueWithoutNotify(enabled);
                Populate();
            }
            standardTab.RegisterValueChangedCallback(_ => SetProject(false));
            projectTab.RegisterValueChangedCallback(_ => SetProject(true));
            tabs.Add(standardTab);
            tabs.Add(projectTab);
            Add(tabs);
            search = new ToolbarSearchField { tooltip = L.Tr("搜索控件") };
            search.AddToClassList("urui-search");
            WorkbenchUi.SetPlaceholder(search, L.Tr("搜索控件…"));
            search.RegisterValueChangedCallback(evt => { query = evt.newValue.Trim(); Populate(); });
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!NodePlacement.Editing || query.Length == 0 || firstResult == null ||
                    evt.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter))
                    return;
                firstResult();
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            Add(search);
            entries.style.flexGrow = 1;
            entries.contentContainer.AddToClassList("urui-control-list");
            Add(entries);
            destination.AddToClassList("urui-destination");
            Add(destination);
            Populate();
            RefreshDestination();
        }

        internal void FocusSearch() => (search.Q<TextField>() as VisualElement ?? search).Focus();

        internal void RefreshDestination()
        {
            var selected = AuthoringEnvironment.ActiveNode;
            var node = SelectedAnchor(selected?.SourceNode);
            if (AuthoringEnvironment.Author.Model == null)
                destination.text = L.Tr("打开页面后添加控件");
            else if (node == null || node.Tag == "body")
                destination.text = L.Tr("添加到页面");
            else
            {
                string text = selected?.SourceKey == node.Key ? selected.Read("text") : null;
                string title = WorkbenchUi.NodeTitle(node.Tag, node.Attribute("id"), node.Attribute("class"), text, node.Attribute("type"));
                destination.text = string.Format(L.Tr(NodePlacement.AcceptsChildren(node) ? "添加到 {0}" : "添加到 {0} 之后"), title);
            }
            destination.tooltip = destination.text;
        }

        internal static SourceDocument.Node SelectedAnchor(SourceDocument.Node node)
        {
            for (var ancestor = node; ancestor != null; ancestor = ancestor.Parent)
            {
                // Quick add must stay outside buttons, even when their label or decoration is selected.
                if (ancestor.Tag == "button")
                    node = ancestor;
                if (ancestor.Tag == "body")
                    return node;
            }
            return null;
        }

        void Populate()
        {
            firstResult = null;
            entries.Clear();
            if (project)
            {
                PopulateProject();
                return;
            }
            foreach (var entry in Standard)
            {
                if (entry.Kind == "particles" && !AuthoringEnvironment.Extensions.Any(extension => extension.Id == "effect"))
                    continue;
                string label = L.Tr(entry.Label);
                if (query.Length != 0 && label.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    entry.Kind.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var control = new Label(label)
                {
                    name = "urui-control-" + entry.Kind, focusable = true, tooltip = label + "\n" + L.Tr("单击添加，或拖到图层和画布")
                };
                control.AddToClassList("urui-control-tile");
                control.AddToClassList("urui-standard-tile");
                control.Add(WorkbenchControlPreview.Create(entry.Kind));
                void Insert() => AuthoringEnvironment.Try(() =>
                {
                    var node = SelectedAnchor(AuthoringEnvironment.ActiveNode?.SourceNode);
                    var placement = NodePlacement.AcceptsChildren(node) ? NodePlacement.ChildOf(node) : NodePlacement.Beside(node, true);
                    placement.Insert(entry.Kind);
                });
                firstResult ??= Insert;
                control.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Space)
                    {
                        Insert();
                        evt.StopImmediatePropagation();
                    }
                });
                control.AddManipulator(new ContextualMenuManipulator(evt => evt.menu.AppendAction(L.Tr("添加节点"), _ => Insert())));
                WorkbenchUi.EnableDrag(control, () => NodePlacement.Editing ? new WorkbenchDrag(kind: entry.Kind) : null,
                    click: Insert, local: localDrag);
                entries.Add(control);
            }
            if (entries.childCount == 0)
                AddEmptyMessage("未找到匹配的控件");
        }

        void PopulateProject()
        {
            var preview = AuthoringEnvironment.Preview;
            var resources = preview?.IsReady == true ? preview.Resources : Array.Empty<URUI.Resources.TextResource>();
            foreach (var resource in resources.Where(resource => resource.Source.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
            {
                string name = Path.GetFileName(resource.Source);
                if (query.Length != 0 && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var label = new Label(name) { tooltip = resource.Source, focusable = true };
                label.AddToClassList("urui-control-tile");
                label.AddToClassList("urui-control-fragment");
                string path = AuthoringAssetPaths.AssetPath(resource.Source);
                label.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    evt.menu.AppendAction(L.Tr("打开源文件"), _ => EditorUtility.OpenWithDefaultApp(path));
                    evt.menu.AppendAction(L.Tr("复制片段为节点"), _ => AuthoringEnvironment.Try(() =>
                        NodePlacement.Beside(AuthoringEnvironment.ActiveNode?.SourceNode, true).InsertMarkup(ValidateFragment(resource.Asset.text))));
                }));
                WorkbenchDrag DragFragment()
                {
                    if (!NodePlacement.Editing)
                        return null;
                    WorkbenchDrag drag = null;
                    AuthoringEnvironment.Try(() => drag = new WorkbenchDrag(fragment: ValidateFragment(resource.Asset.text)));
                    return drag;
                }
                WorkbenchUi.EnableDrag(label, DragFragment, () =>
                {
                    if (path != null)
                        EditorUtility.OpenWithDefaultApp(path);
                }, local: localDrag);
                entries.Add(label);
            }
            if (entries.childCount == 0)
                AddEmptyMessage(query.Length == 0 ? "此页面没有引用项目片段" : "未找到匹配的项目片段");
        }

        void AddEmptyMessage(string message)
        {
            var label = new Label(L.Tr(message));
            label.AddToClassList("urui-empty");
            entries.Add(label);
        }

        static string ValidateFragment(string markup)
        {
            var fragment = SourceDocument.Parse(markup);
            if (fragment.Roots.Count != 1 || fragment.Roots[0].Tag is "html" or "body" or "head" or "template")
                throw new InvalidOperationException(L.Tr("这个片段不能直接插入当前页面。"));
            var ids = new System.Collections.Generic.HashSet<string>(AuthoringEnvironment.Author.Model.Nodes.Select(node => node.Attribute("id")).Where(id => !string.IsNullOrEmpty(id)));
            foreach (var node in fragment.Nodes)
            {
                string id = node.Attribute("id");
                if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                    throw new InvalidOperationException(L.Tr("片段包含重复 id。") + " " + id);
            }
            return markup;
        }

        internal void Refresh()
        {
            entries.SetEnabled(NodePlacement.Editing);
            RefreshDestination();
            long current = AuthoringEnvironment.Preview?.RenderedRevision ?? -1;
            if (project && current != resourceRevision)
            {
                resourceRevision = current;
                Populate();
            }
        }
    }
}
