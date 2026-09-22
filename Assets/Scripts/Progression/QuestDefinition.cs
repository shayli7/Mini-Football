using System.Collections.Generic;

namespace TableFootball.Progression
{
    /// <summary>
    /// Every quest the game can hand out. The value is persisted in
    /// <see cref="DailyQuests"/>, so entries may be added but <b>never renumbered</b> — a reordering
    /// would silently hand yesterday's saved set to the wrong quests.
    /// </summary>
    public enum QuestId
    {
        PlayThreeOnline = 0,
        FirstBlood = 1,
        ScoreFive = 2,
        CleanSheet = 3,
        BackToBack = 4,
        BeatFriend = 5,
        WinByThree = 6,
        Comeback = 7,
        SuddenDeathWin = 8,
        BeatHardAi = 9,
    }

    /// <summary>
    /// What a quest is worth, and how hard it is. One of each is drawn per day, which is what
    /// guarantees the daily set always holds something that cannot really be failed and something
    /// worth chasing — see <see cref="DailyQuests.Roll"/>.
    /// </summary>
    public enum QuestTier
    {
        Bronze = 0,
        Silver = 1,
        Gold = 2,
    }

    public sealed class QuestDefinition
    {
        public QuestId Id { get; }
        public QuestTier Tier { get; }

        /// <summary>Shown on the card and in the result ledger. Upper-cased by the labels themselves.</summary>
        public string Title { get; }

        /// <summary>One line under the title, saying exactly what is being asked.</summary>
        public string Description { get; }

        /// <summary>How many times <see cref="QuestTracker"/> must report it before it is complete.</summary>
        public int Target { get; }

        /// <summary>
        /// False only for <see cref="QuestId.BeatHardAi"/>. Everything else counts in online matches
        /// alone — beating an Easy AI nine times should not be a way to out-earn someone who played
        /// nine real opponents.
        /// </summary>
        public bool OnlineOnly { get; }

        public int Xp => Tier switch
        {
            QuestTier.Bronze => 50,
            QuestTier.Silver => 100,
            _ => 200,
        };

        private QuestDefinition(QuestId id, QuestTier tier, string title, string description,
                                int target, bool onlineOnly = true)
        {
            Id = id;
            Tier = tier;
            Title = title;
            Description = description;
            Target = target;
            OnlineOnly = onlineOnly;
        }

        // ---------- the catalogue ----------

        /// <summary>
        /// Awarded on top of the three quests when all three are cleared in one day. Not a quest
        /// itself: it has no progress of its own, and clearing it is simply clearing the set.
        /// </summary>
        public const int SlamXp = 150;

        private static readonly QuestDefinition[] Table =
        {
            new QuestDefinition(QuestId.PlayThreeOnline, QuestTier.Bronze, "Kick About",
                                "Play 3 online matches. Any result counts.", 3),

            new QuestDefinition(QuestId.FirstBlood, QuestTier.Bronze, "First Blood",
                                "Open the scoring in 2 online matches.", 2),

            new QuestDefinition(QuestId.ScoreFive, QuestTier.Silver, "Sharpshooter",
                                "Score 5 goals online today.", 5),

            new QuestDefinition(QuestId.CleanSheet, QuestTier.Silver, "Shutout",
                                "Win an online match without conceding.", 1),

            new QuestDefinition(QuestId.BackToBack, QuestTier.Silver, "Back to Back",
                                "Win two online matches in a row.", 1),

            new QuestDefinition(QuestId.BeatFriend, QuestTier.Silver, "Bragging Rights",
                                "Beat someone on your friends list.", 1),

            new QuestDefinition(QuestId.WinByThree, QuestTier.Gold, "No Contest",
                                "Win an online match by three goals or more.", 1),

            new QuestDefinition(QuestId.Comeback, QuestTier.Gold, "Off the Ropes",
                                "Win an online match after trailing by two.", 1),

            new QuestDefinition(QuestId.SuddenDeathWin, QuestTier.Gold, "Golden Goal",
                                "Win an online match in sudden death.", 1),

            new QuestDefinition(QuestId.BeatHardAi, QuestTier.Gold, "Machine Killer",
                                "Beat the AI on Hard.", 1, onlineOnly: false),
        };

        public static IReadOnlyList<QuestDefinition> All => Table;

        /// <summary>
        /// The definition for an id. Never null for a value that came from this enum; a value read
        /// back from PlayerPrefs that no longer exists falls through to the first entry rather than
        /// throwing, because a save written by an older build is not a crash.
        /// </summary>
        public static QuestDefinition Get(QuestId id)
        {
            foreach (QuestDefinition definition in Table)
            {
                if (definition.Id == id)
                {
                    return definition;
                }
            }

            return Table[0];
        }

        /// <summary>Everything at one tier, in catalogue order — the pool a day's draw picks from.</summary>
        public static List<QuestDefinition> AtTier(QuestTier tier)
        {
            var pool = new List<QuestDefinition>();
            foreach (QuestDefinition definition in Table)
            {
                if (definition.Tier == tier)
                {
                    pool.Add(definition);
                }
            }

            return pool;
        }
    }
}
