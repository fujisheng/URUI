using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;

namespace URUI.Samples.GameFlow
{
    public enum GamePage { Loading, Login, Home, Inventory, Settings, Leaderboard }

    /// <summary>A complete local game UI flow. Authentication and ranking data are mocked.</summary>
    [RequireComponent(typeof(Document))]
    public sealed partial class GameFlowSample : MonoBehaviour
    {
        public const string SettingsKey = "URUI.GameFlow.Settings.v1";
        [SerializeField] bool simulateStartupFailureOnce;
        [SerializeField] int serviceLatencyMilliseconds = 350;
        [SerializeField, TextArea] string startupError;
        Document document;
        GameFlowResources assets;
        MockGameService service;
        GameSettings settings;
        Camera ownedCamera;
        AudioSource audioSource;
        GameObject ownedAudio;
        AudioClip feedback;
        CancellationTokenSource pageLifetime;
        readonly List<Action> detach = new List<Action>();
        GamePage? requestedPage;
        float entryTime;
        float rewardUntil;
        int backdropWidth, backdropHeight;
        Element entryPanel;
        bool initializationAttempted;
        public GamePage CurrentPage { get; private set; }
        public bool IsBusy { get; private set; }
        public MockGameService Service => service;
        public string StartupError => startupError;

        void OnEnable()
        {
            document = GetComponent<Document>();
            try
            {
                startupError = "";
                settings = PlayerPrefs.HasKey(SettingsKey) ? JsonUtility.FromJson<GameSettings>(PlayerPrefs.GetString(SettingsKey)) : new GameSettings();
                settings = settings ?? new GameSettings();
                settings.volume = Mathf.Clamp01(settings.volume);
                if (!Camera.main)
                {
                    ownedCamera = new GameObject("Aster Vale Camera").AddComponent<Camera>();
                    ownedCamera.tag = "MainCamera";
                    ownedCamera.clearFlags = CameraClearFlags.SolidColor;
                    ownedCamera.backgroundColor = new Color(.035f, .05f, .075f);
                }
                ownedAudio = new GameObject("Aster Vale Audio");
                ownedAudio.transform.SetParent(transform, false);
                if (!FindFirstObjectByType<AudioListener>()) ownedAudio.AddComponent<AudioListener>();
                audioSource = ownedAudio.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                feedback = UnityEngine.Resources.Load<AudioClip>("URUI/GameFlow/Audio/confirm");
                assets = new GameFlowResources();
                assets.Prepare();
                document.Unload();
                document.FontPath = assets.FontPath;
                document.Images = assets.Images;
                Navigate(GamePage.Loading);
            }
            catch (Exception error)
            {
                FailBootstrap(error);
            }
        }

        public void Navigate(GamePage page)
        {
            if (!isActiveAndEnabled) return;
            if (page != GamePage.Loading && service?.IsInitialized != true) page = GamePage.Loading;
            if (page != GamePage.Loading && page != GamePage.Login && service?.CurrentPlayer == null) page = GamePage.Login;
            requestedPage = page;
        }

        void LateUpdate()
        {
            try { UpdatePage(); }
            catch (Exception error) { FailBootstrap(error); }
        }

        void UpdatePage()
        {
            if (requestedPage.HasValue)
            {
                var next = requestedPage.Value;
                requestedPage = null;
                ClosePage();
                CurrentPage = next;
                pageLifetime = new CancellationTokenSource();
                document.Unload();
                document.Images = assets.Images;
                document.LoadMarkup(assets.Page(next));
                DecorateArtwork();
                document.Root.EnableInClassList("cyan", settings.accent == "Glacier");
                document.Root.EnableInClassList("compact", settings.compactRows);
                backdropWidth = backdropHeight = 0;
                entryTime = 0;
                document.Root.Style.SetOpacity(0);
                // A transformed ancestor requires native clip masks. Keep scrolling
                // descendants on the supported scissor path, and slide one panel
                // with relative layout offset rather than a transform matrix.
                entryPanel = document.Q(className: "window-bar") ?? document.Q(className: "player-copy")
                    ?? document.Q(className: "login-card") ?? document.Q(className: "boot-screen");
                if (entryPanel != null) { entryPanel.Style.Position = Position.Relative; entryPanel.Style.Top = 10; }
                BindPage();
            }
            if (document && document.IsLoaded)
            {
                UpdateArtwork();
                if (backdropWidth != Screen.width || backdropHeight != Screen.height)
                {
                    backdropWidth = Screen.width; backdropHeight = Screen.height;
                    var background = document.Q<URUI.Controls.Image>("backdrop");
                    if (background != null)
                    {
                        var size = assets.BackgroundSize;
                        float scale = Mathf.Max(Screen.width / size.x, Screen.height / size.y);
                        background.Style.Width = size.x * scale; background.Style.Height = size.y * scale;
                        background.Style.Left = (Screen.width - size.x * scale) * .5f;
                        background.Style.Top = (Screen.height - size.y * scale) * .5f;
                    }
                }
                if (rewardUntil > 0 && Time.unscaledTime >= rewardUntil)
                {
                    document.Q("item-detail")?.RemoveFromClassList("reward"); rewardUntil = 0;
                }
            }
            if (document && document.IsLoaded && entryTime < .22f)
            {
                entryTime += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(entryTime / .22f);
                float eased = 1 - Mathf.Pow(1 - progress, 3);
                document.Root.Style.SetOpacity(eased);
                if (entryPanel != null && entryPanel.IsValid)
                {
                    entryPanel.Style.Top = 10 * (1 - eased);
                    if (progress >= 1) { entryPanel.Style.Remove("top"); entryPanel.Style.Remove("position"); }
                }
            }
        }

