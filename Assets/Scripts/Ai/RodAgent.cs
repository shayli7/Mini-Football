using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// One rod's working state and the machinery that drives it. The behaviour decides *what* to
    /// do; this decides *how*, and owns two things that must not live anywhere else: the rod's own
    /// belief about the ball, and the shot state machine.
    ///
    /// THE BELIEF. Each rod lags the ball independently. What lags is not the ball's POSITION —
    /// a player can see where the ball is — but its VELOCITY, which is the read on where the ball
    /// is going. That is what "reaction delay" actually is: a rod with a slow read keeps
    /// extrapolating the old direction for a moment after the ball turns, so it is late and wrong
    /// exactly when the ball changes direction, and untroubled when it does not. Lagging position
    /// instead would leave a slow rod unable to touch a stationary ball, which is not slowness, it
    /// is blindness.
    ///
    /// THE SHOT STATE MACHINE. This is the fix for "the rod stops partway through instead of
    /// following through". The old code re-decided the swing every frame: one frame flicked the
    /// rod, and every frame after that fell into a settle routine which zeroed the spin velocity
    /// the moment damping dropped it under 90°/s, then dragged the angle back to the *nearest*
    /// multiple of 360. The arc a shot actually covered was (power - 90) / damping, which for most
    /// role and difficulty combinations never reached 180°, so "nearest" meant backwards and the
    /// rod visibly rewound. Here a shot is committed once and is untouchable until its arc
    /// completes:
    ///
    ///   Idle       -> behaviour may propose a shot
    ///   Preparing  -> sitting on a decoy position (a fake, or a mistimed swing); already committed
    ///   Striking   -> spinning. Nothing re-decides. A velocity floor holds until the committed
    ///                 arc is covered, so a soft shot still turns the whole way round.
    ///   Recovering -> arc done, settling upright. Only now may the rod be interrupted again.
    ///
    /// Because a committed arc is a full 360° by default, the rod finishes where it started and
    /// settling is a couple of degrees rather than a rewind.
    /// </summary>
    public class RodAgent
    {
        public RodController Rod;
        public RodRole Role;
        public RodBehavior Behavior;

        /// <summary>Figure positions along the bar, relative to the rod at rest.</summary>
        public float[] FigureOffsets;
        public int FigureCount => FigureOffsets != null ? FigureOffsets.Length : 0;

        /// <summary>Lateral coordinate of the rod's pivot at rest. Slide is measured from here.</summary>
        public float RestLateral;

        /// <summary>Position down the table, and the same in "advancing" units.</summary>
        public float Depth;
        public float OrderedDepth;

        /// <summary>Spin sign that drives the ball at the opponent's goal.</summary>
        public float KickSign = 1f;

        /// <summary>
        /// 0 = holding its role shape, 1 = fully on the ball. Eased rather than switched, which is
        /// what makes control pass between rods as a handoff instead of a snap.
        /// </summary>
        public float Engagement;

        /// <summary>Set by TeamAI each frame: is this the rod the ball belongs to?</summary>
        public bool IsActive;

        /// <summary>This rod's limitations at the current difficulty.</summary>
        public ResolvedDifficulty Limits = ResolvedDifficulty.Perfect;

        // ---- this rod's belief about the ball ----

        /// <summary>Where the ball is. Known — a player can see it.</summary>
        public float BeliefLateral;
        public float BeliefOrderedDepth;

        /// <summary>Where this rod thinks the ball is GOING. This is what lags.</summary>
        public float BeliefLateralVelocity;
        public float BeliefAdvanceVelocity;

        private Vector3 believedVelocity;
        private Vector3 beliefAcceleration;

        /// <summary>A wandering misjudgement, so a poor read is wrong rather than merely late.</summary>
        public float PredictionBias;
        private float biasTarget;
        private float nextBiasTime;

        /// <summary>
        /// This rod's current aim error, in metres, and when it is next re-rolled. Per rod rather
        /// than per team so one rod being sloppy does not mean all four are sloppy in the same
        /// direction — a shared bias is something a player learns to exploit in about a minute.
        ///
        /// Both this and the prediction bias are EASED toward their freshly rolled values rather
        /// than snapped to them. A step change every re-roll is a teleport, and a rod holding
        /// formation has nothing else moving it, so the steps read as the rod twitching at random
        /// rather than as a player being slightly off.
        /// </summary>
        public float AimError;
        private float aimErrorTarget;
        public float NextErrorTime;

        /// <summary>Which figure the rod is currently steering with. See SlideToward.</summary>
        private int chosenFigure = -1;

        // ---- per-attack read (see UpdateAttackRead) ----

        /// <summary>
        /// Did this rod read the current attack at all? Rolled once per attack, not per frame:
        /// a rod that misses the read simply does not get in front of the ball this time.
        /// </summary>
        public bool ReadsAttack = true;

        /// <summary>
        /// How far off this rod lines up in front of the ball while blocking, in metres. Held for
        /// the whole attack so it is a consistent gap a player can attack, not noise.
        /// </summary>
        public float BlockAlignmentOffset;

        private int lastAttackId = -1;

        // ---- shot state ----
        public ShotState State = ShotState.Idle;
        public ShotPlan Plan;
        public float NextKickTime;
        private float stateDeadline;
        private float arcTurned;
        private float lastSpinAngle;
        private float spinSign = 1f;

        // ---- goalkeeper lunge state (read by GoalkeeperBehavior) ----
        public bool LungeActive;
        public float LungeUntil;
        public float LungeTarget;
        public bool LungeArmed = true;

        public RodProfile Profile => Behavior != null ? Behavior.Movement : null;

        /// <summary>Where this rod's figures stand right now, in world lateral units.</summary>
        public float FigureLateral(int index)
        {
            return RestLateral + FigureOffsets[index] + Rod.CurrentSlideMeters;
        }

        /// <summary>Smallest distance from any figure to a lateral position.</summary>
        public float NearestFigureGap(float lateral)
        {
            float best = float.MaxValue;
            float slide = Rod.CurrentSlideMeters;

            for (int i = 0; i < FigureOffsets.Length; i++)
            {
                float gap = Mathf.Abs(lateral - (RestLateral + FigureOffsets[i] + slide));
                if (gap < best)
                {
                    best = gap;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ belief

        /// <summary>
        /// Updates what this rod thinks the ball is doing. Position is taken as read; the direction
        /// of travel is chased with a lag equal to this rod's reaction delay.
        /// </summary>
        public void UpdateBelief(AiWorld world, float dt)
        {
            BeliefLateral = world.BallLateral;
            BeliefOrderedDepth = world.BallOrderedDepth;

            believedVelocity = Vector3.SmoothDamp(believedVelocity, world.BallVelocity,
                                                  ref beliefAcceleration, Limits.ReactionDelay);

            BeliefLateralVelocity = Vector3.Dot(believedVelocity, world.BarAxis);
            BeliefAdvanceVelocity = Vector3.Dot(believedVelocity, world.LongAxis * world.AttackSign);

            if (world.Now >= nextBiasTime)
            {
                // A poor read does not just arrive late, it arrives in the wrong place. The bias is
                // held for a while rather than re-rolled per frame, or it would average to nothing.
                float spread = (1f - Limits.PredictionAccuracy) * 0.09f;
                biasTarget = Random.Range(-spread, spread);
                nextBiasTime = world.Now + Random.Range(0.35f, 0.75f);
            }

            // Eased, not snapped — a step change here is a teleport, and on a rod holding formation
            // it is the only thing moving, so it reads as a twitch.
            PredictionBias = Mathf.MoveTowards(PredictionBias, biasTarget, 0.25f * dt);
            AimError = Mathf.MoveTowards(AimError, aimErrorTarget, 0.25f * dt);
        }

        /// <summary>Rolls a fresh aim error. Eased in by <see cref="UpdateBelief"/>.</summary>
        public void RollAimError()
        {
            aimErrorTarget = Random.Range(-Limits.AimError, Limits.AimError);
        }

        /// <summary>
        /// Once per attack, decide whether this rod reads it — and if so, how well it lines up.
        ///
        /// This is a discrete gamble per attack rather than continuous noise, and that is the whole
        /// point. A rod that is continuously a bit wrong just looks jittery; a rod that occasionally
        /// fails to come across at all gives the player a specific, winnable opening, and a rod
        /// whose alignment is off by a fixed few centimetres for the whole attack gives them a side
        /// to aim at. Both are legible. Noise is not.
        /// </summary>
        public void UpdateAttackRead(AiWorld world)
        {
            if (world.AttackId == lastAttackId)
            {
                return;
            }

            lastAttackId = world.AttackId;
            ReadsAttack = Random.value < Limits.BlockReadChance;
            BlockAlignmentOffset = Random.Range(-Limits.BlockAlignmentError, Limits.BlockAlignmentError);
        }

        /// <summary>
        /// Where this rod thinks the ball will cross a depth line.
        ///
        /// Prediction accuracy shortens the lookahead rather than scattering the answer: a rod that
        /// reads the game poorly under-leads, so it arrives behind the ball. That is what guessing
        /// late looks like. The held bias on top is what guessing wrong looks like.
        ///
        /// Phase 3 replaces the straight line here with a bounce-aware walk; every behaviour
        /// already asks the question this way, so none of them will need to change when it does.
        /// </summary>
        public float PredictLateralAt(float targetOrderedDepth, float maxPredictTime)
        {
            if (Mathf.Abs(BeliefAdvanceVelocity) < 0.01f)
            {
                return BeliefLateral + PredictionBias;
            }

            float time = (targetOrderedDepth - BeliefOrderedDepth) / BeliefAdvanceVelocity;
            if (time <= 0f)
            {
                return BeliefLateral + PredictionBias;
            }

            time = Mathf.Min(time, maxPredictTime) * Limits.PredictionAccuracy;
            return BeliefLateral + BeliefLateralVelocity * time + PredictionBias;
        }

        /// <summary>Does this rod pick the best available play this time?</summary>
        public bool ChoosesWell()
        {
            return Random.value < Limits.DecisionQuality;
        }

        // ------------------------------------------------------------------ movement

        /// <summary>
        /// Slides so that whichever figure needs the least travel ends up on <paramref name="lateral"/>.
        ///
        /// Aims at an ABSOLUTE slide position: offsets are measured from the rod's resting pivot,
        /// which never moves, so feeding them back as a per-frame delta would accumulate and slam
        /// the rod between its limits.
        /// </summary>
        public void SlideToward(float lateral)
        {
            float range = Rod.SlideRangeMeters;
            if (range <= Mathf.Epsilon || FigureOffsets == null || FigureOffsets.Length == 0)
            {
                return;
            }

            int best = 0;
            float bestGap = float.MaxValue;

            for (int i = 0; i < FigureOffsets.Length; i++)
            {
                float gap = Mathf.Abs(lateral - (RestLateral + FigureOffsets[i]));
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }

            // Stay with the figure already being used unless another is clearly better.
            //
            // Without this the rod tears itself apart. Whenever the target sits near the midpoint
            // between two figures, "nearest" flips from frame to frame, and because the two
            // solutions are a whole figure-spacing apart the commanded slide JUMPS by that spacing
            // every time it flips. On a five-man rod that is a huge, fast, apparently random
            // movement — and it hits the rods AWAY from the ball hardest, because their targets sit
            // near the middle of the bar where several figures are near-equidistant. A one-figure
            // keeper has nothing to flip between, which is why it alone looked stable.
            float switchMargin = Mathf.Max(Profile != null ? Profile.figureSwitchMargin : 0.03f, 0f);

            if (chosenFigure >= 0 && chosenFigure < FigureOffsets.Length)
            {
                float incumbentShift = lateral - (RestLateral + FigureOffsets[chosenFigure]);

                // Unless the incumbent physically cannot reach, in which case switching is right.
                bool incumbentCanReach = Mathf.Abs(incumbentShift) <= range * 0.5f;

                if (incumbentCanReach && bestGap > Mathf.Abs(incumbentShift) - switchMargin)
                {
                    best = chosenFigure;
                }
            }

            chosenFigure = best;
            Rod.SetSlide01(0.5f + (lateral - (RestLateral + FigureOffsets[best])) / range);
        }

        /// <summary>Blends engagement toward where TeamAI says this rod should be.</summary>
        public void UpdateEngagement(float dt)
        {
            RodProfile profile = Profile;
            if (profile == null)
            {
                return;
            }

            float goal = IsActive ? 1f : Limits.OffBallShade;
            Engagement = Mathf.MoveTowards(Engagement, goal, profile.engagementRate * dt);
        }

        // ------------------------------------------------------------------ shooting

        /// <summary>True while the rod is committed to a swing and must not be re-tasked.</summary>
        public bool Committed => State == ShotState.Preparing || State == ShotState.Striking;

        /// <summary>
        /// Commits to a shot. From here the plan is executed without further consultation.
        ///
        /// Difficulty lands here, at the moment of commitment, rather than being smeared over the
        /// behaviours: power is scaled, aim is spread, and the shot may be spoiled outright.
        /// </summary>
        public void BeginShot(ShotPlan plan, float now)
        {
            plan.SwingSpeed *= Limits.SpinPower;

            // The aim spread is a cone on the shot, not just on where the rod stands: it is applied
            // to the contact offset, which is what actually decides where the ball leaves.
            plan.AimOffset += Random.Range(-Limits.AimError, Limits.AimError) * 0.5f;

            if (Random.value < Limits.UnforcedErrorRate)
            {
                SpoilShot(ref plan);
            }

            Plan = plan;
            spinSign = KickSign >= 0f ? 1f : -1f;

            if (plan.FakeDelay > 0f)
            {
                State = ShotState.Preparing;
                stateDeadline = now + plan.FakeDelay;
            }
            else
            {
                EnterStriking(now);
            }
        }

        /// <summary>
        /// An unforced error: the rod swings, but badly. Two ways for that to happen, because they
        /// look completely different and a difficulty setting that only ever produced one of them
        /// would read as a tic rather than as fallibility.
        ///
        ///   Mistimed - the swing is late, so the ball has moved on by the time the foot arrives.
        ///   Whiffed  - the rod stands too far off the ball and catches it thin, or misses it.
        ///
        /// Note neither one fakes a miss. The rod really does swing at the wrong moment or in the
        /// wrong place, and the physics decides what happens — the AI is never given a scripted
        /// failure any more than it is given a scripted goal.
        /// </summary>
        private void SpoilShot(ref ShotPlan plan)
        {
            if (Random.value < 0.5f)
            {
                plan.FakeDelay = Mathf.Max(plan.FakeDelay, Random.Range(0.09f, 0.20f));
                plan.FakeOffset = plan.AimOffset;
            }
            else
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                plan.AimOffset += side * Random.Range(0.022f, 0.042f);
                plan.SwingSpeed *= Random.Range(0.45f, 0.75f);
            }
        }

        private void EnterStriking(float now)
        {
            RodProfile profile = Profile;
            State = ShotState.Striking;
            arcTurned = 0f;
            lastSpinAngle = Rod.CurrentSpinAngle;
            stateDeadline = now + profile.maxFollowThroughTime;

            // No flick. The swing is driven by hand in TickStriking, the same way a finger drives
            // it — see the note there.
            Rod.SetSpinVelocity(0f);
        }

        /// <summary>
        /// Advances the swing. Called every frame while the rod is not Idle.
        /// Returns the lateral the rod should be holding, or float.NaN to let the behaviour choose.
        /// </summary>
        public float TickShot(AiWorld world, float dt)
        {
            switch (State)
            {
                case ShotState.Preparing:
                    // Sit on the decoy. The shot is already decided — this is theatre, not a
                    // reconsideration, so nothing here can cancel it.
                    if (world.Now >= stateDeadline)
                    {
                        EnterStriking(world.Now);
                        return BeliefLateral + Plan.AimOffset;
                    }

                    return BeliefLateral + Plan.FakeOffset;

                case ShotState.Striking:
                    TickStriking(world, dt);
                    return BeliefLateral + Plan.AimOffset;

                case ShotState.Recovering:
                    TickRecovering(dt);
                    return float.NaN;

                default:
                    return float.NaN;
            }
        }

        /// <summary>
        /// Sweeps the rod through the shot.
        ///
        /// The rod's ANGLE is driven directly here, which is precisely what the player's input does
        /// — RodTouchInput and RodPointerInput hold the spin velocity at zero and write the angle
        /// every frame while a finger is down. Doing the same thing makes the AI's swing a real
        /// swing rather than a nudge.
        ///
        /// The old approach flicked a spin velocity and let damping carry the rod round, and that
        /// is why AI shots felt feeble. BallController scores a strike from MeasuredSpinSpeed
        /// against strikeFullSpin (1800°/s): a flick starting at ~700°/s and decaying was worth
        /// about a third of full power at its very peak, and far less than that for most of the
        /// swing, while a player whipping a finger reaches 1800°/s and gets all of it. The AI was
        /// not being outplayed, it was being scored on a different scale.
        ///
        /// Driving the angle also removes the need for a velocity floor to guarantee the rod
        /// completes its turn. The arc is advanced explicitly, so it cannot stall partway.
        /// </summary>
        private void TickStriking(AiWorld world, float dt)
        {
            RodProfile profile = Profile;

            // Clamped to what is left, so the swing lands exactly on the committed arc instead of
            // overshooting it — a 360° shot then finishes precisely where it started, upright.
            float step = Mathf.Min(Plan.SwingSpeed * dt, Plan.FollowThrough - arcTurned);

            if (step > 0f)
            {
                arcTurned += step;
                Rod.SetSpinVelocity(0f);
                Rod.SetSpinAngle(Rod.CurrentSpinAngle + spinSign * step);
            }

            lastSpinAngle = Rod.CurrentSpinAngle;

            if (arcTurned >= Plan.FollowThrough - 0.01f || world.Now >= stateDeadline)
            {
                State = ShotState.Recovering;
                NextKickTime = world.Now + profile.kickCooldown * Limits.StrikeCooldownScale;
            }
        }

        private void TickRecovering(float dt)
        {
            RodProfile profile = Profile;

            if (Mathf.Abs(Rod.CurrentSpinVelocity) >= profile.recoverSpinThreshold)
            {
                return; // still carrying real speed from the swing — let it decay first
            }

            float angle = Rod.CurrentSpinAngle;
            float rest = Mathf.Round(angle / 360f) * 360f;

            // Guard against ever reversing a swing. If the rod somehow sits more than a quarter
            // turn from upright, carry it FORWARD in the direction it was already turning rather
            // than dragging it back the way it came.
            if (Mathf.Abs(angle - rest) > 90f)
            {
                rest = spinSign >= 0f ? 360f : 0f;
            }

            Rod.SetSpinVelocity(0f);
            Rod.SetSpinAngle(Mathf.MoveTowards(angle, rest, profile.recoverSpinSpeed * dt));

            if (Mathf.Abs(Mathf.DeltaAngle(Rod.CurrentSpinAngle, 0f)) < 1.5f)
            {
                State = ShotState.Idle;
            }
        }

        /// <summary>Brings the figures upright while the rod is idle, so none are left lying flat.</summary>
        public void SettleWhileIdle(float dt)
        {
            RodProfile profile = Profile;
            if (profile == null || Mathf.Abs(Rod.CurrentSpinVelocity) >= profile.recoverSpinThreshold)
            {
                return;
            }

            float angle = Rod.CurrentSpinAngle;
            if (Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 0.5f)
            {
                return;
            }

            float rest = Mathf.Round(angle / 360f) * 360f;
            Rod.SetSpinVelocity(0f);
            Rod.SetSpinAngle(Mathf.MoveTowards(angle, rest, profile.recoverSpinSpeed * dt));
        }

        /// <summary>Drops everything back to a clean state — used at kick-off and when play stops.</summary>
        public void ResetState()
        {
            State = ShotState.Idle;
            arcTurned = 0f;
            NextKickTime = 0f;
            Engagement = 0f;
            LungeActive = false;
            LungeArmed = true;
            believedVelocity = Vector3.zero;
            beliefAcceleration = Vector3.zero;
            PredictionBias = 0f;
            biasTarget = 0f;
            AimError = 0f;
            aimErrorTarget = 0f;
            chosenFigure = -1;
            lastAttackId = -1;
            ReadsAttack = true;
            BlockAlignmentOffset = 0f;
        }
    }
}
