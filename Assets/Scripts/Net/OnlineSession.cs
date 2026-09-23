using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Hosting, joining and leaving an online match.
    ///
    /// This uses the unified Sessions API rather than driving Lobby and Relay by hand. One
    /// <see cref="MultiplayerService.CreateSessionAsync"/> with <c>WithRelayNetwork()</c> creates the
    /// lobby, allocates the Relay server, and starts NetworkManager as host or client — so there is no
    /// join code to write into lobby data and read back out, which is where a manual implementation
    /// spends most of its code and nearly all of its bugs.
    ///
    /// Because that call starts NetworkManager itself, a NetworkManager with a UnityTransport must
    /// already be in the scene before any method here is used.
    ///
    /// Like <see cref="GameServices"/>, nothing here throws at the caller. Every entry point returns
    /// false and leaves <see cref="LastError"/> set, because "the network went away" is an ordinary
    /// outcome for all of them and the menu has to be able to say so and stay usable.
    /// </summary>
    public static class OnlineSession
    {
        /// <summary>Two players to a foosball table.</summary>
        private const int MaxPlayers = 2;

        public static ISession Current { get; private set; }

        public static string LastError { get; private set; } = string.Empty;

        /// <summary>True while a host/join/leave call is in flight, so the menu can disable its buttons.</summary>
        public static bool IsBusy { get; private set; }

        public static bool IsInSession => Current != null;

        /// <summary>True when this player is the host, and therefore the authority over the ball.</summary>
        public static bool IsHost => Current != null && Current.IsHost;

        /// <summary>The code the other player types in to join. Empty outside a session.</summary>
        public static string JoinCode => Current != null ? Current.Code : string.Empty;

        /// <summary>
        /// The other player's id, or empty when nobody else is at the table.
        ///
        /// Read while the session is still alive — by the time a match ends the host may already
        /// have gone, taking the roster with it. <see cref="UI.GameFlow"/> captures it at kick-off
        /// for exactly the reason it captures the local team there.
        /// </summary>
        public static string OpponentId
        {
            get
            {
                if (Current == null)
                {
                    return string.Empty;
                }

                string me = GameServices.PlayerId;
                foreach (var player in Current.Players)
                {
                    if (player != null && !string.IsNullOrEmpty(player.Id) && player.Id != me)
                    {
                        return player.Id;
                    }
                }

                return string.Empty;
            }
        }

        /// <summary>Raised when a session is entered, by hosting or joining.</summary>
        public static event Action<ISession> OnJoined;

        /// <summary>Raised when the session ends, for any reason including being kicked or dropped.</summary>
        public static event Action OnLeft;

        /// <summary>Raised when the other player arrives or goes, with the current player count.</summary>
        public static event Action<int> OnPlayerCountChanged;

        /// <summary>
        /// Creates a private session and returns its join code for the host to read out.
        /// </summary>
        public static async Task<bool> HostAsync(string sessionName = UI.ArcadeTheme.GameName)
        {
            if (!await ReadyAsync())
            {
                return false;
            }

            IsBusy = true;
            try
            {
                var options = new SessionOptions
                {
                    Name = sessionName,
                    MaxPlayers = MaxPlayers,
                    IsPrivate = true
                }.WithRelayNetwork();

                Adopt(await MultiplayerService.Instance.CreateSessionAsync(options));
                Debug.Log($"Hosting online match. Join code: {Current.Code}");
                return true;
            }
            catch (Exception e)
            {
                return Fail("Could not create the match", e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Joins the session with this code. Codes are case-insensitive and the service is strict about
        /// stray whitespace, so both are normalised here rather than trusting the text field.
        /// </summary>
        public static async Task<bool> JoinAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return Fail("Enter a join code", null);
            }

            if (!await ReadyAsync())
            {
                return false;
            }

            IsBusy = true;
            try
            {
                Adopt(await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim().ToUpperInvariant()));
                Debug.Log($"Joined online match {Current.Id}.");
                return true;
            }
            catch (Exception e)
            {
                return Fail("Could not join that match", e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Drops into any open public match, hosting one if there is none to join. The single-button
        /// path for players who have nobody to trade a code with.
        /// </summary>
        public static async Task<bool> QuickMatchAsync()
        {
            if (!await ReadyAsync())
            {
                return false;
            }

            IsBusy = true;
            try
            {
                var options = new SessionOptions
                {
                    Name = "Quick Match",
                    MaxPlayers = MaxPlayers,
                    IsPrivate = false
                }.WithRelayNetwork();

                Adopt(await MultiplayerService.Instance.MatchmakeSessionAsync(
                    new QuickJoinOptions { CreateSession = true }, options));

                Debug.Log(Current.IsHost
                    ? "Quick match: no open table, hosting one."
                    : "Quick match: joined an open table.");
                return true;
            }
            catch (Exception e)
            {
                return Fail("Could not find a match", e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Leaves the session and shuts the connection down. Safe to call when not in one, so the menu
        /// and the pause screen can both just call it without checking first.
        /// </summary>
        public static async Task LeaveAsync()
        {
            if (Current == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await Current.LeaveAsync();
            }
            catch (Exception e)
            {
                // Nothing useful to do about a failed leave: the session is being abandoned either
                // way, and the service drops the player on timeout.
                Debug.LogWarning($"Leaving the session failed, abandoning it anyway: {e.Message}");
            }
            finally
            {
                IsBusy = false;
                Release();
            }
        }

        // ---------- plumbing ----------

        /// <summary>Sign-in has to have happened before any session call; Relay rejects an anonymous stranger.</summary>
        private static async Task<bool> ReadyAsync()
        {
            LastError = string.Empty;

            if (IsInSession)
            {
                return Fail("Already in a match", null);
            }

            if (await GameServices.EnsureSignedInAsync())
            {
                return true;
            }

            return Fail("Could not reach the server", null);
        }

        private static void Adopt(ISession session)
        {
            Current = session;

            session.PlayerJoined += OnPlayerJoined;
            session.PlayerLeaving += OnPlayerLeft;
            session.RemovedFromSession += OnRemoved;

            PublishPresence();

            OnJoined?.Invoke(session);
            OnPlayerCountChanged?.Invoke(session.Players.Count);
        }

        private static void Release()
        {
            if (Current != null)
            {
                Current.PlayerJoined -= OnPlayerJoined;
                Current.PlayerLeaving -= OnPlayerLeft;
                Current.RemovedFromSession -= OnRemoved;
                Current = null;
            }

            // Current is already null, so this clears the code rather than republishing it. Order
            // matters: leaving it set would keep a Join button live on every friend's screen,
            // pointing at a table that no longer exists.
            PublishPresence();

            OnLeft?.Invoke();
        }

        /// <summary>
        /// Tells this player's friends whether they can be joined right now.
        ///
        /// Only a host advertises a code. A guest is in a full two-player table, so publishing theirs
        /// would offer friends a seat that does not exist — and both players share the same code, so
        /// it would be the host's table being advertised twice.
        ///
        /// Deliberately not awaited: the session call that triggered this has already succeeded, and
        /// whether a courtesy broadcast to other people's screens lands is no reason to keep the
        /// player waiting or to fail the thing they actually asked for.
        /// </summary>
        private static void PublishPresence()
        {
            bool joinable = Current != null && Current.IsHost;
            _ = FriendsHub.SetPresenceAsync(joinable ? Current.Code : string.Empty);
        }

        private static void OnPlayerJoined(string playerId)
        {
            OnPlayerCountChanged?.Invoke(Current != null ? Current.Players.Count : 0);
        }

        private static void OnPlayerLeft(string playerId)
        {
            OnPlayerCountChanged?.Invoke(Current != null ? Current.Players.Count : 0);
        }

        /// <summary>The host closed the table, or the connection died. Either way this player is out.</summary>
        private static void OnRemoved()
        {
            Debug.Log("Removed from the online match.");
            Release();
        }

        private static bool Fail(string message, Exception e)
        {
            LastError = message;
            Debug.LogWarning(e == null ? message : $"{message}: {e.Message}");
            return false;
        }
    }
}