        void BindPage()
        {
            switch (CurrentPage)
            {
                case GamePage.Loading: BindButton("retry", () => Operation(Initialize)); Operation(Initialize); break;
                case GamePage.Login: BindLogin(); break;
                case GamePage.Home: BindHome(); break;
                case GamePage.Inventory: BindInventory(); break;
                case GamePage.Settings: BindSettings(); break;
                case GamePage.Leaderboard: BindLeaderboard(); break;
            }
            if (CurrentPage >= GamePage.Home)
            {
                BindButton("back", () => Navigate(GamePage.Home));
                BindButton("home", () => Navigate(GamePage.Home));
                BindButton("inventory", () => Navigate(GamePage.Inventory));
                BindButton("settings", () => Navigate(GamePage.Settings));
                BindButton("leaderboard", () => Navigate(GamePage.Leaderboard));
                Text("player-caption", "LV. " + service.CurrentPlayer.level);
                Text("credits", service.CurrentPlayer.credits.ToString("N0"));
                foreach (GamePage page in new[] { GamePage.Home, GamePage.Inventory, GamePage.Settings, GamePage.Leaderboard })
                    document.Q(page.ToString().ToLowerInvariant())?.EnableInClassList("active", page == CurrentPage);
            }
        }

        async Task Initialize(CancellationToken token)
        {
            Show("retry", false);
            var progress = document.Q<ProgressBar>("loading-progress");
            Text("status", "Validating page assets and catalog..."); progress.Value = 20; Text("loading-percent", "20%");
            await Task.Delay(100, token);
            token.ThrowIfCancellationRequested();
            assets.LoadImages();
            if (service == null) service = new MockGameService(assets.ReadCatalog(), serviceLatencyMilliseconds);
            Text("status", "Preparing local inventory and profile..."); progress.Value = 55; Text("loading-percent", "55%");
            await Task.Delay(100, token);
            token.ThrowIfCancellationRequested();
            service.FailNextInitialization = simulateStartupFailureOnce && !initializationAttempted;
            initializationAttempted = true;
            await service.InitializeAsync(token);
            token.ThrowIfCancellationRequested();
            Text("status", "Ready. Connecting to the local demo session..."); progress.Value = 100; Text("loading-percent", "100%");
            await Task.Delay(160, token);
            token.ThrowIfCancellationRequested();
            Navigate(GamePage.Login);
        }

        async void Operation(Func<CancellationToken, Task> action)
        {
            if (IsBusy || pageLifetime == null) return;
            var lifetime = pageLifetime;
            IsBusy = true;
            try { await action(lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!lifetime.IsCancellationRequested && document && document.IsLoaded)
                {
                    Text("status", error.Message);
                    Show("retry", true);
                }
            }
            finally
            {
                if (ReferenceEquals(lifetime, pageLifetime))
                {
                    IsBusy = false;
                    if (document && document.IsLoaded)
                    {
                        var submit = document.Q<Button>("login-submit");
                        if (submit != null) { submit.SetEnabled(true); ButtonText("login-submit", "CONNECT"); }
                        document.Q<Button>("refresh")?.SetEnabled(true);
                    }
                }
            }
        }

        void BindButton(string name, Action action, bool feedback = true)
        {
            var button = document.Q<Button>(name);
            if (button == null) return;
            Action callback = () => { if (feedback) PlayFeedback(); action(); };
            button.Clicked += callback;
            detach.Add(() => button.Clicked -= callback);
        }

        void Subscribe<T>(BaseField<T> field, Action<ChangeEvent<T>> action)
        {
            var subscription = field.RegisterValueChangedCallback(action);
            detach.Add(subscription.Dispose);
        }
        void Text(string name, string text) { var element = document.Q<Label>(name); if (element != null) element.Text = text; }
        void Show(string name, bool visible) { var element = document.Q(name); if (element != null) element.Style.Display = visible ? DisplayStyle.Block : DisplayStyle.None; }
        void PlayFeedback()
        {
            if (audioSource && feedback && settings.sound) { audioSource.volume = settings.volume; audioSource.PlayOneShot(feedback); }
        }
        void ClosePage()
        {
            pageLifetime?.Cancel();
            foreach (var action in detach) action();
            detach.Clear();
            pageLifetime?.Dispose(); pageLifetime = null;
            IsBusy = false;
            entryPanel = null;
            artGroups.Clear();
        }
        void OnDisable()
        {
            requestedPage = null;
            ClosePage();
            if (document) document.Unload();
            assets?.Dispose(); assets = null;
            service = null; initializationAttempted = false;
            if (ownedAudio) { ownedAudio.SetActive(false); Destroy(ownedAudio); ownedAudio = null; audioSource = null; }
            if (ownedCamera) { ownedCamera.gameObject.SetActive(false); Destroy(ownedCamera.gameObject); ownedCamera = null; }
        }

        void FailBootstrap(Exception error)
        {
            startupError = "Game Flow bootstrap failed: " + error.Message + " Reimport the complete sample, then use Retry Game Flow startup in this component's context menu.";
            Debug.LogError(startupError);
            enabled = false;
        }

        [ContextMenu("Retry Game Flow startup")]
        public void RetryStartup()
        {
            if (!Application.isPlaying) return;
            enabled = false;
            enabled = true;
        }
    }
}
