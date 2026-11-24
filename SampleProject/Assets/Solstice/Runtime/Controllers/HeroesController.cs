using System;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Effect;
using URUI.Elements;

namespace Solstice
{
    /// <summary>
    /// Controls Heroes Showcase: squad character switching, live URUI.Effect shader preset modes,
    /// ui-wave crest waving, ArcTextLayout curved emblem subtitle, and stat progression.
    /// </summary>
    public sealed class HeroesController : IDisposable
    {
        readonly SolsticeApp app;
        readonly SolsticeGameData data;
        readonly Document doc;
        readonly CompositeDisposable subs = new();

        Image stageCutout;
        Element arcHost;
        Element arcSource;
        Element arcProto;
        ArcTextLayout arcLayout;
        Element rarityChip;
        Element lvLabel;
        Element nameLabel;
        Element titleLabel;
        Element atkLabel;
        Element hpLabel;
        Element defLabel;
        Element critLabel;
        Element affLabel;
        ProgressBar affBar;
        Element loreLabel;

        string selectedHeroId;

        public HeroesController(SolsticeApp app, SolsticeGameData data, Document doc)
        {
            this.app = app;
            this.data = data;
            this.doc = doc;
            selectedHeroId = data.ActiveHeroId;

            stageCutout = doc.Q<Image>("hero-stage-cutout");
            arcHost = doc.Q("arc-ribbon-host");
            arcSource = doc.Q("arc-ribbon-source");
            arcProto = doc.Q("arc-ribbon-proto");
            rarityChip = doc.Q("hero-detail-rarity");
            lvLabel = doc.Q("hero-detail-lv");
            nameLabel = doc.Q("hero-detail-name");
            titleLabel = doc.Q("hero-detail-title");
            atkLabel = doc.Q("hero-stat-atk");
            hpLabel = doc.Q("hero-stat-hp");
            defLabel = doc.Q("hero-stat-def");
            critLabel = doc.Q("hero-stat-crit");
            affLabel = doc.Q("hero-affinity-text");
            affBar = doc.Q<ProgressBar>("hero-affinity-bar");
            if (affBar != null) { affBar.LowValue = 0f; affBar.HighValue = 100f; }
            loreLabel = doc.Q("hero-detail-lore");

            BindHero("aurelia");
            BindHero("kael");
            BindHero("lyra");
            BindHero("veyra");

            BindFx("fx-mode-foil", "fx-foil");
            BindFx("fx-mode-shine", "fx-shine");
            BindFx("fx-mode-halo", "fx-halo");
            BindFx("fx-mode-dissolve", "fx-dissolve");

            BindClick("btn-hero-upgrade", () =>
            {
                if (data.UpgradeHero(selectedHeroId, out var hero))
                {
                    app.PlaySound("sfx-skill");
                    app.ShowToast($"{hero.NameCn} 晋升至 Lv.{hero.Level}！");
                    app.RefreshTopHud();
                    Refresh();
                }
                else
                {
                    app.ShowToast("已达等级上限或金币不足");
                }
            });

            BindClick("btn-hero-deploy", () =>
            {
                data.ActiveHeroId = selectedHeroId;
                app.PlaySound("sfx-click");
                app.ShowToast($"已将 {data.GetHero(selectedHeroId).NameCn} 设为旅团主将");
                app.RefreshLobbyIfLoaded();
            });

            EnsureArcText();
            Refresh();
        }

        void BindClick(string buttonId, Action cb)
        {
            var btn = doc.Q<Button>(buttonId);
            if (btn == null) return;
            btn.Clicked += cb;
            subs.Add(new DisposableAction(() => btn.Clicked -= cb));
        }

        void BindHero(string id)
        {
            BindClick("hero-card-" + id, () =>
            {
                selectedHeroId = id;
                app.PlaySound("sfx-click");
                Refresh();
            });
        }

        void BindFx(string btnId, string fxClass)
        {
            BindClick(btnId, () =>
            {
                SelectFxMode(fxClass);
                app.PlaySound("sfx-click");
            });
        }

        public void SelectFxMode(string fxClass)
        {
            if (stageCutout != null)
            {
                stageCutout.RemoveFromClassList("fx-foil");
                stageCutout.RemoveFromClassList("fx-shine");
                stageCutout.RemoveFromClassList("fx-halo");
                stageCutout.RemoveFromClassList("fx-dissolve");
                stageCutout.AddToClassList(fxClass);
            }
            string[] modes = { "fx-foil", "fx-shine", "fx-halo", "fx-dissolve" };
            string[] btnIds = { "fx-mode-foil", "fx-mode-shine", "fx-mode-halo", "fx-mode-dissolve" };
            for (var i = 0; i < modes.Length; i++)
            {
                var btn = doc.Q(btnIds[i]);
                btn?.EnableInClassList("is-active", modes[i] == fxClass);
            }
        }

        public void EnsureArcText()
        {
            if (arcHost == null || arcSource == null || arcProto == null || arcLayout != null) return;
            doc.SynchronizeLayout();
            arcLayout = new ArcTextLayout(arcHost, arcSource, arcProto);
        }

        public void Refresh()
        {
            if (doc == null || !doc.IsLoaded) return;
            var hero = data.GetHero(selectedHeroId);
            if (stageCutout != null) stageCutout.Source = hero.CutoutPath;
            if (rarityChip != null) rarityChip.Text = $"{hero.Rarity} · {hero.Role}";
            if (lvLabel != null) lvLabel.Text = $"Lv.{hero.Level} / {hero.MaxLevel}";
            if (nameLabel != null) nameLabel.Text = $"{hero.NameCn} · {hero.NameEn}";
            if (titleLabel != null) titleLabel.Text = hero.Title;
            if (atkLabel != null) atkLabel.Text = $"{hero.Atk:N0}";
            if (hpLabel != null) hpLabel.Text = $"{hero.Hp:N0}";
            if (defLabel != null) defLabel.Text = $"{hero.Def:N0}";
            if (critLabel != null) critLabel.Text = $"{Mathf.RoundToInt(hero.CritRate * 100f)}%";
            var affPct = Mathf.RoundToInt(hero.Affinity * 100f);
            if (affLabel != null) affLabel.Text = $"{affPct}%";
            if (affBar != null) affBar.SetValueWithoutNotify(affPct);
            if (loreLabel != null) loreLabel.Text = hero.Lore;

            string[] ids = { "aurelia", "kael", "lyra", "veyra" };
            for (var i = 0; i < ids.Length; i++)
            {
                var card = doc.Q("hero-card-" + ids[i]);
                card?.EnableInClassList("is-selected", ids[i] == selectedHeroId);
            }

            if (arcSource != null)
            {
                arcSource.Text = $"SOLSTICE · {hero.NameEn} VANGUARD";
                doc.SynchronizeLayout();
            }
        }

        public void Dispose()
        {
            arcLayout?.Dispose();
            subs.Dispose();
        }
    }
}
