using System;
using UnityEngine;
using URUI.Animation;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;

namespace Solstice
{
    /// <summary>
    /// Controls the Summon Astrolabe Altar screen:
    /// multi-track SequencePlayer card reveal + star particle track + pity bar tracking.
    /// </summary>
    public sealed class SummonController : IDisposable
    {
        readonly SolsticeApp app;
        readonly SolsticeGameData data;
        readonly Document doc;
        readonly CompositeDisposable subs = new();

        Image cardImg;
        Element resultName;
        Element resultSub;
        Element statusBadge;
        Element pityText;
        ProgressBar pityBar;
        SequencePlayer sequencePlayer;
        bool flippedToHero;

        public SummonController(SolsticeApp app, SolsticeGameData data, Document doc)
        {
            this.app = app;
            this.data = data;
            this.doc = doc;

            cardImg = doc.Q<Image>("summon-card-img");
            resultName = doc.Q("summon-result-name");
            resultSub = doc.Q("summon-result-sub");
            statusBadge = doc.Q("summon-status-badge");
            pityText = doc.Q("summon-pity-text");
            pityBar = doc.Q<ProgressBar>("summon-pity-bar");
            if (pityBar != null) { pityBar.LowValue = 0f; pityBar.HighValue = data.PityMax; }

            BindClick("btn-summon-once", TriggerSummon);
            BindClick("btn-summon-reset", ResetToCardBack);
            BindClick("btn-summon-to-heroes", () => app.NavigateTo("heroes"));

            Refresh();
        }

        void BindClick(string buttonId, Action cb)
        {
            var btn = doc.Q<Button>(buttonId);
            if (btn == null) return;
            btn.Clicked += cb;
            subs.Add(new DisposableAction(() => btn.Clicked -= cb));
        }

        public void TriggerSummon()
        {
            doc.SynchronizeLayout();
            EnsureSequencePlayer();

            var rolled = data.RollSummon(out var pitied);
            app.PlaySound("sfx-summon-ssr");
            if (cardImg != null) cardImg.Source = rolled.CutoutPath;
            flippedToHero = true;

            if (resultName != null) resultName.Text = $"召唤成功：{rolled.NameCn} ({rolled.Rarity})";
            if (resultSub != null) resultSub.Text = pitied
                ? $"触发 80 抽保底！羁绊同步率提升至 {Mathf.RoundToInt(rolled.Affinity * 100f)}%"
                : $"{rolled.Title} · 羁绊同步率提升至 {Mathf.RoundToInt(rolled.Affinity * 100f)}%";
            if (statusBadge != null) statusBadge.Text = $"{rolled.Rarity} 共鸣响应";

            if (sequencePlayer != null)
            {
                sequencePlayer.Seek(0f);
                sequencePlayer.Play();
            }
            app.RefreshTopHud();
            Refresh();
        }

        public void ResetToCardBack()
        {
            flippedToHero = false;
            if (cardImg != null) cardImg.Source = "/solstice/Images/card-back.png";
            if (resultName != null) resultName.Text = "点击占卜唤醒沉睡英灵";
            if (resultSub != null) resultSub.Text = "序列轨迹多轨道 · 粒子星雨 · 天体卡牌翻转";
            if (statusBadge != null) statusBadge.Text = "星象星盘已就绪";
            sequencePlayer?.Stop();
        }

        void EnsureSequencePlayer()
        {
            if (sequencePlayer != null || doc.DocumentElement == null || !doc.DocumentElement.IsVisibleInHierarchy) return;
            var json = Resources.Load<TextAsset>("URUI/Solstice/Sequences/summon.urui-sequence");
            if (!json) return;
            var def = SequenceDefinition.Parse(json.text);
            sequencePlayer = new SequencePlayer(doc.DocumentElement, def);
        }

        public void SeekDemoSample(double time)
        {
            doc.SynchronizeLayout();
            EnsureSequencePlayer();
            sequencePlayer?.Seek(time);
        }

        public void Refresh()
        {
            if (doc == null || !doc.IsLoaded) return;
            if (pityText != null) pityText.Text = $"{data.PityCount} / {data.PityMax}";
            if (pityBar != null) pityBar.SetValueWithoutNotify(data.PityCount);
            if (!flippedToHero && cardImg != null)
            {
                cardImg.Source = "/solstice/Images/card-back.png";
            }
        }

        public void Dispose()
        {
            sequencePlayer?.Dispose();
            subs.Dispose();
        }
    }
}
