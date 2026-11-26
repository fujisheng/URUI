using System;
using URUI.Controls;

namespace URUI.Samples.Tideglass
{
    public sealed partial class TideglassDemo
    {
        Wing inspectedWing = Wing.Sounder, dispatchWing = Wing.Sounder;
        int surveyRoute, inspectedTitle;
        bool confirmRecall, waitingReceipt, receiptOpen;
        double nextWatchTick;
        long lastWatchSecond = -1;

        void BindWatchDesk()
        {
            page.Q("rank-label").Text = TideglassWatch.Ranks[profile.Rank];
            page.Q("worn-title").Text = TideglassWatch.Titles[profile.WornTitle];
            page.Q<Image>("passport-seal").Source = TideglassAssets.ImageSource("insignia-" + profile.Rank);
            page.Q("escort-name").Text = profile.Escort == Wing.None ? "尚未随航" : TideglassWatch.Names[(int)profile.Escort] + " 随航";
            if (profile.Escort != Wing.None)
            {
                var portrait = page.Q<Image>("watch-portrait");
                portrait.Source = TideglassAssets.ImageSource("face-" + (int)profile.Escort);
                portrait.AddToClassList("on-watch");
            }
            Bind("crew", () =>
            {
                inspectedWing = profile.Escort == Wing.None ? Wing.Sounder : profile.Escort;
                Request(Page.Crew);
            });
            Bind("dispatch", () => Request(Page.Dispatch));
            Bind("career", () => Request(Page.Career));
            TickWatchClock();
        }

        void BindCrew()
        {
            for (int i = 1; i <= 3; i++)
            {
                var officer = (Wing)i;
                Bind("officer-" + i, () => { inspectedWing = officer; DrawCrew(); Animate("CrewRelief"); });
            }
            Bind("assign-wing", () =>
            {
                var next = profile.Escort == inspectedWing ? Wing.None : inspectedWing;
                if (!profile.AssignWing(next)) return;
                DrawCrew();
                PlayAudio(reward);
                Animate("CrewRelief");
            });
            Bind("crew-sorties", () => Request(Page.Dispatch));
            DrawCrew();
        }

        void DrawCrew()
        {
            int index = (int)inspectedWing;
            page.Q<Image>("officer-portrait").Source = TideglassAssets.ImageSource("officer-" + index);
            page.Q("officer-name").Text = TideglassWatch.Names[index];
            page.Q("officer-role").Text = TideglassWatch.Roles[index];
            page.Q("officer-skill").Text = TideglassWatch.Benefits[index];
            page.Q("officer-specialty").Text = "擅长" + TideglassWatch.Routes[index - 1].Name + "　报酬 +" + TideglassWatch.Routes[index - 1].Bonus;
            bool owned = profile.HasWing(inspectedWing), away = profile.Away?.wing == index;
            for (int i = 1; i <= 3; i++)
            {
                var officer = (Wing)i;
                SelectKey("officer-" + i, index == i);
                page.Q("roll-" + i).Text = OfficerState(officer);
            }
            string caption = !owned ? "点亮首座信标后报到" : away ? "远航中 · 返回后可随航" : profile.Escort == inspectedWing ? "解除随航" : "任命随航";
            ActionKey("assign-wing", caption, owned && !away);
        }

        string OfficerState(Wing officer) => !profile.HasWing(officer) ? "待报到" :
            profile.Away?.wing == (int)officer ? "远航中" : profile.Escort == officer ? "随航中" : "待命";

        void ActionKey(string id, string caption, bool enabled)
        {
            var button = page.Q<Button>(id);
            button.Q(className: "control-inner").Text = caption;
            button.SetEnabled(enabled);
        }

        void BindDispatch()
        {
            confirmRecall = receiptOpen = false;
            Bind("file-receipt", () => { receiptOpen = false; DrawDispatch(); });
            if (profile.Away == null && (!profile.Available(dispatchWing) || profile.Escort == dispatchWing))
                for (int i = 1; i <= 3; i++)
                    if (profile.Available((Wing)i) && profile.Escort != (Wing)i) { dispatchWing = (Wing)i; break; }
            for (int i = 0; i < TideglassWatch.Routes.Length; i++)
            {
                int index = i;
                Bind("survey-" + i, () => { surveyRoute = index; DrawDispatch(); Animate("SignalSweep"); });
            }
            for (int i = 1; i <= 3; i++)
            {
                var officer = (Wing)i;
                Bind("send-wing-" + i, () => { dispatchWing = officer; DrawDispatch(); });
            }
            Bind("dispatch-crew", () => { inspectedWing = dispatchWing; Request(Page.Crew); });
            Bind("dispatch-main", DispatchAction);
            Bind("dispatch-undo", () =>
            {
                if (profile.Away == null) return;
                if (!confirmRecall) { confirmRecall = true; DrawDispatch(); return; }
                if (!profile.Recall(TideglassWatch.UtcNow)) { DrawDispatch(); return; }
                confirmRecall = false;
                DrawDispatch();
            });
            DrawDispatch();
        }

