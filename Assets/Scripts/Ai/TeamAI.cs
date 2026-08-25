using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Plays one team's rods. It drives them through exactly the same RodController calls the touch
    /// input uses — sliding and flicking, never teleporting the ball or reading anything a human
    /// player could not see — so a beatable opponent stays beatable for honest reasons.
    ///
    /// This class is deliberately only an orchestrator. It owns no rod logic at all:
    ///
    ///   AiWorld      - the shared, once-per-frame picture of the table
    ///   RodBehavior  - one subclass per role, deciding where to stand and whether to swing
    ///   RodAgent     - one per rod, executing that decision and owning the shot state machine
    ///
    /// What lives here is only what genuinely spans the team: building the rods into agents,
    /// working out the table's geometry, deciding which rod the ball currently belongs to, and
    /// handing control between them smoothly.
    ///
    /// Two things this replaces, both of which were the reason the old AI read as one dumb
    /// behaviour repeated four times:
    ///
    ///   1. Roles were assigned by depth index with `i &lt;= count / 2`, which on a four-rod team
    ///      produced Goalkeeper, Defense, Defense, Attack — RodRole.Midfield was never once
    ///      assigned, so the five-man rod played as a second defender. Roles now come from the
    ///      rod's figure count, which is what actually identifies a foosball rod.
    ///
    ///   2. Only the single nearest rod played, and every other rod froze where it stood. Now every
    ///      rod always holds a role-appropriate shape, and "engagement" is an eased 0..1 blend
    ///      rather than a switch — which is what makes control pass between rods as a handoff
    ///      instead of a snap.
    /// </summary>
    public class TeamAI : MonoBehaviour
    {
        [Header("Who this AI plays")]
        [SerializeField] private Team team = Team.Blue;

        /// <summary>Which side this AI plays. The human takes the other one.</summary>
        public Team Team => team;

        [SerializeField] private TableReferences table;
        [SerializeField] private BallController ball;
        [Tooltip("Optional. When set, the AI stands still between a goal and the next kick-off.")]
        [SerializeField] private MatchManager match;

        [Header("Per-rod behaviour profiles")]
        [Tooltip("The one-man rod: reactive lateral tracking, held inside the goal mouth.")]
        [SerializeField] private GoalkeeperTuning goalkeeper = new GoalkeeperTuning();
        [Tooltip("The two-man rod: blocks the lane to our goal, clears wide.")]
        [SerializeField] private DefenseTuning defense = new DefenseTuning();
        [Tooltip("The five-man rod: holds spacing, feeds the attack, rarely shoots.")]
        [SerializeField] private MidfieldTuning midfield = new MidfieldTuning();
        [Tooltip("The three-man rod: the scoring rod. Shot variety, fakes, picks the open corner.")]
        [SerializeField] private OffenseTuning offense = new OffenseTuning();

        [Header("Difficulty")]
        [Tooltip("0 = Easy, 0.5 = Normal, 1 = Hard. The settings menu writes this from its three " +
                 "presets, but the value itself is continuous — drag it while playing to feel the " +
                 "curve, and a slider can replace the presets later without touching this code.")]
        [Range(0f, 1f)]
        [SerializeField] private float difficulty01 = 0.5f;

        [Tooltip("Extra reaction delay, in seconds, added on top of each rod's own range. 0 at Hard, " +
                 "so Hard keeps the reactions it was authored with.")]
        [SerializeField] private DifficultyRange reactionHandicap = new DifficultyRange(0.16f, 0f);

        [Tooltip("Extra block misalignment, in metres, added on top of each rod's own range. Roughly " +
                 "a ball and a half at Easy, nothing at Hard.\n\n" +
                 "This and the delay above are applied as ADDITIONS rather than by editing the " +
                 "per-rod ranges, because those ranges are already saved in the scene: changing " +
                 "their defaults in code would do nothing to this table. New fields have no saved " +
                 "value, so their defaults are what actually take effect.")]
        [SerializeField] private DifficultyRange blockErrorHandicap = new DifficultyRange(0.05f, 0f);
        [Tooltip("How often each rod's aim error is re-rolled, in seconds. The magnitude comes " +
                 "from each rod's own difficulty block; this is only the cadence.")]
        [SerializeField] private float aimErrorInterval = 0.6f;

        /// <summary>Where this AI sits between Easy (0) and Hard (1).</summary>
        public float Difficulty01 => difficulty01;

        [Header("Possession")]
        [Tooltip("How near a figure must be to the ball, in metres, for that side to count as " +
                 "having it. Drives the once-per-attack read roll on the defending rods.")]
        [SerializeField] private float possessionDistance = 0.075f;
        [Tooltip("Use the ball's OWN record of who last controlled it, instead of guessing from " +
                 "nearest-figure distance alone. The ball knows a controlled touch from a block, so " +
                 "the AI switches to attack the instant a rebound or bad touch turns the ball loose, " +
                 "rather than waiting for a figure to drift closest. Falls back to the distance guess " +
                 "when the ball is loose, or if no BallController possession is available.")]
        [SerializeField] private bool useAuthoritativePossession = true;

        [Header("Rod handoff")]
        [Tooltip("Play only the rod the ball belongs to, and leave every other rod standing where " +
                 "it is. On by default, and it is a FAIRNESS setting as much as a difficulty one: " +
                 "the player has one pair of hands and can only touch one rod at a time, so an AI " +
                 "sliding all four at once is not playing the same game.\n\n" +
                 "Turn off to let every rod hold a role-appropriate formation at once. That reads " +
                 "as a better team, but it is also four hands against one.")]
        [SerializeField] private bool onlyPlayNearestRod = true;
        [Tooltip("How much nearer a rival rod must be to the ball, in metres, before it takes over. " +
                 "Without this margin the two rods either side of the ball trade control every " +
                 "frame and both twitch.")]
        [SerializeField] private float takeoverMargin = 0.03f;

        [Header("Geometry fallbacks")]
        [Tooltip("Used only if no GoalTrigger can be found to measure. Half the goal's width, metres.")]
        [SerializeField] private float fallbackGoalHalfWidth = 0.09f;

        [Header("Debug")]
        [SerializeField] private bool logSetupOnStart = false;

        private readonly List<RodAgent> agents = new List<RodAgent>();
        private readonly AiWorld world = new AiWorld();

        private Vector3 longAxis = Vector3.right;
        private Vector3 barAxis = Vector3.forward;
        private float attackSign = 1f;

        private RodAgent activeAgent;
        private RodAgent attackAgent;
        private OpponentRodView opponentKeeper;
        private bool wasLive = true;
        private bool ballWasComingAtUs;
        private readonly List<float> possessionScratch = new List<float>(8);

        private void Awake()
        {
            if (table == null)
            {
                table = GetComponentInParent<TableReferences>() ?? FindAnyObjectByType<TableReferences>();
            }

            if (ball == null)
            {
                ball = FindAnyObjectByType<BallController>();
            }

            if (match == null)
            {
                match = FindAnyObjectByType<MatchManager>();
            }

            if (table == null || ball == null)
            {
                Debug.LogError($"{name}: needs a TableReferences and a BallController. AI disabled.", this);
                enabled = false;
            }
        }

        /// <summary>
        /// Deliberately Start, not Awake. Building agents reads each rod's BarAxis and BarPivot, and
        /// those only exist once RodController.Awake has cached its rest pose. Unity guarantees every
        /// Awake runs before any Start; it guarantees nothing about Awake order between components.
        /// </summary>
        private void Start()
        {
            BuildAgents();
            BuildGeometry();

            // The settings menu may have written a difficulty before this AI existed.
            ApplyDifficulty(GameAudio.Difficulty);
        }

        // ------------------------------------------------------------------ setup

        private void BuildAgents()
        {
            agents.Clear();

            IReadOnlyList<RodController> rods = table.RodsFor(team);
            if (rods == null || rods.Count == 0)
            {
                Debug.LogWarning($"{name}: no {team} rods found on the table.", this);
                return;
            }

            barAxis = rods[0].BarAxis;

            // A zero axis means the rods had not cached their rest pose yet. Everything downstream
            // depends on this direction and the failure is silent and baffling, so refuse to run.
            if (barAxis.sqrMagnitude < 1e-6f)
            {
                Debug.LogError($"{name}: rods have no bar axis yet — TeamAI must build after " +
                               "RodController.Awake. AI disabled.", this);
                enabled = false;
                return;
            }

            longAxis = Vector3.Cross(barAxis, Vector3.up).normalized;

            // Attack away from our own half: the mean position of our rods tells us which end we
            // defend, so the AI always drives the ball toward the other goal.
            float ownHalf = 0f;
            int counted = 0;
            foreach (RodController rod in rods)
            {
                if (rod != null)
                {
                    ownHalf += Vector3.Dot(rod.BarPivot, longAxis);
                    counted++;
                }
            }

            if (counted > 0)
            {
                ownHalf /= counted;
            }

            attackSign = ownHalf > 0f ? -1f : 1f;

            foreach (RodController rod in rods)
            {
                if (rod == null)
                {
                    continue;
                }

                float[] offsets = FigureOffsets(rod);
                if (offsets.Length == 0)
                {
                    continue;
                }

                float depth = Vector3.Dot(rod.BarPivot, longAxis);

                // Work out which way to flick by asking the rod which way a foot actually swings,
                // then keeping the sign that drives the ball at the opponent's goal. Deriving it
                // per rod means the AI kicks the right way for either team and whichever way round
                // the figures were modelled.
                Vector3 attackDirection = longAxis * attackSign;
                float kickSign = Vector3.Dot(rod.ForwardKickDirection, attackDirection) >= 0f ? 1f : -1f;

                agents.Add(new RodAgent
                {
                    Rod = rod,
                    FigureOffsets = offsets,
                    RestLateral = Vector3.Dot(rod.BarPivot, barAxis),
                    Depth = depth,
                    OrderedDepth = depth * attackSign,
                    KickSign = kickSign
                });
            }

            agents.Sort((a, b) => a.OrderedDepth.CompareTo(b.OrderedDepth));
            AssignRoles();

            foreach (RodAgent agent in agents)
            {
                agent.Behavior = CreateBehavior(agent.Role);

                if (agent.Role == RodRole.Attack)
                {
                    attackAgent = agent;
                }

                if (logSetupOnStart)
                {
                    Debug.Log($"{name}: {agent.Rod.name} ({team}) — {agent.FigureCount} figure(s), " +
                              $"role {agent.Role}, depth {agent.Depth:0.###}, " +
                              $"kick sign {agent.KickSign:+0;-0}", agent.Rod);
                }
            }
        }

        private float[] FigureOffsets(RodController rod)
        {
            var offsets = new List<float>();

            foreach (Transform child in rod.GetComponentsInChildren<Transform>(true))
            {
                if (child == rod.transform ||
                    !child.name.StartsWith("Fig", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                offsets.Add(Vector3.Dot(child.position - rod.BarPivot, barAxis));
            }

            offsets.Sort();
            return offsets.ToArray();
        }

        /// <summary>
        /// Names each rod by how many figures it carries — 1 keeper, 2 defense, 5 midfield,
        /// 3 attack — which is what actually distinguishes the rods on a real table.
        ///
        /// Falls back to depth ordering if the figure counts are not a clean 1/2/5/3 set (a model
        /// with different rods, or figures not named "Fig*"). The fallback uses (count - 1) / 2 as
        /// the defense/midfield split; the old code used count / 2, which on four rods classified
        /// index 2 as Defense and so never produced a Midfield at all.
        /// </summary>
        private void AssignRoles()
        {
            bool assigned = TryAssignRolesByFigureCount();

            if (assigned)
            {
                return;
            }

            for (int i = 0; i < agents.Count; i++)
            {
                agents[i].Role = i == 0 ? RodRole.Goalkeeper
                               : i == agents.Count - 1 ? RodRole.Attack
                               : i <= (agents.Count - 1) / 2 ? RodRole.Defense
                               : RodRole.Midfield;
            }

            if (logSetupOnStart)
            {
                Debug.Log($"{name}: figure counts were not a 1/2/5/3 set — roles assigned by " +
                          "depth order instead.", this);
            }
        }

        private bool TryAssignRolesByFigureCount()
        {
            if (agents.Count != 4)
            {
                return false;
            }

            var seen = new HashSet<RodRole>();

            foreach (RodAgent agent in agents)
            {
                RodRole role;

                switch (agent.FigureCount)
                {
                    case 1: role = RodRole.Goalkeeper; break;
                    case 2: role = RodRole.Defense; break;
                    case 5: role = RodRole.Midfield; break;
                    case 3: role = RodRole.Attack; break;
                    default: return false;
                }

                if (!seen.Add(role))
                {
                    return false; // two rods claiming the same job — trust depth order instead
                }

                agent.Role = role;
            }

            return seen.Count == 4;
        }

        private RodBehavior CreateBehavior(RodRole role)
        {
            switch (role)
            {
                case RodRole.Goalkeeper: return new GoalkeeperBehavior(goalkeeper);
                case RodRole.Defense: return new DefenseBehavior(defense);
                case RodRole.Midfield: return new MidfieldBehavior(midfield);
                default: return new OffenseBehavior(offense);
            }
        }

        /// <summary>
        /// Measures the table once: where the goals are, how wide they are, and how far along a bar
        /// there is table to play on. Measured from the real colliders rather than guessed, so the
        /// keeper covers the actual goal and the attack aims at real corners.
        /// </summary>
        private void BuildGeometry()
        {
            if (agents.Count == 0)
            {
                return;
            }

            world.BarAxis = barAxis;
            world.LongAxis = longAxis;
            world.AttackSign = attackSign;

            world.MinRodOrderedDepth = agents[0].OrderedDepth;
            world.MaxRodOrderedDepth = agents[agents.Count - 1].OrderedDepth;

            // How far along a bar there is table: the widest reach any rod has.
            float extent = 0f;
            foreach (RodAgent agent in agents)
            {
                float half = agent.Rod.SlideRangeMeters * 0.5f;
                foreach (float offset in agent.FigureOffsets)
                {
                    extent = Mathf.Max(extent, Mathf.Abs(agent.RestLateral + offset) + half);
                }
            }

            world.LateralHalfExtent = extent > 0.01f ? extent : 0.3f;

            ReadGoals();
            BuildOpponentViews();
        }

        private void ReadGoals()
        {
            Team opponent = team == Team.Red ? Team.Blue : Team.Red;

            // A GoalTrigger's ScoringTeam is who SCORES there, so the goal we defend is the one
            // the opponent scores in.
            BoxCollider ownGoal = null;
            BoxCollider opponentGoal = null;

            foreach (GoalTrigger trigger in FindObjectsByType<GoalTrigger>(FindObjectsInactive.Include,
                                                                          FindObjectsSortMode.None))
            {
                var box = trigger.GetComponent<BoxCollider>();
                if (box == null)
                {
                    continue;
                }

                if (trigger.ScoringTeam == opponent)
                {
                    ownGoal = box;
                }
                else if (trigger.ScoringTeam == team)
                {
                    opponentGoal = box;
                }
            }

            Vector3 barAbs = new Vector3(Mathf.Abs(barAxis.x), Mathf.Abs(barAxis.y), Mathf.Abs(barAxis.z));

            if (ownGoal != null)
            {
                Bounds b = ownGoal.bounds;
                world.GoalCentreLateral = Vector3.Dot(b.center, barAxis);
                world.GoalHalfWidth = Vector3.Dot(b.extents, barAbs);
                world.OwnGoalOrderedDepth = Vector3.Dot(b.center, longAxis) * attackSign;
            }
            else
            {
                world.GoalCentreLateral = 0f;
                world.GoalHalfWidth = fallbackGoalHalfWidth;
                world.OwnGoalOrderedDepth = world.MinRodOrderedDepth - 0.1f;
            }

            world.OpponentGoalOrderedDepth = opponentGoal != null
                ? Vector3.Dot(opponentGoal.bounds.center, longAxis) * attackSign
                : world.MaxRodOrderedDepth + 0.1f;

            if (world.GoalHalfWidth < 0.01f)
            {
                world.GoalHalfWidth = fallbackGoalHalfWidth;
            }

            if (logSetupOnStart)
            {
                Debug.Log($"{name}: goal mouth {world.GoalHalfWidth * 2f:0.###} m wide at lateral " +
                          $"{world.GoalCentreLateral:0.###}; own goal depth " +
                          $"{world.OwnGoalOrderedDepth:0.###}, opponent goal depth " +
                          $"{world.OpponentGoalOrderedDepth:0.###}.", this);
            }
        }

        /// <summary>
        /// Snapshots the opponent's rods once. Only their resting figure positions are cached —
        /// where the figures actually are is read live, because that is the part that moves and the
        /// part defense and attack need in order to judge a lane.
        /// </summary>
        private void BuildOpponentViews()
        {
            world.OpponentRods.Clear();
            opponentKeeper = null;

            Team opponent = team == Team.Red ? Team.Blue : Team.Red;
            IReadOnlyList<RodController> rods = table.RodsFor(opponent);

            if (rods == null)
            {
                return;
            }

            foreach (RodController rod in rods)
            {
                if (rod == null)
                {
                    continue;
                }

                float[] offsets = FigureOffsets(rod);
                if (offsets.Length == 0)
                {
                    continue;
                }

                float rest = Vector3.Dot(rod.BarPivot, barAxis);
                var laterals = new float[offsets.Length];
                for (int i = 0; i < offsets.Length; i++)
                {
                    laterals[i] = rest + offsets[i];
                }

                var view = new OpponentRodView
                {
                    Rod = rod,
                    OrderedDepth = Vector3.Dot(rod.BarPivot, longAxis) * attackSign,
                    FigureCount = offsets.Length,
                    RestLaterals = laterals
                };

                world.OpponentRods.Add(view);
            }

            opponentKeeper = PickOpponentKeeper();
        }

        /// <summary>
        /// Which of their rods is the keeper we are shooting past. A one-figure rod says so
        /// outright; failing that it is the rod furthest up the table from us, since that is the
        /// one standing in front of the goal we attack.
        /// </summary>
        private OpponentRodView PickOpponentKeeper()
        {
            OpponentRodView furthest = null;

            foreach (OpponentRodView view in world.OpponentRods)
            {
                if (view.FigureCount == 1)
                {
                    return view;
                }

                if (furthest == null || view.OrderedDepth > furthest.OrderedDepth)
                {
                    furthest = view;
                }
            }

            return furthest;
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (agents.Count == 0)
            {
                return;
            }

            bool live = match == null || match.PlayLive;

            if (!live)
            {
                // Between a goal and kick-off the AI waits, like the human does.
                if (wasLive)
                {
                    foreach (RodAgent agent in agents)
                    {
                        agent.ResetState();
                    }

                    wasLive = false;
                }

                return;
            }

            if (!wasLive)
            {
                wasLive = true;
            }

            float dt = Time.deltaTime;
            UpdateWorld(dt);
            UpdateActiveAgent();

            foreach (RodAgent agent in agents)
            {
                if (agent.Rod != null)
                {
                    DriveAgent(agent, dt);
                }
            }
        }

        private void UpdateWorld(float dt)
        {
            // Ground truth, with no lag of its own. Each rod applies its own reaction delay to
            // this in RodAgent.UpdateBelief — a team that shares one belief also shares one
            // mistake, and a single team-wide bias is something a player reads in a minute.
            Vector3 position = ball.transform.position;
            Vector3 velocity = ball.Body.linearVelocity;

            world.Now = Time.time;
            world.DeltaTime = dt;
            world.BallPosition = position;
            world.BallVelocity = velocity;
            world.BallLateral = Vector3.Dot(position, barAxis);
            world.BallOrderedDepth = Vector3.Dot(position, longAxis) * attackSign;
            world.BallLateralVelocity = Vector3.Dot(velocity, barAxis);
            world.BallAdvanceVelocity = Vector3.Dot(velocity, longAxis * attackSign);

            world.ZoneProgress = Mathf.InverseLerp(world.MinRodOrderedDepth, world.MaxRodOrderedDepth,
                                                   world.BallOrderedDepth);

            UpdatePossession();

            world.OpponentKeeperLateral = opponentKeeper != null && opponentKeeper.Rod != null
                ? opponentKeeper.RestLaterals[0] + opponentKeeper.Rod.CurrentSlideMeters
                : world.GoalCentreLateral;

            if (attackAgent != null && attackAgent.Rod != null)
            {
                world.FriendlyAttackOrderedDepth = attackAgent.OrderedDepth;
                world.FriendlyAttackLateral = attackAgent.FigureLateral(attackAgent.FigureCount / 2);
            }
            else
            {
                world.FriendlyAttackOrderedDepth = world.MaxRodOrderedDepth;
                world.FriendlyAttackLateral = world.GoalCentreLateral;
            }
        }

        /// <summary>
        /// Works out whose ball it is, and raises an attack whenever it becomes theirs.
        ///
        /// Possession is judged the way a player judges it: whose figure is nearest, and is it near
        /// enough to actually be in control. Nothing here reads the ball's collision history — that
        /// would be information off the table.
        ///
        /// An attack is raised on the transition into their possession, and again if the ball turns
        /// back toward our goal during it (a rebound or a second phase is a fresh thing to read).
        /// The defending rods roll their read once per attack, so a missed read is one clean missed
        /// block rather than a flicker.
        /// </summary>
        private void UpdatePossession()
        {
            float ours = float.MaxValue;
            foreach (RodAgent agent in agents)
            {
                if (agent.Rod != null)
                {
                    ours = Mathf.Min(ours, agent.NearestFigureGap(world.BallLateral) +
                                           Mathf.Abs(agent.OrderedDepth - world.BallOrderedDepth));
                }
            }

            float theirs = float.MaxValue;
            foreach (OpponentRodView view in world.OpponentRods)
            {
                if (view.Rod == null)
                {
                    continue;
                }

                view.CurrentLaterals(possessionScratch);
                float depthGap = Mathf.Abs(view.OrderedDepth - world.BallOrderedDepth);

                foreach (float figure in possessionScratch)
                {
                    theirs = Mathf.Min(theirs, Mathf.Abs(figure - world.BallLateral) + depthGap);
                }
            }

            bool nowTheirs = theirs < possessionDistance * 2f && theirs < ours;

            // The ball's own record beats the distance guess when it actually knows who is in
            // control: a controlled touch by the opponent is possession however far our figures
            // drifted, and a touch by us is not theirs however near they sit. Only a genuinely loose
            // ball falls through to the nearest-figure reading above.
            if (useAuthoritativePossession && ball != null && !ball.IsLoose)
            {
                nowTheirs = ball.ControllingTeam != team;
            }

            bool turnedOnUs = nowTheirs && world.BallAdvanceVelocity < -0.3f && !ballWasComingAtUs;

            if ((nowTheirs && !world.OpponentHasBall) || turnedOnUs)
            {
                world.AttackId++;
            }

            world.OpponentHasBall = nowTheirs;
            ballWasComingAtUs = world.BallAdvanceVelocity < -0.3f;
        }

        /// <summary>
        /// Chooses the rod the ball belongs to: the one whose line it is nearest.
        ///
        /// The incumbent keeps control unless a rival is nearer by more than takeoverMargin — with
        /// the ball midway between two rods the nearest flips every frame otherwise, and both rods
        /// jerk back and forth handing control to each other. A rod already committed to a swing
        /// keeps control outright: taking the ball off a rod mid-shot is what leaves a swing
        /// half-finished.
        /// </summary>
        private void UpdateActiveAgent()
        {
            if (activeAgent != null && activeAgent.Rod != null && activeAgent.Committed)
            {
                return;
            }

            RodAgent nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (RodAgent agent in agents)
            {
                if (agent.Rod == null)
                {
                    continue;
                }

                float distance = Mathf.Abs(agent.OrderedDepth - world.BallOrderedDepth);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = agent;
                }
            }

            if (nearest == null)
            {
                return;
            }

            if (activeAgent == null || activeAgent.Rod == null)
            {
                activeAgent = nearest;
                return;
            }

            float incumbent = Mathf.Abs(activeAgent.OrderedDepth - world.BallOrderedDepth);
            if (nearestDistance < incumbent - takeoverMargin)
            {
                activeAgent = nearest;
            }
        }

        private void DriveAgent(RodAgent agent, float dt)
        {
            RodProfile profile = agent.Profile;
            if (profile == null)
            {
                return;
            }

            agent.IsActive = agent == activeAgent;
            agent.UpdateEngagement(dt);
            agent.UpdateBelief(world, dt);
            agent.UpdateAttackRead(world);

            // Hand speed: the rod's authored speed, scaled by this difficulty's limit on how fast
            // its hands move, plus any transient boost (the keeper's lunge).
            agent.Rod.SetSlideSpeed(profile.slideSpeed *
                                    agent.Limits.HandSpeed *
                                    agent.Behavior.SpeedMultiplier(agent, world));

            if (world.Now >= agent.NextErrorTime)
            {
                agent.RollAimError();
                agent.NextErrorTime = world.Now + aimErrorInterval;
            }

            // A committed shot owns the rod's position until its arc completes. A rod that is
            // mid-swing or still settling keeps driving itself even after the ball has moved on to
            // another rod — abandoning a swing halfway is the bug this whole state machine exists
            // to prevent, and losing the ball is not a reason to reintroduce it.
            float target = agent.TickShot(world, dt);

            if (float.IsNaN(target) && !agent.IsActive && onlyPlayNearestRod)
            {
                // Not this rod's ball, and only one rod plays at a time. Hold station: the slide is
                // left exactly where it is, and the figures are only brought back upright so none
                // are left lying flat where they block nothing.
                if (agent.State == ShotState.Idle)
                {
                    agent.SettleWhileIdle(dt);
                }

                return;
            }

            if (float.IsNaN(target))
            {
                float rest = agent.Behavior.RestTarget(agent, world);
                float track = agent.Behavior.TrackTarget(agent, world);
                float blended = Mathf.Lerp(rest, track, agent.Engagement * agent.Limits.TrackTightness);

                // Aim error is scaled by engagement so it only applies to a rod actually playing
                // the ball. A rod holding formation has nothing else moving it, so an unscaled
                // error is the ONLY thing driving it — which is what made the off-ball rods look
                // like they were wandering at random.
                target = blended + agent.AimError * agent.Engagement;
            }

            agent.SlideToward(world.ClampToTable(target));

            if (agent.State != ShotState.Idle)
            {
                return;
            }

            bool mayStrike = (agent.IsActive || profile.strikeWhenInactive) &&
                             world.Now >= agent.NextKickTime;

            if (mayStrike && agent.Behavior.TryPlanShot(agent, world, out ShotPlan plan))
            {
                agent.BeginShot(plan, world.Now);
                return;
            }

            agent.SettleWhileIdle(dt);
        }

        // ------------------------------------------------------------------ difficulty

        /// <summary>
        /// Applies a difficulty preset chosen from the settings menu.
        ///
        /// The presets are only three points on a continuous 0..1 scale. Everything downstream
        /// reads the number, not the preset, so replacing this control with a real slider later
        /// means calling <see cref="SetDifficulty"/> instead and changing nothing else.
        /// </summary>
        public void ApplyDifficulty(AiLevel level)
        {
            switch (level)
            {
                case AiLevel.Easy: SetDifficulty(0f); break;
                case AiLevel.Hard: SetDifficulty(1f); break;
                default: SetDifficulty(0.5f); break;
            }
        }

        /// <summary>
        /// Sets difficulty anywhere on the continuous scale, 0 (Easy) to 1 (Hard).
        ///
        /// Nothing authored is mutated. The profiles keep the numbers you typed, and each rod's
        /// limitations are RESOLVED from them — so moving the difficulty back and forth can never
        /// compound or drift your tuning, which is the failure mode of scaling values in place.
        /// </summary>
        public void SetDifficulty(float value01)
        {
            difficulty01 = Mathf.Clamp01(value01);
            ResolveDifficulty();
        }

        /// <summary>
        /// Works out every rod's limitations at the current difficulty, once, rather than
        /// evaluating curves every frame.
        ///
        /// The point of doing this per rod is that the axes do not have to move together. The
        /// keeper's reaction delay collapses across the range while the attack's barely moves, so
        /// Easy is beatable because its LAST line can be beaten — not because its attack has
        /// stopped working, which reads as broken rather than as easy.
        /// </summary>
        private void ResolveDifficulty()
        {
            foreach (RodAgent agent in agents)
            {
                ResolvedDifficulty limits = ResolvedDifficulty.From(DifficultyFor(agent.Role), difficulty01);

                // Applied after resolving, not folded into the ranges, for the reason given on
                // the handicap fields: the per-rod ranges are already saved in the scene and
                // their code defaults no longer reach this table.
                limits.ReactionDelay += Mathf.Max(reactionHandicap.Evaluate(difficulty01), 0f);
                limits.BlockAlignmentError += Mathf.Max(blockErrorHandicap.Evaluate(difficulty01), 0f);

                agent.Limits = limits;
            }
        }

        private RodDifficulty DifficultyFor(RodRole role)
        {
            switch (role)
            {
                case RodRole.Goalkeeper: return goalkeeper.difficulty;
                case RodRole.Defense: return defense.difficulty;
                case RodRole.Midfield: return midfield.difficulty;
                default: return offense.difficulty;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Re-resolves while the inspector is being dragged, so the difficulty slider and the
        /// per-rod curves can be felt live in Play mode instead of needing a restart.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying && agents.Count > 0)
            {
                ResolveDifficulty();
            }
        }
#endif
    }
}
