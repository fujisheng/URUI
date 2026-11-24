using System;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;

namespace Solstice
{
    /// <summary>
    /// Controls the Sanctuary Lobby document: commander status, TextureEffectSurface live shader banner,
    /// interactive vanguard hero switching, and oblique CTA routing.
    /// </summary>
    public sealed class LobbyController : IDisposable
    {
        readonly SolsticeApp app;
        readonly SolsticeGameData data;
        readonly Document doc;
        readonly CompositeDisposable subs = new();

        Element heroName;
        Element heroRole;
        Image heroFigure;
        Image cmdAvatar;
        Element bannerTitle;
        ProgressBar xpBar;

        public LobbyController(SolsticeApp app, SolsticeGameData data, Document doc)
        {
            this.app = app;
            this.data = data;
            this.doc = doc;

            heroName = doc.Q("lobby-hero-name");
            heroRole = doc.Q("lobby-hero-role");
            heroFigure = doc.Q<Image>("lobby-hero-figure");
            cmdAvatar = doc.Q<Image>("lobby-cmd-avatar");
            bannerTitle = doc.Q("lobby-banner-title");
            xpBar = doc.Q<ProgressBar>("lobby-xp-progress");
            if (xpBar != null)
            {
                xpBar.LowValue = 0f;
                xpBar.HighValue = 100f;
            }

            BindLead("lobby-lead-aurelia", "aurelia");
            BindLead("lobby-lead-kael", "kael");
            BindLead("lobby-lead-lyra", "lyra");
            BindLead("lobby-lead-veyra", "veyra");

            BindClick("btn-lobby-battle", () => app.NavigateTo("combat"));
            BindClick("btn-lobby-inspect", () => app.NavigateTo("heroes"));
            BindClick("btn-lobby-summon", () => app.NavigateTo("summon"));
            BindClick("btn-lobby-armory", () => app.NavigateTo("armory"));

            Refresh();
        }

        void BindClick(string buttonId, Action cb)
        {
            var btn = doc.Q<Button>(buttonId);
            if (btn == null) return;
            btn.Clicked += cb;
            subs.Add(new DisposableAction(() => btn.Clicked -= cb));
        }

        void BindLead(string buttonId, string heroId)
        {
            BindClick(buttonId, () =>
            {
                data.ActiveHeroId = heroId;
                app.PlaySound("sfx-click");
                Refresh();
            });
        }

        public void Refresh()
        {
            if (doc == null || !doc.IsLoaded) return;
            var hero = data.GetHero(data.ActiveHeroId);

            if (heroName != null) heroName.Text = $"{hero.NameCn} · {hero.NameEn}";
            if (heroRole != null) heroRole.Text = $"{hero.Rarity} · {hero.Title}";
            if (heroFigure != null) heroFigure.Source = hero.CutoutPath;
            if (cmdAvatar != null) cmdAvatar.Source = hero.PortraitPath;
            if (bannerTitle != null) bannerTitle.Text = data.FlagshipTitle;
            if (xpBar != null) xpBar.SetValueWithoutNotify(78.4f);

            string[] ids = { "aurelia", "kael", "lyra", "veyra" };
            for (var i = 0; i < ids.Length; i++)
            {
                var btn = doc.Q("lobby-lead-" + ids[i]);
                btn?.EnableInClassList("is-selected", ids[i] == data.ActiveHeroId);
            }
        }

        public void Dispose()
        {
            subs.Dispose();
        }
    }
}
