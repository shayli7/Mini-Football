using System;
using System.Collections;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Owns the score and the flow of a match: goal -> pause -> reset -> play on, until someone
    /// reaches the target and the match is won.
    ///
    /// Everything it does is exposed as an event rather than drawn directly, so the Step 6 UI can
    /// subscribe without this class knowing anything about menus or canvases. The AI in Step 5 can
    /// listen to the same events to know when play is live.
    /// </summary>
    [DisallowMultipleComponent]
    public class MatchManager : MonoBehaviour
    {
        [SerializeField] private TableReferences table;
        [SerializeField] private BallController ball;

        [Header("Rules")]
        [Tooltip("First team to this many goals wins. 0 means no goal limit.")]
        [SerializeField] private int goalsToWin = 9;
        [Tooltip("Match length in seconds. At full time the leader wins; level scores go to sudden " +
                 "death. 0 disables the clock.")]
        [SerializeField] private float matchSeconds = 180f;
        [Tooltip("Seconds the ball sits still after a goal before the next kick-off.")]
        [SerializeField] private float resetDelay = 1.2f;
        [Tooltip("Return the rods to centre and unspun after a goal. A new match always resets " +
                 "them regardless, and Park never does.")]
        [SerializeField] private bool resetRodsOnGoal = true;

        [Header("Kick-off")]
        [Tooltip("Small random sideways nudge at kick-off so the ball does not repeat the same path.")]
        [SerializeField] private float kickOffNudge = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool logGoals = true;

        public int RedScore { get; private set; }
        public int BlueScore { get; private set; }

        /// <summary>
        /// False between a goal and the next kick-off. Input and AI should idle while false.
        ///
        /// Starts false, and that is load-bearing rather than tidy. A table that is live from the
        /// moment the scene loads is a table being played before anybody kicked off — which online
        /// meant the host's clock running, and their goals counting, for the whole time they sat
        /// waiting for an opponent to arrive. Nothing is live until something calls
        /// <see cref="KickOff"/>, and every mode does.
        /// </summary>
        public bool PlayLive { get; private set; }

        /// <summary>
        /// True when the match that just ended was won because the opponent left rather than on the
        /// table. Read by the UI alongside <see cref="MatchWon"/>, which is deliberately left carrying
        /// only the winner — every existing listener wants that and nothing else.
        /// </summary>
        public bool LastWinWasForfeit { get; private set; }

        /// <summary>Fired when a team scores. Carries the scoring team.</summary>
        public event Action<Team> GoalScored;

        /// <summary>Fired whenever either score changes. Carries (red, blue).</summary>
        public event Action<int, int> ScoreChanged;

        /// <summary>Fired when a team reaches the target score.</summary>
        public event Action<Team> MatchWon;

        /// <summary>Fired when a new match begins, so UI can clear itself.</summary>
        public event Action MatchRestarted;

        /// <summary>Fired as play resumes from the centre spot — the moment a whistle belongs to.</summary>
        public event Action KickedOff;

        /// <summary>Seconds left on the clock. Counts down only while play is live.</summary>
        public float TimeRemaining { get; private set; }

        /// <summary>True once a level match has run out of time and the next goal decides it.</summary>
        public bool InSuddenDeath { get; private set; }

        /// <summary>Fired every time the clock changes, carrying the seconds left.</summary>
        public event Action<float> TimeChanged;

        /// <summary>Fired when the clock reaches zero, whatever happens next.</summary>
        public event Action FullTime;

        /// <summary>Fired when full time arrives level and the next goal will decide the match.</summary>
        public event Action SuddenDeathStarted;

        private Coroutine pendingKickOff;

        /// <summary>True from the moment a winner is named until the next restart.</summary>
        private bool matchOver;

        /// <summary>
        /// The host's clock, while one is being sent. Online the two machines used to run entirely
        /// separate stopwatches — see <see cref="SyncClock"/> for what that cost.
        /// </summary>
        private float remoteClock;
        private bool hasRemoteClock;

        /// <summary>Clock error, in seconds, past which the local clock is set outright instead of
        /// eased. Ordinary network jitter is far below this; a device that was suspended comes back
        /// whole seconds out and should simply be corrected.</summary>
        private const float ClockSnapSeconds = 1f;

        /// <summary>How fast, in seconds per second, a small clock error is taken up. Slow enough to
        /// be invisible, quick enough that the two clocks never sit apart for long.</summary>
        private const float ClockCatchUpRate = 0.5f;

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

            if (table == null || ball == null)
            {
                Debug.LogError($"{name}: needs a TableReferences and a BallController. Scoring disabled.", this);
                enabled = false;
            }
        }

        private void Start()
        {
            // Announce the opening 0-0 so any UI already listening starts in sync.
            TimeRemaining = matchSeconds;
            ScoreChanged?.Invoke(RedScore, BlueScore);
            TimeChanged?.Invoke(TimeRemaining);
        }

        /// <summary>
        /// Runs the clock. Deliberately on scaled time and gated on PlayLive, which between them
        /// stop it for free in every case it should stop: the pause menu and the front end already
        /// run at timeScale 0, and PlayLive is false from a goal until the next kick-off. No
        /// separate bookkeeping about whether the clock ought to be running.
        /// </summary>
        private void Update()
        {
            if (matchSeconds <= 0f || InSuddenDeath || !PlayLive || TimeRemaining <= 0f)
            {
                return;
            }

            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);

            if (hasRemoteClock)
            {
                TimeRemaining = CorrectedAgainstHost(Time.deltaTime);
            }

            TimeChanged?.Invoke(TimeRemaining);

            if (TimeRemaining <= 0f)
            {
                EndOfNormalTime();
            }
        }

        /// <summary>
        /// Pulls the local clock toward the host's.
        ///
        /// The host's value is run down here too, between packets, so what is being compared is the
        /// host's clock as it is NOW rather than as it was when it was sent — otherwise every packet
        /// would read as the local clock being ahead by however long the packet took to arrive, and
        /// the correction would drag the match time backwards a little on each one.
        /// </summary>
        private float CorrectedAgainstHost(float dt)
        {
            remoteClock = Mathf.Max(0f, remoteClock - dt);

            // Whole seconds apart is not jitter — it is a device that was not running for a while.
            // Nothing is gained by easing across a gap that large, and the two clocks would disagree
            // on screen for the whole time it took.
            if (Mathf.Abs(remoteClock - TimeRemaining) > ClockSnapSeconds)
            {
                return remoteClock;
            }

            return Mathf.MoveTowards(TimeRemaining, remoteClock, ClockCatchUpRate * dt);
        }

        /// <summary>
        /// Takes the authoritative clock from the host. Called on the guest only.
        ///
        /// The clock was the last thing about an online match still being decided in two places at
        /// once: <see cref="Update"/> counts down against local frame time, so the two machines ran
        /// independent stopwatches that drifted apart over a match — and a device that stopped
        /// running for a while (backgrounded, screen off) came back with a clock that was simply
        /// wrong, while its opponent's had carried on. The guest still ticks its own clock down every
        /// frame so the seconds move smoothly; this is what keeps that tick honest.
        /// </summary>
        public void SyncClock(float seconds)
        {
            if (matchSeconds <= 0f)
            {
                return;
            }

            remoteClock = Mathf.Max(0f, seconds);
            hasRemoteClock = true;
        }

        /// <summary>Goes back to running the clock alone, for a match that is no longer online.</summary>
        public void ClearRemoteClock()
        {
            hasRemoteClock = false;
            remoteClock = 0f;
        }

        /// <summary>
        /// The clock has run out. A lead wins it; level scores go to sudden death rather than ending
        /// in a draw, so a match always produces a winner.
        /// </summary>
        private void EndOfNormalTime()
        {
            PlayLive = false;
            FullTime?.Invoke();

            if (RedScore != BlueScore)
            {
                Team winner = RedScore > BlueScore ? Team.Red : Team.Blue;

                if (logGoals)
                {
                    Debug.Log($"Full time — {winner} wins {RedScore} - {BlueScore}.", this);
                }

                EndMatch(winner);
                return;
            }

            InSuddenDeath = true;

            if (logGoals)
            {
                Debug.Log($"Full time — level at {RedScore}. Sudden death: next goal wins.", this);
            }

            SuddenDeathStarted?.Invoke();

            // Straight back into play: sudden death is the same match continuing, not a new one.
            KickOff();
        }

        /// <summary>Called by a <see cref="GoalTrigger"/> when the ball crosses a goal line.</summary>
        public void ScoreGoal(Team scorer)
        {
            // A goal already being processed must not be counted twice if the ball rolls on
            // through the trigger, or if both goal volumes somehow overlap it.
            if (!PlayLive)
            {
                return;
            }

            PlayLive = false;

            if (scorer == Team.Red)
            {
                RedScore++;
            }
            else
            {
                BlueScore++;
            }

            if (logGoals)
            {
                Debug.Log($"GOAL for {scorer}!  Red {RedScore} - {BlueScore} Blue", this);
            }

            // Park the ball the instant it scores. If the goal itself is the trigger there is no
            // solid backing behind it, so an unparked ball would sail off the table in full view
            // during the pause before kick-off.
            ball.ResetBall();

            // The ball is now standing still on purpose. Tell it so, or its own dead-ball rescue
            // will decide it is stuck and fire it off the centre spot during the pause.
            ball.SetRescueActive(false);

            GoalScored?.Invoke(scorer);
            ScoreChanged?.Invoke(RedScore, BlueScore);

            // In sudden death this goal decides it outright, whatever the score reached.
            bool reachedTarget = goalsToWin > 0 && ScoreFor(scorer) >= goalsToWin;

            if (InSuddenDeath || reachedTarget)
            {
                if (logGoals)
                {
                    Debug.Log(InSuddenDeath
                        ? $"Sudden death — {scorer} wins it {RedScore} - {BlueScore}."
                        : $"{scorer} wins the match {RedScore} - {BlueScore}.", this);
                }

                // PlayLive stays false: the match is over until someone restarts it. The ball was
                // already parked above, so EndMatch only has the winner left to announce.
                EndMatch(scorer);
                return;
            }

            pendingKickOff = StartCoroutine(KickOffAfterDelay());
        }

        /// <summary>
        /// Ends the match: parks the ball for good and names the winner.
        ///
        /// Parking is repeated rather than assumed. A goal has already reset the ball by the time it
        /// gets here, but full time and a forfeit have not, and a ball still rolling after the banner
        /// is up — or worse, one the dead-ball rescue fires across the table behind it — is the kind
        /// of thing only the third case would ever have revealed.
        /// </summary>
        private void EndMatch(Team winner)
        {
            PlayLive = false;
            matchOver = true;

            if (pendingKickOff != null)
            {
                StopCoroutine(pendingKickOff);
                pendingKickOff = null;
            }

            ball.ResetBall();
            ball.SetRescueActive(false);

            MatchWon?.Invoke(winner);
        }

        /// <summary>
        /// Hands the match to <paramref name="winner"/> because the other player left.
        ///
        /// The score is left exactly where it stood: the win is real, but the scoreline should say
        /// what actually happened on the table rather than inventing goals nobody scored.
        /// <see cref="LastWinWasForfeit"/> is what lets the UI explain the difference.
        ///
        /// Returns false when there was nothing to award because the match had already been decided.
        /// The caller needs to know: an opponent leaving a finished match changes nothing about the
        /// result, but it does mean nobody is left to play again with.
        /// </summary>
        public bool AwardWinByForfeit(Team winner)
        {
            // Guarded on the match being over rather than on PlayLive, which is also false for the
            // second or so between a goal and the next kick-off. An opponent who quits in that gap —
            // right after conceding is exactly when people do — would otherwise be granted their
            // exit for free, leaving the other player on a dead table with no result at all.
            //
            // What this does still refuse is re-deciding a finished match: someone leaving while the
            // win banner is up left BECAUSE it ended, and relabelling that as a forfeit would rewrite
            // a legitimate win.
            if (matchOver)
            {
                return false;
            }

            LastWinWasForfeit = true;

            if (logGoals)
            {
                Debug.Log($"{winner} wins by forfeit — opponent left at {RedScore} - {BlueScore}.", this);
            }

            EndMatch(winner);
            return true;
        }

        private IEnumerator KickOffAfterDelay()
        {
            yield return new WaitForSeconds(resetDelay);
            KickOff();
            pendingKickOff = null;
        }

        /// <summary>Puts the ball (and optionally the rods) back and resumes play.</summary>
        public void KickOff()
        {
            ParkForKickOff(resetRodsOnGoal);

            if (kickOffNudge > 0f && ball.Body != null)
            {
                // Across the table for variety, and ALONG it so the ball actually leaves the
                // halfway line.
                //
                // The centre spot sits in a dead lane. The two five-man rods stand either side of
                // it and the nearest figure's swept reach still misses the ball there by about
                // 4 mm, so nothing on the table can touch a ball at x = 0. A nudge purely across
                // the table keeps it at exactly x = 0: it rolls up and down the halfway line, out
                // of everyone's reach, and the match never starts.
                //
                // The side is a coin flip so neither team is repeatedly handed the first touch. It
                // still cannot gift a goal - at this speed the ball has three rods and half a table
                // to cross first.
                Vector3 across = Vector3.Cross(Vector3.up, LongAxis()).normalized;
                Vector3 along = LongAxis() * (UnityEngine.Random.value < 0.5f ? -1f : 1f);

                float sideways = UnityEngine.Random.Range(-kickOffNudge, kickOffNudge);
                ball.Body.linearVelocity = across * sideways + along * kickOffNudge;
            }

            ball.SetRescueActive(true);
            PlayLive = true;

            KickedOff?.Invoke();
        }

        /// <summary>
        /// Puts the ball on the centre spot, play stopped, optionally returning the rods to rest.
        ///
        /// Split out of <see cref="KickOff"/> so a countdown has a settled table to count over: the
        /// arrangement play is about to start from sits on screen for the whole count, and nothing
        /// is live until KickOff is actually called. The rescue is off while parked for exactly the
        /// reason SetRescueActive exists - a deliberately motionless ball is not a stuck one.
        ///
        /// Whether the rods move is the CALLER's decision rather than one shared setting: a goal
        /// kick-off obeys resetRodsOnGoal, Park deliberately leaves them where they stand, and a
        /// new match always resets them. One flag governing all three was why turning off the
        /// after-a-goal reset also, silently, left a fresh match holding the last one's rod
        /// positions.
        /// </summary>
        private void ParkForKickOff(bool resetRods)
        {
            PlayLive = false;
            ball.SetRescueActive(false);
            ball.ResetBall();

            if (resetRods)
            {
                table.ResetAllRods();
            }
        }

        /// <summary>
        /// Stops play and settles the table where it stands, keeping the score.
        ///
        /// For the gap between choosing to go online and the match actually starting: the world runs
        /// through all of it, because Netcode's tick does, and without this the table underneath the
        /// menu is a live match nobody called for. <see cref="PrepareMatch"/> is the wrong tool there
        /// — it announces a restart, and on the host that is relayed to a guest who has not arrived.
        /// </summary>
        public void Park()
        {
            if (pendingKickOff != null)
            {
                StopCoroutine(pendingKickOff);
                pendingKickOff = null;
            }

            ParkForKickOff(resetRods: false);
        }

        /// <summary>
        /// Clears the score and parks the table, without starting play.
        ///
        /// The caller decides when the ball goes live, which is what lets a countdown sit between
        /// the two. Anything that wants both at once wants <see cref="RestartMatch"/>.
        /// </summary>
        public void PrepareMatch()
        {
            if (pendingKickOff != null)
            {
                StopCoroutine(pendingKickOff);
                pendingKickOff = null;
            }

            RedScore = 0;
            BlueScore = 0;
            InSuddenDeath = false;
            LastWinWasForfeit = false;
            matchOver = false;
            TimeRemaining = matchSeconds;

            // The host's clock restarts with this one. Without resetting it, the stale value from the
            // match just ended would sit whole seconds below the fresh clock, and the snap in
            // CorrectedAgainstHost would take the new match straight to full time before the host's
            // next packet ever arrived.
            remoteClock = matchSeconds;

            ParkForKickOff(resetRods: true);

            MatchRestarted?.Invoke();
            ScoreChanged?.Invoke(RedScore, BlueScore);
            TimeChanged?.Invoke(TimeRemaining);
        }

        /// <summary>Clears the score and starts a fresh match immediately.</summary>
        [ContextMenu("Restart match")]
        public void RestartMatch()
        {
            PrepareMatch();
            KickOff();

            if (logGoals)
            {
                Debug.Log("Match restarted.", this);
            }
        }

        public int ScoreFor(Team team) => team == Team.Red ? RedScore : BlueScore;

        /// <summary>The goal-to-goal direction, taken from a rod's bar (rods run across the table).</summary>
        private Vector3 LongAxis()
        {
            foreach (RodController rod in table.RedRods)
            {
                if (rod != null)
                {
                    return Vector3.Cross(rod.BarAxis, Vector3.up).normalized;
                }
            }

            return Vector3.right;
        }
    }
}
