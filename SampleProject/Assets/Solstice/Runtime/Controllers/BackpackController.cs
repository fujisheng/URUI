using System;
using System.Collections.Generic;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;

namespace Solstice
{
    /// <summary>
    /// Controls the 10,000-entry virtualized Armory Backpack using URUI's pooled ListView,
    /// live category filters, instant search, item locking, and equip state.
    /// </summary>
    public sealed class BackpackController : IDisposable
    {
        readonly SolsticeApp app;
        readonly SolsticeGameData data;
        readonly Document doc;
        readonly CompositeDisposable subs = new();

        readonly List<SolsticeGameData.RelicItem> filtered = new(10000);
        readonly Dictionary<Element, IDisposable> rowClickSubs = new();

        ListView listView;
        TextField searchInput;
        Element countLabel;
        Element selRarity;
        Element selCode;
        Element selName;
        Element selSlot;
        Element selStat;
        Image selIcon;
        Button equipBtn;
        Button lockBtn;

        string activeCategory = "ALL";
        string searchQuery = "";
        int selectedFilteredIndex = 0;

        public BackpackController(SolsticeApp app, SolsticeGameData data, Document doc)
        {
            this.app = app;
            this.data = data;
            this.doc = doc;

            listView = doc.Q<ListView>("armory-listview");
            searchInput = doc.Q<TextField>("armory-search");
            countLabel = doc.Q("armory-count-label");
            selRarity = doc.Q("armory-sel-rarity");
            selCode = doc.Q("armory-sel-code");
            selName = doc.Q("armory-sel-name");
            selSlot = doc.Q("armory-sel-slot");
            selStat = doc.Q("armory-sel-stat");
            selIcon = doc.Q<Image>("armory-sel-icon");
            equipBtn = doc.Q<Button>("btn-armory-equip");
            lockBtn = doc.Q<Button>("btn-armory-lock");

            BindFilter("filter-all", "ALL");
            BindFilter("filter-weapon", "Weapon");
            BindFilter("filter-relic", "Relic");
            BindFilter("filter-crest", "Crest");
            BindFilter("filter-stone", "Stone");

            if (searchInput != null)
            {
                subs.Add(searchInput.RegisterValueChangedCallback(evt =>
                {
                    searchQuery = evt.NewValue ?? string.Empty;
                    ApplyFilter();
                }));
            }

            if (equipBtn != null)
            {
                Action onEquip = () =>
                {
                    var item = CurrentSelection();
                    if (item == null) return;
                    item.Equipped = !item.Equipped;
                    app.PlaySound("sfx-skill");
                    app.ShowToast(item.Equipped ? $"已装配 {item.Name}" : $"已卸下 {item.Name}");
                    listView?.RefreshItems();
                    RefreshSelectionCard();
                };
                equipBtn.Clicked += onEquip;
                subs.Add(new DisposableAction(() => equipBtn.Clicked -= onEquip));
            }

            if (lockBtn != null)
            {
                Action onLock = () =>
                {
                    var item = CurrentSelection();
                    if (item == null) return;
                    item.Locked = !item.Locked;
                    app.PlaySound("sfx-click");
                    listView?.RefreshItems();
                    RefreshSelectionCard();
                };
                lockBtn.Clicked += onLock;
                subs.Add(new DisposableAction(() => lockBtn.Clicked -= onLock));
            }

            if (listView != null)
            {
                listView.FixedItemHeight = 66f;
                listView.MakeItem = MakeRow;
                listView.BindItem = BindRow;
                listView.UnbindItem = UnbindRow;
                Action<int> onSelectionChanged = idx =>
                {
                    selectedFilteredIndex = idx;
                    RefreshSelectionCard();
                };
                listView.SelectionChanged += onSelectionChanged;
                subs.Add(new DisposableAction(() => listView.SelectionChanged -= onSelectionChanged));
            }

            ApplyFilter();
        }

        void BindFilter(string btnId, string category)
        {
            var btn = doc.Q<Button>(btnId);
            if (btn == null) return;
            Action cb = () => SetCategory(category);
            btn.Clicked += cb;
            subs.Add(new DisposableAction(() => btn.Clicked -= cb));
        }

        public void SetCategory(string category)
        {
            activeCategory = category;
            app.PlaySound("sfx-click");
            ApplyFilter();
        }

        public void SetSearchQuery(string query)
        {
            searchQuery = query ?? string.Empty;
            if (searchInput != null) searchInput.SetValueWithoutNotify(searchQuery);
            ApplyFilter();
        }

