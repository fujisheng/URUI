using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using URUI.Controls;
using URUI.Elements;
using URUI.Events;
using UnityEngine;

namespace URUI.Samples.GameFlow
{
    public sealed partial class GameFlowSample
    {
        string selectedItemId;

        void BindLogin()
        {
            var name = document.Q<TextField>("call-sign");
            var code = document.Q<TextField>("access-code");
            name.MaxLength = 18; code.MaxLength = 24;
            var connection = document.Q<Dropdown>("connection");
            connection.Value = settings.offline ? "Offline" : "Online";
            Subscribe(connection, evt => { settings.offline = evt.NewValue == "Offline"; });
            BindButton("login-submit", () => Operation(async token =>
            {
                document.Q<Button>("login-submit").SetEnabled(false);
                ButtonText("login-submit", "SIGNING IN...");
                Text("status", "Checking your local operator profile...");
                service.IsOffline = settings.offline;
                await service.LoginAsync(name.Value, code.Value, token);
                token.ThrowIfCancellationRequested();
                Navigate(GamePage.Home);
            }));
        }

        void BindHome()
        {
            var profile = service.CurrentPlayer;
            Text("greeting", profile.callSign);
            Text("power", profile.power.ToString("N0"));
            Text("rank", "#" + profile.rank);
            Text("inventory-count", service.FilterInventory("All", "").Count + " supplies available");
            BindButton("open-inventory", () => Navigate(GamePage.Inventory));
            BindButton("open-leaderboard", () => Navigate(GamePage.Leaderboard));
            BindButton("open-settings", () => Navigate(GamePage.Settings));
            BindButton("logout", () => Navigate(GamePage.Login));
            void CloseExpedition() { Show("expedition-dialog", false); document.Q("home-layer").SetEnabled(true); document.Q<Button>("open-expedition").Focus(); }
            BindButton("open-expedition", () => { Show("expedition-dialog", true); document.Q("home-layer").SetEnabled(false); document.Q<Button>("close-expedition").Focus(); });
            BindButton("close-expedition", CloseExpedition);
            BindButton("confirm-expedition", () => Text("expedition-status", "Loadout ready. This local UI sample ends before combat."));
            Text("profile-name", profile.callSign); Text("profile-level", "LEVEL " + profile.level);
            Text("profile-power", profile.power.ToString("N0")); Text("profile-rank", "#" + profile.rank);
            BindButton("open-profile", () => { Show("profile-dialog", true); document.Q("home-layer").SetEnabled(false); document.Q<Button>("close-profile").Focus(); });
            void CloseProfile() { Show("profile-dialog", false); document.Q("home-layer").SetEnabled(true); document.Q<Button>("open-profile").Focus(); }
            BindButton("close-profile", CloseProfile); BindButton("profile-dismiss", CloseProfile);
        }

        static string ItemIcon(InventoryItem item) => item.icon;

