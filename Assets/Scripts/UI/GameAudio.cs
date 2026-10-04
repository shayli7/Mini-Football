using UnityEngine;

namespace TableFootball
{
    /// <summary>AI strength presets chosen from the Settings panel.</summary>
    public enum AiLevel { Easy, Normal, Hard }

    /// <summary>
    /// Persistent game settings the UI writes and the rest of the game reads. Kept tiny and static
    /// so any script (SFX sources, AI) can read the current value without a reference to the UI.
    ///
    ///   Music   -> the looping menu music's own AudioSource, via GameSfx.
    ///   Sfx     -> a value your sound-effect AudioSources should multiply their volume by, or read
    ///              directly, e.g.  source.volume = clipVolume * GameAudio.Sfx;
    ///   Difficulty -> applied to every TeamAI in the scene via TeamAI.ApplyDifficulty.
    /// </summary>
    public static class GameAudio
    {
        const string MusicKey = "tf_music";
        const string SfxKey = "tf_sfx";
        const string DiffKey = "tf_difficulty";
        const string VibrationKey = "tf_vibration";
        const string TouchKey = "tf_touch_sensitivity";

        /// <summary>
        /// Menu music level. Drives the music AudioSource alone, not <c>AudioListener.volume</c> —
        /// a slider labelled "music" that also turned the whistle down would be lying about what it
        /// does. Effects have their own control in <see cref="Sfx"/>.
        /// </summary>
        public static float Music
        {
            get => PlayerPrefs.GetFloat(MusicKey, 0.8f);
            set
            {
                float v = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MusicKey, v);
                GameSfx.ApplyMusicVolume(); // live, so the slider is audible while being dragged
            }
        }

        public static float Sfx
        {
            get => PlayerPrefs.GetFloat(SfxKey, 0.9f);
            set => PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
        }

        /// <summary>
        /// Whether the phone buzzes on ball contact. On by default. Held by <see cref="Haptics.Enabled"/>,
        /// which is the switch the rumble code actually checks; this is the saved copy of it.
        /// </summary>
        public static bool Vibration
        {
            get => PlayerPrefs.GetInt(VibrationKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(VibrationKey, value ? 1 : 0);
                Haptics.Enabled = value;
            }
        }

        /// <summary>The most touch-sensitivity steps Settings offers, from 1 (lowest) to this.</summary>
        public const int TouchSensitivitySteps = 5;

        private static int touchSensitivity;

        /// <summary>
        /// How strongly a finger drag turns a rod, 1 (lowest) to <see cref="TouchSensitivitySteps"/>.
        /// Middle by default. Cached, because <see cref="TouchSensitivityScale"/> is read every frame
        /// a finger is down.
        /// </summary>
        public static int TouchSensitivity
        {
            get
            {
                if (touchSensitivity == 0)
                {
                    touchSensitivity = Mathf.Clamp(PlayerPrefs.GetInt(TouchKey, 3), 1, TouchSensitivitySteps);
                }
                return touchSensitivity;
            }
            set
            {
                touchSensitivity = Mathf.Clamp(value, 1, TouchSensitivitySteps);
                PlayerPrefs.SetInt(TouchKey, touchSensitivity);
            }
        }

        /// <summary>What <see cref="TouchSensitivity"/> multiplies spin by: 0.6 at the lowest step, 1.0
        /// in the middle (so the default plays exactly as tuned), 1.4 at the highest.</summary>
        public static float TouchSensitivityScale => 0.6f + 0.2f * (TouchSensitivity - 1);

        public static AiLevel Difficulty
        {
            get => (AiLevel)PlayerPrefs.GetInt(DiffKey, (int)AiLevel.Normal);
            set { PlayerPrefs.SetInt(DiffKey, (int)value); ApplyDifficultyToScene(value); }
        }

        /// <summary>Push the saved settings into the live scene. Call once at startup.</summary>
        public static void ApplyAll()
        {
            // Held at full: the two sliders each own their own channel now, so the global listener
            // volume is no longer a control and must not sit at some stale saved value.
            AudioListener.volume = 1f;
            GameSfx.ApplyMusicVolume();
            Haptics.Enabled = Vibration;
            ApplyDifficultyToScene(Difficulty);
        }

        private static void ApplyDifficultyToScene(AiLevel level)
        {
            var ais = Object.FindObjectsByType<TeamAI>(FindObjectsSortMode.None);
            foreach (var ai in ais) if (ai != null) ai.ApplyDifficulty(level);
        }
    }
}