        void ApplyFilter()
        {
            filtered.Clear();
            var q = searchQuery.Trim();
            for (var i = 0; i < data.Inventory.Count; i++)
            {
                var item = data.Inventory[i];
                if (activeCategory != "ALL" && item.Category != activeCategory) continue;
                if (q.Length > 0 &&
                    item.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.Code.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.StatLine.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                filtered.Add(item);
            }

            selectedFilteredIndex = filtered.Count > 0 ? 0 : -1;
            if (countLabel != null)
            {
                countLabel.Text = $"{filtered.Count:N0} 项 · 虚拟列表池化中";
            }

            string[] cats = { "ALL", "Weapon", "Relic", "Crest", "Stone" };
            string[] ids = { "filter-all", "filter-weapon", "filter-relic", "filter-crest", "filter-stone" };
            for (var i = 0; i < cats.Length; i++)
            {
                var b = doc.Q(ids[i]);
                b?.EnableInClassList("is-active", cats[i] == activeCategory);
            }

            if (listView != null)
            {
                listView.ItemsSource = filtered;
                if (filtered.Count > 0)
                {
                    listView.SelectedIndex = 0;
                }
            }
            RefreshSelectionCard();
        }

        Element MakeRow()
        {
            var row = doc.CreateElement("div");
            row.AddToClassList("armory-row");

            var left = doc.CreateElement("div");
            left.AddToClassList("armory-row-left");

            var icon = doc.Create<Image>();
            icon.AddToClassList("armory-row-icon");

            var info = doc.CreateElement("div");
            var title = doc.CreateElement("div");
            title.AddToClassList("sol-row-title");
            title.Style.SetPixels("font-size", 13f);

            var sub = doc.CreateElement("div");
            sub.AddToClassList("sol-row-sub");
            sub.Style.SetPixels("font-size", 11f);
            sub.Style.Set("color", "#9db2d4");

            info.Add(title);
            info.Add(sub);
            left.Add(icon);
            left.Add(info);

            var right = doc.CreateElement("div");
            right.Style.Set("display", "flex");
            right.Style.Set("gap", "8px");
            right.Style.Set("align-items", "center");

            var stat = doc.CreateElement("span");
            stat.AddToClassList("mono");
            stat.Style.SetPixels("font-size", 11f);
            stat.Style.Set("color", "#2fe0d0");

            var badge = doc.CreateElement("span");
            badge.AddToClassList("hud-chip-gold");

            right.Add(stat);
            right.Add(badge);
            row.Add(left);
            row.Add(right);
            return row;
        }

        void BindRow(Element row, int index)
        {
            if (index < 0 || index >= filtered.Count) return;
            var item = filtered[index];

            var icon = row.Q<Image>(null, "armory-row-icon");
            if (icon != null) icon.Source = item.IconPath;

            var title = row.Q(null, "sol-row-title");
            if (title != null)
            {
                var lockMark = item.Locked ? " · [锁定]" : "";
                var eqMark = item.Equipped ? " [已装备]" : "";
                title.Text = $"{item.Name}{eqMark}{lockMark}";
            }

            var sub = row.Q(null, "sol-row-sub");
            if (sub != null) sub.Text = $"{item.Code} · {item.Slot} Lv.{item.Level}";

            var chip = row.Q(null, "hud-chip-gold");
            if (chip != null) chip.Text = item.Rarity;

            var stat = row.Q(null, "mono");
            if (stat != null) stat.Text = item.StatLine;

            row.EnableInClassList("is-selected", index == selectedFilteredIndex);
            var clickSub = row.RegisterCallback<ClickEvent>(_ =>
            {
                selectedFilteredIndex = index;
                if (listView != null) listView.SelectedIndex = index;
                app.PlaySound("sfx-click");
                RefreshSelectionCard();
            });
            rowClickSubs[row] = clickSub;
        }

        void UnbindRow(Element row, int index)
        {
            if (rowClickSubs.TryGetValue(row, out var sub))
            {
                sub.Dispose();
                rowClickSubs.Remove(row);
            }
        }

        SolsticeGameData.RelicItem CurrentSelection()
        {
            if (selectedFilteredIndex < 0 || selectedFilteredIndex >= filtered.Count) return null;
            return filtered[selectedFilteredIndex];
        }

        void RefreshSelectionCard()
        {
            var item = CurrentSelection();
            if (item == null) return;
            if (selRarity != null) selRarity.Text = item.Rarity;
            if (selCode != null) selCode.Text = item.Code;
            if (selName != null) selName.Text = item.Name;
            if (selSlot != null) selSlot.Text = $"{item.Slot} · Lv.{item.Level}";
            if (selStat != null) selStat.Text = item.StatLine;
            if (selIcon != null) selIcon.Source = item.IconPath;
            if (equipBtn != null) equipBtn.Text = item.Equipped ? "卸下圣器" : "装配圣器";
            if (lockBtn != null) lockBtn.Text = item.Locked ? "解锁圣器" : "锁定圣器";
        }

        public void Dispose()
        {
            foreach (var sub in rowClickSubs.Values)
            {
                sub.Dispose();
            }
            rowClickSubs.Clear();
            subs.Dispose();
        }
    }
}
