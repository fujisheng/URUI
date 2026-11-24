using System;
using System.Collections.Generic;

namespace URUI.Samples.Solstice
{
    /// <summary>
    /// Self-contained Core RPG sample state powering `Samples~/Solstice` (zero URP or Effect dependencies).
    /// </summary>
    public sealed class SolsticeCoreData
    {
        public sealed class SkillInfo
        {
            public string NameCn;
            public string IconPath;
            public float CooldownSeconds;
            public float RemainingCooldown;
            public int Power;
            public int EnergyDelta;
            public bool IsUltimate;
        }

        public sealed class HeroInfo
        {
            public string Id;
            public string Rarity;
            public string Role;
            public string NameCn;
            public string NameEn;
            public string Title;
            public int Level;
            public int MaxLevel;
            public int Atk;
            public int Def;
            public int Hp;
            public float CritRate;
            public float Affinity;
            public string CutoutPath;
            public string PortraitPath;
            public string Lore;
        }

        public sealed class RelicItem
        {
            public string Code;
            public string Name;
            public string Category;
            public string Rarity;
            public string Slot;
            public string StatLine;
            public int Level;
            public string IconPath;
            public bool Locked;
            public bool Equipped;
        }

        public readonly List<HeroInfo> Heroes = new();
        public readonly List<SkillInfo> CombatSkills = new();
        public readonly List<RelicItem> Inventory = new(10000);
        public readonly List<string> CombatLog = new();

        public int Stamina { get; private set; } = 148;
        public int StaminaCap => 160;
        public int Gold { get; private set; } = 486200;
        public int Gems { get; private set; } = 3240;
        public int PityCount { get; private set; } = 42;
        public int PityMax => 80;

        public string ActiveHeroId { get; set; } = "aurelia";
        public string FlagshipTitle { get; set; } = "日冕旅团 · 曦光重燃";

        public int TurnNumber { get; private set; } = 4;
        public int BossHp { get; private set; } = 168400;
        public int BossMaxHp => 240000;
        public int BossShield { get; private set; } = 34000;
        public int BossMaxShield => 60000;
        public int SquadHp { get; private set; } = 18450;
        public int SquadMaxHp => 21200;
        public int SquadShield { get; private set; } = 3200;
        public int AstralEnergy { get; private set; } = 72;
        public int WaveIndex { get; private set; } = 2;
        public int WaveTotal => 3;
        public bool BattleSpeed2x { get; set; }
        public bool AutoBattle { get; set; }

        readonly Random rng = new(20261125);

