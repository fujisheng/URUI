using System;
using Solstice.Scene;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Effect;
using URUI.Effect.Rendering;
using URUI.Elements;
using URUI.Hosting;

namespace Solstice
{
    /// <summary>
    /// Root orchestrator for the Solstice RPG showcase:
    /// coordinates the URP 3D scenic backdrop, persistent TopHud & overlays,
    /// multi-screen routing (`lobby`, `combat`, `heroes`, `summon`, `armory`),
    /// TextureEffectSurface live banner, audio feedback, and dev telemetry.
    /// </summary>
    public sealed class SolsticeApp : MonoBehaviour
    {
        [SerializeField] Surface surface;
        [SerializeField] SolsticeStageController stage;
        [SerializeField] AudioSource audioSource;

        SolsticeResources resources;
        SolsticeGameData data;

        Document topHudDoc;
        Document pageDoc;
        Document settingsDoc;
        Document toastDoc;
        Document devDoc;

        EffectModule pageEffectModule;
        TextureEffectSurface bannerSurface;
        Material bannerMaterial;
        Texture2D bgLobbyTex;
        IDisposable bannerSurfaceBinding;

        readonly CompositeDisposable topHudSubs = new();
        IDisposable activePageController;
        SettingsController settingsController;
        DevOverlayController devOverlayController;

        Element hudStamina;
        Element hudGold;
        Element hudGems;
        Button soundBtn;
        Button settingsBtn;
        Button devBtn;
        Element toastBox;
        float toastTimer;

        string currentRoute = "lobby";
        string currentTheme = "theme-obsidian";
        bool soundEnabled = true;
        float volume = 0.8f;
        bool reducedMotion;
        bool settingsOpen;
        bool initialized;

        public Surface Surface => surface;
        public SolsticeGameData Data => data;
        public string CurrentRoute => currentRoute;
        public string CurrentTheme => currentTheme;
        public float Volume => volume;
        public bool ReducedMotion => reducedMotion;

        public LobbyController Lobby => activePageController as LobbyController;
        public CombatController Combat => activePageController as CombatController;
        public HeroesController Heroes => activePageController as HeroesController;
        public SummonController Summon => activePageController as SummonController;
        public BackpackController Backpack => activePageController as BackpackController;
        public SettingsController Settings => settingsController;
        public DevOverlayController DevOverlay => devOverlayController;

        void Start()
        {
            InitializeIfNeeded();
        }

        public void InitializeIfNeeded()
        {
            if (initialized) return;
            initialized = true;

            if (!surface) surface = GetComponent<Surface>();
            if (!stage) stage = FindFirstObjectByType<SolsticeStageController>();
            if (!audioSource) audioSource = GetComponent<AudioSource>();

            surface.Viewport = new Vector2Int(1280, 720);

            resources = new SolsticeResources();
            data = new SolsticeGameData();

            stage?.Initialize();
            stage?.SetThemeTint(currentTheme);

            bgLobbyTex = Resources.Load<Texture2D>("URUI/Solstice/Images/bg-lobby");
            var bannerShader = Shader.Find("Solstice/AstralBanner") ?? Shader.Find("Unlit/Texture");
            bannerMaterial = new Material(bannerShader) { name = "SolsticeAstralBannerMat" };
            bannerSurface = new TextureEffectSurface(512, 160);

            topHudDoc = CreateChildDocument("TopHudDoc");
            pageDoc = CreateChildDocument("PageDoc");
            toastDoc = CreateChildDocument("ToastDoc");
            devDoc = CreateChildDocument("DevDoc");
            settingsDoc = CreateChildDocument("SettingsDoc");

            LoadPersistentDocuments();
            LoadScreenImmediate("lobby");
        }

        Document CreateChildDocument(string docName)
        {
            var go = new GameObject(docName);
            go.transform.SetParent(transform, false);
            var doc = go.AddComponent<Document>();
            doc.Surface = surface;
            doc.TextResources = resources.GetSharedTextResources();
            doc.ImageResolver = resources.CreateImageResolver();
            return doc;
        }