        void BindInventory()
        {
            var grid = document.Q("item-grid");
            var search = document.Q<TextField>("search");
            var category = document.Q<Dropdown>("category");
            var filtered = new List<InventoryItem>();
            var slots = new Dictionary<string, Button>();
            void Select(string id)
            {
                var item = filtered.Find(value => value.id == id);
                bool valid = item != null;
                Show("item-detail", valid); Show("selection-empty", !valid);
                foreach (var pair in slots) pair.Value.EnableInClassList("selected", valid && pair.Key == id);
                if (!valid) return;
                selectedItemId = id;
                Text("detail-name", item.name); Text("detail-rarity", item.rarity.ToUpperInvariant());
                Text("detail-description", item.description);
                document.Q<URUI.Controls.Image>("detail-icon").Source = ItemIcon(item);
                Text("detail-power", item.category == "Equipment" ? "+" + item.power + " POWER" : "OWNED  " + item.count);
                var action = document.Q<Button>("item-action");
                ButtonText("item-action", item.category == "Equipment" ? (item.equipped ? "UNEQUIP" : "EQUIP") : item.category == "Consumables" ? "USE ITEM" : "MATERIAL");
                action.SetEnabled(item.category != "Materials");
            }
            void Filter()
            {
                filtered = service.FilterInventory(category.Value, search.Value);
                var viewport = document.Q<ScrollView>("item-scroll");
                if (viewport.HasLayout) viewport.ScrollOffset = Vector2.zero;
                foreach (var child in grid.Children()) child.Destroy();
                slots.Clear();
                foreach (var value in filtered)
                {
                    var item = value;
                    var slot = document.Create<Button>(); slot.Name = "item-" + item.id;
                    slot.AddToClassList("item-slot"); slot.AddToClassList("rarity-" + item.rarity.ToLowerInvariant());
                    AddArt(slot, "slot-normal", "slot-frame");
                    AddArt(slot, "slot-selected", "slot-selected-art");
                    var icon = document.Create<URUI.Controls.Image>(); icon.Source = ItemIcon(item); icon.AddToClassList("item-icon"); slot.Add(icon);
                    var count = document.Create<Label>(); count.Text = "x" + item.count; count.AddToClassList("item-count"); slot.Add(count);
                    var equipped = document.Create<Label>(); equipped.Text = item.equipped ? "E" : ""; equipped.AddToClassList("equipped-mark"); slot.Add(equipped);
                    var title = document.Create<Label>(); title.Text = item.name; title.AddToClassList("slot-name"); slot.Add(title);
                    slot.Clicked += () => Select(item.id);
                    grid.Add(slot); slots.Add(item.id, slot);
                }
                for (int i = filtered.Count; i < 40; i++)
                {
                    var empty = document.Create<Element>(); empty.AddToClassList("empty-slot"); AddArt(empty, "slot-empty", "slot-frame"); grid.Add(empty);
                }
                Text("result-count", filtered.Count + " / 40"); Show("list-empty", filtered.Count == 0);
                document.Q("category-all").EnableInClassList("selected", category.Value == "All" || category.Value == "Equipment");
                document.Q("category-supplies").EnableInClassList("selected", category.Value == "Consumables");
                document.Q("category-materials").EnableInClassList("selected", category.Value == "Materials");
                string selected = filtered.Exists(item => item.id == selectedItemId) ? selectedItemId : filtered.Count > 0 ? filtered[0].id : null;
                Select(selected);
            }
            Subscribe(search, evt => Filter()); Subscribe(category, evt => Filter());
            BindButton("category-all", () => { category.Value = "All"; Filter(); });
            BindButton("category-supplies", () => { category.Value = "Consumables"; Filter(); });
            BindButton("category-materials", () => { category.Value = "Materials"; Filter(); });
            BindButton("clear-search", () => { search.Value = ""; category.Value = "All"; Filter(); });
            BindButton("item-action", () =>
            {
                if (selectedItemId == null) return;
                Text("status", service.UseOrEquipItem(selectedItemId)); Filter();
                document.Q("item-detail").AddToClassList("reward"); rewardUntil = Time.unscaledTime + .45f;
            });
            Filter();
        }

