using System;
using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Generates the game's sound effects as waveforms at runtime, so the game has audio without
    /// shipping a single audio file.
    ///
    /// Every sound here is percussive, which is what makes synthesis viable: a knock is a burst of
    /// noise plus a short pitched body under a fast decay envelope, and that is a handful of lines
    /// of maths rather than a recording. Sustained or musical sounds would not survive this
    /// treatment — those want real recordings, which is why <see cref="GameSfx"/> lets any of these
    /// be overridden by an AudioClip.
    ///
    /// Clips are built once on first use and cached. The noise source is deterministically seeded so
    /// a given sound is identical every run, rather than subtly different each launch.
    /// </summary>
    public static class SfxLibrary
    {
        private const int SampleRate = 44100;

        private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        /// <summary>Sharp knock — a figure striking the ball.</summary>
        public static AudioClip BallHit => Get("sfx_ball_hit", () => Knock(0.085f, 780f, 0.55f, 70f));

        /// <summary>Duller, lower thud — the ball meeting a rail or the table body.</summary>
        public static AudioClip WallThud => Get("sfx_wall_thud", () => Knock(0.13f, 240f, 0.75f, 34f));

        /// <summary>Bright rising sting for a goal.</summary>
        public static AudioClip Goal => Get("sfx_goal", () => Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.16f, 0.55f));

        /// <summary>Two-note referee whistle for kick-off.</summary>
        public static AudioClip Whistle => Get("sfx_whistle", Whistle2);

        // ---- UI palette ----
        // Every tap in the front end makes a faint sound, and what it sounds like says what the tap
        // did: a short high tick for an ordinary button, a falling one for going back, two rising
        // notes for a confirm, a sweep for a screen opening or closing. Kept quiet and short — they
        // are heard hundreds of times a session. Each is a sine or triangle sweep under a fast decay.

        /// <summary>Any button: a short high tick that falls a little.</summary>
        public static AudioClip UiTap => Get("sfx_ui_tap", () => Voices(0.06f, new Tone(0f, 1900f, 1300f, 0.05f, false)));

        /// <summary>Back and close: the same tick, falling further and a little longer.</summary>
        public static AudioClip UiBack => Get("sfx_ui_back", () => Voices(0.08f, new Tone(0f, 1300f, 650f, 0.07f, false)));

        /// <summary>Play, save, next: two rising notes.</summary>
        public static AudioClip UiConfirm => Get("sfx_ui_confirm", () => Voices(0.22f,
            new Tone(0f, 880f, 900f, 0.08f, true), new Tone(0.07f, 1320f, 1340f, 0.12f, true)));

        /// <summary>A screen or sheet opening: a quick upward sweep.</summary>
        public static AudioClip UiOpen => Get("sfx_ui_open", () => Voices(0.14f, new Tone(0f, 500f, 1400f, 0.12f, false)));

        /// <summary>A screen or sheet closing: the opening sweep, reversed.</summary>
        public static AudioClip UiClose => Get("sfx_ui_close", () => Voices(0.13f, new Tone(0f, 1400f, 500f, 0.11f, false)));

        /// <summary>A switch flipping: a very short click.</summary>
        public static AudioClip UiToggle => Get("sfx_ui_toggle", () => Voices(0.05f, new Tone(0f, 2600f, 2300f, 0.025f, true)));

        /// <summary>One step of a slider, or one reward landing: a plain short tone. Pitch it up in play.</summary>
        public static AudioClip UiStep => Get("sfx_ui_step", () => Voices(0.06f, new Tone(0f, 1000f, 1000f, 0.035f, false)));

        /// <summary>Claim or collect: four rising notes. Brighter than <see cref="Reward"/>, which is the quest chime.</summary>
        public static AudioClip UiReward => Get("sfx_ui_reward", () => Voices(0.46f,
            new Tone(0f, 1047f, 1047f, 0.18f, true), new Tone(0.06f, 1319f, 1319f, 0.18f, true),
            new Tone(0.12f, 1568f, 1568f, 0.18f, true), new Tone(0.18f, 2093f, 2093f, 0.18f, true)));

        /// <summary>Opening something destructive: two low falling notes.</summary>
        public static AudioClip UiWarn => Get("sfx_ui_warn", () => Voices(0.26f,
            new Tone(0f, 330f, 300f, 0.08f, true), new Tone(0.09f, 260f, 240f, 0.12f, true)));

        /// <summary>A disabled button pressed: one dull low note.</summary>
        public static AudioClip UiDeny => Get("sfx_ui_deny", () => Voices(0.13f, new Tone(0f, 220f, 200f, 0.09f, true)));

        /// <summary>The "3 - 2 - 1" beep. A clean sustained tone, not the near-instant UI click.</summary>
        public static AudioClip CountdownTick => Get("sfx_countdown_tick", () => Beep(0.18f, 700f));

        /// <summary>The "GO!" - higher and longer than a tick, so the start reads as an arrival.</summary>
        public static AudioClip CountdownGo => Get("sfx_countdown_go", () => Beep(0.40f, 1050f));

        /// <summary>
        /// A quest clearing: two notes, up. Shorter and drier than <see cref="Goal"/>, which is a
        /// four-note flourish over a crowd — a reward that announced itself as loudly as a goal would
        /// be competing with the result it is being handed out alongside.
        /// </summary>
        public static AudioClip Reward => Get("sfx_reward", () => Arpeggio(new[] { 659.25f, 987.77f }, 0.09f, 0.22f));

        private static AudioClip Get(string key, Func<AudioClip> build)
        {
            if (cache.TryGetValue(key, out AudioClip clip) && clip != null)
            {
                return clip;
            }

            clip = build();
            clip.name = key;
            cache[key] = clip;
            return clip;
        }

        private static AudioClip FromSamples(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// An impact: a noise transient for the "click" of contact over a pitched body for its
        /// weight, both under an exponential decay. Higher <paramref name="decay"/> is a tighter,
        /// harder hit.
        /// </summary>
        private static AudioClip Knock(float seconds, float frequency, float noiseAmount, float decay)
        {
            int count = Mathf.CeilToInt(SampleRate * seconds);
            var samples = new float[count];
            var random = new System.Random(1337);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-decay * t);

                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                // The noise burst is much shorter than the body: it is the strike itself, while the
                // tone underneath is the object ringing afterwards.
                float noiseEnvelope = Mathf.Exp(-decay * 6f * t);

                float body = Mathf.Sin(2f * Mathf.PI * frequency * t);
                // A slight downward pitch bend reads as a real impact rather than a beep.
                body += 0.5f * Mathf.Sin(2f * Mathf.PI * frequency * 0.5f * t);

                samples[i] = envelope * ((1f - noiseAmount) * body + noiseAmount * noise * noiseEnvelope);
            }

            Normalise(samples, 0.9f);
            return FromSamples("knock", samples);
        }

        /// <summary>A run of notes, each struck and left to ring — the goal sting.</summary>
        private static AudioClip Arpeggio(float[] frequencies, float noteSeconds, float tailSeconds)
        {
            int count = Mathf.CeilToInt(SampleRate * (frequencies.Length * noteSeconds + tailSeconds));
            var samples = new float[count];

            for (int n = 0; n < frequencies.Length; n++)
            {
                int start = Mathf.RoundToInt(n * noteSeconds * SampleRate);
                float frequency = frequencies[n];

                for (int i = start; i < count; i++)
                {
                    float t = (i - start) / (float)SampleRate;
                    float envelope = Mathf.Exp(-5.5f * t);
                    if (envelope < 0.001f)
                    {
                        break;
                    }

                    // A quiet octave above keeps it bright without raising the fundamental.
                    float tone = Mathf.Sin(2f * Mathf.PI * frequency * t)
                               + 0.3f * Mathf.Sin(4f * Mathf.PI * frequency * t);

                    samples[i] += envelope * tone * 0.45f;
                }
            }

            Normalise(samples, 0.85f);
            return FromSamples("arpeggio", samples);
        }

        /// <summary>Referee whistle: two shrill tones, short then long.</summary>
        private static AudioClip Whistle2()
        {
            const float shortBlast = 0.13f;
            const float gap = 0.06f;
            const float longBlast = 0.26f;

            int count = Mathf.CeilToInt(SampleRate * (shortBlast + gap + longBlast));
            var samples = new float[count];
            var random = new System.Random(99);

            AddBlast(samples, 0f, shortBlast, random);
            AddBlast(samples, shortBlast + gap, longBlast, random);

            Normalise(samples, 0.7f);
            return FromSamples("whistle", samples);
        }

        private static void AddBlast(float[] samples, float startSeconds, float seconds, System.Random random)
        {
            int start = Mathf.RoundToInt(startSeconds * SampleRate);
            int length = Mathf.RoundToInt(seconds * SampleRate);

            for (int i = 0; i < length && start + i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;

                // Soft attack and release, so the blast does not click at either end.
                float envelope = Mathf.Min(1f, t / 0.02f) * Mathf.Min(1f, (seconds - t) / 0.03f);
                envelope = Mathf.Max(0f, envelope);

                // The warble of the pea inside the whistle, and breath noise over the top: without
                // both it is just a sine tone.
                float warble = 2100f + 55f * Mathf.Sin(2f * Mathf.PI * 22f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * warble * t);
                float breath = (float)(random.NextDouble() * 2.0 - 1.0) * 0.12f;

                samples[start + i] += envelope * (tone * 0.8f + breath);
            }
        }

        /// <summary>One note of a UI sound: starts <c>Start</c> seconds in, sweeps from <c>From</c> to
        /// <c>To</c> Hz over <c>Length</c> seconds, at relative loudness <c>Gain</c>. Triangle is a
        /// touch warmer than sine.</summary>
        private readonly struct Tone
        {
            public readonly float Start, From, To, Length, Gain;
            public readonly bool Triangle;

            public Tone(float start, float from, float to, float length, bool triangle, float gain = 1f)
            {
                Start = start; From = from; To = to; Length = length; Triangle = triangle; Gain = gain;
            }
        }

        /// <summary>
        /// Mixes <paramref name="tones"/> into a clip <paramref name="seconds"/> long. Each note has a
        /// 4 ms attack so it does not click on, and decays exponentially to near silence by its end
        /// so it does not click off. The frequency sweep is exponential, which is how pitch is heard.
        /// </summary>
        private static AudioClip Voices(float seconds, params Tone[] tones)
        {
            int count = Mathf.CeilToInt(SampleRate * seconds);
            var samples = new float[count];

            foreach (Tone tone in tones)
            {
                int start = Mathf.RoundToInt(tone.Start * SampleRate);
                int length = Mathf.RoundToInt(tone.Length * SampleRate);
                double phase = 0.0;

                for (int i = 0; i < length && start + i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    float progress = i / (float)Mathf.Max(1, length - 1);

                    float frequency = tone.From * Mathf.Pow(tone.To / tone.From, progress);
                    phase += 2.0 * Mathf.PI * frequency / SampleRate;

                    float wave = tone.Triangle
                        ? (2f / Mathf.PI) * Mathf.Asin(Mathf.Sin((float)phase))
                        : Mathf.Sin((float)phase);

                    float attack = Mathf.Min(1f, t / 0.004f);
                    float decay = Mathf.Pow(0.02f, progress);
                    samples[start + i] += wave * attack * decay * tone.Gain;
                }
            }

            Normalise(samples, 0.5f);
            return FromSamples("ui", samples);
        }

        /// <summary>
        /// A clean beep with a real attack-sustain-release shape, for the countdown.
        ///
        /// Distinct from <see cref="Blip"/>, whose exponential envelope collapses in about 20 ms and
        /// reads as a click. A countdown wants a note you can hear land and hold, so this ramps up
        /// fast, sustains flat, and eases out — and adds a quiet octave above the fundamental so the
        /// tone has a little body rather than sounding like a test-equipment sine.
        /// </summary>
        private static AudioClip Beep(float seconds, float frequency)
        {
            int count = Mathf.CeilToInt(SampleRate * seconds);
            var samples = new float[count];

            float attack = 0.012f;
            float release = seconds * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;

                float envelope;
                if (t < attack)
                {
                    envelope = t / attack;
                }
                else if (t > seconds - release)
                {
                    envelope = Mathf.Clamp01((seconds - t) / release);
                }
                else
                {
                    envelope = 1f;
                }

                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t)
                             + 0.25f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t);

                samples[i] = envelope * tone;
            }

            Normalise(samples, 0.6f);
            return FromSamples("beep", samples);
        }

        /// <summary>Scales to a known peak, so one sound is not wildly louder than the next.</summary>
        private static void Normalise(float[] samples, float peak)
        {
            float max = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                max = Mathf.Max(max, Mathf.Abs(samples[i]));
            }

            if (max < 1e-5f)
            {
                return;
            }

            float scale = peak / max;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= scale;
            }
        }
    }
}