        void LoadPersistentDocuments()
        {
            // TopHud (Layer 10, pass-through except .top-hud-shell)
            topHudDoc.Load(resources.LoadHtmlAsset("TopHud"), "/solstice/Html/TopHud.html");
            topHudDoc.SetPresentation(10, 0, true, false);
            ApplyOverlayPointerPassThrough(topHudDoc);
            WireTopHud();

            // Toast (Layer 20, fully pass-through)
            toastDoc.Load(resources.LoadHtmlAsset("ToastOverlay"), "/solstice/Html/ToastOverlay.html");
            toastDoc.SetPresentation(20, 0, true, false);
            ApplyOverlayPointerPassThrough(toastDoc);
            toastBox = toastDoc.Q("toast-box");

            // Dev Telemetry Drawer (Layer 25, pass-through except .dev-drawer)
            devDoc.Load(resources.LoadHtmlAsset("DevOverlay"), "/solstice/Html/DevOverlay.html");
            devDoc.SetPresentation(25, 0, true, false);
            ApplyOverlayPointerPassThrough(devDoc);
            devOverlayController = new DevOverlayController(this, surface, devDoc);

            // Settings Modal (Layer 30, modal hidden until opened)
            settingsDoc.Load(resources.LoadHtmlAsset("SettingsModal"), "/solstice/Html/SettingsModal.html");
            settingsDoc.SetPresentation(30, 0, false, true);
            settingsController = new SettingsController(this, data, settingsDoc);
        }

        static void ApplyOverlayPointerPassThrough(Document doc)
        {
            if (doc == null || !doc.IsLoaded) return;
            doc.DocumentElement?.Style.Set("pointer-events", "none");
            doc.Body?.Style.Set("pointer-events", "none");
            doc.Body?.Style.Set("background-color", "transparent");
            var root = doc.Q("sol-root");
            root?.Style.Set("pointer-events", "none");
        }

        void WireTopHud()
        {
            topHudSubs.Dispose();

            hudStamina = topHudDoc.Q("hud-stamina");
            hudGold = topHudDoc.Q("hud-gold");
            hudGems = topHudDoc.Q("hud-gems");
            soundBtn = topHudDoc.Q<Button>("btn-sound-toggle");
            settingsBtn = topHudDoc.Q<Button>("btn-settings-open");
            devBtn = topHudDoc.Q<Button>("btn-dev-toggle");

            BindNav("nav-lobby", "lobby");
            BindNav("nav-combat", "combat");
            BindNav("nav-heroes", "heroes");
            BindNav("nav-summon", "summon");
            BindNav("nav-armory", "armory");

            BindTopClick("btn-add-stamina", () =>
            {
                data.AddStaminaDemo();
                PlaySound("sfx-click");
                ShowToast("补给药剂 +12 体力已恢复");
                RefreshTopHud();
            });
            BindTopClick("btn-sound-toggle", () =>
            {
                soundEnabled = !soundEnabled;
                RefreshTopHud();
                ShowToast(soundEnabled ? "音效已开启" : "音效已静音");
            });
            BindTopClick("btn-settings-open", () => SetSettingsOpen(!settingsOpen));
            BindTopClick("btn-dev-toggle", () => SetDevOpen(!(devOverlayController?.IsOpen ?? false)));

            RefreshTopHud();
        }

        void BindNav(string buttonId, string route)
        {
            BindTopClick(buttonId, () => NavigateTo(route));
        }

        void BindTopClick(string buttonId, Action cb)
        {
            var btn = topHudDoc.Q<Button>(buttonId);
            if (btn == null) return;
            btn.Clicked += cb;
            topHudSubs.Add(new DisposableAction(() => btn.Clicked -= cb));
        }

        public void NavigateTo(string route)
        {
            if (string.IsNullOrEmpty(route) || route == currentRoute && activePageController != null)
            {
                return;
            }
            PlaySound("sfx-click");
            if (surface && surface.IsExecutingCallback)
            {
                surface.RunAfterEvent(() => LoadScreenImmediate(route));
            }
            else
            {
                LoadScreenImmediate(route);
            }
        }

