using System;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;

namespace Solstice
{
    /// <summary>
    /// Controls Settings Modal: real-time CSS theme toggling (`theme-obsidian`, `theme-crimson`,
    /// `theme-alabaster`), audio volume Slider, reduced motion Toggle, and Lobby banner text sync.
    /// </summary>
    public sealed class SettingsController : IDisposable
    {
        readonly SolsticeApp app;
        readonly SolsticeGameData data;
        readonly Document doc;
        readonly CompositeDisposable subs = new();

        Slider volumeSlider;
        Element volumeVal;
        Toggle reducedMotionToggle;
        TextField bannerInput;

        public SettingsController(SolsticeApp app, SolsticeGameData data, Document doc)
        {
            this.app = app;
            this.data = data;
            this.doc = doc;

            volumeSlider = doc.Q<Slider>("settings-volume");
            volumeVal = doc.Q("settings-volume-val");
            reducedMotionToggle = doc.Q<Toggle>("settings-reduced-motion");
            bannerInput = doc.Q<TextField>("settings-banner-input");

            BindTheme("theme-obsidian");
            BindTheme("theme-crimson");
            BindTheme("theme-alabaster");

            var closeBtn = doc.Q<Button>("btn-settings-close");
            if (closeBtn != null)
            {
                Action onClose = () => app.SetSettingsOpen(false);
                closeBtn.Clicked += onClose;
                subs.Add(new DisposableAction(() => closeBtn.Clicked -= onClose));
            }

            if (volumeSlider != null)
            {
                subs.Add(volumeSlider.RegisterValueChangedCallback(evt =>
                {
                    app.SetVolume(evt.NewValue / 100f);
                    if (volumeVal != null) volumeVal.Text = $"{Mathf.RoundToInt(evt.NewValue)}%";
                }));
            }

            if (reducedMotionToggle != null)
            {
                subs.Add(reducedMotionToggle.RegisterValueChangedCallback(evt =>
                {
                    app.SetReducedMotion(evt.NewValue);
                }));
            }

            var applyBtn = doc.Q<Button>("btn-settings-apply");
            if (applyBtn != null)
            {
                Action onApply = () =>
                {
                    var text = bannerInput != null ? bannerInput.Value?.Trim() : null;
                    if (!string.IsNullOrEmpty(text))
                    {
                        data.FlagshipTitle = text;
                        app.RefreshLobbyIfLoaded();
                    }
                    app.PlaySound("sfx-skill");
                    app.ShowToast("系统配置已保存并同步");
                };
                applyBtn.Clicked += onApply;
                subs.Add(new DisposableAction(() => applyBtn.Clicked -= onApply));
            }

            Refresh();
        }

        void BindTheme(string themeClass)
        {
            var btn = doc.Q<Button>(themeClass);
            if (btn == null) return;
            Action cb = () =>
            {
                app.ApplyTheme(themeClass);
                app.PlaySound("sfx-click");
                Refresh();
            };
            btn.Clicked += cb;
            subs.Add(new DisposableAction(() => btn.Clicked -= cb));
        }

        public void Refresh()
        {
            if (doc == null || !doc.IsLoaded) return;
            string[] themes = { "theme-obsidian", "theme-crimson", "theme-alabaster" };
            for (var i = 0; i < themes.Length; i++)
            {
                var btn = doc.Q(themes[i]);
                btn?.EnableInClassList("is-active", themes[i] == app.CurrentTheme);
            }
            if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(Mathf.RoundToInt(app.Volume * 100f));
            if (volumeVal != null) volumeVal.Text = $"{Mathf.RoundToInt(app.Volume * 100f)}%";
            if (reducedMotionToggle != null) reducedMotionToggle.SetValueWithoutNotify(app.ReducedMotion);
            if (bannerInput != null) bannerInput.SetValueWithoutNotify(data.FlagshipTitle);
        }

        public void Dispose()
        {
            subs.Dispose();
        }
    }
}
