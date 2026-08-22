using System;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// One parameter's journey from Easy to Hard.
    ///
    /// Difficulty is a single 0..1 number, but every parameter reads it through its own range and
    /// its own curve. That is the whole point of Phase 2: the goalkeeper's reaction delay can
    /// collapse sharply between Easy and Normal while the attack's aim error tightens gently across
    /// the whole span, so "Easy" means a beatable keeper rather than a uniformly broken team.
    ///
    /// The curve is optional shaping on top. Left linear, the value simply interpolates.
    /// </summary>
    [Serializable]
    public class DifficultyRange
    {
        [Tooltip("Value at difficulty 0 (Easy).")]
        public float atEasy;

        [Tooltip("Value at difficulty 1 (Hard).")]
        public float atHard;

        [Tooltip("Shapes the path between Easy and Hard. Linear by default. Pull the middle down " +
                 "to keep a parameter easy for longer, up to make it sharpen early.")]
        public AnimationCurve response = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        /// <summary>Unity needs this to deserialize the class.</summary>
        public DifficultyRange() { }

        public DifficultyRange(float atEasy, float atHard)
        {
            this.atEasy = atEasy;
            this.atHard = atHard;
        }

        public float Evaluate(float difficulty01)
        {
            float t = Mathf.Clamp01(difficulty01);

            if (response != null && response.length > 0)
            {
                t = response.Evaluate(t);
            }

            return Mathf.LerpUnclamped(atEasy, atHard, t);
        }

        /// <summary>
        /// A response that falls away steeply toward Easy and is close to linear above Normal.
        ///
        /// For when only the BOTTOM of a range should change dramatically. Dropping atEasy on a
        /// linear range drags Normal halfway down with it; this lets Easy collapse while Normal and
        /// Hard stay where they were.
        /// </summary>
        /// <param name="atMidpoint">
        /// What the curve returns at difficulty 0.5. Above 0.5 the range is already most of the way
        /// to its Hard value by Normal, which is what keeps Normal untouched.
        /// </param>
        public static AnimationCurve EasyBiased(float atMidpoint = 0.68f)
        {
            float rise = atMidpoint / 0.5f;
            float fall = (1f - atMidpoint) / 0.5f;
            float through = (rise + fall) * 0.5f;

            return new AnimationCurve(
                new Keyframe(0f, 0f, 0f, rise),
                new Keyframe(0.5f, atMidpoint, through, through),
                new Keyframe(1f, 1f, fall, 0f));
        }
    }

    /// <summary>
    /// How one rod's limitations scale with difficulty. Every axis is independent and every axis is
    /// per rod, which is what stops difficulty being one number that makes the whole team uniformly
    /// better or worse.
    ///
    /// These are limitations, not skills: each one describes a way the rod falls short of perfect.
    /// A perfect rod would have zero reaction delay, accuracy 1, zero aim error and zero errors.
    /// </summary>
    [Serializable]
    public class RodDifficulty
    {
        [Tooltip("Seconds before this rod notices the ball has CHANGED DIRECTION. It always knows " +
                 "where the ball is — what lags is its read on where the ball is going, which is " +
                 "what being slow to react actually feels like.")]
        public DifficultyRange reactionDelay = new DifficultyRange(0.34f, 0.06f);

        [Tooltip("How well this rod extrapolates the ball, 0..1. Below 1 it under-leads (arriving " +
                 "late) and carries a wandering bias, so at low settings it guesses late and " +
                 "sometimes guesses wrong.")]
        public DifficultyRange predictionAccuracy = new DifficultyRange(0.35f, 0.92f);

        [Tooltip("How far off this rod's positioning and shot aim sit, in metres. This is the " +
                 "spread applied to where it stands and therefore to where the ball leaves.")]
        public DifficultyRange aimError = new DifficultyRange(0.055f, 0.007f);

        [Tooltip("Multiplier on this rod's slide speed — its 'hands'. Below 1 the rod physically " +
                 "cannot get across in time, which beats any amount of good decision-making.")]
        public DifficultyRange handSpeed = new DifficultyRange(0.65f, 1.25f);

        [Tooltip("Multiplier on shot power, and so on how fast the rod rotates through a strike. " +
                 "The follow-through is protected separately and never falls below what completes " +
                 "the swing — a soft shot must look soft, not look broken.")]
        public DifficultyRange spinPower = new DifficultyRange(0.82f, 1.10f);

        [Tooltip("Multiplier on the gap between this rod's strikes. Above 1 at Easy, so a weak AI " +
                 "cannot machine-gun the ball.")]
        public DifficultyRange strikeCooldownScale = new DifficultyRange(1.55f, 0.72f);

        [Tooltip("Chance of choosing the best available play rather than a weaker one, 0..1. " +
                 "Governs pass vs shoot vs clear, and which corner the attack picks.")]
        public DifficultyRange decisionQuality = new DifficultyRange(0.45f, 0.93f);

        [Tooltip("Chance that a committed shot is mistimed or whiffed outright, 0..1. Near zero at " +
                 "maximum difficulty.")]
        public DifficultyRange unforcedErrorRate = new DifficultyRange(0.18f, 0.01f);

        [Tooltip("Chance this rod READS an attack at all, 0..1, rolled once per attack when the " +
                 "opponent takes the ball. Fail the roll and the rod never comes across to block " +
                 "this time — it holds its shape and watches. Low settings should miss reads often; " +
                 "this is what makes an easy AI beatable in a way the player can see and exploit, " +
                 "rather than merely slow.")]
        public DifficultyRange blockReadChance = new DifficultyRange(0.45f, 1f);

        [Tooltip("How far off this rod lines up in front of the ball while blocking, in metres. " +
                 "Held for the whole attack rather than re-rolled, so it is a consistent gap on one " +
                 "side that a player can find and attack — not noise.")]
        public DifficultyRange blockAlignmentError = new DifficultyRange(0.075f, 0.006f);

        [Tooltip("How fully this rod commits to the ball's lateral position when it is the rod on " +
                 "the ball. 1 = sits exactly on it, lower = hedges toward its resting shape.\n\n" +
                 "This is the single most important number on a five-man rod, because that rod's " +
                 "figures already overlap enough to cover the whole width: how tightly it shadows " +
                 "the ball IS whether the opponent can get through at all. It used to be a flat " +
                 "constant on the profile, so the difficulty setting changed reactions and aim but " +
                 "never once loosened the wall.")]
        public DifficultyRange trackTightness = new DifficultyRange(0.40f, 0.95f);

        [Tooltip("How much this rod shades toward the ball when it is NOT the rod on the ball. " +
                 "0 = holds its shape and ignores the ball, 1 = tracks as if engaged.")]
        public DifficultyRange offBallShade = new DifficultyRange(0.18f, 0.55f);
    }

    /// <summary>
    /// One rod's limitations resolved at a specific difficulty. Computed when difficulty changes
    /// rather than every frame, so a behaviour reads plain numbers and never touches a curve.
    /// </summary>
    public struct ResolvedDifficulty
    {
        public float ReactionDelay;
        public float PredictionAccuracy;
        public float AimError;
        public float HandSpeed;
        public float SpinPower;
        public float StrikeCooldownScale;
        public float DecisionQuality;
        public float UnforcedErrorRate;
        public float BlockReadChance;
        public float BlockAlignmentError;
        public float TrackTightness;
        public float OffBallShade;

        public static ResolvedDifficulty From(RodDifficulty source, float difficulty01)
        {
            return new ResolvedDifficulty
            {
                ReactionDelay = Mathf.Max(source.reactionDelay.Evaluate(difficulty01), 0.001f),
                PredictionAccuracy = Mathf.Clamp01(source.predictionAccuracy.Evaluate(difficulty01)),
                AimError = Mathf.Max(source.aimError.Evaluate(difficulty01), 0f),
                HandSpeed = Mathf.Max(source.handSpeed.Evaluate(difficulty01), 0.05f),
                SpinPower = Mathf.Max(source.spinPower.Evaluate(difficulty01), 0.1f),
                StrikeCooldownScale = Mathf.Max(source.strikeCooldownScale.Evaluate(difficulty01), 0.05f),
                DecisionQuality = Mathf.Clamp01(source.decisionQuality.Evaluate(difficulty01)),
                UnforcedErrorRate = Mathf.Clamp01(source.unforcedErrorRate.Evaluate(difficulty01)),
                BlockReadChance = Mathf.Clamp01(source.blockReadChance.Evaluate(difficulty01)),
                BlockAlignmentError = Mathf.Max(source.blockAlignmentError.Evaluate(difficulty01), 0f),
                TrackTightness = Mathf.Clamp01(source.trackTightness.Evaluate(difficulty01)),
                OffBallShade = Mathf.Clamp01(source.offBallShade.Evaluate(difficulty01))
            };
        }

        /// <summary>A rod with no limitations at all. Used before difficulty has been applied.</summary>
        public static ResolvedDifficulty Perfect => new ResolvedDifficulty
        {
            ReactionDelay = 0.001f,
            PredictionAccuracy = 1f,
            AimError = 0f,
            HandSpeed = 1f,
            SpinPower = 1f,
            StrikeCooldownScale = 1f,
            DecisionQuality = 1f,
            UnforcedErrorRate = 0f,
            BlockReadChance = 1f,
            BlockAlignmentError = 0f,
            TrackTightness = 1f,
            OffBallShade = 0.5f
        };
    }
}