        public void LoadScreenImmediate(string route)
        {
            InitializeIfNeeded();
            currentRoute = route;

            activePageController?.Dispose();
            activePageController = null;
            bannerSurfaceBinding?.Dispose();
            bannerSurfaceBinding = null;
            if (pageDoc.IsLoaded)
            {
                pageDoc.Unload();
            }
            pageEffectModule?.Dispose();
            pageEffectModule = null;

            pageEffectModule = new EffectModule(pageDoc, resources);

            if (route == "lobby")
            {
                pageDoc.ImageResolver = resources.CreateImageResolver(url =>
                    url == "/solstice/ShaderBanner/Flagship" ? bannerSurface.Output.Acquire() : null);
                pageDoc.Load(resources.LoadHtmlAsset("Lobby"), "/solstice/Html/Lobby.html");

                var bannerImg = pageDoc.Q("lobby-surface-banner");
                if (bannerImg != null && bgLobbyTex && bannerMaterial)
                {
                    bannerSurfaceBinding = pageEffectModule.BindSurface(
                        bannerImg,
                        new SurfaceEffectInput(bannerSurface, bgLobbyTex, bannerMaterial, () => 1UL, usesTime: true));
                }
                pageDoc.SetPresentation(0, 0, true, false);
                stage?.SetBackdrop(SolsticeStageController.StageBackdrop.Sanctuary);
                activePageController = new LobbyController(this, data, pageDoc);
            }
            else if (route == "combat")
            {
                pageDoc.ImageResolver = resources.CreateImageResolver();
                pageDoc.Load(resources.LoadHtmlAsset("Combat"), "/solstice/Html/Combat.html");
                pageDoc.SetPresentation(0, 0, true, false);
                stage?.SetBackdrop(SolsticeStageController.StageBackdrop.Battlefield);
                activePageController = new CombatController(this, data, pageDoc);
            }
            else if (route == "heroes")
            {
                pageDoc.ImageResolver = resources.CreateImageResolver();
                pageDoc.Load(resources.LoadHtmlAsset("Heroes"), "/solstice/Html/Heroes.html");
                pageDoc.SetPresentation(0, 0, true, false);
                stage?.SetBackdrop(SolsticeStageController.StageBackdrop.Sanctuary);
                activePageController = new HeroesController(this, data, pageDoc);
            }
            else if (route == "summon")
            {
                pageDoc.ImageResolver = resources.CreateImageResolver();
                pageDoc.Load(resources.LoadHtmlAsset("Summon"), "/solstice/Html/Summon.html");
                pageDoc.SetPresentation(0, 0, true, false);
                stage?.SetBackdrop(SolsticeStageController.StageBackdrop.Battlefield);
                activePageController = new SummonController(this, data, pageDoc);
            }
            else
            {
                pageDoc.ImageResolver = resources.CreateImageResolver();
                var compiled = resources.LoadCompiledAsset("Backpack.compiled");
                if (compiled)
                {
                    pageDoc.Load(compiled);
                }
                else
                {
                    pageDoc.Load(resources.LoadHtmlAsset("Backpack"), "/solstice/Html/Backpack.html");
                }
                pageDoc.SetPresentation(0, 0, true, false);
                stage?.SetBackdrop(SolsticeStageController.StageBackdrop.Sanctuary);
                activePageController = new BackpackController(this, data, pageDoc);
            }

            ApplyThemeClassToDocument(pageDoc);
            RefreshTopHud();
            pageDoc.SynchronizeLayout();
        }

        public void ApplyTheme(string themeClass)
        {
            currentTheme = string.IsNullOrEmpty(themeClass) ? "theme-obsidian" : themeClass;
            ApplyThemeClassToDocument(topHudDoc);
            ApplyThemeClassToDocument(pageDoc);
            ApplyThemeClassToDocument(settingsDoc);
            ApplyThemeClassToDocument(toastDoc);
            ApplyThemeClassToDocument(devDoc);
            stage?.SetThemeTint(currentTheme);
            settingsController?.Refresh();
        }

