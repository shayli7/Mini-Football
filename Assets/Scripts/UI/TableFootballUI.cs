using TableFootball.Net;
using TableFootball.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace TableFootball.UI
{
    /// <summary>
    /// The single entry point. Drop this component on one empty GameObject in your MainScene and press
    /// Play — it builds the whole Arcade Neon UI at runtime (Canvas, EventSystem, center score HUD,
    /// top-right pause button, main/pause menu and settings) and wires it to your MatchManager.
    /// No manual canvas assembly, no prefabs, no imported art.
    /// </summary>
    [DisallowMultipleComponent]
    public class TableFootballUI : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [SerializeField] private MatchManager match;

        [Header("Startup")]
        [Tooltip("Seconds the loading bar takes on first boot. Only used if no start screen was " +
                 "built — normally the title screen holds the boot instead, until the player taps.")]
        [SerializeField] private float bootLoadSeconds = 1.5f;
        [Tooltip("Seconds the loading bar takes when leaving a match for the main menu.")]
        [SerializeField] private float transitionLoadSeconds = 1f;

        [Header("Menu artwork (optional — blank placeholders are drawn until these are assigned)")]
        [SerializeField] private Sprite onlineImage;
        [SerializeField] private Sprite localImage;
        [SerializeField] private Sprite playerVsPlayerImage;
        [SerializeField] private Sprite aiVsPlayerImage;

        [Header("Fonts (optional — assign a condensed bold TMP font for the full arcade look)")]
        [SerializeField] private TMP_FontAsset displayFont;
        [SerializeField] private TMP_FontAsset uiFont;

        [Header("Accessibility")]
        [Tooltip("Force reduced motion: every tween snaps to its end state.")]
        [SerializeField] private bool reducedMotion = false;

        [Header("Online")]
        [Tooltip("Sign in to Unity Gaming Services during the boot loading screen. Off = local play only.")]
        [SerializeField] private bool signInAtBoot = true;

        private void Start()
        {
            if (match == null) match = FindAnyObjectByType<MatchManager>();

            // Started before the UI and never awaited: the title screen sits there until the player
            // taps, so the round trip is free if it happens underneath it. Nothing here depends on
            // the result either way — every online entry point awaits GameServices for itself, so
            // arriving at the menu before the sign-in lands costs a wait, not a failure.
            //
            // Friends comes up with it, rather than waiting for the friends list to be opened. An
            // invitation is a message from somebody else, and the game has to be listening before it
            // arrives — a player who never opens the list would otherwise never be invitable, and
            // the main menu's banner would only appear to people who had already gone looking. It
            // signs in for itself, so this needs no ordering against the call above.
            if (signInAtBoot)
            {
                _ = GameServices.EnsureSignedInAsync();
                _ = PlayerAccount.RefreshWithRetryAsync();
                _ = FriendsHub.EnsureReadyAsync();

                // Pulls this player's cloud save into the local progression stores. Fire-and-forget
                // like the sign-in above and for the same reason: it awaits the sign-in itself, and
                // the UI need not block on it — the monotonic merge can only ADD to what is on screen,
                // so a chip that drew the local value first is corrected upward by the redraw, never
                // down. See CloudSync.
                _ = CloudSync.LoadAsync();
            }

            if (displayFont != null) ArcadeTheme.DisplayFont = displayFont;
            if (uiFont != null) ArcadeTheme.UiFont = uiFont;
            ArcadeTheme.ReducedMotion = reducedMotion;

            EnsureEventSystem();
            EnsureAudio();
            var canvasRoot = BuildCanvas();

            var pause = gameObject.AddComponent<GameMenu>();
            var hud = gameObject.AddComponent<ScoreHud>();
            var loading = gameObject.AddComponent<LoadingScreen>();
            var mainMenu = gameObject.AddComponent<MainMenu>();
            var onlineMenu = gameObject.AddComponent<OnlineMenu>();
            var leagueMenu = gameObject.AddComponent<LeagueMenu>();
            var storeMenu = gameObject.AddComponent<StoreMenu>();
            var levelPathMenu = gameObject.AddComponent<LevelPathMenu>();
            var friendsMenu = gameObject.AddComponent<FriendsMenu>();
            var profileMenu = gameObject.AddComponent<ProfileMenu>();
            var friendProfileMenu = gameObject.AddComponent<FriendProfileMenu>();
            var questsMenu = gameObject.AddComponent<QuestsMenu>();
            var countdown = gameObject.AddComponent<CountdownScreen>();
            var onboarding = gameObject.AddComponent<OnboardingScreen>();
            var start = gameObject.AddComponent<StartScreen>();
            var flow = gameObject.AddComponent<GameFlow>();

            // One more listener on MatchManager, alongside the HUD and the audio. It is added here
            // rather than dropped on the table so the whole progression system boots and dies with
            // the UI that shows it.
            var questTracker = gameObject.AddComponent<QuestTracker>();

            // BEFORE the HUD, and that order is load-bearing rather than tidy. Both subscribe to
            // MatchWon; the HUD sizes its result panel around the quests the tracker has just
            // banked, so a tracker that subscribed second would hand it the PREVIOUS match's
            // ledger — an empty one first time out, and a stale one every time after. Multicast
            // delegates fire in subscription order, so subscribing first is the whole fix.
            questTracker.Build(match);

            hud.Build(canvasRoot, match);
            hud.OnOpenMenu = pause.Open;

            // The pause menu no longer opens on start — the main menu is the front door now.
            pause.Build(canvasRoot, match, openOnStart: false);

            loading.Build(canvasRoot);
            mainMenu.Build(canvasRoot, onlineImage, localImage, playerVsPlayerImage, aiVsPlayerImage);
            onlineMenu.Build(canvasRoot);
            leagueMenu.Build(canvasRoot);
            storeMenu.Build(canvasRoot);
            levelPathMenu.Build(canvasRoot);
            friendsMenu.Build(canvasRoot);
            profileMenu.Build(canvasRoot);
            friendProfileMenu.Build(canvasRoot);
            questsMenu.Build(canvasRoot);
            countdown.Build(canvasRoot);

            // Built before the title screen so it sits under it: the title comes up first at boot, and
            // the onboarding screen is what the title tap fades over — the same front-to-back order the
            // main menu has relative to the title.
            onboarding.Build(canvasRoot);

            // Built after every other screen, so it is the last sibling and comes up over all of
            // them at boot without having to fight for the front.
            start.Build(canvasRoot);

            // Push saved volume + difficulty into the live scene before anything can play.
            GameAudio.ApplyAll();

            // Built last: it raises the title screen immediately, so everything it drives has to
            // exist first.
            flow.Build(start, onboarding, loading, mainMenu, onlineMenu, leagueMenu,
                       storeMenu, levelPathMenu, friendsMenu,
                       profileMenu, friendProfileMenu, questsMenu, pause, hud, countdown, match,
                       questTracker, bootLoadSeconds, transitionLoadSeconds);
        }

        private Transform BuildCanvas()
        {
            var go = new GameObject("TableFootballCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 1600x900 rather than 1920x1080: the reference is what the UI's own numbers are drawn
            // against, so shrinking it enlarges every panel, control and margin by the same 20% at
            // once. Sizing them up individually would drift the proportions apart, and there are a
            // hundred of them.
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f; // balance phones (portrait) and desktop (landscape)

            // Every screen hangs off this rather than off the Canvas, so all of them clear notches
            // and gesture bars without any of them knowing safe areas exist. Backdrops and dims bleed
            // back out past it (ArcadeTheme.Bleed) so opaque menus still cover the screen edge to edge.
            var safe = new GameObject("SafeAreaRoot", typeof(RectTransform), typeof(SafeArea));
            var srt = safe.GetComponent<RectTransform>();
            srt.SetParent(go.transform, false);
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;

            return srt;
        }

        /// <summary>
        /// Creates the sound effects player if the scene has none, on its own object so its pool of
        /// AudioSources does not clutter this one. Built before the UI, so the first button that
        /// appears can already click.
        /// </summary>
        private static void EnsureAudio()
        {
            if (FindAnyObjectByType<GameSfx>() != null)
            {
                return;
            }

            new GameObject("GameSfx", typeof(GameSfx));
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            var module = es.AddComponent<InputSystemUIInputModule>();
            // A module added at runtime has no actions wired, so pointer/click/navigation would be
            // dead. Assign the built-in default UI actions so the menu is usable immediately.
            module.AssignDefaultActions();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
