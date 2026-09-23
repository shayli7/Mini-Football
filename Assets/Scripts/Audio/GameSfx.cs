using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Plays the game's sound effects. Sounds come from <see cref="SfxLibrary"/> by default, so
    /// audio works with no files in the project; assigning a clip in the inspector overrides the
    /// synthesised one, which is how real recordings get added later without a code change.
    ///
    /// Reachable statically because the things that make noise — a ball striking a figure, a button
    /// being pressed — are scattered and should not each carry a serialized reference to the audio
    /// system. Anything can call GameSfx.Play*; if no instance exists the call is simply ignored.
    ///
    /// Volume follows <see cref="GameAudio.Sfx"/>, which the settings slider already writes, so that
    /// slider finally controls something.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameSfx : MonoBehaviour
    {
        [Header("Optional overrides (loaded from Resources/Audio, else synthesised)")]
        [SerializeField] private AudioClip ballHitClip;
        [SerializeField] private AudioClip ballHitClipAlt;
        [SerializeField] private AudioClip wallThudClip;
        [SerializeField] private AudioClip goalClip;
        [SerializeField] private AudioClip kickOffWhistleClip;
        [SerializeField] private AudioClip fullTimeWhistleClip;
        [SerializeField] private AudioClip uiClickClip;
        [SerializeField] private AudioClip countdownTickClip;
        [SerializeField] private AudioClip countdownGoClip;

        [Header("Menu music (placeholder — silent until a clip is assigned)")]
        [Tooltip("Looping music for the main menu. Drop a clip here or place one at Resources/Audio/MenuMusic. " +
                 "Left empty on purpose so the game ships silent until real music arrives.")]
        [SerializeField] private AudioClip menuMusicClip;
        [Range(0f, 1f)] [SerializeField] private float menuMusicVolume = 0.3f;

        // Recordings live in Resources/Audio so they load by name at runtime, rather than needing
        // five clips dragged into the inspector. A missing file is not an error: the resolver below
        // falls through to the synthesised sound, so the game keeps its audio either way.
        private const string ResourceFolder = "Audio/";
        private const string KickA = "FootballKick01";
        private const string KickB = "FootballKick02";
        private const string Crowd = "CrowdCheer";
        private const string StartWhistle = "StartWhistle";
        private const string EndWhistle = "EndWhistle";
        private const string MenuMusic = "MenuMusic";
        private const string CountdownTickName = "CountdownTick";
        private const string CountdownGoName = "CountdownGo";

        [Header("Mix")]
        [Tooltip("Kick loudness. Allowed above 1 to amplify: the recording's own level sets the " +
                 "ceiling, and 1.0 is not necessarily loud enough to sit over the rest of the mix.")]
        [Range(0f, 3f)] [SerializeField] private float ballHitVolume = 1.4f;
        [Tooltip("How quiet the gentlest touch is, as a fraction of full volume. Raise this if soft " +
                 "dribbles are inaudible; lower it for more contrast between a nudge and a shot.")]
        [Range(0f, 1f)] [SerializeField] private float ballHitQuietest = 0.45f;
        [Range(0f, 1f)] [SerializeField] private float wallThudVolume = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float goalVolume = 0.7f;
        [Range(0f, 1f)] [SerializeField] private float whistleVolume = 0.45f;
        [Range(0f, 1f)] [SerializeField] private float uiVolume = 0.35f;
        [Tooltip("Loudness of the pre-match countdown beeps and the GO.")]
        [Range(0f, 1f)] [SerializeField] private float countdownVolume = 0.55f;

        [Header("Voices")]
        [Tooltip("How many sounds may overlap. A busy rally can start several impacts at once.")]
        [SerializeField] private int voices = 10;

        private AudioSource[] sources;
        private AudioSource musicSource;
        private int nextVoice;
        private MatchManager match;
        private bool useAltKick;

        private static readonly Dictionary<string, AudioClip> loaded = new Dictionary<string, AudioClip>();

        /// <summary>
        /// Picks the best available sound: an inspector override first, then a recording in
        /// Resources/Audio, then the synthesised fallback. Results are cached, since Resources.Load
        /// hits the asset database and this runs on every ball impact.
        /// </summary>
        private static AudioClip Resolve(AudioClip inspectorClip, string resourceName, AudioClip synthesised)
        {
            if (inspectorClip != null)
            {
                return inspectorClip;
            }

            if (!loaded.TryGetValue(resourceName, out AudioClip clip))
            {
                clip = Resources.Load<AudioClip>(ResourceFolder + resourceName);
                loaded[resourceName] = clip; // caches null too — a missing file is asked for once
            }

            return clip != null ? clip : synthesised;
        }

        private static GameSfx instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;

            // A pool, because PlayOneShot on a single source is fine but a rally can start several
            // impacts in the same frame and one source would cut its own tail off.
            sources = new AudioSource[Mathf.Max(1, voices)];
            for (int i = 0; i < sources.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // 2D: the table fills the screen, panning adds nothing

                // The pause menu sets AudioListener.pause, which would silence the very buttons
                // being pressed. Effects opt out of it. Gameplay sounds are unaffected either way,
                // since a frozen table produces no impacts to hear.
                source.ignoreListenerPause = true;

                sources[i] = source;
            }

            // Music lives on its own source: it loops, needs a stable clip reference, and must not
            // fight the one-shot pool for a voice slot every frame.
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            musicSource.ignoreListenerPause = true;

            if (FindAnyObjectByType<AudioListener>() == null)
            {
                Debug.LogWarning($"{name}: no AudioListener in the scene — nothing will be audible. " +
                                 "Add one to the Main Camera.", this);
            }
        }

        private void Start()
        {
            // Subscribed to rather than called from MatchManager, so the match logic stays unaware
            // that anything is listening — the same way the HUD attaches to it.
            match = FindAnyObjectByType<MatchManager>();
            if (match != null)
            {
                match.GoalScored += OnGoalScored;
                match.KickedOff += OnKickedOff;
                match.FullTime += OnFullTime;
            }
        }

        private void OnDestroy()
        {
            if (match != null)
            {
                match.GoalScored -= OnGoalScored;
                match.KickedOff -= OnKickedOff;
                match.FullTime -= OnFullTime;
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnGoalScored(Team scorer) => PlayGoal();

        private void OnKickedOff() => PlayWhistle();

        private void OnFullTime() => PlayFullTime();

        // ---------- static entry points ----------

        public static void PlayBallHit(float strength01)
        {
            if (instance == null) return;

            // Alternate the two kick recordings. One sample repeated through a fast rally reads as a
            // machine gun; swapping between two breaks that up before the ear notices a pattern.
            instance.useAltKick = !instance.useAltKick;

            AudioClip clip = instance.useAltKick
                ? Resolve(instance.ballHitClipAlt, KickB, SfxLibrary.BallHit)
                : Resolve(instance.ballHitClip, KickA, SfxLibrary.BallHit);

            instance.Play(clip,
                          instance.ballHitVolume * Mathf.Lerp(instance.ballHitQuietest, 1f, strength01),
                          // Harder hits ring higher, softer ones duller — one clip covers the range.
                          Mathf.Lerp(0.88f, 1.22f, strength01));
        }

        public static void PlayWallThud(float strength01)
        {
            if (instance == null) return;
            // Deliberately still synthesised: keeping rails audibly distinct from the kick
            // recordings is what lets you tell a wall bounce from a player strike by ear.
            instance.Play(instance.wallThudClip ?? SfxLibrary.WallThud,
                          instance.wallThudVolume * Mathf.Lerp(0.2f, 1f, strength01),
                          Mathf.Lerp(0.9f, 1.12f, strength01));
        }

        public static void PlayGoal()
        {
            if (instance == null) return;
            instance.Play(Resolve(instance.goalClip, Crowd, SfxLibrary.Goal), instance.goalVolume, 1f);
        }

        /// <summary>Kick-off. Distinct from full time, which has its own recording.</summary>
        public static void PlayWhistle()
        {
            if (instance == null) return;
            instance.Play(Resolve(instance.kickOffWhistleClip, StartWhistle, SfxLibrary.Whistle),
                          instance.whistleVolume, 1f);
        }

        /// <summary>Full time. Falls back to the kick-off whistle if no end recording is present.</summary>
        public static void PlayFullTime()
        {
            if (instance == null) return;
            instance.Play(Resolve(instance.fullTimeWhistleClip, EndWhistle, SfxLibrary.Whistle),
                          instance.whistleVolume, 1f);
        }

        public static void PlayUiClick()
        {
            if (instance == null) return;
            instance.Play(instance.uiClickClip ?? SfxLibrary.UiClick, instance.uiVolume, 1f);
        }

        /// <summary>One "3", "2" or "1" of the pre-match countdown.</summary>
        public static void PlayCountdownTick()
        {
            if (instance == null) return;
            instance.Play(Resolve(instance.countdownTickClip, CountdownTickName, SfxLibrary.CountdownTick),
                          instance.countdownVolume, 1f);
        }

        /// <summary>The "GO!" that starts play.</summary>
        public static void PlayCountdownGo()
        {
            if (instance == null) return;
            instance.Play(Resolve(instance.countdownGoClip, CountdownGoName, SfxLibrary.CountdownGo),
                          instance.countdownVolume, 1f);
        }

        /// <summary>
        /// Starts the menu music loop. Silent until a clip is assigned on the inspector or dropped
        /// into Resources/Audio/MenuMusic — the placeholder is intentional so the game ships without
        /// stock filler music. Safe to call every time the menu opens; already-playing loops are not
        /// restarted.
        /// </summary>
        public static void PlayMenuMusic()
        {
            if (instance == null || instance.musicSource == null) return;

            AudioClip clip = instance.menuMusicClip;
            if (clip == null)
            {
                if (!loaded.TryGetValue(MenuMusic, out clip))
                {
                    clip = Resources.Load<AudioClip>(ResourceFolder + MenuMusic);
                    loaded[MenuMusic] = clip;
                }
            }
            if (clip == null) return; // no music available — stay silent

            if (instance.musicSource.clip == clip && instance.musicSource.isPlaying) return;

            instance.musicSource.clip = clip;
            instance.musicSource.volume = instance.menuMusicVolume * GameAudio.Music;
            instance.musicSource.Play();
        }

        /// <summary>
        /// Pushes the saved music level onto the playing loop. Called by the settings slider, so the
        /// change is audible while it is being dragged rather than only on the next track.
        /// </summary>
        public static void ApplyMusicVolume()
        {
            if (instance == null || instance.musicSource == null) return;
            instance.musicSource.volume = instance.menuMusicVolume * GameAudio.Music;
        }

        /// <summary>Stops the menu music. Safe to call when nothing is playing.</summary>
        public static void StopMenuMusic()
        {
            if (instance == null || instance.musicSource == null) return;
            instance.musicSource.Stop();
        }

        /// <summary>Clears the Resources cache. Only needed if clips are added while playing.</summary>
        public static void ForgetLoadedClips() => loaded.Clear();

        // ---------- playback ----------

        private void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || sources == null || sources.Length == 0)
            {
                return;
            }

            AudioSource source = sources[nextVoice];
            nextVoice = (nextVoice + 1) % sources.Length;

            source.pitch = pitch;

            // Not clamped to 1: PlayOneShot amplifies above it, which is the only way to lift a
            // quietly-recorded sample over the rest of the mix. The ceiling is where the clip starts
            // to distort, which depends on the recording, so it is left to the mix values above.
            source.PlayOneShot(clip, Mathf.Max(0f, volume) * GameAudio.Sfx);
        }
    }
}
