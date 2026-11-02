using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>固定行高的虚拟列表；只创建可见区域及前后一行，行在文档内复用。</summary>
    public class ListView : ScrollView
    {
        sealed class Row
        {
            internal Element Element;
            internal int Index = -1;
            internal int BindingVersion;
            internal Action<Element, int> Unbind;
            internal readonly Dictionary<int, int> PressedVersions = new Dictionary<int, int>();
        }
        readonly List<Row> rows = new List<Row>();
        IList items;
        float itemHeight = 24;
        int selectedIndex = -1;
        bool refreshing;
        /// <summary>文档工厂构造入口。</summary>
        protected internal ListView(ElementContext context) : base(context) { }
        /// <summary>数据源；设置会重建行。原地修改后调用 RefreshItems。</summary>
        public IList ItemsSource
        {
            get { RequireValid(); return items; }
            set { RequireValid(); items = value; Rebuild(); }
        }
        /// <summary>创建可复用行的工厂；返回同一文档、未挂载且未被其他行使用的元素。</summary>
        public Func<Element> MakeItem { get; set; }
        /// <summary>把数据索引绑定到行；应与 UnbindItem 配合清理业务订阅。</summary>
        public Action<Element, int> BindItem { get; set; }
        /// <summary>行更换数据或退役前调用，索引代表旧绑定。</summary>
        public Action<Element, int> UnbindItem { get; set; }
        /// <summary>每行像素高度，必须为正有限值。</summary>
        public float FixedItemHeight
        {
            get { RequireValid(); return itemHeight; }
            set
            {
                RequireValid(); ElementStyle.RequireFinite(value);
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                itemHeight = value; Rebuild();
            }
        }
        /// <summary>当前已实例化行数量，可用于检查虚拟化范围。</summary>
        public int RealizedItemCount { get { RequireValid(); return rows.Count; } }
        /// <summary>单选索引，-1 取消选择。</summary>
        public int SelectedIndex
        {
            get { RequireValid(); return selectedIndex; }
            set
            {
                RequireValid();
                if (value < -1 || value >= (items?.Count ?? 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                if (selectedIndex == value)
                {
                    return;
                }

                selectedIndex = value;
                RefreshSelection();
                NotifyIndex(SelectionChanged, value);
            }
        }
        /// <summary>选中索引改变时触发；数据缩短导致选择失效时也触发。</summary>
        public event Action<int> SelectionChanged;
        /// <summary>列表自身获得焦点后，Enter 确认当前选中项。</summary>
        public event Action<int> ItemChosen;
        /// <summary>刷新现有数据绑定，保留可复用行。</summary>
        public void RefreshItems() => RefreshVisible(true);
        /// <summary>销毁现有行并使用当前工厂重新构建。</summary>
        public void Rebuild()
        {
            RequireValid();
            if (refreshing)
            {
                throw new InvalidOperationException("不能在行绑定/解绑中重建列表。");
            }

            Document.BeginControlUpdate();
            try
            {
                foreach (var row in rows)
                {
                    Retire(row);
                    if (Document.HasPendingInvalidation)
                    {
                        return;
                    }
                }
                rows.Clear();
                if (HasLayout)
                {
                    RefreshVisible(true);
                }
            }
            finally { Document.EndControlUpdate(); }
        }
        /// <summary>滚动到指定项。</summary>
        public void ScrollToItem(int index)
        {
            RequireValid();
            if (index < 0 || index >= (items?.Count ?? 0))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            ScrollOffset = new Vector2(ScrollOffset.x, index * itemHeight);
            if (IsValid && !Document.HasPendingInvalidation)
            {
                RefreshVisible(false);
            }
        }
        /// <summary>布局完成后按视口和偏移更新可见行。</summary>
        protected override void OnUpdate()
        {
            base.OnUpdate();
            RefreshVisible(false);
        }

        void RefreshVisible(bool rebind)
        {
            RequireValid();
            if (refreshing)
            {
                throw new InvalidOperationException("不能在行回调中刷新列表。");
            }

            if (!HasLayout)
            {
                return;
            }

            int count = items?.Count ?? 0;
            float totalHeight = count * itemHeight;
            if (!float.IsFinite(totalHeight))
            {
                throw new InvalidOperationException("列表总高度超出可表示范围。");
            }

            ContentContainer.Style.Height = totalHeight;
            int visible = Mathf.Min(count, Mathf.Max(0, Mathf.CeilToInt(ViewportRect.height / itemHeight)) + 2);
            int first = Mathf.Clamp(Mathf.FloorToInt(ScrollOffset.y / itemHeight) - 1, 0, Mathf.Max(0, count - visible));
            refreshing = true;
            Document.BeginControlUpdate();
            try
            {
                while (rows.Count > visible)
                {
                    var row = rows[rows.Count - 1]; rows.RemoveAt(rows.Count - 1); Retire(row);
                    if (Document.HasPendingInvalidation)
                    {
                        return;
                    }
                }
                while (rows.Count < visible)
                {
                    var element = MakeItem != null ? MakeItem() : Document.Create<Label>();
                    if (Document.HasPendingInvalidation)
                    {
                        return;
                    }

                    if (element == null || !element.IsValid || element.IsAttached || !ReferenceEquals(element.Document, Document)
                                            || rows.Exists(row => ReferenceEquals(row.Element, element)))
                    {
                        throw new InvalidOperationException("行工厂必须返回唯一、同文档、脱离层级的有效元素。");
                    }

                    var row = new Row { Element = element };
                    element.Style.Position = Position.Absolute;
                    element.Style.Width = Length.Percent(100);
                    element.RegisterCallback<PointerDownEvent>(evt => row.PressedVersions[evt.PointerId] = row.BindingVersion);
                    element.RegisterCallback<PointerCancelEvent>(evt => row.PressedVersions.Remove(evt.PointerId));
                    element.RegisterCallback<ClickEvent>(evt =>
                    {
                        bool current = !row.PressedVersions.TryGetValue(evt.PointerId, out var version) || version == row.BindingVersion;
                        row.PressedVersions.Remove(evt.PointerId);
                        if (current && row.Index >= 0 && row.Index < (items?.Count ?? 0))
                        {
                            SelectedIndex = row.Index;
                            if (IsValid && !Document.HasPendingInvalidation && !evt.Target.Focusable)
                            {
                                Focus();
                            }
                        }
                    });
                    ContentContainer.Add(element); rows.Add(row);
                }
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i]; int index = first + i;
                    if (row.Index != index || rebind)
                    {
                        if (row.Index >= 0)
                        {
                            row.Unbind?.Invoke(row.Element, row.Index);
                            row.Unbind = null;
                            if (Document.HasPendingInvalidation) { row.Index = -1; return; }
                        }

                        row.BindingVersion++;
                        row.Index = index;
                        row.Unbind = UnbindItem;
                        if (BindItem != null)
                        {
                            BindItem(row.Element, index);
                            if (Document.HasPendingInvalidation)
                            {
                                return;
                            }
                        }
                        else
                        {
                            row.Element.Text = items[index]?.ToString() ?? "";
                        }
                    }
                    row.Element.Style.Top = index * itemHeight;
                    row.Element.Style.Height = itemHeight;
                }
                if (selectedIndex >= count) { selectedIndex = -1; NotifyIndex(SelectionChanged, -1); }
                if (Document.HasPendingInvalidation)
                {
                    return;
                }

                RefreshSelection();
            }
            finally { refreshing = false; Document.EndControlUpdate(); }
        }
        void RefreshSelection()
        {
            foreach (var row in rows)
            {
                if (row.Element.IsValid)
                {
                    row.Element.EnableInClassList("urui-selected", row.Index == selectedIndex);
                }
            }
        }
        void Retire(Row row)
        {
            if (row.Index >= 0)
            {
                row.Unbind?.Invoke(row.Element, row.Index);
                row.Unbind = null;
            }

            row.Index = -1;
            if (row.Element.IsValid && !Document.HasPendingInvalidation)
            {
                row.Element.Destroy();
            }
        }
        /// <summary>文档或节点失效后释放数据及业务委托；此时不再访问原生行。</summary>
        protected override void OnInvalidated()
        {
            foreach (var row in rows)
            {
                if (row.Index < 0)
                {
                    continue;
                }

                try { row.Unbind?.Invoke(row.Element, row.Index); }
                catch (Exception error) { Debug.LogException(error); }
                row.Index = -1;
            }
            rows.Clear(); items = null; MakeItem = null; BindItem = null; UnbindItem = null; SelectionChanged = null; ItemChosen = null;
        }
        /// <summary>只处理列表自身焦点的导航，不截获行内输入控件的编辑按键。</summary>
        protected override void OnEvent(EventBase evt)
        {
            if (evt is KeyDownEvent key && ReferenceEquals(evt.Target, this) && EnabledInHierarchy)
            {
                int count = items?.Count ?? 0;
                int next = selectedIndex;
                switch (key.KeyCode)
                {
                    case KeyCode.DownArrow: next = selectedIndex < 0 ? 0 : selectedIndex + 1; break;
                    case KeyCode.UpArrow: next = selectedIndex < 0 ? count - 1 : selectedIndex - 1; break;
                    case KeyCode.Home: next = 0; break;
                    case KeyCode.End: next = count - 1; break;
                    case KeyCode.PageDown: next = Math.Max(0, selectedIndex) + Math.Max(1, Mathf.FloorToInt(ViewportRect.height / itemHeight)); break;
                    case KeyCode.PageUp: next = Math.Max(0, selectedIndex) - Math.Max(1, Mathf.FloorToInt(ViewportRect.height / itemHeight)); break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        key.PreventDefault();
                        if (selectedIndex >= 0 && selectedIndex < count)
                        {
                            NotifyIndex(ItemChosen, selectedIndex);
                        }

                        return;
                    default: base.OnEvent(evt); return;
                }
                key.PreventDefault();
                if (count > 0)
                {
                    SelectedIndex = Mathf.Clamp(next, 0, count - 1);
                    if (IsValid && !Document.HasPendingInvalidation)
                    {
                        ScrollToItem(selectedIndex);
                    }
                }
                return;
            }
            base.OnEvent(evt);
        }
        void NotifyIndex(Action<int> callbacks, int index)
        {
            if (callbacks == null)
            {
                return;
            }

            foreach (Action<int> callback in callbacks.GetInvocationList())
            {
                if (!IsValid || Document.HasPendingInvalidation)
                {
                    break;
                }

                try { callback(index); } catch (Exception error) { Debug.LogException(error); }
            }
        }
    }
}
