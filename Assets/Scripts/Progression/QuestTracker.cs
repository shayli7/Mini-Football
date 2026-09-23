using System.Collections.Generic;
using UnityEngine;

namespace TableFootball.Progression
{
    /// <summary>One thing cleared in one match, for the result ledger to draw.</summary>
    public readonly struct QuestAward
    {
        public QuestId Id { get; }
        public string Title { get; }
        public int Xp { get; }

        /// <summary>True for the all-three bonus, which has no quest id and its own icon.</summary>
        public bool IsSlam { get; }

        public QuestAward(QuestId id, string title, int xp, bool isSlam = false)
        {
            Id = id;
            Title = title;
            Xp = xp;
            IsSlam = isSlam;
        }
    }

    /// <summary>
    /// Everything about a match that <see cref="MatchManager"/> does not know and quests need:
    /// whether it is online, which side belongs to this device, and who is on the other one.
    /// Handed over by <see cref="UI.GameFlow"/>, which is the only class that knows all of it.
    /// </summary>
    public struct MatchContext
    {
        public bool Online;
        public Team LocalTeam;
        public bool AiOpponent;
        public AiLevel Difficulty;

        /// <summary>
        /// Resolved at kick-off, not at the final whistle. By the time a match ends the session may
        /// be gone and unable to say who was in it — the same reason GameFlow captures the local team
        /// when it does.
        /// </summary>
        public bool OpponentIsFriend;
    }

