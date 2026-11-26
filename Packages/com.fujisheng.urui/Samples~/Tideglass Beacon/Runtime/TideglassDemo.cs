using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using URUI.Animation;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using URUI.Hosting;

namespace URUI.Samples.Tideglass
{
    /// <summary>Owns the showcase documents and the local guest session.</summary>
    [RequireComponent(typeof(Surface), typeof(AudioSource))]
    public sealed partial class TideglassDemo : MonoBehaviour
    {
        enum Page { Login, Loading, Lobby, Shop, Bag, Hero, Chart, Logbook, Loadout, Range, Debrief, Trial, Crew, Dispatch, Career }

        int selectedRoute;
        int flownRoute;
        bool practiceFlight;
        bool showRecords;
        readonly List<Page> voyageTrail = new();

        static readonly string[] StationImages = { "operations", "pilot", "module-0", "module-1", "module-2", "lens" };
        static readonly string[] TrialImages = { "reactor", "shock", "slash", "shock-violet", "skill-0", "skill-1", "skill-2" };

        Surface surface;
        AudioSource audioSource;
        AudioClip tap;
        AudioClip reward;
        TideglassAssets assets;
        TideglassProfile profile;
        Document page;
        Document result;
        Document transition;
        SequencePlayer transitionMotion;
        Coroutine transitionRoutine;
        bool transitionSwapped;
        SequencePlayer pageMotion;
        SequencePlayer resultMotion;
        TideglassTrial trial;
        TideglassOrbit orbit;
        TideglassPortraitCard crewPortrait;
        TideglassKeyDeck pageKeys, resultKeys;
        internal static Action<Document> RegisterInstrumentEffects;
        Seal offeredSeal;
        Element diagnostics;
        Page current;
        Seal inspectedSeal;
        int inspectedSkill;
        bool reducedMotion;
        bool showDiagnostics;
        double nextDiagnosticsAt;
        double noticeUntil;

        void OnEnable()
        {
            surface = GetComponent<Surface>();
            audioSource = GetComponent<AudioSource>();
            assets = new TideglassAssets();
            tap = TideglassAssets.Get<AudioClip>(TideglassAssets.Root + "Audio/tap");
            reward = TideglassAssets.Get<AudioClip>(TideglassAssets.Root + "Audio/reward");
            ResizeSurface();
            profile = TideglassSave.Load();
            selectedRoute = profile.LatestOpen;
            page = assets.CreateDocument(surface, transform, "Tideglass Page", opticalGlass: true);
            RegisterInstrumentEffects?.Invoke(page);
            Open(Page.Login);
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
                crewPortrait?.Reset();
        }

        void Update()
        {
            if (!page || !page.IsLoaded)
                return;

            ResizeSurface();
            if (transition && (!transitionSwapped || trial is { Armed: false }))
                return;

            double now = surface.UIClock.Now;
            if (result && result.IsLoaded)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    Request(practiceFlight ? Page.Range : Page.Lobby);
                return;
            }

