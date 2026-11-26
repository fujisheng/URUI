using System;
using System.Globalization;
using UnityEngine;
using URUI.Animation;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using URUI.Hosting;

namespace URUI.Samples.Tideglass
{
    internal interface ITrialBurst : IDisposable
    {
        void Fire(int skill);
        void Stop();
    }

    internal sealed class TideglassTrial : IDisposable
    {
        internal static Func<TideglassAssets, Surface, Transform, ITrialBurst> CreateBurst;
        internal static readonly string[] Waves = { "shock", "slash", "shock-violet" };

        readonly TrialSession state;
        readonly int route;
        readonly bool training;
        readonly Button orderKey;
        readonly Element orderState;
        SequencePlayer orderMotion;
        readonly Element enemyPhase;
        readonly Element outerShell, innerShell, eclipseVane;
        readonly TideglassProfile profile;
        readonly TideglassAssets assets;
        readonly Document page;
        readonly Action<int, double> onWin;
        readonly Action<AudioClip> play;
        readonly AudioClip castSound;
        readonly AudioClip bellSound;
        readonly Button[] buttons = new Button[3];
        readonly Image[] cooldowns = new Image[3];
        readonly Element[] counts = new Element[3];
        readonly int[] visibleCounts = { -1, -1, -1 };
        readonly float[] visibleArcs = new float[3];
        readonly Image health;
        readonly Image charge;
        readonly Image pulseImage;
        readonly Image echoImage;
        readonly Element healthText;
        readonly Element chargeText;
        readonly Element damage;
        readonly Element resonance, reserve;
        int lastNeeded = -1;
        bool lastRecharging;
        readonly ITrialBurst burst;
        double startedAt;
        internal bool Armed { get; private set; }
        SequencePlayer motion;
        double finishedAt = double.PositiveInfinity;
        double clearSeconds;
        double hideDamageAt = double.PositiveInfinity;
        bool reducedMotion;
        bool finisherPrimed, orderShown;

        internal TideglassTrial(Document page, TideglassAssets assets, TideglassProfile profile, int route, bool training, bool reducedMotion,
            Transform owner, Action<int, double> onWin, Action<AudioClip> play)
        {
            this.page = page;
            this.assets = assets;
            this.onWin = onWin;
            this.play = play;
            this.reducedMotion = reducedMotion;
            this.profile = profile;
            this.route = route;
            this.training = training;
            state = new TrialSession(profile.Equipped, route, profile.Order, training, profile.Escort);
            page.Q("screen").EnableInClassList("training", training);
            orderKey = page.Q<Button>("issue-order");
            orderKey.Clicked += () => page.Surface.RunAfterEvent(IssueOrder);
            page.Q("order-title").Text = FlightPlan.Orders[(int)profile.Order];
            orderState = page.Q("order-state");
            page.Q<Image>("order-icon").Source = TideglassAssets.ImageSource("order-" + (int)profile.Order);
            if (training)
            {
                page.Q("exit-label").Text = "退出演练";
                page.Q<Button>("back").SetAttribute("aria-label", "退出演练");
            }
            var destination = TideglassRoute.All[route];
            page.Q<Image>("trial-scenery").Source = TideglassAssets.ImageSource(training ? "range" : destination.Art);
            page.Q("enemy-name").Text = training ? FlightPlan.DrillNames[route] : destination.Target;
            enemyPhase = page.Q("enemy-phase");
            outerShell = page.Q("mirror-outer");
            innerShell = page.Q("mirror-inner");
            eclipseVane = page.Q("eclipse-vane");
            eclipseVane.EnableInClassList("gone", route != 2);
            page.Q("screen").AddToClassList("aligning");

            health = page.Q<Image>("health");
            charge = page.Q<Image>("charge");
            pulseImage = page.Q<Image>("pulse-image");
            echoImage = page.Q<Image>("impact-echo");
            healthText = page.Q("enemy-health-value");
            chargeText = page.Q("charge-value");
            damage = page.Q("damage");
            resonance = page.Q("resonance");
            reserve = page.Q("reserve-fill");
            page.Q("screen").EnableInClassList("wave-instrument", page.Effects.TryGet("ui-wave", out _));
            health.FillMode = ImageFillMode.Horizontal;
            charge.FillMode = ImageFillMode.Radial360;
            castSound = TideglassAssets.Get<AudioClip>(TideglassAssets.Root + "Audio/cast");
            bellSound = TideglassAssets.Get<AudioClip>(TideglassAssets.Root + "Audio/bell");

            for (int index = 0; index < buttons.Length; index++)
            {
                int skill = index;
                buttons[skill] = page.Q<Button>($"cast-{skill}");
                buttons[skill].Clicked += () => page.Surface.RunAfterEvent(() => Cast(skill));
                counts[skill] = page.Q($"count-{skill}");
                cooldowns[skill] = page.Q<Image>($"cooldown-{skill}");
                cooldowns[skill].FillMode = ImageFillMode.Radial360;
                cooldowns[skill].FillOrigin = 2;
                cooldowns[skill].FillClockwise = false;
                cooldowns[skill].FillAmount = 0;
            }

            burst = CreateBurst?.Invoke(assets, page.Surface, owner);
            ReflectMeters();
            Tick(page.Surface.UIClock.Now);
        }

        internal void Arm()
        {
            if (Armed) return;
            startedAt = page.Surface.UIClock.Now;
            Armed = true;
            page.Q("screen").RemoveFromClassList("aligning");
            page.SetInputEnabled(true);
        }