        void ApplyThemeClassToDocument(Document doc)
        {
            if (doc == null || !doc.IsLoaded) return;
            var root = doc.Q("sol-root");
            if (root == null) return;
            root.RemoveFromClassList("theme-obsidian");
            root.RemoveFromClassList("theme-crimson");
            root.RemoveFromClassList("theme-alabaster");
            root.AddToClassList(currentTheme);
        }

        public void SetSettingsOpen(bool open)
        {
            settingsOpen = open;
            if (settingsDoc && settingsDoc.IsLoaded)
            {
                settingsDoc.SetPresentation(30, 0, open, true);
                if (open)
                {
                    ApplyThemeClassToDocument(settingsDoc);
                    settingsController?.Refresh();
                }
            }
            PlaySound("sfx-click");
        }

        public void SetDevOpen(bool open)
        {
            devOverlayController?.SetOpen(open);
            devBtn?.EnableInClassList("is-active", open);
        }

        public void SetVolume(float value)
        {
            volume = Mathf.Clamp01(value);
            if (audioSource) audioSource.volume = volume;
        }

        public void SetReducedMotion(bool value)
        {
            reducedMotion = value;
        }

        public void RefreshTopHud()
        {
            if (topHudDoc == null || !topHudDoc.IsLoaded) return;
            if (hudStamina != null) hudStamina.Text = $"{data.Stamina}/{data.StaminaCap}";
            if (hudGold != null) hudGold.Text = $"{data.Gold:N0}";
            if (hudGems != null) hudGems.Text = $"{data.Gems:N0}";
            if (soundBtn != null)
            {
                soundBtn.Text = soundEnabled ? "音效" : "静音";
                soundBtn.EnableInClassList("is-active", soundEnabled);
            }

            string[] routes = { "lobby", "combat", "heroes", "summon", "armory" };
            for (var i = 0; i < routes.Length; i++)
            {
                var nav = topHudDoc.Q("nav-" + routes[i]);
                nav?.EnableInClassList("is-active", routes[i] == currentRoute);
            }
        }

        public void RefreshLobbyIfLoaded()
        {
            Lobby?.Refresh();
        }

        public void ShowToast(string message)
        {
            if (toastBox == null || string.IsNullOrEmpty(message)) return;
            toastBox.Text = message;
            toastBox.Style.Set("display", "flex");
            toastTimer = 2.4f;
        }

        public void PlaySound(string clipName)
        {
            if (!soundEnabled || !audioSource || resources == null) return;
            var clip = resources.LoadAudio(clipName);
            if (clip)
            {
                audioSource.PlayOneShot(clip, volume);
            }
        }

        void Update()
        {
            if (!initialized) return;

            var dt = Time.deltaTime;
            stage?.Tick(dt, reducedMotion);

            if (activePageController is CombatController combat)
            {
                combat.Tick(dt);
            }

            if (toastTimer > 0f)
            {
                toastTimer -= dt;
                if (toastTimer <= 0f && toastBox != null)
                {
                    toastBox.Style.Set("display", "none");
                }
            }

            devOverlayController?.Tick();
        }

        void OnDestroy()
        {
            activePageController?.Dispose();
            activePageController = null;
            bannerSurfaceBinding?.Dispose();
            bannerSurfaceBinding = null;
            topHudSubs.Dispose();
            settingsController?.Dispose();
            settingsController = null;
            devOverlayController?.Dispose();
            devOverlayController = null;

            if (pageDoc && pageDoc.IsLoaded) pageDoc.Unload();
            if (topHudDoc && topHudDoc.IsLoaded) topHudDoc.Unload();
            if (settingsDoc && settingsDoc.IsLoaded) settingsDoc.Unload();
            if (toastDoc && toastDoc.IsLoaded) toastDoc.Unload();
            if (devDoc && devDoc.IsLoaded) devDoc.Unload();

            pageEffectModule?.Dispose();
            pageEffectModule = null;
            bannerSurface?.Dispose();
            bannerSurface = null;
            if (bannerMaterial)
            {
                Destroy(bannerMaterial);
                bannerMaterial = null;
            }
        }
    }
}
