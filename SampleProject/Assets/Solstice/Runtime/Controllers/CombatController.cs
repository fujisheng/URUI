using System;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Effect.Elements;
using URUI.Elements;

namespace Solstice
{
    /// <summary>
    /// Controls live turn-based Boss Encounter HUD:
    /// Boss HP/Shield progress bars, 4 radial360 cooldown sweep skills,
    /// ember hit bursts, and floating CRIT callout.
    /// </summary>
    public sealed class CombatController : IDisposable
    {
        readonly SolsticeApp app;
        readonly SolsticeGameData data;
        readonly Document doc;
        readonly CompositeDisposable subs = new();

        ProgressBar bossHpBar;
        ProgressBar bossShieldBar;
        ProgressBar squadHpBar;
        ProgressBar squadEnergyBar;
        Element bossHpText;
        Element bossShieldText;
        Element squadHpText;
        Element squadEnergyText;
        Element waveLabel;
        Element turnLabel;
        Element log0;
        Element log1;
        Element dmgCallout;
        Image heroImg;
        Image bossImg;
        ParticlesElement hitParticles;
        Button speedBtn;
        Button autoBtn;

        readonly Image[] cdDiscs = new Image[4];
        readonly Image[] cdRings = new Image[4];
        readonly Element[] cdLabels = new Element[4];

        float dmgTimer;
        float hitFlashTimer;
        float autoCooldown;

        public CombatController(SolsticeApp app, SolsticeGameData data, Document doc)
        {
            this.app = app;
            this.data = data;
            this.doc = doc;

            bossHpBar = doc.Q<ProgressBar>("boss-hp-bar");
            bossShieldBar = doc.Q<ProgressBar>("boss-shield-bar");
            squadHpBar = doc.Q<ProgressBar>("squad-hp-bar");
            squadEnergyBar = doc.Q<ProgressBar>("squad-energy-bar");
            if (bossHpBar != null) { bossHpBar.LowValue = 0f; bossHpBar.HighValue = data.BossMaxHp; }
            if (bossShieldBar != null) { bossShieldBar.LowValue = 0f; bossShieldBar.HighValue = data.BossMaxShield; }
            if (squadHpBar != null) { squadHpBar.LowValue = 0f; squadHpBar.HighValue = data.SquadMaxHp; }
            if (squadEnergyBar != null) { squadEnergyBar.LowValue = 0f; squadEnergyBar.HighValue = 100f; }

            bossHpText = doc.Q("boss-hp-text");
            bossShieldText = doc.Q("boss-shield-text");
            squadHpText = doc.Q("squad-hp-text");
            squadEnergyText = doc.Q("squad-energy-text");
            waveLabel = doc.Q("combat-wave-label");
            turnLabel = doc.Q("combat-turn-label");
            log0 = doc.Q("combat-log-0");
            log1 = doc.Q("combat-log-1");
            dmgCallout = doc.Q("damage-callout");
            heroImg = doc.Q<Image>("combat-hero-img");
            bossImg = doc.Q<Image>("combat-boss-img");
            hitParticles = doc.Q<ParticlesElement>("combat-hit-particles");
            speedBtn = doc.Q<Button>("btn-combat-speed");
            autoBtn = doc.Q<Button>("btn-combat-auto");

            for (var i = 0; i < 4; i++)
            {
                var idx = i;
                BindClick("skill-btn-" + idx, () => CastSkill(idx));
                cdDiscs[i] = doc.Q<Image>("skill-cd-disc-" + idx);
                cdRings[i] = doc.Q<Image>("skill-ring-" + idx);
                cdLabels[i] = doc.Q("skill-cd-text-" + idx);
                ConfigureRadial(cdDiscs[i], false);
                ConfigureRadial(cdRings[i], true);
            }

            BindClick("btn-combat-speed", () =>
            {
                data.BattleSpeed2x = !data.BattleSpeed2x;
                app.PlaySound("sfx-click");
                Refresh();
            });
            BindClick("btn-combat-auto", () =>
            {
                data.AutoBattle = !data.AutoBattle;
                app.PlaySound("sfx-click");
                Refresh();
            });
            BindClick("btn-combat-reset", () =>
            {
                data.ResetBattleCooldownsForDemo();
                app.PlaySound("sfx-click");
                app.ShowToast("全队星能已充能完毕");
                Refresh();
            });

            Refresh();
        }

        void BindClick(string buttonId, Action cb)
        {
            var btn = doc.Q<Button>(buttonId);
            if (btn == null) return;
            btn.Clicked += cb;
            subs.Add(new DisposableAction(() => btn.Clicked -= cb));
        }

        static void ConfigureRadial(Image img, bool clockwise)
        {
            if (img == null) return;
            img.FillMode = ImageFillMode.Radial360;
            img.FillOrigin = 2; // Top
            img.FillClockwise = clockwise;
            img.FillAmount = clockwise ? 1f : 0f;
        }

