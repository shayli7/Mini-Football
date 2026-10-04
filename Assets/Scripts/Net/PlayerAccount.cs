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
    /// ever sees. They are separate, and changing one afterwards does not touch the other — the one
    /// exception is the moment an account is created, when the typed username is also taken as the
    /// display name (see <see cref="CreateAccountAsync"/>).
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
        /// <remarks>
        /// Falls back to the last name seen on this device, so a slow or failed network call shows the
        /// player's name rather than a dash. Cleared whenever the device changes hands.
        /// </remarks>
        public static string DisplayName
        {
            get
            {
                string live = GameServices.IsSignedIn ? AuthenticationService.Instance.PlayerName : null;
                return string.IsNullOrEmpty(live) ? PlayerPrefs.GetString(CachedNameKey, string.Empty) : live;
            }
        }

        private const string CachedNameKey = "tf_cached_name";

        /// <summary>Set once the name is one this game picked or the player typed, so the
        /// server-made default is only ever replaced once.</summary>
        private const string NameChosenKey = "tf_name_chosen";

        /// <summary>Longest server-made name (before its #tag) left alone.</summary>
        private const int MaxDefaultNameLength = 12;

        private const int RefreshTimeoutMs = 12000;

        /// <summary>The look of a server-made name (CapitalisedWords, maybe digits), so a long name
        /// someone typed is never overwritten.</summary>
        private static readonly Regex GeneratedNameShape = new(@"^([A-Z][a-z]+)+[0-9]*$", RegexOptions.Compiled);

        // Every word is five letters or fewer, so a name is at most 5 + 5 + 2 digits = 12 characters.
        private static readonly string[] NameFirst =
            { "Red", "Blue", "Fast", "Bold", "Swift", "Lucky", "Nova", "Blaze", "Cool", "Iron",
              "High", "Wild", "Mega", "Turbo", "Rapid", "Epic" };
        private static readonly string[] NameSecond =
            { "Fox", "Owl", "Bat", "Elk", "Ram", "Yak", "Wolf", "Hawk", "Lynx", "Bear",
              "Goat", "Viper", "Bolt", "Comet", "Blitz", "Champ", "Ace", "Kite" };

        private static void CacheName()
        {
            string live = AuthenticationService.Instance.PlayerName;
            if (string.IsNullOrEmpty(live)) return;

            PlayerPrefs.SetString(CachedNameKey, live);
            PlayerPrefs.Save();
        }

        /// <summary>Forgets the cached name. Called wherever the device stops belonging to one person.</summary>
        internal static void ClearCachedName()
        {
            PlayerPrefs.DeleteKey(CachedNameKey);
            PlayerPrefs.DeleteKey(NameChosenKey);
            PlayerPrefs.Save();
        }

        /// <summary>A short random name such as <c>SwiftFox42</c>: two words and exactly two digits, at most
        /// 12 characters. The one generator for every random name — the guest default and the username
        /// dice — so they can never drift apart in length or style.</summary>
        public static string RandomName() =>
            NameFirst[UnityEngine.Random.Range(0, NameFirst.Length)]
            + NameSecond[UnityEngine.Random.Range(0, NameSecond.Length)]
            + UnityEngine.Random.Range(10, 100);

        /// <summary>Awaits a service call for at most <see cref="RefreshTimeoutMs"/>; the SDK call
        /// itself cannot be cancelled, so on timeout it is left to finish and be ignored.</summary>
        private static async Task<bool> CompletesInTime(Task task)
        {
            Task winner = await Task.WhenAny(task, Task.Delay(RefreshTimeoutMs));
            if (winner != task) return false;

            await task; // surfaces the SDK's own exception, if any
            return true;
        }

        /// <summary>
        /// <see cref="RefreshAsync"/> with up to three attempts, for boot: a phone that has just
        /// woken its radio often fails the first call and succeeds a few seconds later.
        /// </summary>
        public static async Task<bool> RefreshWithRetryAsync()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (await RefreshAsync()) return true;
                await Task.Delay(4000 * (attempt + 1));
            }

            return false;
        }

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

            // Two separate calls, each on its own, so the name reaches the screen as soon as it
            // arrives instead of waiting on (or being lost with) the account lookup.
            try
            {
                // The name is fetched rather than read off the property because a player who has
                // never had one gets it minted by this call.
                if (!await CompletesInTime(AuthenticationService.Instance.GetPlayerNameAsync()))
                {
                    return Fail("Loading your profile timed out", null);
                }

                await ShortenDefaultNameAsync();
                CacheName();
                OnChanged?.Invoke();
            }
            catch (Exception e)
            {
                return Fail("Could not load your profile", e);
            }

            try
            {
                Task<PlayerInfo> infoTask = AuthenticationService.Instance.GetPlayerInfoAsync();
                if (!await CompletesInTime(infoTask))
                {
                    return Fail("Loading your account timed out", null);
                }

                HasAccount = infoTask.Result?.Username != null;
                OnChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                return Fail("Could not load your account", e);
            }
        }

        /// <summary>
        /// Replaces the service's long generated name (AdjectiveNoun + digits) with a short one, once.
        /// Skipped if the player has ever named themselves, and on any failure — the long name still works.
        /// </summary>
        private static async Task ShortenDefaultNameAsync()
        {
            if (PlayerPrefs.GetInt(NameChosenKey, 0) == 1) return;

            string full = AuthenticationService.Instance.PlayerName ?? string.Empty;
            int hash = full.IndexOf('#');
            string bare = hash > 0 ? full.Substring(0, hash) : full;

            if (bare.Length == 0) return;

            if (bare.Length <= MaxDefaultNameLength || !GeneratedNameShape.IsMatch(bare))
            {
                PlayerPrefs.SetInt(NameChosenKey, 1);
                return;
            }

            try
            {
                if (await CompletesInTime(AuthenticationService.Instance.UpdatePlayerNameAsync(RandomName())))
                {
                    PlayerPrefs.SetInt(NameChosenKey, 1);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not shorten the default name: {e.Message}");
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
                PlayerPrefs.SetInt(NameChosenKey, 1);
                CacheName();
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

                // The name picked on the sign-up form is the name the player expects to appear as.
                await AdoptUsernameAsDisplayNameAsync(username.Trim());

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
        /// Makes the name typed when the account was created the public name too, once, at creation.
        ///
        /// A username is a credential and a display name is what others see, and they stay separate
        /// afterwards — renaming does not touch the login. But at the moment of creating an account the
        /// player has typed ONE name, and showing up as a different one (the random name the game gave
        /// them as a guest) reads as a bug. The username's alphabet is a little wider than a display
        /// name's (it allows <c>@</c>), so characters a display name cannot hold are dropped, and if
        /// nothing is left the existing name is simply kept.
        ///
        /// Never fails the account: it is already created by the time this runs, and a name that does not
        /// stick is something the player can fix from the profile.
        /// </summary>
        private static async Task AdoptUsernameAsDisplayNameAsync(string username)
        {
            var kept = new System.Text.StringBuilder();
            foreach (char c in username)
            {
                if (char.IsLetterOrDigit(c) && c < 128 || c == '.' || c == '-' || c == '_') kept.Append(c);
            }

            string name = kept.ToString();
            if (!PlayerNameShape.IsMatch(name)) return;

            try
            {
                if (await CompletesInTime(AuthenticationService.Instance.UpdatePlayerNameAsync(name)))
                {
                    PlayerPrefs.SetInt(NameChosenKey, 1);
                    CacheName();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not use the username as the display name: {e.Message}");
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
                // Save the OUTGOING player's progress before their identity goes away. Everything
                // below wipes the local stores and loads the incoming account, so a reward earned in
                // the last few seconds — still inside CloudSync's debounce and not yet uploaded —
                // would otherwise be lost with the sign-out. Flushed while still signed in as them, so
                // it lands in their cloud and not the new account's.
                await CloudSync.FlushAsync();

                GameServices.SignOut(clearCredentials: false);
                // Harden here too, and identically. A password transformed on the way in but not on
                // the way back is an account nobody can ever open again.
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username.Trim(), Harden(password));

                // This device's win/loss count belongs to whoever was just signed OUT, not whoever
                // just signed in. Until MatchStats can be re-hydrated from a real leaderboard (see
                // LeaderboardHub), the honest thing is to admit this device does not know the
                // incoming player's record rather than show them the previous player's.
                MatchStats.ResetLocal();
                PlayerProgress.ResetLocal();
                // The rewards half of the same record: coins earned, cosmetics unlocked, and any
                // uncollected weekly ranked payout. All three are things a PERSON earned, so the
                // incoming player opens a fresh purse and an empty collection rather than inheriting
                // the outgoing one's.
                Wallet.ResetLocal();
                Inventory.ResetLocal();
                RankedRewards.ResetLocal();
                Ladder.ResetLocal();
                Progression.DailyQuests.ResetForNewPlayer();
                Progression.PlayerXp.ResetForNewPlayer();
                ClearCachedName();
                FriendsHub.Reset();

                // The local record is now blank; pull the incoming player's cloud save into it, so
                // signing in as someone else opens THEIR collection rather than an empty one. Merges
                // against the just-wiped local, so it is simply a load of the other account's document.
                await CloudSync.LoadAsync();

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

                // The server-side save first, while this identity can still reach Cloud Code: once the
                // account is gone nothing can say whose document to erase. Best effort — a failure
                // leaves the document behind but must not stop the player deleting their account.
                await CloudSync.EraseRemoteAsync();

                await AuthenticationService.Instance.DeleteAccountAsync();

                HasAccount = false;
                GameServices.SignOut(clearCredentials: true);
                FriendsHub.Reset();
                MatchStats.ResetLocal();
                PlayerProgress.ResetLocal();
                Wallet.ResetLocal();
                Inventory.ResetLocal();
                RankedRewards.ResetLocal();
                Ladder.ResetLocal();
                Progression.DailyQuests.ResetForNewPlayer();
                Progression.PlayerXp.ResetForNewPlayer();
                ClearCachedName();

                // A player with no identity at all cannot host, join or be added, and nothing in the
                // menu would explain why. Replacing it immediately keeps the game in a working state.
                await GameServices.EnsureSignedInAsync();

                // The replacement is a brand-new anonymous player with no cloud document; loading now
                // just confirms that and sets the sync's loaded gate, so the fresh 250-coin float this
                // player is about to be seeded flushes up as their first save rather than colliding.
                await CloudSync.LoadAsync();

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

        /// <summary>
        /// Changes the password of the account the player is signed in to. Needs the current one, so a
        /// phone left unlocked on a table cannot be used to take an account over.
        ///
        /// Both passwords go through <see cref="Harden"/>, exactly as when they were set and as when
        /// signing in: a password transformed on the way in but not here would be an account nobody
        /// can open again. The new one is validated first, so a weak one is refused instantly and by
        /// name rather than after a round trip.
        /// </summary>
        public static async Task<bool> ChangePasswordAsync(string current, string next)
        {
            if (string.IsNullOrEmpty(current))
            {
                return Fail("Enter your current password", null);
            }

            if (!ValidatePassword(next))
            {
                return false;
            }

            if (!HasAccount)
            {
                return Fail("This account has no password yet", null);
            }

            if (!await GameServices.EnsureSignedInAsync())
            {
                return Fail("Not signed in", null);
            }

            IsBusy = true;
            try
            {
                await AuthenticationService.Instance.UpdatePasswordAsync(Harden(current), Harden(next));
                LastError = string.Empty;
                return true;
            }
            catch (Exception e)
            {
                return Fail($"Could not change the password: {Describe(e)}", e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Signs out of the account and leaves the device with a fresh guest. The account itself is
        /// untouched on the service — signing in with its username and password brings everything back.
        ///
        /// The mirror of <see cref="SignInAsync"/>, in the order that matters: the outgoing player's
        /// progress is uploaded FIRST, while still signed in as them, and only then are the local
        /// stores wiped. Credentials are cleared, unlike a failed sign-in, because the cached token
        /// names the account being left — keeping it would sign the next boot straight back in.
        /// </summary>
        public static async Task<bool> LogOutAsync()
        {
            if (!GameServices.IsSignedIn || !HasAccount)
            {
                return Fail("You are not logged in to an account", null);
            }

            IsBusy = true;
            try
            {
                // Leave first, for the same reason deleting does: a table left open under a player who
                // has just gone would hold a seat on the service.
                if (OnlineSession.IsInSession)
                {
                    await OnlineSession.LeaveAsync();
                }

                await CloudSync.FlushAsync();

                HasAccount = false;
                GameServices.SignOut(clearCredentials: true);
                FriendsHub.Reset();
                MatchStats.ResetLocal();
                PlayerProgress.ResetLocal();
                Wallet.ResetLocal();
                Inventory.ResetLocal();
                RankedRewards.ResetLocal();
                Ladder.ResetLocal();
                Progression.DailyQuests.ResetForNewPlayer();
                Progression.PlayerXp.ResetForNewPlayer();
                ClearCachedName();

                // Replaced at once so the game keeps working, and its (empty) cloud document loaded so
                // the sync gate opens for the new guest.
                await GameServices.EnsureSignedInAsync();
                await CloudSync.LoadAsync();
                await RefreshAsync();

                Debug.Log($"Logged out. New anonymous PlayerId: {GameServices.PlayerId}");
                return true;
            }
            catch (Exception e)
            {
                return Fail($"Could not log out: {Describe(e)}", e);
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