        void DispatchAction()
        {
            if (receiptOpen) return;
            long now = TideglassWatch.UtcNow;
            if (profile.Away == null)
            {
                if (!profile.Dispatch(surveyRoute, dispatchWing, now)) return;
            }
            else
            {
                if (!profile.ReceiveSurvey(now)) return;
                receiptOpen = true;
            }
            confirmRecall = false;
            DrawDispatch();
            PlayAudio(reward);
            Animate("SignalSweep");
        }

        void DrawDispatch()
        {
            long now = TideglassWatch.UtcNow;
            var flight = profile.Away;
            if (flight != null) { surveyRoute = flight.route; dispatchWing = (Wing)flight.wing; }
            var route = TideglassWatch.Routes[surveyRoute];
            bool active = flight != null, open = profile.SurveyOpen(surveyRoute);
            waitingReceipt = active && now >= flight.returnsAt;
            page.Q("dispatch-body").EnableInClassList("gone", receiptOpen);
            page.Q("post-receipt").EnableInClassList("gone", !receiptOpen);
            page.Q("survey-selector").EnableInClassList("gone", receiptOpen);
            if (receiptOpen)
            {
                page.Q("signed-route").Text = TideglassWatch.Routes[profile.LastSurveyRoute].Name;
                page.Q("signed-wing").Text = "执航　" + TideglassWatch.Names[(int)profile.LastSurveyWing];
                page.Q("signed-pay").Text = "潮晶 +" + profile.LastSurveyPay;
            }
            page.Q("screen").EnableInClassList("underway", active);
            page.Q("screen").EnableInClassList("returned", waitingReceipt);
            page.Q<Image>("survey-sky").Source = TideglassAssets.ImageSource(route.Art);
            page.Q("survey-name").Text = route.Name;
            page.Q("survey-brief").Text = route.Brief;
            page.Q("survey-pay").Text = "潮晶 +" + route.Reward(dispatchWing);
            page.Q("survey-length").Text = TideglassWatch.Countdown(route.Seconds);
            string specialty = TideglassWatch.Names[(int)route.Specialist] + " 专长 +" + route.Bonus;
            page.Q("survey-special").Text = dispatchWing == route.Specialist ? specialty + " 已计入" : specialty;
            for (int i = 0; i < TideglassWatch.Routes.Length; i++)
            {
                SelectKey("survey-" + i, surveyRoute == i);
                page.Q<Button>("survey-" + i).SetEnabled(!active || surveyRoute == i);
            }
            for (int i = 1; i <= 3; i++)
            {
                var officer = (Wing)i;
                SelectKey("send-wing-" + i, dispatchWing == officer);
                page.Q<Button>("send-wing-" + i).SetEnabled(!active && profile.Available(officer) && profile.Escort != officer);
                page.Q("send-state-" + i).Text = OfficerState(officer);
            }
            string action = waitingReceipt ? "签收回执" : active ? "航行中" : !open ? "签收一次派遣后开放" : "派出" + TideglassWatch.Names[(int)dispatchWing];
            ActionKey("dispatch-main", action, waitingReceipt || !active && open && profile.Available(dispatchWing) && profile.Escort != dispatchWing);
            var recall = page.Q<Button>("dispatch-undo");
            recall.EnableInClassList("gone", !active || waitingReceipt);
            recall.Q(className: "control-inner").Text = confirmRecall ? "确认撤回 · 无报酬" : "召回舰员";
            page.Q("dispatch-crew").EnableInClassList("gone", active);
            bool receipt = !active && profile.LastSurveyPay > 0;
            page.Q("last-receipt").EnableInClassList("gone", !receipt);
            if (receipt) page.Q("last-receipt").Text = "上次签收　" + TideglassWatch.Routes[profile.LastSurveyRoute].Name + "　+" + profile.LastSurveyPay;
            WriteBalance();
            SetSurveyInstrument(now);
        }

