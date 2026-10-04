using System;
using System.Text;
using Unity.Collections;
using Unity.Netcode;

namespace TableFootball.Net
{
    /// <summary>
    /// What one player shows the other before an online match: name, level and the league badge they
    /// wear. Built from this device's own stores by <see cref="Local"/>, and carried to the opponent by
    /// <see cref="OnlineMatchDirector"/>.
    ///
    /// The guest's card arrives on the host as untrusted input, so every received card goes through
    /// <see cref="Sanitised"/> before anything draws it: the name is cut to the length a real name can
    /// have and stripped of anything that could be read as markup, the level is clamped to the level
    /// road, and a badge outside the known leagues is dropped.
    /// </summary>
    public struct PlayerCard : INetworkSerializable, IEquatable<PlayerCard>
    {
        /// <summary><see cref="Badge"/> when no badge is worn.</summary>
        public const byte NoBadge = byte.MaxValue;

        /// <summary>Longest name shown — the same limit <see cref="PlayerAccount.ValidateName"/> sets.</summary>
        public const int MaxNameLength = 20;

        public FixedString64Bytes Name;
        public int Level;

        /// <summary>A <see cref="League"/> as a byte, or <see cref="NoBadge"/>.</summary>
        public byte Badge;

        public bool HasBadge => Badge < Leagues.Count;

        /// <summary>This player's own card, read fresh from the local stores.</summary>
        public static PlayerCard Local()
        {
            CosmeticItem worn = LeagueBadges.Worn;
            var card = new PlayerCard
            {
                Name = ToFixed(PlayerAccount.ShortName),
                Level = PlayerProgress.Level,
                Badge = worn.Valid ? (byte)LeagueBadges.LeagueOf(worn.Id) : NoBadge,
            };
            return card.Sanitised();
        }

        /// <summary>A copy that is safe to draw, whatever the sender put in it.</summary>
        public PlayerCard Sanitised()
        {
            return new PlayerCard
            {
                Name = ToFixed(Name.ToString()),
                Level = Math.Clamp(Level, 1, PlayerProgress.MaxLevel),
                Badge = Badge < Leagues.Count ? Badge : NoBadge,
            };
        }

        /// <summary>
        /// Printable characters only, no angle brackets (a label that ever rendered rich text would
        /// otherwise obey them), at most <see cref="MaxNameLength"/>. Twenty UTF-16 units encode to at
        /// most 60 UTF-8 bytes, inside the 61 a <see cref="FixedString64Bytes"/> holds, and a cut never
        /// splits a surrogate pair.
        /// </summary>
        private static FixedString64Bytes ToFixed(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return default;
            }

            var clean = new StringBuilder(MaxNameLength);
            foreach (char c in text.Trim())
            {
                if (clean.Length >= MaxNameLength)
                {
                    break;
                }

                if (char.IsControl(c) || c == '<' || c == '>')
                {
                    continue;
                }

                clean.Append(c);
            }

            if (clean.Length > 0 && char.IsHighSurrogate(clean[clean.Length - 1]))
            {
                clean.Length--;
            }

            return new FixedString64Bytes(clean.ToString());
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Level);
            serializer.SerializeValue(ref Badge);
        }

        public bool Equals(PlayerCard other) =>
            Name.Equals(other.Name) && Level == other.Level && Badge == other.Badge;
    }
}