            if (Input.GetKeyDown(KeyCode.F1))
                ToggleDiagnostics();
            if (Input.GetKeyDown(KeyCode.Escape))
                Back();
            if (current is Page.Lobby or Page.Dispatch) TickWatchClock();
            if (current is Page.Shop or Page.Bag)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) StepOrbit(-1);
                if (Input.GetKeyDown(KeyCode.RightArrow)) StepOrbit(1);
            }
            if (trial != null)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) trial.Cast(0);
                if (Input.GetKeyDown(KeyCode.Alpha2)) trial.Cast(1);
                if (Input.GetKeyDown(KeyCode.Alpha3)) trial.Cast(2);
                if (Input.GetKeyDown(KeyCode.Alpha4)) trial.IssueOrder();
                trial.Tick(now);
            }
            if (noticeUntil > 0 && now >= noticeUntil)
            {
                noticeUntil = 0;
                page.Q("notice")?.Style.Remove("opacity");
            }
            if (showDiagnostics && now >= nextDiagnosticsAt)
            {
                var stats = surface.GetRenderingStatistics();
                diagnostics.Text = $"URUI  |  docs {stats.LoadedDocuments}  |  commands {stats.NativePlanCommandCount}  |  draws {stats.RecordedGeometryDraws}  |  fx {stats.RecordedExternalGeometryDraws}";
                nextDiagnosticsAt = now + .28;
            }
        }

        void ResizeSurface()
        {
            float aspect = (float)Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height);
            int height = Mathf.Max(720, Mathf.RoundToInt(960 / aspect));
            var size = new Vector2Int(Mathf.RoundToInt(height * aspect), height);
            if (surface.Viewport != size)
                surface.Viewport = size;
        }

        void Request(Page destination) => surface.RunAfterEvent(() => Navigate(destination, () =>
        {
            bool connectedPages = (current, destination) is (Page.Chart, Page.Logbook or Page.Debrief)
                or (Page.Logbook, Page.Chart) or (Page.Debrief, Page.Chart or Page.Range) or (Page.Range, Page.Debrief)
                or (Page.Loadout, Page.Shop) or (Page.Shop, Page.Loadout)
                or (Page.Dispatch, Page.Crew) or (Page.Crew, Page.Dispatch);
            if (connectedPages)
            {
                if (voyageTrail.Count > 0 && voyageTrail[voyageTrail.Count - 1] == destination)
                    voyageTrail.RemoveAt(voyageTrail.Count - 1);
                else
                    voyageTrail.Add(current);
            }
            else
            {
                voyageTrail.Clear();
            }
            if (destination == Page.Chart && current != Page.Logbook)
                inspectedRoute = selectedRoute;
            Open(destination);
        }));

        void Navigate(Page destination, Action swap)
        {
            if (transition)
            {
                if (!transitionSwapped)
                    return;

                StopCoroutine(transitionRoutine);
                ReleaseTransition();
            }

            if (reducedMotion)
                swap();
            else
                transitionRoutine = StartCoroutine(PlayTransition(destination, swap));
        }

        IEnumerator PlayTransition(Page destination, Action swap)
        {
            Document suspended = result ? result : page;
            var (cue, coveredAt) = (current, destination) switch
            {
                (_, Page.Trial) => practiceFlight ? ("RangeGate", .20) : ("Warp", .252),
                (Page.Trial, Page.Lobby) => ("Dock", .23),
                (_, Page.Loadout or Page.Bag) => ("Mount", .19),
                (Page.Loadout or Page.Bag, Page.Lobby) => ("Unseat", .17),
                (_, Page.Hero) or (_, Page.Crew) or (Page.Hero or Page.Crew, Page.Lobby) => ("Focus", .21),
                (_, Page.Logbook or Page.Debrief or Page.Career) => ("PageTurn", .19),
                (Page.Logbook or Page.Debrief or Page.Career, Page.Lobby) => ("FileAway", .16),
                (_, Page.Range) => ("RangeGate", .20),
                (_, Page.Shop) => ("Prism", .185),
                (_, Page.Chart or Page.Dispatch) => ("Atlas", .214),
                _ => ("Transition", .155)
            };
            transition = assets.CreateDocument(surface, transform, "Tideglass Transition", opticalGlass: cue == "Prism");
            try
            {
                TideglassAssets.Load(transition, "Transition");
                transition.DocumentElement.PickingMode = PickingMode.Ignore;
                transition.Body.PickingMode = PickingMode.Ignore;
                transition.SetPresentation(30, 0, true, false);
                transition.SetInputEnabled(false);
                if (cue == "Prism")
                    transition.Q("screen").AddToClassList("with-prisms");
                if (cue == "Focus")
                {
                    var diaphragm = transition.Q<Image>("focus-fan");
                    diaphragm.FillMode = ImageFillMode.Radial360;
                    diaphragm.FillOrigin = 2;
                    diaphragm.FillClockwise = destination is Page.Hero or Page.Crew;
                }
                transitionMotion = assets.Play(transition, cue);
                suspended.SetInputEnabled(false);

                while (transitionMotion.IsPlaying && transitionMotion.Time < coveredAt)
                    yield return null;

                swap();
                transitionSwapped = true;
                while (transitionMotion.IsPlaying)
                    yield return null;
                if (destination == Page.Trial)
                    trial?.Arm();
            }
            finally
            {
                if (!transitionSwapped && suspended && suspended.IsLoaded)
                    suspended.SetInputEnabled(true);
                ReleaseTransition();
            }
        }

        void ReleaseTransition()
        {
            transitionMotion?.Dispose();
            transitionMotion = null;
            transitionRoutine = null;
            transitionSwapped = false;
            if (transition)
            {
                transition.Unload();
                Destroy(transition.gameObject);
                transition = null;
            }
        }

        void Open(Page destination)
        {
            ReleaseResult();
            pageKeys?.Dispose();
            pageKeys = null;
            crewPortrait?.Dispose();
            crewPortrait = null;
            orbit?.Dispose();
            orbit = null;
            trial?.Dispose();
            trial = null;
            pageMotion?.Dispose();
            pageMotion = null;
            noticeUntil = 0;
            diagnostics = null;
            page.Unload();
            current = destination;
            lastWatchSecond = -1;

            TideglassAssets.Load(page, destination.ToString());
            page.SetPresentation(0, 0, true, false);
            page.SetInputEnabled(destination != Page.Trial || !transition);
            ApplyMotionSetting();
            Bind("motion", ToggleMotion);
            Bind("back", Back);

            switch (destination)
            {
                case Page.Login:
                    if (PlayerPrefs.HasKey(TideglassSave.Key))
                        page.Q("login").Q(className: "control-inner").Text = "继续航程";
                    Bind("login", Login);
                    break;
                case Page.Lobby:
                    WriteBalance();
                    page.Q<Image>("loadout-icon").Source = SealImage(profile.Equipped);
                    page.Q("equipped").Text = TideglassCatalog.Names[(int)profile.Equipped];
                    Bind("shop", () => Request(Page.Shop));
                    Bind("bag", () => Request(Page.Bag));
                    Bind("hero", () => Request(Page.Hero));
                    BindVoyageDesk();
                    break;
                case Page.Shop:
                    BindShop();
                    break;
                case Page.Bag:
                    BindBag();
                    break;
                case Page.Hero:
                    BindHero();
                    BindOrbit(null);
                    break;
                case Page.Chart:
                    BindChart();
                    break;
                case Page.Logbook:
                    BindLogbook();
                    break;
                case Page.Loadout:
                    BindLoadout();
                    break;
                case Page.Range:
                    BindRange();
                    break;
                case Page.Debrief:
                    BindDebrief();
                    break;
                case Page.Crew:
                    crewPortrait = new TideglassPortraitCard(page.Q("officer-card"), reducedMotion);
                    BindCrew();
                    break;
                case Page.Dispatch:
                    BindDispatch();
                    break;
                case Page.Career:
                    BindCareer();
                    break;
                case Page.Trial:
                    trial = new TideglassTrial(page, assets, profile, flownRoute, practiceFlight, reducedMotion, transform, ShowResult, PlayAudio);
                    if (!transition) trial.Arm();
                    break;
            }
            pageKeys = new TideglassKeyDeck(page.Body, reducedMotion);
            if (showDiagnostics)
                AddDiagnostics();
        }

        void Bind(string id, Action callback)
        {
            if (page.Q<Button>(id) is Button button)
            {
                button.Clicked += () => surface.RunAfterEvent(() =>
                {
                    PlayAudio(tap);
                    callback();
                });
                if (id == "back")
                    button.SetAttribute("aria-label", current == Page.Trial ? "撤离" : "返回");
            }
        }

        void Login()
        {
            StartLoading(Page.Lobby, StationImages, "连接引潮站");
        }

        void StartLoading(Page destination, string[] images, string label)
        {
            Navigate(Page.Loading, () =>
            {
                Open(Page.Loading);
                StartCoroutine(LoadImages(destination, images, label));
            });
        }

        IEnumerator LoadImages(Page destination, string[] images, string label)
        {
            float startedAt = Time.realtimeSinceStartup;
            float visibleUntil = startedAt + (reducedMotion ? .20f : 1.05f);
            Element status = page.Q("load-status");
            Element value = page.Q("load-value");
            Image meter = page.Q<Image>("load-progress");
            meter.FillMode = ImageFillMode.Radial360;
            meter.FillAmount = 0;
            int lastPercent = 0;
            status.Text = label;
            yield return null;

            for (int index = 0; index < images.Length; index++)
            {
                string name = images[index];
                if (!assets.HasTexture(name))
                {
                    ResourceRequest request = assets.LoadTextureAsync(name);
                    yield return request;
                    assets.KeepTexture(name, request);
                }
                ShowProgress((float)(index + 1) / images.Length);
                yield return null;
            }

            while (Time.realtimeSinceStartup < visibleUntil)
            {
                ShowProgress(1);
                yield return null;
            }

            ShowProgress(1);
            Request(destination);

            void ShowProgress(float loaded)
            {
                float progress = reducedMotion
                    ? loaded
                    : Mathf.Min(loaded, Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / .86f));
                meter.FillAmount = progress;
                int percent = Mathf.RoundToInt(progress * 100);
                if (percent != lastPercent)
                {
                    value.Text = $"{percent}%";
                    lastPercent = percent;
                }
            }
        }

        void Back()
        {
            if (current == Page.Trial && practiceFlight)
                Request(Page.Range);
            else if (!result && (current is Page.Chart or Page.Logbook or Page.Range or Page.Debrief or Page.Shop or Page.Dispatch or Page.Crew) && voyageTrail.Count > 0)
                Request(voyageTrail[voyageTrail.Count - 1]);
            else if (result || current is Page.Shop or Page.Bag or Page.Hero or Page.Chart or Page.Logbook or Page.Loadout or Page.Range or Page.Debrief or Page.Trial or Page.Crew or Page.Dispatch or Page.Career)
                Request(Page.Lobby);
            else if (current == Page.Lobby)
                Request(Page.Login);
        }

        void BindShop()
        {
            offeredSeal = shopTarget ?? profile.Equipped;
            shopTarget = null;
            for (int index = 0; index < TideglassCatalog.Names.Length; index++)
            {
                Seal seal = (Seal)index;
                Bind($"offer-{index}", () => SelectOffer(seal));
                Bind($"buy-{index}", () => Buy(seal));
            }
            BindOrbit(StepOrbit);
            SelectOffer(offeredSeal);
        }

        void SelectOffer(Seal seal)
        {
            offeredSeal = seal;
            int index = (int)seal;
            page.Q<Image>("shop-preview").Source = SealImage(seal);
            page.Q("shop-name").Text = TideglassCatalog.Names[index];
            page.Q("shop-description").Text = TideglassCatalog.Descriptions[index];
            RefreshOffers();
            Animate("Inspect");
        }

        void RefreshOffers()
        {
            WriteBalance();
            for (int index = 0; index < TideglassCatalog.Names.Length; index++)
            {
                bool owned = profile.Owns((Seal)index);
                int cost = TideglassCatalog.Prices[index];
                bool affordable = !owned && profile.Balance >= cost;
                bool selected = (int)offeredSeal == index;
                var purchase = page.Q<Button>($"buy-{index}");
                purchase.SetEnabled(affordable);
                purchase.EnableInClassList("gone", !selected);
                page.Q($"offer-{index}").EnableInClassList("selected", selected);
                page.Q($"price-icon-{index}").EnableInClassList("gone", !affordable);
                page.Q($"price-{index}").Text = owned ? "已拥有" : affordable ? cost.ToString(CultureInfo.InvariantCulture) : "潮晶不足";
            }
        }

        void Buy(Seal seal)
        {
            if (!profile.TryBuy(seal))
                return;
            RefreshOffers();
            PlayAudio(reward);
            Notice("已收入背包");
            Animate("Purchase", new Dictionary<string, Element> { ["item-glint"] = page.Q("offer-glint") });
        }

        void BindBag()
        {
            inspectedSeal = profile.Equipped;
            for (int index = 0; index < TideglassCatalog.Names.Length; index++)
            {
                Seal seal = (Seal)index;
                Element item = page.Q($"item-{index}");
                item.EnableInClassList("gone", !profile.Owns(seal));
                Bind($"item-{index}", () => Inspect(seal));
            }
            Bind("equip", Equip);
            BindOrbit(StepOrbit);
            Inspect(inspectedSeal);
            WriteBalance();
        }

        void Inspect(Seal seal)
        {
            if (!profile.Owns(seal))
                return;

            inspectedSeal = seal;
            int index = (int)seal;
            page.Q<Image>("seal-preview").Source = SealImage(seal);
            page.Q("seal-name").Text = TideglassCatalog.Names[index];
            page.Q("seal-description").Text = TideglassCatalog.Descriptions[index];
            for (int item = 0; item < TideglassCatalog.Names.Length; item++)
            {
                bool equipped = item == (int)profile.Equipped;
                page.Q($"item-{item}").EnableInClassList("selected", item == index);
                page.Q($"own-{item}").Text = equipped ? "已装配" : "";
            }
            bool currentSeal = seal == profile.Equipped;
            var equip = page.Q<Button>("equip");
            equip.Q(className: "control-inner").Text = currentSeal ? "已装配" : "装配";
            equip.SetEnabled(!currentSeal);
            Animate("Inspect");
        }

        void Equip()
        {
            if (!profile.TryEquip(inspectedSeal))
                return;
            Inspect(inspectedSeal);
            PlayAudio(reward);
            Notice("模组已校准");
        }

        void BindOrbit(Action<int> step)
        {
            orbit = new TideglassOrbit(page.Q("orbit-input"), page.Q("orbit-object"), step, reducedMotion);
        }

        void StepOrbit(int direction)
        {
            int count = TideglassCatalog.Names.Length;
            if (current == Page.Shop)
            {
                SelectOffer((Seal)(((int)offeredSeal + direction + count) % count));
                return;
            }
            if (current != Page.Bag)
                return;
            for (int index = 1; index < count; index++)
            {
                var candidate = (Seal)(((int)inspectedSeal + index * direction + count) % count);
                if (profile.Owns(candidate))
                {
                    Inspect(candidate);
                    return;
                }
            }
        }

        void BindHero()
        {
            for (int index = 0; index < TideglassCatalog.SkillNames.Length; index++)
            {
                int skill = index;
                Bind($"preview-{index}", () => SelectSkill(skill));
            }
            Bind("rehearse", Rehearse);
            SelectSkill(0);
        }

        void SelectSkill(int skill)
        {
            inspectedSkill = skill;
            var session = new TrialSession(profile.Equipped, escort: profile.Escort);
            int charge = session.ChargeChange(skill);
            string cooldown = session.Cooldown(skill).ToString("0.0", CultureInfo.InvariantCulture);
            page.Q("preview-damage").Text = $"伤害 {session.Damage(skill)}";
            page.Q("preview-charge").Text = charge >= 0 ? $"回能 +{charge}" : $"消耗 {-charge}";
            page.Q("preview-cooldown").Text = $"冷却 {cooldown} 秒";
            for (int index = 0; index < TideglassCatalog.SkillNames.Length; index++)
                page.Q($"preview-{index}").EnableInClassList("selected", index == skill);
        }

        void Rehearse()
        {
            var fx = page.Q("hero-fx");
            fx.Q<Image>("hero-wave").Source = TideglassAssets.ImageSource(TideglassTrial.Waves[inspectedSkill]);
            PlayAudio(TideglassAssets.Get<AudioClip>(TideglassAssets.Root + "Audio/cast"));
            if (reducedMotion)
                Notice(TideglassCatalog.SkillNames[inspectedSkill] + "预演完成");
            else
                Animate("Preview" + inspectedSkill);
        }

        void ShowResult(int casts, double elapsed)
        {
            result = assets.CreateDocument(surface, transform, "Tideglass Result");
            TideglassAssets.Load(result, "Result");
            result.Q("screen").EnableInClassList("reduced", reducedMotion);
            var destination = TideglassRoute.All[flownRoute];
            if (practiceFlight)
            {
                result.Q("screen").AddToClassList("drill-result");
                result.Q("result-title").Text = "演练达标";
                result.Q("result-sector").Text = FlightPlan.DrillNames[flownRoute];
                result.Q("result-currency").AddToClassList("gone");
                result.Q("result-pay").Text = "个人最快 · " + TideglassRoute.TimeLabel(profile.DrillBest(flownRoute));
                result.Q("finish").Q(className: "control-inner").Text = "再练一次";
                result.Q<Button>("finish").Clicked += () => surface.RunAfterEvent(() => BeginFlight(flownRoute, true));
                result.Q("result-next").Text = "返回演练舱";
                result.Q("result-link-tail").Text = " ›";
                result.Q<Button>("result-chart").Clicked += () => Request(Page.Range);
            }
            else
            {
                result.Q("result-sector").Text = destination.Name;
                result.Q("result-pay").Text = $"潮晶 +{destination.Pay}";
                result.Q("result-next").Text = profile.Clears(flownRoute) == 1 && flownRoute < TideglassRoute.All.Length - 1
                    ? "已开放 · " + TideglassRoute.All[flownRoute + 1].Name
                    : "航线最佳 · " + TideglassRoute.TimeLabel(profile.Best(flownRoute));
                if (flownRoute == 0 && profile.Clears(0) == 1)
                    result.Q("result-next").Text += "　·　弦昼已报到";
                selectedRoute = profile.LatestOpen;
                result.Q<Button>("result-chart").Clicked += () => Request(Page.Chart);
                result.Q<Button>("finish").Clicked += () => Request(Page.Lobby);
            }
            result.Q("result-time").Text = $"用时 {elapsed.ToString("0.0", CultureInfo.InvariantCulture)} 秒";
            result.Q("result-casts").Text = $"释放 {casts} 次";
            page.SetInputEnabled(false);
            result.SetPresentation(10, 0, true, true);
            result.SetInputEnabled(true);
            resultKeys = new TideglassKeyDeck(result.Body, reducedMotion);
            result.SynchronizeLayout();
            result.Q<Button>("finish").Focus();
            PlayAudio(reward);
            if (!reducedMotion)
                resultMotion = assets.Play(result, practiceFlight ? "DrillPass" : "Result");
        }

        void ReleaseResult()
        {
            resultKeys?.Dispose();
            resultKeys = null;
            resultMotion?.Dispose();
            resultMotion = null;
            if (!result)
                return;
            result.Unload();
            Destroy(result.gameObject);
            result = null;
        }

        void Animate(string name, IDictionary<string, Element> slots = null)
        {
            pageMotion?.Dispose();
            pageMotion = reducedMotion ? null : assets.Play(page, name, slots);
        }

        void Notice(string text)
        {
            var notice = page.Q("notice");
            notice.Text = text;
            notice.Style.Opacity = 1;
            noticeUntil = surface.UIClock.Now + 1.45;
        }

        void ToggleMotion()
        {
            reducedMotion = !reducedMotion;
            if (!reducedMotion) page.Q("screen").AddToClassList("motion-resumed");
            if (reducedMotion)
            {
                pageMotion?.Dispose();
                pageMotion = null;
            }
            trial?.SetReducedMotion(reducedMotion);
            orbit?.SetReducedMotion(reducedMotion);
            crewPortrait?.SetReducedMotion(reducedMotion);
            pageKeys?.SetReducedMotion(reducedMotion);
            ApplyMotionSetting();
        }

        void ApplyMotionSetting()
        {
            page.Q("screen").EnableInClassList("reduced", reducedMotion);
            page.Q<Button>("motion")?.SetAttribute("aria-pressed", reducedMotion ? "false" : "true");
            if ((page.Q("motion-label") ?? page.Q("motion")) is Element toggle)
                toggle.Text = reducedMotion ? "动效 · 简" : "动效 · 开";
        }

        void ToggleDiagnostics()
        {
            showDiagnostics = !showDiagnostics;
            if (showDiagnostics)
                AddDiagnostics();
            else
            {
                diagnostics.Destroy();
                diagnostics = null;
            }
        }

        void AddDiagnostics()
        {
            diagnostics = page.Create<Label>();
            diagnostics.AddToClassList("diagnostics");
            page.Q("screen").Add(diagnostics);
            nextDiagnosticsAt = 0;
        }

        void WriteBalance()
        {
            page.Q("balance").Text = profile.Balance.ToString(CultureInfo.InvariantCulture);
        }

        static string SealImage(Seal seal) => TideglassAssets.ImageSource(TideglassCatalog.Seals[(int)seal]);

        void PlayAudio(AudioClip clip) => audioSource.PlayOneShot(clip, .65f);

        void OnDisable()
        {
            StopAllCoroutines();
            ReleaseTransition();
            ReleaseResult();
            pageKeys?.Dispose();
            pageKeys = null;
            crewPortrait?.Dispose();
            crewPortrait = null;
            orbit?.Dispose();
            orbit = null;
            trial?.Dispose();
            trial = null;
            pageMotion?.Dispose();
            pageMotion = null;
            if (page)
            {
                page.Unload();
                Destroy(page.gameObject);
                page = null;
            }
        }
    }
}
