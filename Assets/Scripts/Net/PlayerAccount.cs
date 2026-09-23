using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The player's name, and the optional username account behind it.
    ///
    /// Every player already has an identity: <see cref="GameServices"/> signs them in anonymously at
    /// boot and that is enough to play, host and be added as a friend. What it is not is durable —
    /// the anonymous identity lives in a token cached on this device, so a reinstall or a new phone
    /// silently becomes a different person, taking the friends list with it. Adding a username and
    /// password is what makes it recoverable, and is entirely optional.
    ///
    /// The distinction that matters throughout: <see cref="DisplayName"/> is what other players see
    /// and type to add you (<c>Name#1234</c>), while the username below is a credential nobody else
    /// ever sees. They are unrelated, and changing one does not touch the other.
    ///
    /// Nothing here throws at the caller, matching <see cref="GameServices"/> and
    /// <see cref="OnlineSession"/>: every entry point returns false and leaves <see cref="LastError"/>
    /// set, because losing the network mid-call is ordinary and the menu has to stay usable.
    /// </summary>
    public static class PlayerAccount
    {
        /// <summary>Documented in the SDK as 3-20 alphanumerics plus these four symbols.</summary>
        private static readonly Regex UsernameShape = new(@"^[A-Za-z0-9.\-@_]{3,20}$", RegexOptions.Compiled);

        /// <summary>
        /// What a public display name may contain.
        ///
        /// The SDK enforces one rule of its own — no whitespace — and says nothing about the rest, so
        /// this is the only place the shape of a name is decided. It matters more than the username's
        /// rule does, because a username is a credential nobody else ever sees while THIS string is
        /// drawn on other people's screens, and TextMeshPro reads markup in whatever it is given.
        /// The labels that show names have rich text switched off, so this is the second of two
        /// locks rather than the only one; it is here because the input field's own filter guards a
        /// keyboard, not a method, and nothing stops another client calling the service directly.
        /// </summary>
        private static readonly Regex PlayerNameShape = new(@"^[A-Za-z0-9._\-]{1,20}$", RegexOptions.Compiled);

        /// <summary>
        /// Appended to symbol-free passwords to satisfy the service. Frozen forever — see
        /// <see cref="Harden"/> for why changing this character breaks every existing account.
        /// </summary>
        private const string Suffix = "!";

        public static string LastError { get; private set; } = string.Empty;

        /// <summary>True while a call is in flight, so the profile screen can disable its buttons.</summary>
        public static bool IsBusy { get; private set; }

        /// <summary>
        /// True once a username and password are attached, so the account can be recovered on another
        /// device. False means the player exists only as this device's cached token.
        /// </summary>
        public static bool HasAccount { get; private set; }

        /// <summary>Raised whenever the name or account state changes, for the UI to redraw.</summary>
        public static event Action OnChanged;

        /// <summary>
        /// The public <c>Name#1234</c>. Empty until signed in, and briefly empty for a brand new
        /// player until the service mints one — <see cref="RefreshAsync"/> is what fills it in.
        /// </summary>
        public static string DisplayName =>
            GameServices.IsSignedIn ? AuthenticationService.Instance.PlayerName ?? string.Empty : string.Empty;

        /// <summary>The name without its #tag, for greeting the player in their own UI.</summary>
        public static string ShortName
        {
            get
            {
                string full = DisplayName;
                int hash = full.IndexOf('#');
                return hash > 0 ? full.Substring(0, hash) : full;
            }
        }

        /// <summary>
        /// Loads the display name and works out whether an account is attached. Call once after
        /// sign-in, before showing anything that reports either.
        /// </summary>
        public static async Task<bool> RefreshAsync()
        {
            if (!await GameServices.EnsureSignedInAsync())
            {
                return Fail("Not signed in", null);
            }

            try
            {
                // Both are round trips. The name is fetched rather than read off the property because
                // a player who has never had one gets it minted by this call.
                await AuthenticationService.Instance.GetPlayerNameAsync();

                PlayerInfo info = await AuthenticationService.Instance.GetPlayerInfoAsync();
                HasAccount = info?.Username != null;

                OnChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                return Fail("Could not load your profile", e);
            }
        }

        /// <summary>
        /// Changes the public name. The service keeps the #tag, so two players may share a name and
        /// still be told apart — which is the whole reason friends are added by name AND tag.
        /// </summary>
        public static async Task<bool> SetNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Fail("Enter a name", null);
            }

            if (!ValidateName(name))
            {
                return false;
            }

            if (!await GameServices.EnsureSignedInAsync())
            {
                return Fail("Not signed in", null);
            }

            IsBusy = true;
            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(name.Trim());
                OnChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                return Fail("Could not change your name", e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Attaches a username and password to the account the player is ALREADY signed in to.
        ///
        /// This is deliberately not SignUpWithUsernamePasswordAsync, which throws
        /// ClientInvalidUserState when a player is already signed in — and ours always are, from the
        /// anonymous sign-in at boot. More importantly, Add keeps the existing PlayerId, so securing
        /// an account preserves every friend already added; signing up would have created a second,
        /// empty identity and quietly abandoned the first.
        /// </summary>
        public static async Task<bool> CreateAccountAsync(string username, string password)
        {
            if (!ValidateUsername(username) || !ValidatePassword(password))
            {
                return false;
            }

            if (!await GameServices.EnsureSignedInAsync())
            {
                return Fail("Not signed in", null);
            }

            if (HasAccount)
            {
                return Fail("This account already has a username", null);
            }

            IsBusy = true;
            try
            {
                await AuthenticationService.Instance.AddUsernamePasswordAsync(username.Trim(), Harden(password));
                HasAccount = true;

                Debug.Log($"Account secured. PlayerId is unchanged: {GameServices.PlayerId}");
                OnChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                // Only this one code is safe to name. AccountAlreadyLinked means THIS account already
                // carries a username — not that somebody else took the one just typed, which the
                // service reports separately and which the player fixes by choosing another. Guessing
                // between the two would tell half of them to do the wrong thing, so anything else
                // carries the service's own words rather than an invented diagnosis.
                return Fail(IsAlreadyLinked(e)
                    ? "This account already has a username"
                    : $"Could not create the account: {Describe(e)}", e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Signs in to an existing account, for a player arriving on a second device.
        ///
        /// The ordering here is the delicate part of this whole class. Signing in requires being
        /// signed out first, so a mistyped password would leave the player signed out of everything —
        /// and the anonymous identity they had is not something they can type back in. So the sign-out
        /// deliberately does NOT clear credentials: the cached anonymous token survives it, and the
        /// failure path below signs back in with it to restore the very same PlayerId. Passing true
        /// there would destroy the identity holding their friends over a typo.
        /// </summary>
        public static async Task<bool> SignInAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Fail("Enter your username and password", null);
            }

            if (!await GameServices.EnsureSignedInAsync())
            {
                return Fail("Could not reach the server", null);
            }

            IsBusy = true;
            try
            {
                GameServices.SignOut(clearCredentials: false);
                // Harden here too, and identically. A password transformed on the way in but not on
                // the way back is an account nobody can ever open again.
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username.Trim(), Harden(password));

                // This device's win/loss count belongs to whoever was just signed OUT, not whoever
                // just signed in. Until MatchStats can be re-hydrated from a real leaderboard (see
                // LeaderboardHub), the honest thing is to admit this device does not know the
                // incoming player's record rather than show them the previous player's.
                MatchStats.ResetLocal();
                Progression.DailyQuests.ResetForNewPlayer();
                Progression.PlayerXp.ResetForNewPlayer();
                FriendsHub.Reset();

                await RefreshAsync();
                Debug.Log($"Signed in as {DisplayName}.");
                return true;
            }
            catch (Exception e)
            {
                Fail("Wrong username or password", e);

                // Put the player back where they were. Without this they are left signed out, and the
                // next boot mints a brand new anonymous id — a different person, with no friends.
                await GameServices.EnsureSignedInAsync();
                return false;
            }
            finally
            {
                IsBusy = false;
                OnChanged?.Invoke();
            }
        }

        /// <summary>
        /// Deletes this player from the service permanently, and hands the device a fresh anonymous
        /// one so the game is still playable afterwards.
        ///
        /// This deletes the PLAYER, not just the username — the friends list, the display name and
        /// the id every friend has stored all go with it. There is no undo and no recovery, which is
        /// why the only route to it is behind an explicit confirmation.
        ///
        /// Note the sign-out here clears credentials, the exact opposite of <see cref="SignInAsync"/>.
        /// There the cached token was a lifeline worth protecting; here it names a player who no
        /// longer exists, and keeping it would leave the next boot trying to restore a dead account.
        /// </summary>
        public static async Task<bool> DeleteAccountAsync()
        {
            if (!GameServices.IsSignedIn)
            {
                return Fail("Not signed in", null);
            }

            IsBusy = true;
            try
            {
                // Leave first, while there is still an identity to leave as. An abandoned session
                // would otherwise hold a table open under a player the service has just erased.
                if (OnlineSession.IsInSession)
                {
                    await OnlineSession.LeaveAsync();
                }

                await AuthenticationService.Instance.DeleteAccountAsync();

                HasAccount = false;
                GameServices.SignOut(clearCredentials: true);
                FriendsHub.Reset();
                MatchStats.ResetLocal();
                Progression.DailyQuests.ResetForNewPlayer();
                Progression.PlayerXp.ResetForNewPlayer();

                // A player with no identity at all cannot host, join or be added, and nothing in the
                // menu would explain why. Replacing it immediately keeps the game in a working state.
                await GameServices.EnsureSignedInAsync();

                // Mints the replacement's name straight away. Without it the profile screen would sit
                // showing a blank code until something else happened to ask for one.
                await RefreshAsync();

                Debug.Log($"Account deleted. New anonymous PlayerId: {GameServices.PlayerId}");
                return true;
            }
            catch (Exception e)
            {
                return Fail($"Could not delete the account: {Describe(e)}", e);
            }
            finally
            {
                IsBusy = false;
                OnChanged?.Invoke();
            }
        }

        // ---------- validation ----------

        /// <summary>
        /// Checked here as well as by the service so a bad password is answered instantly and by name,
        /// rather than after a round trip and in the SDK's words.
        /// </summary>
        /// <summary>
        /// Checks a display name before it is sent.
        ///
        /// Deliberately on the method rather than only on the text field. The rename field filters
        /// keystrokes, which stops a player typing a bracket and stops nothing else: the field is one
        /// caller of this, the service is reachable without it, and a name set anywhere else still
        /// arrives on every friend's screen. A rule that only exists in the UI is a rule about this
        /// build's keyboard, not about what the game will display.
        /// </summary>
        public static bool ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Fail("Enter a name", null);
            }

            return PlayerNameShape.IsMatch(name.Trim())
                || Fail("Name: up to 20 letters, numbers or . - _", null);
        }

        public static bool ValidateUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return Fail("Enter a username", null);
            }

            return UsernameShape.IsMatch(username.Trim())
                || Fail("Username: 3-20 letters, numbers or . - @ _", null);
        }

        public static bool ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8)
            {
                return Fail("Password: at least 8 characters", null);
            }

            // Measured after hardening, since that is the string the service will actually see and
            // the only length it will judge.
            if (Harden(password).Length > 30)
            {
                return Fail("Password: 29 characters at most", null);
            }

            bool upper = false, lower = false, digit = false;
            foreach (char c in password)
            {
                if (char.IsUpper(c)) upper = true;
                else if (char.IsLower(c)) lower = true;
                else if (char.IsDigit(c)) digit = true;
            }

            if (!upper) return Fail("Password needs a capital letter", null);
            if (!lower) return Fail("Password needs a lower-case letter", null);
            if (!digit) return Fail("Password needs a number", null);

            LastError = string.Empty;
            return true;
        }

        /// <summary>
        /// Adds the symbol the service insists on, if the player did not type one.
        ///
        /// The requirement is Unity's and is enforced on their servers, not here — dropping the check
        /// alone would only move the rejection from this screen to a round trip, and word it worse.
        /// So the rule is satisfied on the player's behalf instead: they are asked for a capital, a
        /// lower-case letter and a number, and the punctuation is supplied.
        ///
        /// Two things this MUST keep doing, or accounts stop opening:
        ///
        /// 1. Be applied identically wherever a password is sent — creating and signing in both go
        ///    through here. A password hardened on the way in and not on the way back is a locked
        ///    account.
        /// 2. Never change. <see cref="Suffix"/> is baked into every password already stored, so
        ///    editing it, or removing this method later, silently invalidates every account created
        ///    before the change. It is a one-way commitment.
        ///
        /// Passwords that already contain a symbol are passed through untouched, so any account made
        /// before this existed still signs in.
        /// </summary>
        private static string Harden(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return password;
            }

            foreach (char c in password)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    return password;
                }
            }

            return password + Suffix;
        }

        // ---------- plumbing ----------

        private static bool IsAlreadyLinked(Exception e) =>
            e is AuthenticationException auth
            && auth.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked;

        /// <summary>
        /// The service's own message, trimmed to something that fits a menu line. Used where the
        /// failure cannot be identified confidently — better a slightly technical sentence the player
        /// can act on or report than a confident guess at the wrong cause.
        /// </summary>
        private static string Describe(Exception e)
        {
            string message = e?.Message ?? "unknown error";
            return message.Length <= 60 ? message : message.Substring(0, 57) + "...";
        }

        private static bool Fail(string message, Exception e)
        {
            LastError = message;
            Debug.LogWarning(e == null ? message : $"{message}: {e.Message}");
            return false;
        }
    }
}
