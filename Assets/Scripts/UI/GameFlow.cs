using System.Collections;
using TableFootball.Net;
using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Owns the sequence of the game, and is the only place that knows it:
    ///
    ///   boot -> start screen -> menu -> (mode chosen) -> playing
    ///   playing -> (match won | left from pause) -> loading -> menu
    ///
    /// The menu reports choices, the HUD reports events, and neither knows what comes next. Putting
    /// the order in one class is what keeps them that way.
    ///
    /// It also owns <see cref="Time.timeScale"/> for the front end. GameMenu still pauses for itself,
    /// which is safe only because the two are never up at once — so returning to the menu closes the
    /// pause menu first, explicitly, rather than trusting that it happened to be shut.
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        private StartScreen start;
        private LoadingScreen loading;
        private MainMenu menu;
        private OnlineMenu online;
        private FriendsMenu friends;
        private ProfileMenu profile;
        private FriendProfileMenu friendProfile;
        private GameMenu pause;
        private ScoreHud hud;
        private CountdownScreen countdown;
        private MatchManager match;

        [SerializeField] private float bootLoadSeconds = 1.5f;
        [SerializeField] private float transitionLoadSeconds = 1.0f;

        /// <summary>True once a mode has been chosen and the table is live.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>Which team the local player has online, captured at kick-off — see <see cref="AwardForfeit"/>.</summary>
        private Team onlineLocalTeam = Team.Red;

        /// <summary>Set while this player is the one leaving, so their own exit is not read as the opponent's.</summary>
        private bool leavingDeliberately;

        /// <summary>Set from a session callback, acted on in <see cref="Update"/>.</summary>
        private bool opponentLeft;

        /// <summary>Which door the account screen was entered by, so Back can retrace it.</summary>
        private bool profileFromFriends;

        /// <summary>
        /// True when the current — or most recently finished — match is an online one.
        ///
        /// Deliberately NOT cleared when the result is counted. It has to outlive the end of the match
        /// so that what happens on the result screen still knows it is looking at an online game.
        /// </summary>
        private bool onlineMatch;

        /// <summary>Guards the record against counting one match twice. Reset at every kick-off.</summary>
        private bool resultCounted;

        /// <summary>True between asking for a rematch and being answered, which changes what a
        /// departing opponent means: not a forfeit to award, but a rematch nobody is coming to.</summary>
        private bool waitingForRematch;

        /// <summary>The pause between telling the player nobody is coming and taking them to the menu.</summary>
        private Coroutine declineRoutine;

        /// <summary>Who turned down this player's invitation, set from a service callback and acted
        /// on in <see cref="Update"/>. Null when there is nothing to report.</summary>
        private string inviteDeclinedBy;

        /// <summary>The pause between saying the invited friend refused and leaving the table.</summary>
        private Coroutine inviteDeclineRoutine;

        /// <summary>The pre-match countdown, running between PrepareMatch and the kick-off.</summary>
        private Coroutine startRoutine;

        public void Build(StartScreen startScreen, LoadingScreen loadingScreen,
                          MainMenu mainMenu, OnlineMenu onlineMenu,
                          FriendsMenu friendsMenu, ProfileMenu profileMenu,
                          FriendProfileMenu friendProfileMenu,
                          GameMenu pauseMenu, ScoreHud scoreHud, CountdownScreen countdownScreen,
                          MatchManager matchManager,
                          float bootSeconds, float transitionSeconds)
        {
            start = startScreen;
            loading = loadingScreen;
            menu = mainMenu;
            online = onlineMenu;
            friends = friendsMenu;
            profile = profileMenu;
            friendProfile = friendProfileMenu;
            pause = pauseMenu;
            hud = scoreHud;
            countdown = countdownScreen;
            match = matchManager;
            bootLoadSeconds = bootSeconds;
            transitionLoadSeconds = transitionSeconds;

            if (menu != null)
            {
                menu.OnStartLocal += StartLocalMatch;
                menu.OnOpenOnline += OpenOnline;
                // Reuses the pause menu's settings panel rather than building a second one, so the
                // sliders and the difficulty control stay in one place.
                menu.OnOpenSettings += OpenSettings;
                menu.OnOpenFriends += OpenFriends;
                menu.OnOpenProfile += OpenProfileFromMenu;
                menu.OnAcceptInvite = AcceptInvite;
            }

            if (online != null)
            {
                // Deliberately NOT StartOnlineMatch. The session reporting two players means the
                // lobby has recorded the guest, not that the guest can see the table — the match
                // starts when the host says so, over the connection itself.
                online.OnMatchReady = () => online.ShowConnecting();
                online.OnBack = OpenMenu;
            }

            if (friends != null)
            {
                // Joining from the list lands in the same place as joining by typed code.
                friends.OnJoinedFriend = OpenOnlineConnecting;
                // Inviting opens a table and then waits at it, which is exactly the host's screen —
                // so an invite and a Host Table tap end up in the same place, as they should.
                friends.OnInviteSent = OpenOnlineWaiting;
                friends.OnOpenFriendProfile = OpenFriendProfile;
                friends.OnOpenProfile = OpenProfile;
                friends.OnBack = OpenMenu;
            }

            if (friendProfile != null)
            {
                friendProfile.OnJoinedFriend = OpenOnlineConnecting;
                friendProfile.OnInviteSent = OpenOnlineWaiting;
                // Back to the list they were picked from, never the main menu.
                friendProfile.OnBack = OpenFriends;
            }

            if (profile != null)
            {
                // Back returns wherever the player actually came from. The account screen has two
                // doors now — the friends list and the main menu's avatar — and a fixed destination
                // would drop half of its visitors somewhere they were never standing.
                profile.OnBack = () =>
                {
                    if (profileFromFriends) OpenFriends();
                    else OpenMenu();
                };
            }

            if (hud != null)
            {
                hud.OnReturnToMenu = ReturnToMenu;
                hud.OnRematchRequested = RequestRematch;
            }

            if (pause != null)
            {
                pause.OnLeaveMatch = ReturnToMenu;
            }

            // The record is kept here rather than in MatchManager, which is deliberately ignorant of
            // whether a match is online at all — and an AI match must not count towards an online
            // record. This class already knows both facts.
            if (match != null)
            {
                match.MatchWon += RecordOnlineResult;
            }

            // Play starts and ends on the transport's word, not the lobby's. The director raises both
            // of these off the NGO connection itself, which is the only thing that knows when the two
            // players can actually see each other.
            OnlineMatchDirector.OnMatchShouldStart += StartOnlineMatch;
            OnlineMatchDirector.OnOpponentGone += HandleOpponentGone;
            OnlineMatchDirector.OnRematchStarting += HandleRematchStarting;
            OnlineMatchDirector.OnRematchDeclined += HandleRematchDeclined;

            // Kept as a second opinion on the DEPARTURE only — the lobby no longer starts anything.
            // It is slower than the transport and sometimes silent, but it occasionally sees a player
            // vanish that the transport has not given up on yet. Both routes set the same flag, so a
            // duplicate costs nothing and whichever arrives first ends the match.
            OnlineSession.OnPlayerCountChanged += HandlePlayerCount;
            OnlineSession.OnLeft += HandleSessionLeft;

            FriendsHub.OnInviteDeclined += HandleInviteDeclined;

            Boot();
        }

        private void OnDestroy()
        {
            if (menu != null)
            {
                menu.OnStartLocal -= StartLocalMatch;
                menu.OnOpenOnline -= OpenOnline;
                menu.OnOpenSettings -= OpenSettings;
                menu.OnOpenFriends -= OpenFriends;
                menu.OnOpenProfile -= OpenProfileFromMenu;
            }

            if (match != null)
            {
                match.MatchWon -= RecordOnlineResult;
            }

            OnlineMatchDirector.OnMatchShouldStart -= StartOnlineMatch;
            OnlineMatchDirector.OnOpponentGone -= HandleOpponentGone;
            OnlineMatchDirector.OnRematchStarting -= HandleRematchStarting;
            OnlineMatchDirector.OnRematchDeclined -= HandleRematchDeclined;

            OnlineSession.OnPlayerCountChanged -= HandlePlayerCount;
            OnlineSession.OnLeft -= HandleSessionLeft;

            FriendsHub.OnInviteDeclined -= HandleInviteDeclined;
        }

        /// <summary>
        /// Counts one online result, win or loss.
        ///
        /// <c>onlineMatch</c> is cleared as it fires, so a match can only ever be counted once. Both
        /// players reach this from their own copy of the same MatchWon — the host's from the goal that
        /// decided it, the guest's from the relayed one — and a forfeit counts exactly like any other
        /// result, since walking out is a way of losing.
        /// </summary>
        private void RecordOnlineResult(Team winner)
        {
            if (!onlineMatch || resultCounted)
            {
                return;
            }

            resultCounted = true;
            MatchStats.RecordResult(winner == onlineLocalTeam);
        }

        // ---------- opponent left ----------

        private void HandlePlayerCount(int players)
        {
            // Gated on IsPlaying because the count also sits at one for as long as a host waits in
            // the online menu for anybody to arrive, which is not somebody leaving. Gated on the
            // deliberate flag for the same reason the other two routes are: this player's own exit
            // drops the count as surely as their opponent's does.
            if (IsPlaying && !leavingDeliberately && players < 2)
            {
                opponentLeft = true;
            }
        }

        /// <summary>The transport's account of the opponent leaving, and the one that actually arrives.</summary>
        private void HandleOpponentGone()
        {
            if (IsPlaying && !leavingDeliberately)
            {
                opponentLeft = true;
            }
        }

        private void HandleSessionLeft()
        {
            // LeaveAsync raises this on the way out too, so without the flag the player who quit
            // would arrive here and award themselves the match they just abandoned.
            if (IsPlaying && !leavingDeliberately)
            {
                opponentLeft = true;
            }
        }

        /// <summary>
        /// Session callbacks are acted on here rather than where they arrive, matching
        /// <see cref="OnlineMenu"/>: awarding the match runs straight into MatchManager's events and
        /// the whole HUD behind them, which is more than belongs in an SDK callback.
        /// </summary>
        private void Update()
        {
            if (opponentLeft)
            {
                opponentLeft = false;

                // The same departure means two different things depending on when it lands. Mid-match
                // it is a forfeit and this player wins it; while a rematch is being decided the match
                // is already over and there is nothing left to award — only a rematch that is not
                // going to happen. Sending it down the forfeit path was why the waiting player was
                // simply left on the banner.
                if (waitingForRematch)
                {
                    HandleRematchDeclined();
                }
                else
                {
                    AwardForfeit();
                }
            }

            if (inviteDeclinedBy != null)
            {
                string who = inviteDeclinedBy;
                inviteDeclinedBy = null;
                ShowInviteDeclined(who);
            }
        }

        /// <summary>
        /// Answering an invitation from the main menu. Lands in exactly the same place as answering
        /// one from the friends list: the online screen, connecting, waiting for the host's kick-off.
        /// </summary>
        private async void AcceptInvite(string joinCode)
        {
            OpenOnlineConnecting();

            if (!await OnlineSession.JoinAsync(joinCode) && online != null)
            {
                // Most often their table filled between the invitation and the tap.
                online.ShowJoinFailed(OnlineSession.LastError);
            }
        }

        // ---------- invitation refused ----------

        private void HandleInviteDeclined(string who)
        {
            // Only means anything while still waiting at the table opened for that person. Once a
            // match is under way somebody took the seat, and a refusal arriving after that is about
            // a table that has already moved on.
            if (IsPlaying)
            {
                return;
            }

            inviteDeclinedBy = who ?? string.Empty;
        }

        private void ShowInviteDeclined(string who)
        {
            if (online == null || !online.IsOpen)
            {
                return;
            }

            online.ShowInviteDeclined(who);

            if (inviteDeclineRoutine != null) StopCoroutine(inviteDeclineRoutine);
            inviteDeclineRoutine = StartCoroutine(LeaveTableAfterDecline());
        }

        /// <summary>
        /// Long enough to read the refusal, then out. A table opened for one named person has no
        /// purpose left once that person has answered, and leaving it sitting there holds a session
        /// open on the service that this player would then be refused their next match for.
        /// </summary>
        private IEnumerator LeaveTableAfterDecline()
        {
            yield return new WaitForSecondsRealtime(2.2f);

            inviteDeclineRoutine = null;

            // Still there? They may have backed out themselves, or a stranger may have walked into
            // the same table while the refusal was in flight — in which case they are mid-match and
            // this is the last thing they want.
            if (!IsPlaying && online != null && online.IsOpen)
            {
                ReturnToMenu();
            }
        }

        // ---------- rematch ----------

        private void RequestRematch()
        {
            waitingForRematch = true;
            OnlineMatchDirector.RequestRematch();
        }

        /// <summary>
        /// Both agreed. Restarts exactly as the first kick-off does — off one message, on both
        /// machines, with the director doing the restarting.
        /// </summary>
        private void HandleRematchStarting()
        {
            waitingForRematch = false;

            // A fresh match, so its result has not been counted yet.
            onlineMatch = true;
            resultCounted = false;
            IsPlaying = true;

            if (hud != null) hud.ResultsHidden = false;
        }

        /// <summary>
        /// Nobody is coming — the opponent left, or let the offer time out. Says so on the banner the
        /// player is already reading, then takes them back to the menu rather than leaving them at a
        /// table with no second player.
        /// </summary>
        private void HandleRematchDeclined()
        {
            if (!waitingForRematch)
            {
                return;
            }

            waitingForRematch = false;

            if (hud != null) hud.ShowRematchDeclined("YOUR OPPONENT ISN'T PLAYING AGAIN");

            if (declineRoutine != null) StopCoroutine(declineRoutine);
            declineRoutine = StartCoroutine(LeaveAfterDecline());
        }

        private IEnumerator LeaveAfterDecline()
        {
            // Long enough to read, short enough not to feel stuck. Unscaled, since the world behind
            // the banner may be frozen.
            yield return new WaitForSecondsRealtime(2.2f);

            declineRoutine = null;
            ReturnToMenu();
        }

        /// <summary>
        /// Gives the match to the player still here.
        ///
        /// Uses the team captured at kick-off rather than asking who is host now: by the time the
        /// host's departure reaches the guest, NetworkManager has already shut down and would answer
        /// for a session that no longer exists.
        /// </summary>
        private void AwardForfeit()
        {
            if (!IsPlaying)
            {
                return;
            }

            // Before the award, not after: MatchManager raises MatchWon synchronously, and the HUD
            // is entitled to ask what state the flow is in while handling it.
            IsPlaying = false;

            // The opponent can perfectly well leave while this player is sitting in the pause menu,
            // and the win banner would then be raised behind it — under a menu still offering to
            // resume a match that no longer exists, in a world stopped at timeScale 0.
            if (pause != null && pause.IsOpen)
            {
                pause.Resume();
            }

            bool awarded = match != null && match.AwardWinByForfeit(onlineLocalTeam);

            // Nothing to award: the match had already been decided and they left afterwards. The
            // result stands, but the banner is still offering a rematch to an empty table, so say so
            // and take them out rather than letting them press it and wait for a timeout.
            if (!awarded && onlineMatch)
            {
                if (hud != null) hud.ShowRematchDeclined("YOUR OPPONENT LEFT");

                if (declineRoutine != null) StopCoroutine(declineRoutine);
                declineRoutine = StartCoroutine(LeaveAfterDecline());
            }
        }

        // ---------- flow ----------

        /// <summary>
        /// First boot. The title screen holds until the player taps, rather than a loading bar
        /// counting down a wait nobody was told about — and the tap covers the sign-in round trip
        /// better than the bar did, because it takes longer.
        ///
        /// The bar is still the fallback if no title screen was built, which keeps
        /// <see cref="bootLoadSeconds"/> meaningful and the boot path working either way.
        /// </summary>
        private void Boot()
        {
            Freeze();
            if (menu != null) menu.Close();
            if (hud != null) hud.SetVisible(false);

            if (start != null) start.Show(OpenMenu);
            else loading.Show(bootLoadSeconds, OpenMenu);
        }

        /// <summary>Leaves play and returns to the menu, behind the loading curtain.</summary>
        public void ReturnToMenu()
        {
            // First, before anything can raise a result on this screen. Leaving is this player's own
            // doing: nothing that follows from it — their opponent's forfeit win, their own recorded
            // loss — is news they should be handed a scoreboard about on their way to the menu.
            leavingDeliberately = true;

            if (hud != null)
            {
                hud.ResultsHidden = true;
                hud.SetVisible(false);
            }

            // Walking out of a live online match is a loss, and has to be recorded here because no
            // MatchWon will ever fire on this machine — the opponent's does, on theirs. Without this
            // the player who quit is the only one whose record does not change, which would make
            // quitting the cheapest way to protect a win rate.
            if (IsPlaying && onlineMatch && !resultCounted)
            {
                resultCounted = true;
                MatchStats.RecordResult(won: false);
            }

            IsPlaying = false;
            onlineMatch = false;
            waitingForRematch = false;

            if (declineRoutine != null)
            {
                StopCoroutine(declineRoutine);
                declineRoutine = null;
            }

            if (inviteDeclineRoutine != null)
            {
                StopCoroutine(inviteDeclineRoutine);
                inviteDeclineRoutine = null;
            }

            inviteDeclinedBy = null;

            // Left before the count finished, so the kick-off waiting at the end of it must not
            // fire into a match nobody is in any more.
            if (startRoutine != null)
            {
                StopCoroutine(startRoutine);
                startRoutine = null;
            }

            if (countdown != null) countdown.Cancel();

            // Shut the pause menu first: it owns timeScale while it is open, and would otherwise
            // restore it the moment it closed and unfreeze a menu that expects a stopped world.
            if (pause != null && pause.IsOpen)
            {
                pause.Resume();
            }

            // Not awaited: the menu should come up at once, and the session can tear itself down
            // behind the loading curtain. Leaving matters even so — an abandoned session holds a
            // table open on the service and would refuse this player their next one.
            //
            // Safe to call because leavingDeliberately was set at the top of this method: LeaveAsync
            // raises OnLeft, and this player must not be handed the match they are walking out of.
            if (OnlineSession.IsInSession)
            {
                _ = OnlineSession.LeaveAsync();
            }

            Freeze();
            loading.Show(transitionLoadSeconds, OpenMenu);
        }

        private void OpenSettings()
        {
            if (pause != null) pause.OpenSettingsStandalone();
        }

        private void OpenMenu()
        {
            Freeze();
            if (hud != null) hud.SetVisible(false);
            if (online != null) online.Close();
            if (friends != null) friends.Close();
            if (profile != null) profile.Close();
            if (friendProfile != null) friendProfile.Close();
            if (menu != null) menu.Open();
            GameSfx.PlayMenuMusic();
        }

        /// <summary>
        /// A friend's profile. Unfrozen like the rest of the friends flow, since both of its buttons
        /// lead straight into a session and the transport needs a clock to get there.
        /// </summary>
        private void OpenFriendProfile(string memberId)
        {
            if (menu != null) menu.Close();
            if (friends != null) friends.Close();
            if (profile != null) profile.Close();
            if (hud != null) hud.SetVisible(false);

            Unfreeze();

            if (friendProfile != null) friendProfile.Open(memberId);
        }

        /// <summary>
        /// Where inviting lands: the online screen showing the code and waiting, which is the same
        /// place Host Table leads. The invited friend is on their way, and if they do not come the
        /// player is already sitting somewhere they can read the code out or back out from.
        /// </summary>
        private void OpenOnlineWaiting()
        {
            if (menu != null) menu.Close();
            if (friends != null) friends.Close();
            if (friendProfile != null) friendProfile.Close();
            if (hud != null) hud.SetVisible(false);

            Unfreeze();

            if (online != null)
            {
                online.Open();
                online.ShowHosting();
            }
        }

        /// <summary>
        /// Opens the friends list. Runs unfrozen for the same reason the online screen does — a Join
        /// from here goes straight into a session, and the transport needs a clock to reach it.
        /// </summary>
        private void OpenFriends()
        {
            if (menu != null) menu.Close();
            if (profile != null) profile.Close();
            if (friendProfile != null) friendProfile.Close();
            if (hud != null) hud.SetVisible(false);

            Unfreeze();

            if (friends != null) friends.Open();
        }

        /// <summary>
        /// Joining from the friends list has already put this player in the session, so it lands on
        /// the online screen's connecting state rather than starting play. Without somewhere to wait,
        /// closing the friends list would leave a blank screen until the host's kick-off arrived.
        /// </summary>
        private void OpenOnlineConnecting()
        {
            if (friends != null) friends.Close();
            if (friendProfile != null) friendProfile.Close();
            if (menu != null) menu.Close();
            if (hud != null) hud.SetVisible(false);

            Unfreeze();

            if (online != null)
            {
                online.Open();
                online.ShowConnecting();
            }
        }

        /// <summary>Reached from the friends list's Account button, so Back goes back there.</summary>
        private void OpenProfile()
        {
            profileFromFriends = true;
            ShowProfile();
        }

        /// <summary>Reached from the main menu's avatar, so Back goes back to the main menu.</summary>
        private void OpenProfileFromMenu()
        {
            profileFromFriends = false;
            ShowProfile();
        }

        private void ShowProfile()
        {
            if (menu != null) menu.Close();
            if (friends != null) friends.Close();
            if (friendProfile != null) friendProfile.Close();
            if (hud != null) hud.SetVisible(false);

            Unfreeze();

            if (profile != null) profile.Open();
        }

        /// <summary>
        /// Opens the online screen, and deliberately leaves the world running while it is up.
        ///
        /// Everything else in the front end sits at timeScale 0, but Netcode advances its tick on
        /// scaled delta time: a host waiting at zero stops sending, and times the guest out before
        /// they can arrive. The table behind the panel is parked and hidden by the backdrop anyway.
        /// </summary>
        private void OpenOnline()
        {
            if (menu != null) menu.Close();
            if (hud != null) hud.SetVisible(false);

            Unfreeze();

            if (online != null) online.Open();
        }

        private void StartLocalMatch(bool aiOpponent)
        {
            Team playerTeam = ApplyMode(aiOpponent);

            if (menu != null) menu.Close();
            if (hud != null) hud.SetVisible(true);
            GameSfx.StopMenuMusic();

            if (pause != null) pause.OnlineMatch = false;
            if (hud != null)
            {
                hud.OnlineMatch = false;
                hud.ResultsHidden = false;

                // Against the AI there is one player and the result is theirs to be told; head to
                // head there are two, and the banner has to name a team because "you" would be
                // half right and half wrong at the same table.
                hud.LocalTeam = aiOpponent ? playerTeam : (Team?)null;
            }
            onlineMatch = false;
            resultCounted = false;
            waitingForRematch = false;

            IsPlaying = true;

            // Reset the score, the ball and the rods now, but do NOT start play: the countdown wants
            // a settled table to count over, and the kick-off whistle belongs after "GO!" rather than
            // before the count. PrepareMatch is everything RestartMatch does except going live.
            if (match != null) match.PrepareMatch();

            if (startRoutine != null) StopCoroutine(startRoutine);
            startRoutine = StartCoroutine(CountdownThenKickOff());
        }

        /// <summary>
        /// Holds the world at a stop for "3 - 2 - 1 - GO!", then starts play.
        ///
        /// Frozen through the count so the ball cannot be touched before GO. The count itself runs on
        /// unscaled time, like every other front-end animation, so a stopped clock does not stop it.
        ///
        /// Local matches only. An online match must not freeze: Netcode advances its tick on scaled
        /// delta time, so a machine sitting at timeScale 0 stops sending and times its opponent out -
        /// the same reason <see cref="OpenOnline"/> leaves the world running.
        /// </summary>
        private IEnumerator CountdownThenKickOff()
        {
            Freeze();

            if (countdown != null)
            {
                yield return countdown.Run();
            }

            Unfreeze();

            // The whistle rides MatchManager's KickedOff, so kicking off here is precisely what puts
            // it after the GO rather than before the count.
            if (match != null) match.KickOff();

            startRoutine = null;
        }

        /// <summary>
        /// Starts the online match once both players are in.
        ///
        /// No ApplyMode call: teams, rod ownership and switching the AI off are all
        /// OnlineMatchDirector's, decided from who is host rather than from a menu choice.
        /// </summary>
        private void StartOnlineMatch()
        {
            // The kick-off message is delivered to everyone including its sender, and a reconnect
            // would send another. A second one must not restart a match already in progress.
            if (IsPlaying)
            {
                return;
            }

            if (online != null) online.Close();
            if (menu != null) menu.Close();
            if (hud != null) hud.SetVisible(true);
            GameSfx.StopMenuMusic();

            // Captured now, while the session is unambiguously alive. See AwardForfeit.
            onlineLocalTeam = OnlineMatchDirector.LocalTeam;
            leavingDeliberately = false;

            if (pause != null) pause.OnlineMatch = true;
            if (hud != null)
            {
                hud.OnlineMatch = true;
                hud.ResultsHidden = false;

                // The same team the forfeit award uses, and captured at the same moment for the same
                // reason: by the time a result lands, NetworkManager may be gone and unable to say
                // which side this machine was playing.
                hud.LocalTeam = onlineLocalTeam;
            }
            onlineMatch = true;
            resultCounted = false;
            waitingForRematch = false;

            Unfreeze();
            IsPlaying = true;

            // The table itself is not started here. The director restarts both machines off the one
            // message that brought us here, so the kick-off cannot depend on this side working out
            // for itself whether it is the one that should call it.
        }

        /// <summary>
        /// Sets up the two local modes. Both are driven through the normal components — the AI is
        /// simply switched off for two-player, rather than a separate code path existing for it.
        ///
        /// Returns the team the human is left holding, which the HUD needs in order to say YOU WIN
        /// rather than name a colour. It is derived here and nowhere else, so handing it back beats
        /// working it out a second time somewhere that could disagree.
        /// </summary>
        private Team ApplyMode(bool aiOpponent)
        {
            TeamAI[] ais = FindObjectsByType<TeamAI>(FindObjectsInactive.Include,
                                                     FindObjectsSortMode.None);

            // The human plays whichever side the AI does not. Deriving this rather than naming a
            // team is the whole point: hardcoding one meant that if the AI was set to that same
            // team, both ended up on the same rods and the human's actual team could not be
            // touched at all.
            Team playerTeam = Team.Red;
            foreach (TeamAI ai in ais)
            {
                if (ai != null)
                {
                    playerTeam = ai.Team == Team.Red ? Team.Blue : Team.Red;
                    break;
                }
            }

            foreach (TeamAI ai in ais)
            {
                if (ai != null)
                {
                    ai.enabled = aiOpponent;
                }
            }

            RodTouchInput[] inputs = FindObjectsByType<RodTouchInput>(FindObjectsInactive.Include,
                                                                     FindObjectsSortMode.None);
            foreach (RodTouchInput input in inputs)
            {
                if (input == null)
                {
                    continue;
                }

                // Against the AI the player may only touch their own rods; head to head, either
                // player may reach any rod.
                input.SetTeamRestriction(aiOpponent, playerTeam);
            }

            Debug.Log(aiOpponent
                ? $"Mode: AI vs Player — you play {playerTeam}."
                : "Mode: Player vs Player — both teams playable.", this);

            return playerTeam;
        }

        // ---------- world freezing ----------

        private static void Freeze()
        {
            Time.timeScale = 0f;
        }

        private static void Unfreeze()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }
}
