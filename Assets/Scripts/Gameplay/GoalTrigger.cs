using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// An invisible volume sitting inside a goal. When the ball reaches it, the team that attacks
    /// this goal is awarded a point.
    ///
    /// Note <see cref="scoringTeam"/> is the team that SCORES here, not the one defending it — the
    /// volume in Red's own goal awards Blue.
    ///
    /// Detection is deliberately generous in depth: a ball travelling at the 4 m/s cap covers 4 cm
    /// per physics step, so a thin trigger could be stepped straight over without ever registering.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GoalTrigger : MonoBehaviour
    {
        [Tooltip("The team awarded a point when the ball enters here — the ATTACKER of this goal, " +
                 "not its defender.")]
        [SerializeField] private Team scoringTeam = Team.Red;
        [SerializeField] private MatchManager match;
        [Tooltip("Logs everything that enters this volume. Turn on when goals are not registering: " +
                 "it separates 'the ball never got here' from 'it arrived but scoring failed'.")]
        [SerializeField] private bool logEntries = false;

        public Team ScoringTeam
        {
            get => scoringTeam;
            set => scoringTeam = value;
        }

        public MatchManager Match
        {
            get => match;
            set => match = value;
        }

        private void Awake()
        {
            if (match == null)
            {
                match = FindAnyObjectByType<MatchManager>();
            }

            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;

            if (match == null)
            {
                Debug.LogError($"{name}: no MatchManager in the scene — this goal cannot score.", this);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Match on the ball's controller rather than a tag, so nothing else can score.
            BallController ball = other.GetComponentInParent<BallController>();

            if (logEntries)
            {
                Debug.Log($"{name}: '{other.name}' entered — " +
                          $"{(ball != null ? "is the ball" : "not the ball, ignored")}" +
                          $"{(match == null ? ", but MatchManager is MISSING" : string.Empty)}", this);
            }

            if (ball == null || match == null)
            {
                return;
            }

            match.ScoreGoal(scoringTeam);
        }
    }
}