        public bool CastSkill(int skillIndex)
        {
            if (!data.TriggerSkill(skillIndex, out var skill, out var dmg, out var isCrit))
            {
                app.ShowToast(skill.IsUltimate && data.AstralEnergy < 60 ? "星能不足 60，暂不可释放奥义" : $"{skill.NameCn} 冷却中");
                return false;
            }

            app.PlaySound(isCrit ? "sfx-crit" : "sfx-skill");
            if (hitParticles != null && skillIndex != 2)
            {
                hitParticles.Restart();
            }
            if (dmgCallout != null)
            {
                if (skillIndex == 2)
                {
                    dmgCallout.EnableInClassList("is-crit", false);
                    dmgCallout.Text = "SHIELD +4,500";
                }
                else
                {
                    dmgCallout.EnableInClassList("is-crit", isCrit);
                    dmgCallout.Text = (isCrit ? "CRIT " : "") + $"{dmg:N0}";
                }
                dmgCallout.Style.SetOpacity(1f);
                dmgTimer = 1.15f;
            }
            if (bossImg != null && skillIndex != 2)
            {
                bossImg.AddToClassList("is-hit");
                hitFlashTimer = 0.22f;
            }
            app.RefreshTopHud();
            Refresh();
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (doc == null || !doc.IsLoaded) return;
            data.TickCombat(deltaTime);

            if (dmgTimer > 0f)
            {
                dmgTimer -= deltaTime;
                if (dmgTimer <= 0f && dmgCallout != null)
                {
                    dmgCallout.Style.SetOpacity(0f);
                }
            }
            if (hitFlashTimer > 0f)
            {
                hitFlashTimer -= deltaTime;
                if (hitFlashTimer <= 0f && bossImg != null)
                {
                    bossImg.RemoveFromClassList("is-hit");
                }
            }
            if (data.AutoBattle)
            {
                autoCooldown -= deltaTime;
                if (autoCooldown <= 0f)
                {
                    autoCooldown = data.BattleSpeed2x ? 0.55f : 1.0f;
                    for (var i = 3; i >= 0; i--)
                    {
                        if (data.CombatSkills[i].RemainingCooldown <= 0.05f &&
                            (!data.CombatSkills[i].IsUltimate || data.AstralEnergy >= 60))
                        {
                            CastSkill(i);
                            break;
                        }
                    }
                }
            }

            UpdateCooldownVisuals();
        }

        public void Refresh()
        {
            if (doc == null || !doc.IsLoaded) return;
            var lead = data.GetHero(data.ActiveHeroId);
            if (heroImg != null) heroImg.Source = lead.CutoutPath;

            if (waveLabel != null) waveLabel.Text = $"WAVE {data.WaveIndex}/{data.WaveTotal}";
            if (turnLabel != null) turnLabel.Text = $"ROUND T{data.TurnNumber:00} · 先遣队行动";

            if (bossHpBar != null) bossHpBar.SetValueWithoutNotify(data.BossHp);
            if (bossHpText != null) bossHpText.Text = $"{data.BossHp:N0} / {data.BossMaxHp:N0}";
            if (bossShieldBar != null) bossShieldBar.SetValueWithoutNotify(data.BossShield);
            if (bossShieldText != null) bossShieldText.Text = $"{data.BossShield:N0} / {data.BossMaxShield:N0}";

            if (squadHpBar != null) squadHpBar.SetValueWithoutNotify(data.SquadHp);
            if (squadHpText != null) squadHpText.Text = $"{data.SquadHp:N0} / {data.SquadMaxHp:N0} (+{data.SquadShield:N0})";
            if (squadEnergyBar != null) squadEnergyBar.SetValueWithoutNotify(data.AstralEnergy);
            if (squadEnergyText != null) squadEnergyText.Text = $"{data.AstralEnergy} / 100";

            if (log0 != null && data.CombatLog.Count > 0) log0.Text = data.CombatLog[0];
            if (log1 != null && data.CombatLog.Count > 1) log1.Text = data.CombatLog[1];

            if (speedBtn != null)
            {
                speedBtn.Text = data.BattleSpeed2x ? "2x 倍速" : "1x 速度";
                speedBtn.EnableInClassList("is-active", data.BattleSpeed2x);
            }
            if (autoBtn != null)
            {
                autoBtn.EnableInClassList("is-active", data.AutoBattle);
            }

            UpdateCooldownVisuals();
        }

        void UpdateCooldownVisuals()
        {
            for (var i = 0; i < 4; i++)
            {
                var skill = data.CombatSkills[i];
                var frac = skill.CooldownSeconds > 0f
                    ? Mathf.Clamp01(skill.RemainingCooldown / skill.CooldownSeconds)
                    : 0f;
                if (cdDiscs[i] != null) cdDiscs[i].FillAmount = frac;
                if (cdRings[i] != null) cdRings[i].FillAmount = 1f - frac;
                if (cdLabels[i] != null)
                {
                    cdLabels[i].Text = skill.RemainingCooldown > 0.05f
                        ? $"{skill.RemainingCooldown:F1}s"
                        : string.Empty;
                }
            }
        }

        public void Dispose()
        {
            subs.Dispose();
        }
    }
}
