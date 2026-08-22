using System;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Which job a rod does. Assigned from the rod's figure count where possible (1 = keeper,
    /// 2 = defense, 5 = midfield, 3 = attack), because that is what actually identifies a foosball
    /// rod — the old depth-index heuristic silently never produced a Midfield on a 4-rod team.
    /// </summary>
    public enum RodRole { Goalkeeper, Defense, Midfield, Attack }

    /// <summary>What a rod is doing with its spin right now. See RodAgent for the transitions.</summary>
    public enum ShotState
    {
        /// <summary>Free to position and free to start a shot.</summary>
        Idle,
        /// <summary>Feinting — deliberately sitting on a decoy position before committing.</summary>
        Preparing,
        /// <summary>Committed. Nothing may interrupt the swing until the arc is complete.</summary>
        Striking,
        /// <summary>Swing done, bringing the figures back upright before it may strike again.</summary>
        Recovering
    }

    /// <summary>The kind of contact a rod is going for. Drives power, aim offset and follow-through.</summary>
    public enum ShotKind { Clear, Pass, Straight, Angled, Push, Roll, Lunge }

    /// <summary>
    /// A committed shot. Produced by a behaviour, then executed by <see cref="RodAgent"/> without
    /// further consultation — that "decide once, then commit" split is what stops the swing being
    /// re-decided every frame and abandoned partway.
    /// </summary>
    public struct ShotPlan
    {
        public ShotKind Kind;

        /// <summary>Sustained swing speed, degrees/second. Sets how hard the ball leaves.</summary>
        public float SwingSpeed;

        /// <summary>Total arc the rod commits to turning, degrees. 360 = a full follow-through.</summary>
        public float FollowThrough;

        /// <summary>
        /// Where to stand relative to the ball, in metres along the bar. The ball squirts away
        /// from the figure that hits it, so standing off-centre is how a shot gets angled — this
        /// is real contact geometry, not a fake velocity nudge.
        /// </summary>
        public float AimOffset;

        /// <summary>Seconds to sit on a decoy position before committing. 0 = no fake.</summary>
        public float FakeDelay;

        /// <summary>Where to stand during the fake, in metres along the bar, relative to the ball.</summary>
        public float FakeOffset;
    }

    /// <summary>
    /// The tunables every rod has, whatever its job. One instance lives inside each role's tuning
    /// block, so a keeper's hands can be quick while its shot is soft without touching code.
    /// </summary>
    [Serializable]
    public class RodProfile
    {
        [Header("Hands (movement)")]
        [Tooltip("How fast this rod slides along its bar, m/s. This is the 'hand speed' cap — a " +
                 "five-man rod is heavier to shift than a one-man keeper.")]
        public float slideSpeed = 1.5f;

        [Tooltip("How far ahead of the ball this rod aims, in seconds. Higher meets the ball; " +
                 "lower trails it.")]
        [Range(0f, 0.5f)] public float interceptLead = 0.12f;

        [Tooltip("How fast this rod blends between its on-ball and off-ball targets, per second. " +
                 "This is the handoff smoothing: low values make control pass between rods " +
                 "gradually instead of snapping.")]
        public float engagementRate = 6f;

        [Tooltip("How much closer another figure must be to the target, in metres, before this rod " +
                 "switches which figure it steers with. Zero makes a multi-figure rod jump by a " +
                 "whole figure-spacing whenever the target passes the midpoint between two figures, " +
                 "which looks like violent random movement. Roughly a quarter of the figure spacing " +
                 "is a good value.")]
        public float figureSwitchMargin = 0.03f;

        [Header("Striking")]
        [Tooltip("How near the ball must be to this rod's line, in metres of depth, before it can strike.")]
        public float strikeDistance = 0.09f;

        [Tooltip("How near a figure must be to the ball along the bar, in metres, before it swings. " +
                 "This is the 'ball is actually in my lane' test that replaces spinning on spec.")]
        public float laneWidth = 0.035f;

        [Tooltip("How fast the rod is swept through a shot, in degrees/second.\n\n" +
                 "This is a SUSTAINED swing speed, not a flick that decays: the AI drives the rod " +
                 "through the shot the same way a finger does. BallController scores a strike off " +
                 "MeasuredSpinSpeed against its Strike Full Spin (1800°/s by default), so this is " +
                 "directly how hard the ball leaves. Around 1800 is a full-power shot.")]
        public float swingSpeed = 1400f;

        [Tooltip("Minimum seconds between this rod's shots.")]
        public float kickCooldown = 0.7f;

        [Tooltip("Leave the ball alone once it is already heading at the opponent's goal this " +
                 "fast, in m/s.")]
        public float leaveAloneSpeed = 0.5f;

        [Tooltip("May this rod strike even when it is not the active rod? True for the keeper — " +
                 "the last line never stands and watches.\n\n" +
                 "Has no effect while TeamAI's 'Only Play Nearest Rod' is on, since a rod that is " +
                 "not the active one is held completely still. It matters only in the four-hands " +
                 "formation mode.")]
        public bool strikeWhenInactive = false;

        [Header("Follow-through")]
        [Tooltip("Degrees the rod commits to turning once a shot starts. 360 is a full rotation " +
                 "that ends upright. Anything under 180 makes the rod visibly stop and rewind.")]
        public float followThroughDegrees = 360f;

        [Tooltip("Hard ceiling on a single swing, in seconds, so a jammed rod can never lock up.\n\n" +
                 "Must comfortably exceed followThroughDegrees / swingSpeed AT THE LOWEST " +
                 "DIFFICULTY, which is where swings are slowest — an Easy keeper turns at about " +
                 "375°/s, so a full rotation takes nearly a second. Set this too low and the " +
                 "timeout fires mid-swing and cuts the follow-through short, which is the exact " +
                 "thing the commit exists to prevent.")]
        public float maxFollowThroughTime = 1.6f;

        [Tooltip("How fast the figures swing back upright after the arc completes, degrees/second.")]
        public float recoverSpinSpeed = 420f;

        [Tooltip("Only start settling once the spin has decayed below this, degrees/second.")]
        public float recoverSpinThreshold = 90f;
    }

    /// <summary>
    /// Keeper: reactive lateral tracking, tight to the goal line, minimal forward commitment, and
    /// the occasional timed lunge.
    /// </summary>
    [Serializable]
    public class GoalkeeperTuning
    {
        public RodProfile movement = new RodProfile
        {
            slideSpeed = 1.9f,
            interceptLead = 0.09f,
            engagementRate = 9f,
            strikeDistance = 0.075f,
            laneWidth = 0.040f,
            swingSpeed = 1250f,
            kickCooldown = 0.55f,
            leaveAloneSpeed = 0.35f,
            strikeWhenInactive = true,
            followThroughDegrees = 360f
        };

        [Header("Goalkeeper")]
        [Tooltip("How far past the goalposts the keeper will stray, as a multiple of the goal's " +
                 "half-width. 1 = never leaves the mouth. Keeping this near 1 is what 'minimal " +
                 "forward commitment' means for a rod that cannot move forward.")]
        [Range(0.5f, 2f)] public float goalMouthCoverage = 1.15f;

        [Tooltip("How strongly the keeper returns to the centre of its goal when the ball is not " +
                 "a threat. 1 = snaps to centre, 0 = stays wherever it last was.")]
        [Range(0f, 1f)] public float centreReturn = 0.7f;

        [Tooltip("Ball must be closing on the goal within this distance, in metres, before a " +
                 "lunge is considered.")]
        public float lungeRange = 0.35f;

        [Tooltip("Chance of committing to a lunge when a shot comes in, 0..1. A lunge covers more " +
                 "ground than a normal slide but is wrong when it is wrong.")]
        [Range(0f, 1f)] public float lungeChance = 0.35f;

        [Tooltip("How far past the predicted crossing point a lunge over-commits, in metres.")]
        public float lungeOvershoot = 0.02f;

        [Tooltip("Slide speed multiplier while lunging.")]
        public float lungeSpeedBoost = 1.6f;

        [Tooltip("How long a lunge holds before the keeper reverts to normal tracking, in seconds.")]
        public float lungeDuration = 0.28f;

        [Header("Difficulty scaling")]
        [Tooltip("The keeper carries the widest swing of any rod. Easy is meant to be beatable, " +
                 "and a beatable team is one whose LAST line can be beaten — not one whose attack " +
                 "has stopped working.")]
        public RodDifficulty difficulty = new RodDifficulty
        {
            reactionDelay = new DifficultyRange(0.55f, 0.045f),
            predictionAccuracy = new DifficultyRange(0.25f, 0.95f),
            aimError = new DifficultyRange(0.075f, 0.006f),
            handSpeed = new DifficultyRange(0.55f, 1.35f),
            spinPower = new DifficultyRange(0.30f, 1.15f) { response = DifficultyRange.EasyBiased() },
            strikeCooldownScale = new DifficultyRange(1.5f, 0.75f),
            decisionQuality = new DifficultyRange(0.50f, 0.95f),
            unforcedErrorRate = new DifficultyRange(0.22f, 0.010f),
            // The keeper still mostly reacts even at Easy. A last line that simply ignores a shot
            // reads as the game being broken rather than as the AI being weak — its weakness is
            // meant to come from being late and misaligned, not absent.
            blockReadChance = new DifficultyRange(0.82f, 1f),
            blockAlignmentError = new DifficultyRange(0.050f, 0.004f),
            // The keeper stays the tightest tracker at every level - a last line that wanders
            // reads as broken rather than as easy. Its Easy weakness is being late and
            // misaligned, which the two ranges above already deliver.
            trackTightness = new DifficultyRange(0.72f, 1f),
            offBallShade = new DifficultyRange(0.35f, 0.60f)
        };
    }

    /// <summary>
    /// Defense: blocks the lane to our goal rather than chasing the ball, and clears wide instead
    /// of squaring the ball across its own box.
    /// </summary>
    [Serializable]
    public class DefenseTuning
    {
        public RodProfile movement = new RodProfile
        {
            slideSpeed = 1.6f,
            interceptLead = 0.11f,
            engagementRate = 6.5f,
            strikeDistance = 0.085f,
            laneWidth = 0.038f,
            swingSpeed = 1300f,
            kickCooldown = 0.65f,
            leaveAloneSpeed = 0.45f,
            followThroughDegrees = 360f
        };

        [Header("Defense")]
        [Tooltip("How much this rod plays the lane from the ball to our goal rather than the ball " +
                 "itself. 1 = pure lane blocking, 0 = pure ball chasing. High is what makes it " +
                 "read as defending rather than following.")]
        [Range(0f, 1f)] public float laneBlockWeight = 0.75f;

        [Tooltip("How far outside the ball to stand when clearing, in metres. The ball leaves away " +
                 "from the figure, so standing inside it sends the clearance wide instead of " +
                 "squaring it across our own goal.")]
        public float clearToSideBias = 0.022f;

        [Tooltip("Ball within this distance, in metres, counts as pressure and switches the rod " +
                 "from lane-holding to actively winning the ball.")]
        public float pressureDistance = 0.13f;

        [Tooltip("Power multiplier on a clearance under pressure — get it away, do not finesse it.")]
        public float clearPowerScale = 1.05f;

        [Header("Difficulty scaling")]
        public RodDifficulty difficulty = new RodDifficulty
        {
            reactionDelay = new DifficultyRange(0.48f, 0.05f),
            predictionAccuracy = new DifficultyRange(0.35f, 0.92f),
            aimError = new DifficultyRange(0.055f, 0.007f),
            handSpeed = new DifficultyRange(0.65f, 1.25f),
            spinPower = new DifficultyRange(0.32f, 1.15f) { response = DifficultyRange.EasyBiased() },
            strikeCooldownScale = new DifficultyRange(1.45f, 0.75f),
            decisionQuality = new DifficultyRange(0.45f, 0.93f),
            unforcedErrorRate = new DifficultyRange(0.18f, 0.010f),
            // The big one. At Easy this rod fails to come across for more than half of all
            // attacks, which is what actually opens the table up for the player.
            blockReadChance = new DifficultyRange(0.42f, 1f),
            blockAlignmentError = new DifficultyRange(0.075f, 0.006f),
            trackTightness = new DifficultyRange(0.45f, 0.92f),
            offBallShade = new DifficultyRange(0.25f, 0.55f)
        };
    }

    /// <summary>
    /// Midfield: holds spacing across the bar, feeds the attack when a lane opens, and only rarely
    /// takes the low-percentage long shot.
    /// </summary>
    [Serializable]
    public class MidfieldTuning
    {
        public RodProfile movement = new RodProfile
        {
            slideSpeed = 1.35f,
            interceptLead = 0.13f,
            engagementRate = 5.5f,
            strikeDistance = 0.09f,
            laneWidth = 0.036f,
            swingSpeed = 1400f,
            kickCooldown = 0.7f,
            leaveAloneSpeed = 0.5f,
            followThroughDegrees = 360f
        };

        [Header("Midfield")]
        [Tooltip("How wide a passing lane must be clear of opponent figures, in metres, before " +
                 "this rod will play the ball forward.")]
        public float passLaneWidth = 0.055f;

        [Tooltip("Swing speed of a pass forward, in degrees/second. Deliberately well below a shot " +
                 "— a pass the attack rod cannot control is just a giveaway.")]
        public float passSwingSpeed = 950f;

        [Tooltip("Chance of taking a long shot when the lane to goal happens to be clear, 0..1. " +
                 "Low on purpose: from midfield it is a low-percentage ball.")]
        [Range(0f, 1f)] public float longShotChance = 0.12f;

        [Tooltip("Swing speed of a long shot from midfield, in degrees/second.")]
        public float longShotSwingSpeed = 1550f;

        [Tooltip("How much the row shades toward the ball's side while holding spacing, versus " +
                 "staying central and available to receive.")]
        [Range(0f, 1f)] public float spacingBias = 0.45f;

        [Header("Difficulty scaling")]
        [Tooltip("Midfield carries the widest DECISION swing: judging whether a passing lane is " +
                 "really open is the skill this rod is actually made of, so that is what scales " +
                 "most here rather than its hands.")]
        public RodDifficulty difficulty = new RodDifficulty
        {
            reactionDelay = new DifficultyRange(0.42f, 0.06f),
            predictionAccuracy = new DifficultyRange(0.35f, 0.90f),
            aimError = new DifficultyRange(0.050f, 0.009f),
            handSpeed = new DifficultyRange(0.70f, 1.20f),
            spinPower = new DifficultyRange(0.33f, 1.12f) { response = DifficultyRange.EasyBiased() },
            strikeCooldownScale = new DifficultyRange(1.4f, 0.80f),
            decisionQuality = new DifficultyRange(0.35f, 0.95f),
            unforcedErrorRate = new DifficultyRange(0.16f, 0.010f),
            blockReadChance = new DifficultyRange(0.40f, 0.98f),
            blockAlignmentError = new DifficultyRange(0.070f, 0.008f),
            // The five-man row. Its figures already overlap enough to cover the whole width,
            // so this one number decides whether midfield can be passed at all - which is why
            // it swings further here than on any other rod.
            trackTightness = new DifficultyRange(0.38f, 0.95f),
            offBallShade = new DifficultyRange(0.15f, 0.45f)
        };
    }

    /// <summary>
    /// Attack: the scoring rod. Picks a corner from where the opponent keeper actually is, varies
    /// the contact, and sometimes fakes before committing.
    /// </summary>
    [Serializable]
    public class OffenseTuning
    {
        public RodProfile movement = new RodProfile
        {
            slideSpeed = 1.75f,
            interceptLead = 0.14f,
            engagementRate = 7f,
            strikeDistance = 0.095f,
            laneWidth = 0.042f,
            swingSpeed = 1600f,
            kickCooldown = 0.6f,
            leaveAloneSpeed = 0.6f,
            followThroughDegrees = 360f,
            // Headroom for the 540° roll shot below. At Easy that turns at roughly 390°/s, so the
            // slowest legitimate swing on the team takes about 1.4 s.
            maxFollowThroughTime = 1.8f
        };

        [Header("Shot selection (relative weights)")]
        [Tooltip("Straight: square contact, hardest, most predictable.")]
        public float straightWeight = 1f;

        [Tooltip("Angled/pull: stands off-centre to cut the ball toward a corner.")]
        public float angledWeight = 1.3f;

        [Tooltip("Push: takes the near corner instead of the far one, against a cheating keeper.")]
        public float pushWeight = 0.7f;

        [Tooltip("Roll: softer, rolled contact. Slower ball but a much wider aiming window.")]
        public float rollWeight = 0.5f;

        [Header("Aim")]
        [Tooltip("How far into the corner to aim, as a fraction of the goal's half-width.")]
        [Range(0f, 1f)] public float cornerBias = 0.72f;

        [Tooltip("Hard cap on how far off-centre a figure will stand to angle a shot, in metres. " +
                 "Past this it simply misses the ball, so this is a real accuracy/angle trade.")]
        public float maxAimOffset = 0.024f;

        [Tooltip("Power multiplier for a rolled shot.")]
        public float rollPowerScale = 0.72f;

        [Tooltip("Follow-through for a rolled shot, degrees. A roll carries further round.")]
        public float rollFollowThrough = 540f;

        [Header("Fakes")]
        [Tooltip("Chance of feinting before a shot, 0..1.")]
        [Range(0f, 1f)] public float fakeChance = 0.3f;

        [Tooltip("How long the feint holds before the real shot, in seconds.")]
        public float fakeDuration = 0.16f;

        [Tooltip("How far to the wrong side the feint sits, in metres.")]
        public float fakeOffset = 0.03f;

        [Header("Difficulty scaling")]
        [Tooltip("Deliberately the NARROWEST swing on the team. An Easy attack that cannot hit a " +
                 "shot reads as broken rather than easy — the difficulty should be felt in the " +
                 "keeper you can beat, not in an opponent that never threatens. Aim error still " +
                 "tightens a lot, because that is the axis a player actually perceives as skill.")]
        public RodDifficulty difficulty = new RodDifficulty
        {
            reactionDelay = new DifficultyRange(0.34f, 0.045f),
            predictionAccuracy = new DifficultyRange(0.45f, 0.95f),
            aimError = new DifficultyRange(0.045f, 0.005f),
            handSpeed = new DifficultyRange(0.75f, 1.30f),
            spinPower = new DifficultyRange(0.34f, 1.20f) { response = DifficultyRange.EasyBiased() },
            strikeCooldownScale = new DifficultyRange(1.35f, 0.70f),
            decisionQuality = new DifficultyRange(0.50f, 0.96f),
            unforcedErrorRate = new DifficultyRange(0.14f, 0.005f),
            // The attack rod does not block, so these never come into play for it.
            blockReadChance = new DifficultyRange(1f, 1f),
            blockAlignmentError = new DifficultyRange(0f, 0f),
            trackTightness = new DifficultyRange(0.45f, 0.95f),
            offBallShade = new DifficultyRange(0.20f, 0.50f)
        };
    }
}