        internal void Cast(int skill)
        {
            if (!Armed) return;
            double now = page.Surface.UIClock.Now;
            if (!state.TryCast(skill, now, out int dealt))
                return;

            if (state.Won)
            {
                clearSeconds = now - startedAt;
                if (training) profile.CompleteDrill(route, clearSeconds);
                else profile.Complete(route, clearSeconds, state);
                finishedAt = now + (reducedMotion ? .15 : skill == 2 ? .88 : .78);
            }

            play(skill == 2 ? bellSound : castSound);
            ReflectMeters();
            UpdateAbilities(now);
            motion?.Dispose();
            damage.Text = "−" + dealt.ToString(CultureInfo.InvariantCulture);
            damage.Style.Opacity = reducedMotion ? 1f : null;
            hideDamageAt = now + (reducedMotion ? .68 : .9);
            if (!reducedMotion)
            {
                pulseImage.Source = TideglassAssets.ImageSource(Waves[skill]);
                echoImage.Source = TideglassAssets.ImageSource(skill == 0 ? "shock" : "shock-violet");
                motion = assets.Play(page, "Impact" + skill);
                burst?.Fire(skill);
            }
        }

        internal void IssueOrder()
        {
            if (!Armed) return;
            double now = page.Surface.UIClock.Now;
            if (!state.IssueOrder(now)) return;
            play(bellSound);
            ReflectMeters();
            UpdateAbilities(now);
            if (!reducedMotion)
                orderMotion = assets.Play(page, "OrderPulse");
        }

        void ReflectMeters()
        {
            enemyPhase.Text = training ? FlightPlan.DrillNotes[route] : state.Phase;
            outerShell.EnableInClassList("gone", state.Shells < 2);
            innerShell.EnableInClassList("gone", state.Shells < 1);
            eclipseVane.EnableInClassList("polar", state.ChainToLight);
            health.FillAmount = training ? (float)state.DrillProgress / FlightPlan.DrillGoals[route] : (float)state.Health / state.HealthCap;
            charge.FillAmount = (float)state.Charge / TrialSession.MaxCharge;
            healthText.Text = training ? $"目标 {state.DrillProgress} / {FlightPlan.DrillGoals[route]}" : $"{state.Health} / {state.HealthCap}";
            chargeText.Text = $"{state.Charge} / {TrialSession.MaxCharge}";
            int pct = Math.Min(100, 100 * state.Charge / TrialSession.FinisherCost);
            reserve.Style.Set("width", pct.ToString(CultureInfo.InvariantCulture) + "%");
        }

        void UpdateAbilities(double now)
        {
            int needed = Math.Max(0, TrialSession.FinisherCost - state.Charge);
            bool recharging = state.Remaining(2, now) > 0;
            if (lastNeeded != needed || lastRecharging != recharging)
            {
                resonance.Text = needed > 0 ? $"距潮鸣还差 {needed} 潮能" : recharging ? "潮鸣冷却中" : "潮鸣可释放";
                resonance.EnableInClassList("resonant", needed == 0 && !recharging);
                lastNeeded = needed;
                lastRecharging = recharging;
            }
            bool issue = state.CanIssueOrder(now);
            if (orderKey.EnabledSelf != issue || !orderShown)
            {
                orderShown = true;
                orderKey.SetEnabled(issue);
                orderState.Text = state.OrderUsed ? "已使用" : issue ? "可用 1 次" : profile.Order == FlightOrder.Rewind ? "等待技能冷却" : "潮能已满";
            }
            for (int skill = 0; skill < buttons.Length; skill++)
            {
                double time = state.Remaining(skill, now);
                bool canCast = state.CanCast(skill, now);
                if (canCast != buttons[skill].EnabledSelf)
                    buttons[skill].SetEnabled(canCast);
                float arc = (float)(time / state.Cooldown(skill));
                if (Math.Abs(arc - visibleArcs[skill]) > .006f || (arc == 0 && visibleArcs[skill] != 0))
                {
                    if (arc == 0 && visibleArcs[skill] > 0)
                        buttons[skill].AddToClassList("recovered");
                    else if (arc > 0 && visibleArcs[skill] <= 0)
                        buttons[skill].RemoveFromClassList("recovered");
                    cooldowns[skill].FillAmount = arc;
                    visibleArcs[skill] = arc;
                }
                int count = (int)Math.Ceiling(time);
                if (visibleCounts[skill] != count)
                {
                    counts[skill].Text = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "";
                    visibleCounts[skill] = count;
                }
                if (skill == 2 && finisherPrimed != canCast)
                {
                    finisherPrimed = canCast;
                    buttons[skill].EnableInClassList("primed", canCast);
                }
            }
        }

        internal void Tick(double now)
        {
            UpdateAbilities(now);
            if (now >= hideDamageAt)
            {
                damage.Style.Opacity = null;
                hideDamageAt = double.PositiveInfinity;
            }
            if (now >= finishedAt)
            {
                finishedAt = double.PositiveInfinity;
                onWin(state.Casts, clearSeconds);
            }
        }

        internal void SetReducedMotion(bool value)
        {
            reducedMotion = value;
            if (!value)
                return;
            motion?.Dispose();
            motion = null;
            orderMotion?.Dispose();
            orderMotion = null;
            burst?.Stop();
        }

        public void Dispose()
        {
            motion?.Dispose();
            orderMotion?.Dispose();
            burst?.Dispose();
        }
    }
}