    /// <summary>
    /// Turns match events into quest progress, and nothing else does.
    ///
    /// It is a listener in exactly the way <see cref="UI.ScoreHud"/> is: <see cref="MatchManager"/>
    /// gains no knowledge that quests exist, which is the property its own comments ask callers to
    /// preserve. Every rule about what counts lives in <see cref="Resolve"/> and nowhere else.
    ///
    /// Deliberately hung off <see cref="MatchManager"/>'s events rather than off
    /// <see cref="GoalTrigger"/>: goal triggers are disabled on the guest, and the guest's copy of a
    /// goal arrives as a replayed <c>ScoreGoal</c> call. The manager's events are the only ones that
    /// fire on both machines.
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestTracker : MonoBehaviour
    {
        private MatchManager match;
        private MatchContext context;

        private bool inMatch;

        /// <summary>True once a context has been handed over, so a rematch knows what it is.</summary>
        private bool armed;

        private int goalsFor;
        private int goalsAgainst;
        private int worstDeficit;
        private bool sawSuddenDeath;

        private readonly List<QuestAward> pending = new List<QuestAward>();

        /// <summary>
        /// What the match that just finished cleared, for the result screen to show.
        ///
        /// Static for the same reason <see cref="GameSfx"/>'s entry points are: the one screen that
        /// wants it is built by a different class, and threading a reference through for a read-only
        /// snapshot would be more plumbing than the fact deserves. It is written synchronously from
        /// <c>MatchWon</c>, so the HUD's win sequence — which reaches the ledger a second and a half
        /// later — cannot race it whichever of the two subscribed first.
        /// </summary>
        public static IReadOnlyList<QuestAward> LastAwards { get; private set; } = new QuestAward[0];

        /// <summary>Total XP from <see cref="LastAwards"/>, and the levels it bought.</summary>
        public static int LastXp { get; private set; }

        public static int LastLevelsGained { get; private set; }

        public void Build(MatchManager matchManager)
        {
            match = matchManager;
            if (match == null)
            {
                return;
            }

            match.GoalScored += OnGoalScored;
            match.MatchWon += OnMatchWon;
            match.MatchRestarted += OnMatchRestarted;
            match.SuddenDeathStarted += OnSuddenDeath;

            DailyQuests.EnsureToday();
        }

        private void OnDestroy()
        {
            if (match == null)
            {
                return;
            }

            match.GoalScored -= OnGoalScored;
            match.MatchWon -= OnMatchWon;
            match.MatchRestarted -= OnMatchRestarted;
            match.SuddenDeathStarted -= OnSuddenDeath;
        }

        // ---------- the match ----------

        /// <summary>Called by <see cref="UI.GameFlow"/> as a match starts, before the kick-off.</summary>
        public void BeginMatch(MatchContext matchContext)
        {
            context = matchContext;
            armed = true;
            inMatch = true;

            ResetCounters();
            pending.Clear();
            ClearSnapshot();

            DailyQuests.EnsureToday();
        }

        private void ResetCounters()
        {
            goalsFor = 0;
            goalsAgainst = 0;
            worstDeficit = 0;
            sawSuddenDeath = false;
        }

        /// <summary>
        /// Called when this player walks out of a live match instead of finishing it.
        ///
        /// One of the four ways a match ends, and the only one that raises no <c>MatchWon</c> on this
        /// machine — the opponent's does, on theirs. Without this the match would go uncounted and,
        /// worse, the win run would survive a quit, making leaving the cheapest way to protect it.
        ///
        /// No ledger comes of it: the player is already on their way to the menu, and
        /// <see cref="UI.ScoreHud.ResultsHidden"/> exists because being shown a scoreboard for a
        /// match you chose to stop watching is not a reward. The XP still lands, and the quests
        /// screen still shows it.
        /// </summary>
        public void AbandonMatch()
        {
            if (!inMatch)
            {
                return;
            }

            inMatch = false;

            // Disarmed too, unlike a finished match. A rematch this player is no longer in can still
            // relay a RestartMatch at them — the host pressing Play Again as the transport tears
            // down — and re-arming on that would start counting a match they walked out of.
            armed = false;

            if (context.Online)
            {
                Try(QuestId.PlayThreeOnline);
                DailyQuests.BreakWinRun();
            }

            Bank(snapshot: false);
        }

        private void OnGoalScored(Team scorer)
        {
            if (!inMatch)
            {
                return;
            }

            bool mine = scorer == context.LocalTeam;

            if (mine)
            {
                goalsFor++;
            }
            else
            {
                goalsAgainst++;
            }

            // The deepest hole climbed out of, which is what a comeback is. Recorded as it happens
            // rather than reconstructed at the end, where only the final score survives.
            worstDeficit = Mathf.Max(worstDeficit, goalsAgainst - goalsFor);

            if (!context.Online || !mine)
            {
                return;
            }

            Try(QuestId.ScoreFive);

            // The first goal of the match, whoever it belongs to, is the only one that can open the
            // scoring — counted here because a goal is an event and the score is only its result.
            if (goalsFor + goalsAgainst == 1)
            {
                Try(QuestId.FirstBlood);
            }
        }

        private void OnSuddenDeath() => sawSuddenDeath = true;

        private void OnMatchWon(Team winner) =>
            Resolve(winner, match != null && match.LastWinWasForfeit);

        /// <summary>
        /// A fresh match clears the last one's ledger — and re-arms this one.
        ///
        /// The re-arming is the part that is easy to miss. A rematch never goes through
        /// <see cref="UI.GameFlow"/>: the result screen's Play Again calls <c>RestartMatch</c>
        /// straight on the manager, and online the director relays the same call. So no
        /// <see cref="BeginMatch"/> arrives, and without this every match after the first one of a
        /// sitting would quietly count for nothing.
        ///
        /// The context carries over unchanged, which is correct by definition: a rematch is the same
        /// mode, the same teams and the same opponent, which is the whole of what a context holds.
        /// </summary>
        private void OnMatchRestarted()
        {
            ClearSnapshot();

            if (!armed)
            {
                return;
            }

            ResetCounters();
            pending.Clear();
            inMatch = true;
        }

        /// <summary>
        /// Every rule about what a result is worth, in one place.
        ///
        /// A forfeit is a win but not a performance: it counts wherever the quest asks who won, and
        /// never where the quest asks how. Two players trading walk-outs would otherwise farm the
        /// gold tier off 0–0 scorelines.
        /// </summary>
        private void Resolve(Team winner, bool forfeit)
        {
            if (!inMatch)
            {
                return;
            }

            inMatch = false;
            bool won = winner == context.LocalTeam;

            if (context.Online)
            {
                // However it ended, it was played.
                Try(QuestId.PlayThreeOnline);

                if (won)
                {
                    DailyQuests.RecordOnlineWin();
                    if (DailyQuests.WinRun >= 2)
                    {
                        Try(QuestId.BackToBack);
                    }
                }
                else
                {
                    DailyQuests.BreakWinRun();
                }

                if (won)
                {
                    if (!forfeit)
                    {
                        if (goalsAgainst == 0)
                        {
                            Try(QuestId.CleanSheet);
                        }

                        if (goalsFor - goalsAgainst >= 3)
                        {
                            Try(QuestId.WinByThree);
                        }
                    }

                    // These two stand even on a forfeit: trailing by two, and reaching sudden death,
                    // both happened on the table before anybody walked off it.
                    if (worstDeficit >= 2)
                    {
                        Try(QuestId.Comeback);
                    }

                    if (sawSuddenDeath)
                    {
                        Try(QuestId.SuddenDeathWin);
                    }

                    if (context.OpponentIsFriend)
                    {
                        Try(QuestId.BeatFriend);
                    }
                }
            }
            else if (won && context.AiOpponent && context.Difficulty == AiLevel.Hard)
            {
                // The one quest that counts offline, and the only reason this branch exists.
                Try(QuestId.BeatHardAi);
            }

            Bank(snapshot: true);
        }

        // ---------- awarding ----------

        private void Try(QuestId id)
        {
            if (!DailyQuests.Report(id))
            {
                return;
            }

            QuestDefinition definition = QuestDefinition.Get(id);
            pending.Add(new QuestAward(id, definition.Title, definition.Xp));
        }

        /// <summary>
        /// Pays out everything cleared, adds the slam if the set is now finished, and publishes the
        /// snapshot the result screen reads.
        ///
        /// One <c>PlayerPrefs.Save</c> at the end of it, rather than one per goal — a disk write
        /// mid-rally is a stutter on the phones this runs on.
        /// </summary>
        private void Bank(bool snapshot)
        {
            if (pending.Count > 0 && DailyQuests.TryAwardSlam())
            {
                pending.Add(new QuestAward(default, "Daily Slam", QuestDefinition.SlamXp, isSlam: true));
            }

            int xp = 0;
            foreach (QuestAward award in pending)
            {
                xp += award.Xp;
            }

            int levels = PlayerXp.Award(xp);
            DailyQuests.Flush();

            if (snapshot)
            {
                LastAwards = pending.ToArray();
                LastXp = xp;
                LastLevelsGained = levels;
            }

            pending.Clear();
        }

        private static void ClearSnapshot()
        {
            LastAwards = new QuestAward[0];
            LastXp = 0;
            LastLevelsGained = 0;
        }
    }
}