        void TickWatchClock()
        {
            double clock = surface.UIClock.Now;
            if (lastWatchSecond >= 0 && clock < nextWatchTick) return;
            nextWatchTick = clock + .2;
            long now = TideglassWatch.UtcNow;
            if (lastWatchSecond == now) return;
            lastWatchSecond = now;
            var flight = profile.Away;
            if (current == Page.Lobby)
            {
                page.Q("dispatch-state").Text = flight == null ? "可派遣" : now >= flight.returnsAt ? "待签收" : TideglassWatch.Countdown(flight.returnsAt - now);
                page.Q("bridge-flight-track").EnableInClassList("gone", flight == null);
                if (flight != null)
                {
                    var gauge = page.Q<Image>("bridge-flight-fill");
                    gauge.FillMode = ImageFillMode.Horizontal;
                    gauge.FillAmount = 1f - (float)Math.Clamp(flight.returnsAt - now, 0, TideglassWatch.Routes[flight.route].Seconds) / TideglassWatch.Routes[flight.route].Seconds;
                }
                page.Q("dispatch").EnableInClassList("available", flight != null && now >= flight.returnsAt);
            }
            else if (current == Page.Dispatch && flight != null)
            {
                if (!waitingReceipt && now >= flight.returnsAt) DrawDispatch();
                else if (!waitingReceipt) SetSurveyInstrument(now);
            }
        }

        void SetSurveyInstrument(long now)
        {
            var flight = profile.Away;
            var route = TideglassWatch.Routes[surveyRoute];
            long remaining = flight == null ? route.Seconds : Math.Clamp(flight.returnsAt - now, 0, route.Seconds);
            float fraction = flight == null ? 0 : 1f - (float)remaining / route.Seconds;
            var arc = page.Q<Image>("survey-trace");
            arc.FillMode = ImageFillMode.Radial360;
            arc.FillOrigin = 2;
            arc.FillAmount = fraction;
            page.Q("radar-ship").Style.Set("transform", FormattableString.Invariant($"rotate({fraction * 360:0.#}deg)"));
            page.Q("survey-read").Text = receiptOpen ? "回执已签" : flight == null ? "待出港" : remaining == 0 ? "已归港" : TideglassWatch.Countdown(remaining);
        }

        void BindCareer()
        {
            inspectedTitle = profile.WornTitle;
            for (int i = 0; i < TideglassWatch.Titles.Length; i++)
            {
                int index = i;
                Bind("badge-" + i, () => { inspectedTitle = index; DrawCareer(); Animate("MintSeal"); });
            }
            Bind("wear-title", () =>
            {
                if (!profile.WearTitle(inspectedTitle)) return;
                DrawCareer(); PlayAudio(reward); Animate("MintSeal");
            });
            DrawCareer();
        }

        void DrawCareer()
        {
            int rank = profile.Rank;
            page.Q<Image>("rank-medal").Source = TideglassAssets.ImageSource("insignia-" + rank);
            page.Q("career-rank").Text = TideglassWatch.Ranks[rank];
            page.Q("career-merit").Text = rank == 3 ? "航籍满阶　9 / 9" : $"功绩 {profile.Merit} / {(rank + 1) * 3}　下阶 {TideglassWatch.Ranks[rank + 1]}";
            var ink = page.Q<Image>("rank-fill");
            ink.FillMode = ImageFillMode.Horizontal;
            ink.FillAmount = rank == 3 ? 1 : (float)(profile.Merit % 3) / 3;
            page.Q("career-stats").Text = $"信标 {profile.LitCount}/3　演练 {profile.PassedDrills}/3　派遣航区 {profile.SurveyKinds}/3";
            page.Q("title-rule").Text = TideglassWatch.BadgeGoals[inspectedTitle];
            for (int i = 0; i < TideglassWatch.Titles.Length; i++)
            {
                SelectKey("badge-" + i, inspectedTitle == i);
                page.Q("badge-state-" + i).Text = profile.WornTitle == i ? "佩戴中" : profile.HasTitle(i) ? "已获授" : "未获授";
            }
            bool earned = profile.HasTitle(inspectedTitle), worn = inspectedTitle == profile.WornTitle;
            ActionKey("wear-title", worn ? "正在佩戴" : earned ? "佩戴" + TideglassWatch.Titles[inspectedTitle] : "尚未获授", earned && !worn);
        }
    }
}
