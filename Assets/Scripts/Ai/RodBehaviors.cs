using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// What one rod is trying to do. Each role is a subclass with its own reasoning, rather than a
    /// shared routine with a switch on the role — so changing how defense thinks cannot alter how
    /// the keeper thinks, and each one can be read on its own.
    ///
    /// Every behaviour answers three questions and nothing else:
    ///   RestTarget   - where to stand when the ball is not mine
    ///   TrackTarget  - where to stand when it is
    ///   TryPlanShot  - do I want to hit it, and how
    /// Execution (sliding, the swing, the follow-through) belongs to RodAgent.
    ///
    /// Behaviours read the ball through <see cref="RodAgent"/>'s belief, never through AiWorld's
    /// ground truth. A rod with a slow read must make its decisions from what it thinks it sees,
    /// or reaction delay would only ever slow the rod down and never make it wrong.
    ///
    /// Difficulty enters here in exactly one way — <see cref="RodAgent.ChoosesWell"/>, the choice
    /// between the best play and a worse one. Everything else (aim spread, power, mistimes) is
    /// applied by the agent at the moment of commitment, so no behaviour has to know its own
    /// difficulty to be written correctly.
    /// </summary>
    public abstract class RodBehavior
    {
        public abstract RodRole Role { get; }
        public abstract RodProfile Movement { get; }

        /// <summary>Where to stand while this is not the rod on the ball.</summary>
        public abstract float RestTarget(RodAgent agent, AiWorld world);

        /// <summary>Where to stand while this rod is playing the ball.</summary>
        public abstract float TrackTarget(RodAgent agent, AiWorld world);

        /// <summary>Does this rod want to swing right now, and at what?</summary>
        public abstract bool TryPlanShot(RodAgent agent, AiWorld world, out ShotPlan plan);

        /// <summary>Transient hand-speed multiplier — the keeper's lunge uses it.</summary>
        public virtual float SpeedMultiplier(RodAgent agent, AiWorld world) => 1f;

        /// <summary>Scratch list for lane queries. Per-behaviour, so nothing allocates per frame.</summary>
        protected readonly List<float> Scratch = new List<float>(8);

        /// <summary>
        /// The shared, purely physical part of "can I hit it": the ball is beside my line, a figure
        /// is actually on it, and it is not already travelling where I want it.
        ///
        /// The figure test is measured from where the figures currently stand, not from whether the
        /// ball lies somewhere within the rod's reach — testing reachability instead keeps passing
        /// the whole time the rod is still travelling, so the rod swings at a ball it has not caught
        /// up with yet.
        /// </summary>
        protected bool BallInStrikeRange(RodAgent agent, AiWorld world)
        {
            RodProfile profile = Movement;

            if (Mathf.Abs(agent.BeliefOrderedDepth - agent.OrderedDepth) > profile.strikeDistance)
            {
                return false;
            }

            if (agent.NearestFigureGap(agent.BeliefLateral) > profile.laneWidth)
            {
                return false;
            }

            // Judged from the rod's own read: a rod slow to notice the ball turned may well swing
            // at a ball that is already going where it wanted.
            return agent.BeliefAdvanceVelocity <= profile.leaveAloneSpeed;
        }

        /// <summary>Where the ball will cross this rod's line, with the rod's own lead applied.</summary>
        protected float LeadTarget(RodAgent agent, AiWorld world)
        {
            float predicted = agent.PredictLateralAt(agent.OrderedDepth, world.MaxPredictTime);
            float lead = agent.BeliefLateralVelocity * Movement.interceptLead;
            return world.ClampToTable(predicted + lead);
        }

        /// <summary>
        /// Where a blocking rod actually lines up, given how well it read this attack.
        ///
        /// Two separate failures, rolled once per attack rather than per frame:
        ///   - missed the read entirely: it never comes across, and holds its shape instead
        ///   - read it but lined up badly: it comes across, but off by a fixed few centimetres
        ///
        /// Holding both for the whole attack is deliberate. A rod that is continuously a little bit
        /// wrong just looks like it is trembling; a rod that is consistently short on one side for
        /// a whole attack gives the player a gap to aim at, and a rod that occasionally does not
        /// come at all gives them a clean run. Those are the two things a player can actually see
        /// and exploit, which is what "easier" should mean.
        /// </summary>
        protected float BlockTarget(RodAgent agent, AiWorld world, float aligned, float holding)
        {
            if (!agent.ReadsAttack && world.OpponentHasBall)
            {
                return holding;
            }

            return world.ClampToTable(aligned + agent.BlockAlignmentOffset);
        }
    }

    /// <summary>
    /// Keeper. Pure reactive lateral tracking, held inside the goal mouth, with an occasional
    /// timed lunge. It never shades far up the table: a keeper that wanders is a keeper that is
    /// out of position when the shot actually arrives.
    /// </summary>
    public class GoalkeeperBehavior : RodBehavior
    {
        private readonly GoalkeeperTuning tuning;

        public GoalkeeperBehavior(GoalkeeperTuning tuning) { this.tuning = tuning; }

        public override RodRole Role => RodRole.Goalkeeper;
        public override RodProfile Movement => tuning.movement;

        public override float RestTarget(RodAgent agent, AiWorld world)
        {
            // Ball is nowhere near: come back to the middle of the goal rather than drifting.
            float ballSide = world.ClampToTable(agent.BeliefLateral);
            return Mathf.Lerp(ballSide, world.GoalCentreLateral, tuning.centreReturn);
        }

        public override float TrackTarget(RodAgent agent, AiWorld world)
        {
            UpdateLunge(agent, world);

            if (agent.LungeActive)
            {
                return world.ClampToTable(agent.LungeTarget);
            }

            float aim = BlockTarget(agent, world,
                                    aligned: LeadTarget(agent, world),
                                    holding: world.GoalCentreLateral);

            // Stay in front of the goal. Straying wider than the mouth accomplishes nothing —
            // there is no goal out there to defend.
            float limit = world.GoalHalfWidth * tuning.goalMouthCoverage;
            return Mathf.Clamp(aim, world.GoalCentreLateral - limit, world.GoalCentreLateral + limit);
        }

        /// <summary>
        /// A lunge is a single committed guess at where the shot is going, made once per approach.
        /// Re-rolling it every frame would just average out into normal tracking.
        ///
        /// It is guessed from the keeper's own read of the ball, so a slow keeper lunges at where
        /// it thought the shot was going — which is what makes a lunge a genuine gamble rather than
        /// a free save.
        /// </summary>
        private void UpdateLunge(RodAgent agent, AiWorld world)
        {
            if (agent.LungeActive)
            {
                if (world.Now >= agent.LungeUntil)
                {
                    agent.LungeActive = false;
                }

                return;
            }

            float distance = Mathf.Abs(agent.BeliefOrderedDepth - agent.OrderedDepth);

            // Re-arm only once the ball has gone away again, so one attack gets one lunge.
            if (distance > tuning.lungeRange * 1.5f)
            {
                agent.LungeArmed = true;
                return;
            }

            bool incoming = agent.BeliefAdvanceVelocity < -0.25f;
            if (!agent.LungeArmed || !incoming || distance > tuning.lungeRange)
            {
                return;
            }

            agent.LungeArmed = false;

            if (Random.value > tuning.lungeChance)
            {
                return;
            }

            float predicted = agent.PredictLateralAt(agent.OrderedDepth, world.MaxPredictTime);
            float side = Mathf.Sign(predicted - world.GoalCentreLateral);
            agent.LungeTarget = predicted + side * tuning.lungeOvershoot;
            agent.LungeActive = true;
            agent.LungeUntil = world.Now + tuning.lungeDuration;
        }

        public override float SpeedMultiplier(RodAgent agent, AiWorld world)
        {
            return agent.LungeActive ? tuning.lungeSpeedBoost : 1f;
        }

        public override bool TryPlanShot(RodAgent agent, AiWorld world, out ShotPlan plan)
        {
            plan = default;

            if (!BallInStrikeRange(agent, world))
            {
                return false;
            }

            RodProfile profile = Movement;

            float outward = Mathf.Sign(agent.BeliefLateral - world.GoalCentreLateral);
            if (Mathf.Approximately(outward, 0f))
            {
                outward = Random.value < 0.5f ? -1f : 1f;
            }

            // Good keepers clear away from the middle. A poor one shovels it back across its own
            // goal, which is the mistake that actually costs goals rather than merely looking bad.
            float aimOffset = agent.ChoosesWell() ? -outward * 0.012f : outward * 0.010f;

            plan = new ShotPlan
            {
                Kind = agent.LungeActive ? ShotKind.Lunge : ShotKind.Clear,
                SwingSpeed = profile.swingSpeed,
                FollowThrough = profile.followThroughDegrees,
                AimOffset = aimOffset,
                FakeDelay = 0f
            };

            return true;
        }
    }

    /// <summary>
    /// Defense. Holds the line between the ball and our goal rather than chasing the ball itself,
    /// and clears wide instead of squaring it across its own box.
    /// </summary>
    public class DefenseBehavior : RodBehavior
    {
        private readonly DefenseTuning tuning;

        public DefenseBehavior(DefenseTuning tuning) { this.tuning = tuning; }

        public override RodRole Role => RodRole.Defense;
        public override RodProfile Movement => tuning.movement;

        /// <summary>
        /// Where this rod's line crosses the lane from the ball to our goal. Standing here blocks
        /// the shot; standing on the ball's lateral position merely follows it.
        /// </summary>
        private float LaneBlockLateral(RodAgent agent, AiWorld world)
        {
            float span = world.OwnGoalOrderedDepth - agent.BeliefOrderedDepth;
            if (Mathf.Abs(span) < 1e-4f)
            {
                return agent.BeliefLateral;
            }

            float t = Mathf.Clamp01((agent.OrderedDepth - agent.BeliefOrderedDepth) / span);
            return Mathf.Lerp(agent.BeliefLateral, world.GoalCentreLateral, t);
        }

        public override float RestTarget(RodAgent agent, AiWorld world)
        {
            return world.ClampToTable(LaneBlockLateral(agent, world));
        }

        public override float TrackTarget(RodAgent agent, AiWorld world)
        {
            float lane = LaneBlockLateral(agent, world);
            float ball = LeadTarget(agent, world);

            // Close enough to actually win it: stop holding the lane and go and take the ball.
            float distance = Mathf.Abs(agent.BeliefOrderedDepth - agent.OrderedDepth);
            float weight = distance < tuning.pressureDistance
                ? tuning.laneBlockWeight * 0.25f
                : tuning.laneBlockWeight;

            // Missing the read leaves this rod sitting in its formation shape while the attack goes
            // past it — which is exactly the opening a weaker setting is supposed to give away.
            return BlockTarget(agent, world,
                               aligned: Mathf.Lerp(ball, lane, weight),
                               holding: RestTarget(agent, world));
        }

        public override bool TryPlanShot(RodAgent agent, AiWorld world, out ShotPlan plan)
        {
            plan = default;

            if (!BallInStrikeRange(agent, world))
            {
                return false;
            }

            RodProfile profile = Movement;

            float outward = Mathf.Sign(agent.BeliefLateral - world.GoalCentreLateral);
            if (Mathf.Approximately(outward, 0f))
            {
                outward = Random.value < 0.5f ? -1f : 1f;
            }

            // Stand on the inside of the ball so it leaves toward the nearer touchline. The ball
            // departs away from the figure that struck it, so inside contact sends it wide — and
            // the poor version of this decision squares it across our own goal instead.
            float aimOffset = agent.ChoosesWell()
                ? -outward * tuning.clearToSideBias
                : outward * tuning.clearToSideBias * 0.8f;

            plan = new ShotPlan
            {
                Kind = ShotKind.Clear,
                SwingSpeed = profile.swingSpeed * tuning.clearPowerScale,
                FollowThrough = profile.followThroughDegrees,
                AimOffset = aimOffset,
                FakeDelay = 0f
            };

            return true;
        }
    }

    /// <summary>
    /// Midfield. Holds spacing across the bar, feeds the attack the moment a lane opens, and only
    /// occasionally takes the long shot — from here it is a low-percentage ball and taking it every
    /// time is how a midfield gives possession away.
    ///
    /// This is the rod where decision quality bites hardest, because judging whether a lane is
    /// actually open is the entire skill of the position.
    /// </summary>
    public class MidfieldBehavior : RodBehavior
    {
        private readonly MidfieldTuning tuning;

        public MidfieldBehavior(MidfieldTuning tuning) { this.tuning = tuning; }

        public override RodRole Role => RodRole.Midfield;
        public override RodProfile Movement => tuning.movement;

        public override float RestTarget(RodAgent agent, AiWorld world)
        {
            // Shade toward the ball's side but stay available to receive, rather than committing
            // the whole five-man row to wherever the ball happens to be.
            return world.ClampToTable(Mathf.Lerp(world.GoalCentreLateral, agent.BeliefLateral,
                                                 tuning.spacingBias));
        }

        public override float TrackTarget(RodAgent agent, AiWorld world)
        {
            // Midfield blocks too when the ball is theirs, so it carries the same per-attack read.
            return BlockTarget(agent, world,
                               aligned: LeadTarget(agent, world),
                               holding: RestTarget(agent, world));
        }

        public override bool TryPlanShot(RodAgent agent, AiWorld world, out ShotPlan plan)
        {
            plan = default;

            if (!BallInStrikeRange(agent, world))
            {
                return false;
            }

            RodProfile profile = Movement;
            bool readsItRight = agent.ChoosesWell();

            bool laneReallyOpen = world.IsLaneClear(agent.BeliefLateral, agent.BeliefOrderedDepth,
                                                    world.FriendlyAttackLateral,
                                                    world.FriendlyAttackOrderedDepth,
                                                    tuning.passLaneWidth, Scratch);

            // A good midfield plays the pass when it is on. A poor one misreads the picture: it
            // either forces a pass into traffic, or refuses one that was there.
            bool playsPass = readsItRight ? laneReallyOpen : !laneReallyOpen;

            if (playsPass)
            {
                float toward = world.FriendlyAttackLateral - agent.BeliefLateral;
                float slope = Mathf.Clamp(toward / 0.25f, -1f, 1f);

                plan = new ShotPlan
                {
                    Kind = ShotKind.Pass,
                    SwingSpeed = tuning.passSwingSpeed,
                    FollowThrough = profile.followThroughDegrees,
                    AimOffset = -slope * 0.018f,
                    FakeDelay = 0f
                };

                return true;
            }

            bool goalLaneOpen = world.IsLaneClear(agent.BeliefLateral, agent.BeliefOrderedDepth,
                                                  world.GoalCentreLateral,
                                                  world.OpponentGoalOrderedDepth,
                                                  tuning.passLaneWidth, Scratch);

            // The long shot is only a good idea when it is genuinely on; a poor read takes it anyway.
            if ((goalLaneOpen || !readsItRight) && Random.value < tuning.longShotChance)
            {
                plan = new ShotPlan
                {
                    Kind = ShotKind.Straight,
                    SwingSpeed = tuning.longShotSwingSpeed,
                    FollowThrough = profile.followThroughDegrees,
                    AimOffset = 0f,
                    FakeDelay = 0f
                };

                return true;
            }

            // Otherwise move it on rather than holding it and being closed down.
            plan = new ShotPlan
            {
                Kind = ShotKind.Clear,
                SwingSpeed = profile.swingSpeed * 0.8f,
                FollowThrough = profile.followThroughDegrees,
                AimOffset = 0f,
                FakeDelay = 0f
            };

            return true;
        }
    }

    /// <summary>
    /// Attack. The scoring rod: it reads where the opponent keeper actually stands, picks the
    /// corner that is open rather than always firing down the middle, varies the contact, and
    /// sometimes feints before committing.
    /// </summary>
    public class OffenseBehavior : RodBehavior
    {
        private readonly OffenseTuning tuning;

        public OffenseBehavior(OffenseTuning tuning) { this.tuning = tuning; }

        public override RodRole Role => RodRole.Attack;
        public override RodProfile Movement => tuning.movement;

        public override float RestTarget(RodAgent agent, AiWorld world)
        {
            // Sit where a rebound is most likely to land: the ball's side, ready to finish.
            return world.ClampToTable(Mathf.Lerp(world.GoalCentreLateral, agent.BeliefLateral, 0.6f));
        }

        public override float TrackTarget(RodAgent agent, AiWorld world)
        {
            return LeadTarget(agent, world);
        }

        public override bool TryPlanShot(RodAgent agent, AiWorld world, out ShotPlan plan)
        {
            plan = default;

            if (!BallInStrikeRange(agent, world))
            {
                return false;
            }

            RodProfile profile = Movement;
            ShotKind kind = PickKind();

            // Aim away from wherever the keeper is standing. This is the whole difference between
            // "always shoots at the middle" and a shot that actually asks a question.
            float keeperSide = Mathf.Sign(world.OpponentKeeperLateral - world.GoalCentreLateral);
            if (Mathf.Approximately(keeperSide, 0f))
            {
                keeperSide = Random.value < 0.5f ? -1f : 1f;
            }

            // A push shot deliberately takes the side the keeper is already on, for a keeper that
            // commits early. Everything else takes the open side — unless the read is poor, in
            // which case the shot goes straight down the keeper's throat.
            bool readsItRight = agent.ChoosesWell();
            float aimSide = kind == ShotKind.Push ? keeperSide : -keeperSide;

            if (!readsItRight)
            {
                aimSide = -aimSide;
            }

            float targetLateral = world.GoalCentreLateral +
                                  aimSide * world.GoalHalfWidth * tuning.cornerBias;

            float aimOffset = AimOffsetFor(agent, world, targetLateral, kind);

            float swing = profile.swingSpeed;
            float follow = profile.followThroughDegrees;

            if (kind == ShotKind.Roll)
            {
                swing *= tuning.rollPowerScale;
                follow = tuning.rollFollowThrough;
            }

            float fakeDelay = 0f;
            float fakeOffset = 0f;

            if (Random.value < tuning.fakeChance)
            {
                fakeDelay = tuning.fakeDuration;

                // Show the other side first. A feint toward the side you are about to shoot at is
                // not a feint.
                float decoySide = aimOffset >= 0f ? -1f : 1f;
                fakeOffset = decoySide * tuning.fakeOffset;
            }

            plan = new ShotPlan
            {
                Kind = kind,
                SwingSpeed = swing,
                FollowThrough = follow,
                AimOffset = aimOffset,
                FakeDelay = fakeDelay,
                FakeOffset = fakeOffset
            };

            return true;
        }

        /// <summary>
        /// How far off-centre to stand to send the ball at a chosen point. The ball leaves away
        /// from the figure, so the offset is opposite the direction we want it to travel — and it
        /// is capped, because past a point the figure simply misses the ball entirely.
        /// </summary>
        private float AimOffsetFor(RodAgent agent, AiWorld world, float targetLateral, ShotKind kind)
        {
            if (kind == ShotKind.Straight)
            {
                return 0f;
            }

            float depthGap = Mathf.Max(world.OpponentGoalOrderedDepth - agent.OrderedDepth, 0.05f);
            float slope = Mathf.Clamp((targetLateral - agent.BeliefLateral) / depthGap, -1f, 1f);

            float scale = kind == ShotKind.Roll ? 0.55f : 1f;
            return -slope * tuning.maxAimOffset * scale;
        }

        private ShotKind PickKind()
        {
            float straight = Mathf.Max(tuning.straightWeight, 0f);
            float angled = Mathf.Max(tuning.angledWeight, 0f);
            float push = Mathf.Max(tuning.pushWeight, 0f);
            float roll = Mathf.Max(tuning.rollWeight, 0f);

            float total = straight + angled + push + roll;
            if (total <= 0f)
            {
                return ShotKind.Straight;
            }

            float pick = Random.value * total;

            if ((pick -= straight) < 0f) return ShotKind.Straight;
            if ((pick -= angled) < 0f) return ShotKind.Angled;
            if ((pick -= push) < 0f) return ShotKind.Push;
            return ShotKind.Roll;
        }
    }
}