        void BindSettings()
        {
            var draft = settings.Clone();
            void SelectGroup(string selected)
            {
                foreach (string group in new[] { "audio", "display", "network" })
                {
                    bool active = group == selected;
                    Show("group-" + group, active);
                    document.Q("group-" + group).SetEnabled(active);
                    document.Q("tab-" + group).EnableInClassList("selected", active);
                }
            }
            BindButton("tab-audio", () => SelectGroup("audio"));
            BindButton("tab-display", () => SelectGroup("display"));
            BindButton("tab-network", () => SelectGroup("network"));
            SelectGroup("audio");
            var volume = document.Q<Slider>("volume"); volume.SetValueWithoutNotify(draft.volume * 100);
            var sound = document.Q<Toggle>("sound"); sound.SetValueWithoutNotify(draft.sound);
            void SoundVisual() { document.Q("sound-switch").EnableInClassList("on", draft.sound); Text("sound-caption", draft.sound ? "ON" : "OFF"); }
            SoundVisual();
            var density = document.Q<Dropdown>("density"); density.SetValueWithoutNotify(draft.compactRows ? "Compact" : "Comfortable");
            var accent = document.Q<Dropdown>("accent"); accent.SetValueWithoutNotify(draft.accent);
            var connection = document.Q<Dropdown>("connection"); connection.SetValueWithoutNotify(draft.offline ? "Offline" : "Online");
            Text("volume-caption", Math.Round(draft.volume * 100) + "%");
            Subscribe(volume, evt => { draft.volume = evt.NewValue / 100; Text("volume-caption", Math.Round(evt.NewValue) + "%"); });
            Subscribe(sound, evt => { draft.sound = evt.NewValue; SoundVisual(); });
            Subscribe(density, evt => draft.compactRows = evt.NewValue == "Compact");
            Subscribe(accent, evt => draft.accent = evt.NewValue);
            Subscribe(connection, evt => draft.offline = evt.NewValue == "Offline");
            BindButton("preview-audio", () =>
            {
                if (audioSource && feedback && draft.sound) { audioSource.volume = draft.volume; audioSource.PlayOneShot(feedback); }
                Text("status", draft.sound ? "Playing the sample-only confirmation tone." : "Sound is disabled in this draft.");
            }, false);
            BindButton("settings-cancel", () => Navigate(GamePage.Home));
            BindButton("save-settings", () =>
            {
                settings = draft.Clone(); service.IsOffline = settings.offline;
                PlayerPrefs.SetString(SettingsKey, JsonUtility.ToJson(settings)); PlayerPrefs.Save();
                document.Root.EnableInClassList("cyan", settings.accent == "Glacier");
                document.Root.EnableInClassList("compact", settings.compactRows);
                Text("status", "Preferences saved locally. Audio and appearance affect this sample only.");
            });
        }

        void BindLeaderboard()
        {
            var list = document.Q<ListView>("rankings");
            var scope = document.Q<Dropdown>("ranking-scope");
            IReadOnlyList<RankingEntry> entries = Array.Empty<RankingEntry>();
            list.FixedItemHeight = settings.compactRows ? 56 : 64;
            list.MakeItem = () =>
            {
                var row = document.Create<Element>(); row.AddToClassList("ranking-row");
                foreach (var part in new[] { "position", "pilot", "score" })
                {
                    var label = document.Create<Label>(); label.Name = part; label.AddToClassList("ranking-" + part); row.Add(label);
                    if (part == "position")
                    {
                        var avatar = document.Create<URUI.Controls.Image>(); avatar.AddToClassList("rank-avatar"); row.Add(avatar);
                    }
                }
                AddFrame(row);
                return row;
            };
            list.BindItem = (row, index) =>
            {
                row.EnableInClassList("first-rank", index == 0); row.EnableInClassList("second-rank", index == 1); row.EnableInClassList("third-rank", index == 2);
                row.Q<Label>("position").Text = (index + 1).ToString();
                row.Q<URUI.Controls.Image>().Source = "gameflow/rank-avatar-" + (index % 6 + 1) + ".png";
                row.Q<Label>("pilot").Text = entries[index].name;
                row.Q<Label>("score").Text = entries[index].score.ToString("N0") + " PTS";
            };
            Text("your-rank", "#" + service.CurrentPlayer.rank + "  /  " + service.CurrentPlayer.callSign);
            async Task Refresh(CancellationToken token)
            {
                Show("retry", false); Show("ranking-empty", false);
                document.Q<Button>("refresh").SetEnabled(false); scope.SetEnabled(false);
                Text("status", "Retrieving a new local ranking snapshot...");
                service.IsOffline = settings.offline;
                try
                {
                    entries = await service.GetRankingsAsync(scope.Value, token);
                    token.ThrowIfCancellationRequested();
                    var rows = new List<RankingEntry>(entries); list.ItemsSource = rows;
                    Show("ranking-empty", entries.Count == 0);
                    Text("status", entries.Count == 0 ? "No friends have joined this local demo yet." : "Updated just now. Simulated standings; no real backend.");
                }
                finally { if (!token.IsCancellationRequested && scope.IsValid) scope.SetEnabled(true); }
            }
            Subscribe(scope, evt => Operation(Refresh));
            BindButton("refresh", () => Operation(Refresh)); BindButton("retry", () => Operation(Refresh));
            Operation(Refresh);
        }
    }
}
