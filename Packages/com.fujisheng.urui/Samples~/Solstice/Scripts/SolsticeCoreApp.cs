using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Animation;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Samples.Solstice
{
    /// <summary>
    /// Built-in & URP compatible Core showcase entry component for `Packages/com.fujisheng.urui/Samples~/Solstice`.
    /// Demonstrates pure URUI Core capabilities without URUI.Effect or URP package dependencies.
    /// </summary>
    public sealed class SolsticeCoreApp : MonoBehaviour
    {
        const string ResRoot = "URUI/SolsticeCore/";

        [SerializeField] Surface surface;
        [SerializeField] AudioSource audioSource;

        SolsticeCoreData data;
        Document topHudDoc;
        Document pageDoc;
        Document settingsDoc;
        Document devDoc;
        SequencePlayer sequencePlayer;

        readonly List<IDisposable> pageSubs = new();
        readonly List<IDisposable> topSubs = new();
        readonly Dictionary<Element, IDisposable> rowSubs = new();
        readonly Dictionary<string, Texture2D> texCache = new(StringComparer.Ordinal);
        readonly List<SolsticeCoreData.RelicItem> filteredArmory = new(10000);

        TextResource[] sharedTextResources;
        string currentRoute = "lobby";
        string currentTheme = "theme-obsidian";
        string selectedHeroId = "aurelia";
        string armoryCategory = "ALL";
        string armoryQuery = "";
        int selectedArmoryIndex = 0;
        bool soundEnabled = true;
        float volume = 0.8f;
        bool settingsOpen;
        bool devOpen;
        float autoTimer;
        float dmgTimer;
        bool initialized;

        void Start() => InitializeIfNeeded();

        public void InitializeIfNeeded()
        {
            if (initialized) return;
            initialized = true;
            if (!surface) surface = GetComponent<Surface>();
            if (!audioSource) audioSource = GetComponent<AudioSource>();

            surface.Viewport = new Vector2Int(1280, 720);
            data = new SolsticeCoreData();
            BuildSharedResources();

            topHudDoc = CreateChildDoc("CoreTopHudDoc");
            pageDoc = CreateChildDoc("CorePageDoc");
            devDoc = CreateChildDoc("CoreDevDoc");
            settingsDoc = CreateChildDoc("CoreSettingsDoc");

            topHudDoc.Load(UnityEngine.Resources.Load<TextAsset>(ResRoot + "Html/TopHud"), "/solstice-core/Html/TopHud.html");
            topHudDoc.SetPresentation(10, 0, true, false);
            MakePassThrough(topHudDoc);
            WireTopHud();

            devDoc.Load(UnityEngine.Resources.Load<TextAsset>(ResRoot + "Html/DevOverlay"), "/solstice-core/Html/DevOverlay.html");
            devDoc.SetPresentation(25, 0, true, false);
            MakePassThrough(devDoc);
            WireDevOverlay();

            settingsDoc.Load(UnityEngine.Resources.Load<TextAsset>(ResRoot + "Html/SettingsModal"), "/solstice-core/Html/SettingsModal.html");
            settingsDoc.SetPresentation(30, 0, false, true);
            WireSettings();

            LoadPage("lobby");
        }

        Document CreateChildDoc(string goName)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            var doc = go.AddComponent<Document>();
            doc.Surface = surface;
            doc.TextResources = sharedTextResources;
            doc.ImageResolver = ResolveImage;
            return doc;
        }

        void BuildSharedResources()
        {
            var list = new List<TextResource>(24);
            void Add(string url, string path)
            {
                var a = UnityEngine.Resources.Load<TextAsset>(path);
                if (a) list.Add(new TextResource(url, a));
            }
            Add("/urui/base.css", "URUI/base");
            Add("/urui/controls.css", "URUI/controls");
            Add("/urui/behaviors.css", "URUI/behaviors");
            Add("/solstice-core/Css/tokens.css", ResRoot + "Css/tokens");
            Add("/solstice-core/Css/base.css", ResRoot + "Css/base");
            Add("/solstice-core/Css/solstice.css", ResRoot + "Css/solstice");
            Add("/solstice-core/Fonts/LatoLatin-Regular.ttf.bytes", ResRoot + "Fonts/LatoLatin-Regular.ttf");
            Add("/solstice-core/Fonts/RobotoMono-Regular.ttf.bytes", ResRoot + "Fonts/RobotoMono-Regular.ttf");
            Add("/solstice-core/Fonts/Solstice-SC.ttf.bytes", ResRoot + "Fonts/Solstice-SC.ttf");

            foreach (var fam in new[] { "Serif", "Sans", "Mono" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = $"Liberation{fam}-{face}.ttf";
                    Add($"/urui/Fonts/{name}.bytes", "URUI/Fonts/" + name);
                }
            }
            sharedTextResources = list.ToArray();
        }

        ImageLease ResolveImage(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("/solstice-core/Images/", StringComparison.Ordinal))
            {
                return null;
            }
            var path = ResRoot + url.Substring("/solstice-core/".Length);
            var dot = path.LastIndexOf('.');
            if (dot > 0) path = path.Substring(0, dot);
            if (!texCache.TryGetValue(path, out var tex) || !tex)
            {
                tex = UnityEngine.Resources.Load<Texture2D>(path);
                texCache[path] = tex;
            }
            return tex ? new ImageLease(tex) : null;
        }

        static void MakePassThrough(Document doc)
        {
            if (doc == null || !doc.IsLoaded) return;
            doc.DocumentElement?.Style.Set("pointer-events", "none");
            doc.Body?.Style.Set("pointer-events", "none");
            doc.Body?.Style.Set("background-color", "transparent");
            doc.Q("sol-root")?.Style.Set("pointer-events", "none");
        }

        void BindClick(List<IDisposable> bag, Document doc, string id, Action cb)
        {
            var btn = doc.Q<Button>(id);
            if (btn == null) return;
            btn.Clicked += cb;
            bag.Add(new Subscription(() => btn.Clicked -= cb));
        }

        void Navigate(string route)
        {
            if (route == currentRoute && pageDoc.IsLoaded) return;
            PlayClip("sfx-click");
            if (surface && surface.IsExecutingCallback) surface.RunAfterEvent(() => LoadPage(route));
            else LoadPage(route);
        }

        void LoadPage(string route)
        {
            currentRoute = route;
            ClearSubs(pageSubs);
            ClearRowSubs();
            sequencePlayer?.Dispose();
            sequencePlayer = null;
            if (pageDoc.IsLoaded) pageDoc.Unload();

            var htmlName = route switch
            {
                "combat" => "Combat",
                "heroes" => "Heroes",
                "summon" => "Summon",
                "armory" => "Backpack",
                _ => "Lobby"
            };
            pageDoc.Load(UnityEngine.Resources.Load<TextAsset>(ResRoot + "Html/" + htmlName), $"/solstice-core/Html/{htmlName}.html");
            pageDoc.SetPresentation(0, 0, true, false);
            ApplyThemeClass(pageDoc);

            if (route == "lobby") WireLobby();
            else if (route == "combat") WireCombat();
            else if (route == "heroes") WireHeroes();
            else if (route == "summon") WireSummon();
            else WireArmory();

            RefreshTopHud();
            pageDoc.SynchronizeLayout();
        }

        void WireTopHud()
        {
            BindClick(topSubs, topHudDoc, "nav-lobby", () => Navigate("lobby"));
            BindClick(topSubs, topHudDoc, "nav-combat", () => Navigate("combat"));
            BindClick(topSubs, topHudDoc, "nav-heroes", () => Navigate("heroes"));
            BindClick(topSubs, topHudDoc, "nav-summon", () => Navigate("summon"));
            BindClick(topSubs, topHudDoc, "nav-armory", () => Navigate("armory"));
            BindClick(topSubs, topHudDoc, "btn-add-stamina", () => { data.AddStaminaDemo(); PlayClip("sfx-click"); RefreshTopHud(); });
            BindClick(topSubs, topHudDoc, "btn-sound-toggle", () => { soundEnabled = !soundEnabled; RefreshTopHud(); });
            BindClick(topSubs, topHudDoc, "btn-settings-open", () => { settingsOpen = !settingsOpen; settingsDoc.SetPresentation(30, 0, settingsOpen, true); ApplyThemeClass(settingsDoc); });
            BindClick(topSubs, topHudDoc, "btn-dev-toggle", () =>
            {
                devOpen = !devOpen;
                devDoc.Q("dev-drawer")?.Style.Set("display", devOpen ? "flex" : "none");
                topHudDoc.Q("btn-dev-toggle")?.EnableInClassList("is-active", devOpen);
            });
            RefreshTopHud();
        }

        void RefreshTopHud()
        {
            if (topHudDoc == null || !topHudDoc.IsLoaded) return;
            var st = topHudDoc.Q("hud-stamina"); if (st != null) st.Text = $"{data.Stamina}/{data.StaminaCap}";
            var gd = topHudDoc.Q("hud-gold"); if (gd != null) gd.Text = $"{data.Gold:N0}";
            var gm = topHudDoc.Q("hud-gems"); if (gm != null) gm.Text = $"{data.Gems:N0}";
            var snd = topHudDoc.Q("btn-sound-toggle");
            if (snd != null) { snd.Text = soundEnabled ? "音效" : "静音"; snd.EnableInClassList("is-active", soundEnabled); }
            foreach (var r in new[] { "lobby", "combat", "heroes", "summon", "armory" })
            {
                topHudDoc.Q("nav-" + r)?.EnableInClassList("is-active", r == currentRoute);
            }
        }

        void WireLobby()
        {
            foreach (var hid in new[] { "aurelia", "kael", "lyra", "veyra" })
            {
                var id = hid;
                BindClick(pageSubs, pageDoc, "lobby-lead-" + id, () => { data.ActiveHeroId = id; PlayClip("sfx-click"); RefreshLobby(); });
            }
            BindClick(pageSubs, pageDoc, "btn-lobby-battle", () => Navigate("combat"));
            BindClick(pageSubs, pageDoc, "btn-lobby-inspect", () => Navigate("heroes"));
            BindClick(pageSubs, pageDoc, "btn-lobby-summon", () => Navigate("summon"));
            BindClick(pageSubs, pageDoc, "btn-lobby-armory", () => Navigate("armory"));
            RefreshLobby();
        }

        void RefreshLobby()
        {
            var hero = data.GetHero(data.ActiveHeroId);
            var nm = pageDoc.Q("lobby-hero-name"); if (nm != null) nm.Text = $"{hero.NameCn} · {hero.NameEn}";
            var rl = pageDoc.Q("lobby-hero-role"); if (rl != null) rl.Text = $"{hero.Rarity} · {hero.Title}";
            var fig = pageDoc.Q<Image>("lobby-hero-figure"); if (fig != null) fig.Source = hero.CutoutPath;
            var av = pageDoc.Q<Image>("lobby-cmd-avatar"); if (av != null) av.Source = hero.PortraitPath;
            var bt = pageDoc.Q("lobby-banner-title"); if (bt != null) bt.Text = data.FlagshipTitle;
            var xp = pageDoc.Q<ProgressBar>("lobby-xp-progress");
            if (xp != null) { xp.LowValue = 0f; xp.HighValue = 100f; xp.SetValueWithoutNotify(78.4f); }
            foreach (var id in new[] { "aurelia", "kael", "lyra", "veyra" })
            {
                pageDoc.Q("lobby-lead-" + id)?.EnableInClassList("is-selected", id == data.ActiveHeroId);
            }
        }

        void WireCombat()
        {
            for (var i = 0; i < 4; i++)
            {
                var idx = i;
                BindClick(pageSubs, pageDoc, "skill-btn-" + idx, () => TriggerSkill(idx));
                var disc = pageDoc.Q<Image>("skill-cd-disc-" + idx);
                if (disc != null) { disc.FillMode = ImageFillMode.Radial360; disc.FillOrigin = 2; disc.FillClockwise = false; }
                var ring = pageDoc.Q<Image>("skill-ring-" + idx);
                if (ring != null) { ring.FillMode = ImageFillMode.Radial360; ring.FillOrigin = 2; ring.FillClockwise = true; }
            }
            BindClick(pageSubs, pageDoc, "btn-combat-speed", () => { data.BattleSpeed2x = !data.BattleSpeed2x; PlayClip("sfx-click"); RefreshCombat(); });
            BindClick(pageSubs, pageDoc, "btn-combat-auto", () => { data.AutoBattle = !data.AutoBattle; PlayClip("sfx-click"); RefreshCombat(); });
            RefreshCombat();
        }

        void TriggerSkill(int idx)
        {
            if (!data.TriggerSkill(idx, out var sk, out var dmg, out var isCrit)) return;
            PlayClip(isCrit ? "sfx-crit" : "sfx-skill");
            var callout = pageDoc.Q("damage-callout");
            if (callout != null)
            {
                callout.Text = idx == 2 ? "SHIELD +4,500" : ((isCrit ? "CRIT " : "") + $"{dmg:N0}");
                callout.EnableInClassList("is-crit", idx != 2 && isCrit);
                callout.Style.SetOpacity(1f);
                dmgTimer = 1.15f;
            }
            RefreshTopHud();
            RefreshCombat();
        }

        void RefreshCombat()
        {
            if (pageDoc == null || !pageDoc.IsLoaded || currentRoute != "combat") return;
            var lead = data.GetHero(data.ActiveHeroId);
            var hImg = pageDoc.Q<Image>("combat-hero-img"); if (hImg != null) hImg.Source = lead.CutoutPath;
            var hpBar = pageDoc.Q<ProgressBar>("boss-hp-bar");
            if (hpBar != null) { hpBar.LowValue = 0f; hpBar.HighValue = data.BossMaxHp; hpBar.SetValueWithoutNotify(data.BossHp); }
            var shBar = pageDoc.Q<ProgressBar>("boss-shield-bar");
            if (shBar != null) { shBar.LowValue = 0f; shBar.HighValue = data.BossMaxShield; shBar.SetValueWithoutNotify(data.BossShield); }
            var sqHp = pageDoc.Q<ProgressBar>("squad-hp-bar");
            if (sqHp != null) { sqHp.LowValue = 0f; sqHp.HighValue = data.SquadMaxHp; sqHp.SetValueWithoutNotify(data.SquadHp); }
            var sqEn = pageDoc.Q<ProgressBar>("squad-energy-bar");
            if (sqEn != null) { sqEn.LowValue = 0f; sqEn.HighValue = 100f; sqEn.SetValueWithoutNotify(data.AstralEnergy); }

            var bhp = pageDoc.Q("boss-hp-text"); if (bhp != null) bhp.Text = $"{data.BossHp:N0} / {data.BossMaxHp:N0}";
            var bsh = pageDoc.Q("boss-shield-text"); if (bsh != null) bsh.Text = $"{data.BossShield:N0} / {data.BossMaxShield:N0}";
            var shp = pageDoc.Q("squad-hp-text"); if (shp != null) shp.Text = $"{data.SquadHp:N0} / {data.SquadMaxHp:N0}";
            var sen = pageDoc.Q("squad-energy-text"); if (sen != null) sen.Text = $"{data.AstralEnergy} / 100";
            var sp = pageDoc.Q("btn-combat-speed"); if (sp != null) { sp.Text = data.BattleSpeed2x ? "2x 倍速" : "1x 速度"; sp.EnableInClassList("is-active", data.BattleSpeed2x); }
            pageDoc.Q("btn-combat-auto")?.EnableInClassList("is-active", data.AutoBattle);

            for (var i = 0; i < 4; i++)
            {
                var sk = data.CombatSkills[i];
                var frac = Mathf.Clamp01(sk.RemainingCooldown / Mathf.Max(0.1f, sk.CooldownSeconds));
                var d = pageDoc.Q<Image>("skill-cd-disc-" + i); if (d != null) d.FillAmount = frac;
                var r = pageDoc.Q<Image>("skill-ring-" + i); if (r != null) r.FillAmount = 1f - frac;
                var l = pageDoc.Q("skill-cd-text-" + i); if (l != null) l.Text = sk.RemainingCooldown > 0.05f ? $"{sk.RemainingCooldown:F1}s" : "";
            }
        }

        void WireHeroes()
        {
            selectedHeroId = data.ActiveHeroId;
            foreach (var hid in new[] { "aurelia", "kael", "lyra", "veyra" })
            {
                var id = hid;
                BindClick(pageSubs, pageDoc, "hero-card-" + id, () => { selectedHeroId = id; PlayClip("sfx-click"); RefreshHeroes(); });
            }
            BindClick(pageSubs, pageDoc, "btn-hero-upgrade", () =>
            {
                if (data.UpgradeHero(selectedHeroId, out _)) { PlayClip("sfx-skill"); RefreshTopHud(); RefreshHeroes(); }
            });
            BindClick(pageSubs, pageDoc, "btn-hero-deploy", () => { data.ActiveHeroId = selectedHeroId; PlayClip("sfx-click"); });
            RefreshHeroes();
        }

        void RefreshHeroes()
        {
            var hero = data.GetHero(selectedHeroId);
            var img = pageDoc.Q<Image>("hero-stage-cutout"); if (img != null) img.Source = hero.CutoutPath;
            var lv = pageDoc.Q("hero-detail-lv"); if (lv != null) lv.Text = $"Lv.{hero.Level} / {hero.MaxLevel}";
            var nm = pageDoc.Q("hero-detail-name"); if (nm != null) nm.Text = $"{hero.NameCn} · {hero.NameEn}";
            var atk = pageDoc.Q("hero-stat-atk"); if (atk != null) atk.Text = $"{hero.Atk:N0}";
            var hp = pageDoc.Q("hero-stat-hp"); if (hp != null) hp.Text = $"{hero.Hp:N0}";
            var bar = pageDoc.Q<ProgressBar>("hero-affinity-bar");
            if (bar != null) { bar.LowValue = 0f; bar.HighValue = 100f; bar.SetValueWithoutNotify(Mathf.RoundToInt(hero.Affinity * 100f)); }
            foreach (var id in new[] { "aurelia", "kael", "lyra", "veyra" })
            {
                pageDoc.Q("hero-card-" + id)?.EnableInClassList("is-selected", id == selectedHeroId);
            }
        }

        void WireSummon()
        {
            BindClick(pageSubs, pageDoc, "btn-summon-once", () =>
            {
                pageDoc.SynchronizeLayout();
                if (sequencePlayer == null && pageDoc.DocumentElement.IsVisibleInHierarchy)
                {
                    var json = UnityEngine.Resources.Load<TextAsset>(ResRoot + "Sequences/summon.urui-sequence");
                    if (json) sequencePlayer = new SequencePlayer(pageDoc.DocumentElement, SequenceDefinition.Parse(json.text));
                }
                var h = data.RollSummon();
                PlayClip("sfx-summon-ssr");
                var ci = pageDoc.Q<Image>("summon-card-img"); if (ci != null) ci.Source = h.CutoutPath;
                var rn = pageDoc.Q("summon-result-name"); if (rn != null) rn.Text = $"召唤成功：{h.NameCn} ({h.Rarity})";
                if (sequencePlayer != null) { sequencePlayer.Seek(0); sequencePlayer.Play(); }
                RefreshTopHud();
                RefreshSummon();
            });
            BindClick(pageSubs, pageDoc, "btn-summon-reset", () =>
            {
                var ci = pageDoc.Q<Image>("summon-card-img"); if (ci != null) ci.Source = "/solstice-core/Images/card-back.png";
                var rn = pageDoc.Q("summon-result-name"); if (rn != null) rn.Text = "点击占卜唤醒沉睡英灵";
                sequencePlayer?.Stop();
            });
            RefreshSummon();
        }

        void RefreshSummon()
        {
            var pt = pageDoc.Q("summon-pity-text"); if (pt != null) pt.Text = $"{data.PityCount} / {data.PityMax}";
            var pb = pageDoc.Q<ProgressBar>("summon-pity-bar");
            if (pb != null) { pb.LowValue = 0f; pb.HighValue = data.PityMax; pb.SetValueWithoutNotify(data.PityCount); }
        }

        void WireArmory()
        {
            foreach (var pair in new[] { ("filter-all", "ALL"), ("filter-weapon", "Weapon"), ("filter-relic", "Relic"), ("filter-crest", "Crest"), ("filter-stone", "Stone") })
            {
                var cat = pair.Item2;
                BindClick(pageSubs, pageDoc, pair.Item1, () => { armoryCategory = cat; PlayClip("sfx-click"); ApplyArmoryFilter(); });
            }
            var search = pageDoc.Q<TextField>("armory-search");
            if (search != null)
            {
                pageSubs.Add(search.RegisterValueChangedCallback(evt => { armoryQuery = evt.NewValue ?? ""; ApplyArmoryFilter(); }));
            }
            var lv = pageDoc.Q<ListView>("armory-listview");
            if (lv != null)
            {
                lv.FixedItemHeight = 66f;
                lv.MakeItem = () =>
                {
                    var row = pageDoc.CreateElement("div"); row.AddToClassList("armory-row");
                    var left = pageDoc.CreateElement("div"); left.AddToClassList("armory-row-left");
                    var icon = pageDoc.Create<Image>(); icon.AddToClassList("armory-row-icon");
                    var info = pageDoc.CreateElement("div");
                    var title = pageDoc.CreateElement("div"); title.AddToClassList("sol-row-title");
                    var sub = pageDoc.CreateElement("div"); sub.AddToClassList("sol-row-sub");
                    info.Add(title); info.Add(sub); left.Add(icon); left.Add(info);
                    var chip = pageDoc.CreateElement("span"); chip.AddToClassList("hud-chip-gold");
                    row.Add(left); row.Add(chip);
                    return row;
                };
                lv.BindItem = (row, idx) =>
                {
                    if (idx < 0 || idx >= filteredArmory.Count) return;
                    var item = filteredArmory[idx];
                    var ic = row.Q<Image>(null, "armory-row-icon"); if (ic != null) ic.Source = item.IconPath;
                    var tt = row.Q(null, "sol-row-title"); if (tt != null) tt.Text = item.Name;
                    var sb = row.Q(null, "sol-row-sub"); if (sb != null) sb.Text = $"{item.Code} · {item.StatLine}";
                    var cp = row.Q(null, "hud-chip-gold"); if (cp != null) cp.Text = item.Rarity;
                    rowSubs[row] = row.RegisterCallback<ClickEvent>(_ => { selectedArmoryIndex = idx; lv.SelectedIndex = idx; RefreshArmorySelection(); });
                };
                lv.UnbindItem = (row, _) => { if (rowSubs.TryGetValue(row, out var s)) { s.Dispose(); rowSubs.Remove(row); } };
            }
            ApplyArmoryFilter();
        }

        void ApplyArmoryFilter()
        {
            filteredArmory.Clear();
            var q = armoryQuery.Trim();
            for (var i = 0; i < data.Inventory.Count; i++)
            {
                var item = data.Inventory[i];
                if (armoryCategory != "ALL" && item.Category != armoryCategory) continue;
                if (q.Length > 0 && item.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                filteredArmory.Add(item);
            }
            selectedArmoryIndex = filteredArmory.Count > 0 ? 0 : -1;
            var cnt = pageDoc.Q("armory-count-label"); if (cnt != null) cnt.Text = $"{filteredArmory.Count:N0} 项 · Core ListView";
            var lv = pageDoc.Q<ListView>("armory-listview");
            if (lv != null) { lv.ItemsSource = filteredArmory; if (filteredArmory.Count > 0) lv.SelectedIndex = 0; }
            RefreshArmorySelection();
        }

        void RefreshArmorySelection()
        {
            if (selectedArmoryIndex < 0 || selectedArmoryIndex >= filteredArmory.Count) return;
            var item = filteredArmory[selectedArmoryIndex];
            var nm = pageDoc.Q("armory-sel-name"); if (nm != null) nm.Text = item.Name;
            var st = pageDoc.Q("armory-sel-stat"); if (st != null) st.Text = item.StatLine;
            var ic = pageDoc.Q<Image>("armory-sel-icon"); if (ic != null) ic.Source = item.IconPath;
        }

        void WireDevOverlay()
        {
            BindClick(topSubs, devDoc, "btn-dev-close", () =>
            {
                devOpen = false;
                devDoc.Q("dev-drawer")?.Style.Set("display", "none");
                topHudDoc.Q("btn-dev-toggle")?.EnableInClassList("is-active", false);
            });
        }

        void WireSettings()
        {
            BindClick(topSubs, settingsDoc, "btn-settings-close", () => { settingsOpen = false; settingsDoc.SetPresentation(30, 0, false, true); });
            foreach (var th in new[] { "theme-obsidian", "theme-crimson", "theme-alabaster" })
            {
                var t = th;
                BindClick(topSubs, settingsDoc, t, () =>
                {
                    currentTheme = t;
                    ApplyThemeClass(topHudDoc); ApplyThemeClass(pageDoc); ApplyThemeClass(settingsDoc); ApplyThemeClass(devDoc);
                    PlayClip("sfx-click");
                });
            }
            var vs = settingsDoc.Q<Slider>("settings-volume");
            if (vs != null) topSubs.Add(vs.RegisterValueChangedCallback(evt => { volume = evt.NewValue / 100f; }));
        }

        void ApplyThemeClass(Document doc)
        {
            if (doc == null || !doc.IsLoaded) return;
            var r = doc.Q("sol-root");
            if (r == null) return;
            r.RemoveFromClassList("theme-obsidian"); r.RemoveFromClassList("theme-crimson"); r.RemoveFromClassList("theme-alabaster");
            r.AddToClassList(currentTheme);
        }

        void PlayClip(string name)
        {
            if (!soundEnabled || !audioSource) return;
            var clip = UnityEngine.Resources.Load<AudioClip>(ResRoot + "Audio/" + name);
            if (clip) audioSource.PlayOneShot(clip, volume);
        }

        void Update()
        {
            if (!initialized) return;
            var dt = Time.deltaTime;
            if (currentRoute == "combat" && pageDoc && pageDoc.IsLoaded)
            {
                data.TickCombat(dt);
                if (dmgTimer > 0f) { dmgTimer -= dt; if (dmgTimer <= 0f) pageDoc.Q("damage-callout")?.Style.SetOpacity(0f); }
                if (data.AutoBattle)
                {
                    autoTimer -= dt;
                    if (autoTimer <= 0f) { autoTimer = data.BattleSpeed2x ? 0.55f : 1.0f; for (var i = 3; i >= 0; i--) { if (data.CombatSkills[i].RemainingCooldown <= 0.05f && (!data.CombatSkills[i].IsUltimate || data.AstralEnergy >= 60)) { TriggerSkill(i); break; } } }
                }
                RefreshCombat();
            }
            if (devOpen && devDoc && devDoc.IsLoaded)
            {
                var stats = surface.GetRenderingStatistics();
                var d = devDoc.Q("stat-draws"); if (d != null) d.Text = stats.RecordedGeometryDraws.ToString();
                var c = devDoc.Q("stat-cmds"); if (c != null) c.Text = stats.NativePlanCommandCount.ToString();
                var p = devDoc.Q("stat-plan"); if (p != null) p.Text = $"{stats.NativePlanFrameBytes:N0} B";
            }
        }

        void ClearSubs(List<IDisposable> bag) { for (var i = bag.Count - 1; i >= 0; i--) bag[i]?.Dispose(); bag.Clear(); }
        void ClearRowSubs() { foreach (var s in rowSubs.Values) s?.Dispose(); rowSubs.Clear(); }

        void OnDestroy()
        {
            ClearSubs(pageSubs); ClearSubs(topSubs); ClearRowSubs();
            sequencePlayer?.Dispose(); sequencePlayer = null;
            if (pageDoc && pageDoc.IsLoaded) pageDoc.Unload();
            if (topHudDoc && topHudDoc.IsLoaded) topHudDoc.Unload();
            if (settingsDoc && settingsDoc.IsLoaded) settingsDoc.Unload();
            if (devDoc && devDoc.IsLoaded) devDoc.Unload();
        }

        sealed class Subscription : IDisposable
        {
            Action dispose;
            public Subscription(Action dispose) => this.dispose = dispose;
            public void Dispose() { var d = dispose; dispose = null; d?.Invoke(); }
        }
    }
}
