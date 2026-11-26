using URUI.Controls;

namespace URUI.Samples.Tideglass
{
    public sealed partial class TideglassDemo
    {
        int draftSlot, drill;
        bool restoreDraft;
        Seal? shopTarget;
        Seal draftSeal;
        FlightOrder draftOrder;

        void BindLoadout()
        {
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                Bind("preset-" + i, () => InspectPreset(index));
                Bind("order-" + i, () => { draftOrder = (FlightOrder)index; DrawPreset(); Animate("Calibrate"); });
            }
            for (int i = 0; i < TideglassCatalog.Seals.Length; i++)
            {
                var seal = (Seal)i;
                Bind("socket-" + i, () => { draftSeal = seal; DrawPreset(); Animate("Calibrate"); });
            }
            Bind("apply-preset", () =>
            {
                if (!profile.ApplyPreset(draftSlot, draftSeal, draftOrder)) return;
                DrawPreset();
                PlayAudio(reward);
                Notice("已启用" + SlotName(draftSlot));
                Animate("Latch");
            });
            Bind("get-module", () =>
            {
                shopTarget = draftSeal;
                restoreDraft = true;
                Request(Page.Shop);
            });
            if (restoreDraft)
            {
                restoreDraft = false;
                DrawPreset();
                Animate("Calibrate");
            }
            else InspectPreset(profile.ActivePreset);
        }

        static string SlotName(int i) => i == 0 ? "一号预案" : "二号预案";

        void InspectPreset(int slot)
        {
            draftSlot = slot;
            draftSeal = profile.PresetSeal(slot);
            draftOrder = profile.PresetOrder(slot);
            DrawPreset();
            Animate("Calibrate");
        }

        void SelectKey(string id, bool selected)
        {
            var key = page.Q<Button>(id);
            key.EnableInClassList("selected", selected);
            key.SetAttribute("aria-pressed", selected ? "true" : "false");
        }

        void DrawPreset()
        {
            for (int i = 0; i < 2; i++)
            {
                SelectKey("preset-" + i, i == draftSlot);
                SelectKey("order-" + i, i == (int)draftOrder);
                page.Q("preset-label-" + i).Text = SlotName(i) + (profile.ActivePreset == i ? " · 当前" : "");
            }
            for (int i = 0; i < TideglassCatalog.Names.Length; i++)
            {
                SelectKey("socket-" + i, i == (int)draftSeal);
                page.Q("socket-" + i).EnableInClassList("unowned", !profile.Owns((Seal)i));
            }
            var stats = new TrialSession(draftSeal, escort: profile.Escort);
            page.Q<Image>("plan-art").Source = SealImage(draftSeal);
            page.Q("plan-title").Text = TideglassCatalog.Names[(int)draftSeal];
            page.Q("plan-spec").Text = TideglassCatalog.Descriptions[(int)draftSeal];
            page.Q("plan-order-note").Text = FlightPlan.OrderNotes[(int)draftOrder];
            page.Q("plan-values").Text = $"引潮 +{stats.ChargeChange(0)} 潮能　裂光 {stats.Damage(1)} 伤害";
            bool owns = profile.Owns(draftSeal);
            bool active = draftSlot == profile.ActivePreset && draftSeal == profile.Equipped && draftOrder == profile.Order;
            var apply = page.Q<Button>("apply-preset");
            apply.SetEnabled(owns && !active);
            page.Q("get-module").EnableInClassList("gone", owns);
            apply.Q(className: "control-inner").Text = !owns ? "需先在商城获得" : active ? "当前预案已启用" : "启用这套预案";
        }

        void BindRange()
        {
            for (int i = 0; i < FlightPlan.DrillGoals.Length; i++)
            {
                int index = i;
                Bind("drill-" + i, () => SelectDrill(index));
            }
            Bind("drill-start", () => BeginFlight(drill, true));
            SelectDrill(drill);
        }

        void SelectDrill(int index)
        {
            drill = index;
            for (int i = 0; i < FlightPlan.DrillGoals.Length; i++) SelectKey("drill-" + i, i == drill);
            page.Q<Image>("range-glyph").Source = TideglassAssets.ImageSource("skill-" + (index == 0 ? 2 : index == 1 ? 1 : 0));
            page.Q("drill-title").Text = FlightPlan.DrillNames[index];
            page.Q("drill-goal").Text = FlightPlan.DrillNotes[index];
            int best = profile.DrillBest(index);
            page.Q("drill-best").Text = best > 0 ? "最快 " + TideglassRoute.TimeLabel(best) : "尚无演练记录";
            Animate("Acquire");
        }

        void BindDebrief()
        {
            var report = profile.LastFlight;
            bool hasFlight = report != null;
            page.Q("screen").EnableInClassList("unfiled", !hasFlight);
            page.Q("brief-data").EnableInClassList("gone", !hasFlight);
            page.Q("brief-empty").EnableInClassList("gone", hasFlight);
            var action = page.Q<Button>("brief-go");
            action.Q(className: "control-inner").Text = hasFlight ? "演练本航区" : "打开航线图";
            Bind("brief-go", () =>
            {
                if (hasFlight)
                {
                    drill = report.route;
                    Request(Page.Range);
                }
                else Request(Page.Chart);
            });
            if (!hasFlight) return;
            var destination = TideglassRoute.All[report.route];
            page.Q<Image>("brief-scenery").Source = TideglassAssets.ImageSource(destination.Art);
            page.Q("brief-area").Text = destination.Name;
            page.Q("brief-time").Text = TideglassRoute.TimeLabel(report.milliseconds);
            page.Q("brief-equipment").Text = TideglassCatalog.Names[report.seal] + "　·　" + FlightPlan.Orders[report.order] +
                (report.orderUsed ? " 已使用" : " 未使用");
            page.Q("brief-wing").Text = report.escort == 0 ? "本次独航" : "随航 · " + TideglassWatch.Names[report.escort];
            for (int i = 0; i < 3; i++)
            {
                var bar = page.Q<Image>("damage-bar-" + i);
                bar.FillMode = ImageFillMode.Horizontal;
                bar.FillAmount = (float)report.damage[i] / report.TotalDamage;
                page.Q("damage-read-" + i).Text = $"{report.casts[i]} 次　{report.damage[i]} 伤害";
            }
            Animate("Telemetry");
        }
    }
}