        public SolsticeCoreData()
        {
            Heroes.Add(new HeroInfo
            {
                Id = "aurelia", Rarity = "SSR", Role = "圣殿骑士 · 先锋", NameCn = "艾蕾莉亚", NameEn = "AURELIA",
                Title = "曦光圣骑 · 第七旅团长", Level = 72, MaxLevel = 80, Atk = 2840, Def = 1620, Hp = 18400, CritRate = 0.54f, Affinity = 0.86f,
                CutoutPath = "/solstice-core/Images/hero-aurelia.png", PortraitPath = "/solstice-core/Images/port-aurelia.png",
                Lore = "持星辉圣枪镇守日冕回廊的前线统领，能在护盾破防瞬间号令全队发动连携强袭。"
            });
            Heroes.Add(new HeroInfo
            {
                Id = "kael", Rarity = "SR", Role = "影刃斥候 · 突袭", NameCn = "凯尔", NameEn = "KAEL",
                Title = "夜潮斥候 · 断界双刃", Level = 65, MaxLevel = 80, Atk = 2590, Def = 980, Hp = 13200, CritRate = 0.68f, Affinity = 0.72f,
                CutoutPath = "/solstice-core/Images/hero-kael.png", PortraitPath = "/solstice-core/Images/port-kael.png",
                Lore = "穿梭于圣殿暗巷的孤影刺客，以双刃星纹削减敌方韧性护盾，擅长高速连段。"
            });
            Heroes.Add(new HeroInfo
            {
                Id = "lyra", Rarity = "SSR", Role = "星枢魔导 · 控场", NameCn = "莱拉", NameEn = "LYRA",
                Title = "星穹织秘者 · 时序司仪", Level = 70, MaxLevel = 80, Atk = 3120, Def = 1140, Hp = 14800, CritRate = 0.49f, Affinity = 0.91f,
                CutoutPath = "/solstice-core/Images/hero-lyra.png", PortraitPath = "/solstice-core/Images/port-lyra.png",
                Lore = "以古黄铜浑仪校准星辰轨迹的大魔导师，召来星穹漩涡重构友军技能冷却。"
            });
            Heroes.Add(new HeroInfo
            {
                Id = "veyra", Rarity = "SSR", Role = "烬火战姬 · 破阵", NameCn = "薇拉", NameEn = "VEYRA",
                Title = "炽焰女武神 · 焚天戟翼", Level = 68, MaxLevel = 80, Atk = 3280, Def = 1280, Hp = 16100, CritRate = 0.61f, Affinity = 0.64f,
                CutoutPath = "/solstice-core/Images/hero-veyra.png", PortraitPath = "/solstice-core/Images/port-veyra.png",
                Lore = "身披凤凰金羽重甲的突击女武神，长戟横扫间引爆灰烬火环，瓦解巨像防御。"
            });

            CombatSkills.Add(new SkillInfo { NameCn = "晨曦贯刺", IconPath = "/solstice-core/Images/icon-skill-1.png", CooldownSeconds = 1.6f, RemainingCooldown = 0f, Power = 9400, EnergyDelta = 16 });
            CombatSkills.Add(new SkillInfo { NameCn = "星穹漩涡", IconPath = "/solstice-core/Images/icon-skill-2.png", CooldownSeconds = 3.5f, RemainingCooldown = 1.4f, Power = 15800, EnergyDelta = 24 });
            CombatSkills.Add(new SkillInfo { NameCn = "圣盾庇护", IconPath = "/solstice-core/Images/icon-skill-3.png", CooldownSeconds = 5.0f, RemainingCooldown = 0f, Power = 6200, EnergyDelta = 14 });
            CombatSkills.Add(new SkillInfo { NameCn = "日冕裁决", IconPath = "/solstice-core/Images/icon-skill-4.png", CooldownSeconds = 7.0f, RemainingCooldown = 2.6f, Power = 28500, EnergyDelta = -60, IsUltimate = true });

            CombatLog.Add("T04 · 灰烬巨像开启日冕护盾，全队蓄能 72%");
            CombatLog.Add("T03 · 艾蕾莉亚【晨曦贯刺】触发暴击 +14,820");

            string[] cats = { "Weapon", "Relic", "Crest", "Stone" };
            string[] slots = { "主手圣器", "星盘核心", "护甲纹章", "日冕秘石" };
            string[] prefixes = { "日冕", "星穹", "曜金", "夜潮", "炽焰", "圣殿", "苍穹", "残晖" };
            string[] nouns = { "裁决剑", "观测仪", "辉光章", "共鸣核", "断界刃", "守护印", "星图录", "火凤羽" };
            string[] icons =
            {
                "/solstice-core/Images/icon-item-1.png",
                "/solstice-core/Images/icon-item-2.png",
                "/solstice-core/Images/icon-item-3.png",
                "/solstice-core/Images/icon-item-4.png"
            };
            for (var i = 0; i < 10000; i++)
            {
                var catIdx = i % 4;
                var tierRoll = i % 10;
                var rarity = tierRoll == 0 ? "SSR" : (tierRoll <= 3 ? "SR" : "R");
                var lvl = 1 + (i * 7 % 20);
                var power = (rarity == "SSR" ? 960 : rarity == "SR" ? 640 : 380) + lvl * 28 + (i % 45);
                Inventory.Add(new RelicItem
                {
                    Code = $"SOL-{i + 1:00000}",
                    Name = $"{prefixes[(i / 4) % prefixes.Length]}{nouns[(i * 3 + catIdx) % nouns.Length]} #{i + 1}",
                    Category = cats[catIdx],
                    Rarity = rarity,
                    Slot = slots[catIdx],
                    StatLine = $"PWR +{power} · Lv.{lvl}",
                    Level = lvl,
                    IconPath = icons[catIdx],
                    Locked = i % 9 == 0,
                    Equipped = i < 4
                });
            }
        }

        public HeroInfo GetHero(string id)
        {
            for (var i = 0; i < Heroes.Count; i++) if (Heroes[i].Id == id) return Heroes[i];
            return Heroes[0];
        }

        public bool UpgradeHero(string id, out HeroInfo hero)
        {
            hero = GetHero(id);
            if (hero.Level >= hero.MaxLevel || Gold < 4800) return false;
            Gold -= 4800;
            hero.Level += 1;
            hero.Atk += 46;
            hero.Def += 22;
            hero.Hp += 260;
            return true;
        }

        public bool TriggerSkill(int idx, out SkillInfo skill, out int dmg, out bool isCrit)
        {
            skill = CombatSkills[idx];
            dmg = 0;
            isCrit = false;
            if (skill.RemainingCooldown > 0.05f) return false;
            if (skill.IsUltimate && AstralEnergy < 60) return false;

            skill.RemainingCooldown = skill.CooldownSeconds;
            TurnNumber++;
            if (idx == 2)
            {
                SquadShield = Math.Min(10000, SquadShield + 4500);
                SquadHp = Math.Min(SquadMaxHp, SquadHp + 1850);
                AstralEnergy = Math.Min(100, AstralEnergy + skill.EnergyDelta);
                CombatLog.Insert(0, $"T{TurnNumber:00} · 释放【{skill.NameCn}】全队护盾 +4,500");
            }
            else
            {
                isCrit = skill.IsUltimate || rng.NextDouble() < 0.42;
                dmg = (int)(skill.Power * (isCrit ? 1.45 : 1.0) * (0.94 + rng.NextDouble() * 0.14));
                BossShield = Math.Max(0, BossShield - Math.Min(BossShield, (int)(dmg * 0.65)));
                BossHp -= dmg;
                if (BossHp <= 0)
                {
                    WaveIndex = WaveIndex >= WaveTotal ? 1 : WaveIndex + 1;
                    BossHp = BossMaxHp;
                    BossShield = BossMaxShield;
                    Gold += 6500;
                    CombatLog.Insert(0, $"T{TurnNumber:00} · 击溃灰烬巨像！进入波次 {WaveIndex}/{WaveTotal}");
                }
                else
                {
                    CombatLog.Insert(0, $"T{TurnNumber:00} · {skill.NameCn} 造成 {(isCrit ? "暴击 " : "")}{dmg:N0} 伤害");
                }
                AstralEnergy = Math.Max(0, Math.Min(100, AstralEnergy + skill.EnergyDelta));
            }
            while (CombatLog.Count > 3) CombatLog.RemoveAt(CombatLog.Count - 1);
            return true;
        }

        public void TickCombat(float dt)
        {
            var mult = BattleSpeed2x ? 2f : 1f;
            for (var i = 0; i < CombatSkills.Count; i++)
            {
                if (CombatSkills[i].RemainingCooldown > 0f)
                {
                    CombatSkills[i].RemainingCooldown = Math.Max(0f, CombatSkills[i].RemainingCooldown - dt * mult);
                }
            }
        }

        public HeroInfo RollSummon()
        {
            if (Gems >= 160) Gems -= 160;
            PityCount++;
            HeroInfo picked;
            if (PityCount >= PityMax || rng.NextDouble() < 0.35)
            {
                PityCount = 0;
                HeroInfo[] ssrs = { Heroes[0], Heroes[2], Heroes[3] };
                picked = ssrs[rng.Next(ssrs.Length)];
            }
            else
            {
                picked = Heroes[rng.Next(Heroes.Count)];
            }
            picked.Affinity = Math.Min(1f, picked.Affinity + 0.04f);
            return picked;
        }

        public void AddStaminaDemo() => Stamina = Math.Min(StaminaCap, Stamina + 12);
    }
}
