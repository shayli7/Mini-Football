using System.Collections;
using TableFootball.Net;
using TableFootball.Progression;
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
        private OnboardingScreen onboarding;
        private LoadingScreen loading;

        /// <summary>The 3D table behind the front end. Optional — null if not added to the scene.</summary>
        private MenuStageCamera stage;

        private MainMenu menu;
        private OnlineMenu online;
        private LeagueMenu league;
        private StoreMenu store;
        private LevelPathMenu levelPath;
        private FriendsMenu friends;
        private ProfileMenu profile;
        private FriendProfileMenu friendProfile;
        private QuestsMenu quests;
        private GameMenu pause;
        private ScoreHud hud;
        private CountdownScreen countdown;
        private MatchManager match;
        private QuestTracker questTracker;

        [SerializeField] private float bootLoadSeconds = 1.5f;
        [SerializeField] private float transitionLoadSeconds = 1.0f;

        /// <summary>True once a mode has been chosen and the table is live.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>Which team the local player has online, captured at kick-off — see <see cref="AwardForfeit"/>.</summary>
        private Team onlineLocalTeam = Team.Red;

        /// <summary>The local player's team for the current match, or null when there is no single local
        /// player (local PvP, two people on one device). Mirrors <c>hud.LocalTeam</c>; the gate for
        /// crediting progression.</summary>
        private Team? localTeam;

        /// <summary>Guards progression against counting one match twice. Reset at every kick-off.</summary>
        private bool progressCounted;

        /// <summary>Set while this player is the one leaving, so their own exit is not read as the opponent's.</summary>
        private bool leavingDeliberately;

        /// <summary>Set from a session callback, acted on in <see cref="Update"/>.</summary>
        private bool opponentLeft;

        /// <summary>Which door the account screen was entered by, so Back can retrace it.</summary>
        private bool profileFromFriends;

        /// <summary>Which door the league screen was entered by, so Back can retrace it — the main
        /// menu's trophy button, or the online menu's own Ranked button.</summary>
        private bool leagueFromMainMenu;

        /// <summary>
        /// True when the current — or most recently finished — match is an online one.
        ///
        /// Deliberately NOT cleared when the result is counted. It has to outlive the end of the match
        /// so that what happens on the result screen still knows it is looking at an online game.
        /// </summary>
        private bool onlineMatch;
        private bool rankedMatch;

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

        /// <summary>Live play time not yet written to <see cref="PlayerProgress"/>. Accumulated whole
        /// while a match runs and flushed in whole seconds at the transitions, so it is never a
        /// per-frame PlayerPrefs write.</summary>
        private float playClock;

        public void Build(StartScreen startScreen, OnboardingScreen onboardingScreen,
                          LoadingScreen loadingScreen,
                          MainMenu mainMenu, OnlineMenu onlineMenu, LeagueMenu leagueMenu,
                          StoreMenu storeMenu, LevelPathMenu levelPathMenu,
                          FriendsMenu friendsMenu, ProfileMenu profileMenu,
                          FriendProfileMenu friendProfileMenu, QuestsMenu questsMenu,
                          GameMenu pauseMenu, ScoreHud scoreHud, CountdownScreen countdownScreen,
                          MatchManager matchManager, QuestTracker tracker,
                          float bootSeconds, float transitionSeconds)
        {
            start = startScreen;
            onboarding = onboardingScreen;
            loading = loadingScreen;
            // Optional and found rather than passed: the menu works without it, just with a flat
            // backdrop instead of the live table.
            stage = FindAnyObjectByType<MenuStageCamera>();
            menu = mainMenu;
            online = onlineMenu;
            league = leagueMenu;
            store = storeMenu;
            levelPath = levelPathMenu;
            friends = friendsMenu;
            profile = profileMenu;
            friendProfile = friendProfileMenu;
            quests = questsMenu;
            pause = pauseMenu;
            hud = scoreHud;
            countdown = countdownScreen;
            match = matchManager;
            questTracker = tracker;
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
                menu.OnOpenRanked += OpenRankedFromMenu;
                menu.OnOpenStore += OpenStore;
                menu.OnOpenLevelPath += OpenLevelPath;
                menu.OnOpenQuests += OpenQuests;
                menu.OnAcceptInvite = AcceptInvite;
            }

            // Both are front-end viewers reached only from the main menu, so both go straight back to
            // it — there is no second door to retrace, unlike the league screen.
            if (store != null) store.OnBack = OpenMenu;
            if (levelPath != null) levelPath.OnBack = OpenMenu;

            if (quests != null)
            {
                quests.OnBack = OpenMenu;
            }

            if (online != null)
            {
                // Deliberately NOT StartOnlineMatch. The session reporting two players means the
                // lobby has recorded the guest, not that the guest can see the table — the match
                // starts when the host says so, over the connection itself.
                online.OnMatchReady = () => online.ShowConnecting();
                online.OnOpenRanked = OpenRanked;
                online.OnBack = OpenMenu;
            }

            if (league != null)
            {
                // Retraces whichever door it was opened by — the main menu's trophy button, or the
                // online menu's own Ranked button — set in OpenRankedFromMenu / OpenRanked below.
                league.OnBack = () => { if (leagueFromMainMenu) OpenMenu(); else OpenOnline(); };
                league.OnPlayRanked = PlayRanked;
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
                match.MatchWon += OnMatchWon;
                // Real goals for the account's stat wall. Credited here rather than in MatchManager,
                // which is deliberately ignorant of who is playing — only this class knows which team
                // the local player holds, and that local PvP has no single owner to credit.
                match.GoalScored += OnGoalScored;
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

            // Ranked's two rewards, both driven off the standing rather than off an event the ladder
            // does not raise: the weekly coin payout, and the league badges. Owned here, with the rest
            // of the cross-system consequences of a match, because neither belongs to a screen — a
            // player who never opens Ranked must still be paid for the week they finished, and the
            // ladder rolls over inside the backend where nothing can watch it happen. See
            // RankedRewards.Sync and LeagueBadges.Sync, both of which are safe to run repeatedly.
            Ladder.OnChanged += SyncRankedRewards;
            SyncRankedRewards();

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
                menu.OnOpenRanked -= OpenRankedFromMenu;
                menu.OnOpenStore -= OpenStore;
                menu.OnOpenLevelPath -= OpenLevelPath;
                menu.OnOpenQuests -= OpenQuests;
            }

            Ladder.OnChanged -= SyncRankedRewards;

            if (match != null)
            {
                match.MatchWon -= OnMatchWon;
                match.GoalScored -= OnGoalScored;
            }

            // Whatever match time was still in the clock when the app tore this down.
            FlushPlayTime();

            OnlineMatchDirector.OnMatchShouldStart -= StartOnlineMatch;
            OnlineMatchDirector.OnOpponentGone -= HandleOpponentGone;
            OnlineMatchDirector.OnRematchStarting -= HandleRematchStarting;
            OnlineMatchDirector.OnRematchDeclined -= HandleRematchDeclined;

            OnlineSession.OnPlayerCountChanged -= HandlePlayerCount;
            OnlineSession.OnLeft -= HandleSessionLeft;

            FriendsHub.OnInviteDeclined -= HandleInviteDeclined;
        }

        /// <summary>
        /// Records one finished match, from the same MatchWon both players see — the host's from the
        /// goal that decided it, the guest's from the relayed one — and a forfeit counts like any other
        /// result, since walking out is a way of losing.
        ///
        /// Two separate records, each guarded so a match can only count once: the online-only
        /// leaderboard feed (<see cref="MatchStats"/>), and the overall progression
        /// (<see cref="PlayerProgress"/>) that also credits vs-AI. Local PvP credits neither — with two
        /// players on one device there is no single account owner, which <c>localTeam == null</c> marks.
        /// </summary>
        private void OnMatchWon(Team winner)
        {
            if (localTeam == null)
            {
                return;
            }

            bool won = winner == localTeam.Value;

            if (onlineMatch && !resultCounted)
            {
                resultCounted = true;
                MatchStats.RecordResult(won);

                if (rankedMatch)
                {
                    // The win-only flourish, using the league's own win value synchronously —
                    // SubmitResult below is fire-and-forget and its round trip has not resolved by the
                    // time the banner needs a number, but the league does not change mid-match, so
                    // EloRating's flat per-league rule already IS the exact points this result is worth.
                    if (won && hud != null)
                    {
                        hud.ShowRankedProgress(EloRating.WinDelta(Ladder.Standing.League));
                    }

                    Ladder.SubmitResult(0, won);
                }
            }

            if (!progressCounted)
            {
                progressCounted = true;
                PlayerProgress.MatchOutcome outcome = PlayerProgress.RecordMatch(won);
                if (hud != null) hud.ShowMatchProgress(outcome);
            }
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

            // Count time only while a match is genuinely being played — not while the pause menu holds
            // the world stopped, which would otherwise pad the total with time spent doing nothing.
            if (IsPlaying && (pause == null || !pause.IsOpen))
            {
                playClock += Time.unscaledDeltaTime;
            }
        }

        /// <summary>
        /// Credits a goal to the local player when their team scores. Gated exactly like the win/loss
        /// record: <see cref="localTeam"/> is null for local PvP, where two people share one device and
        /// there is no single account to credit.
        /// </summary>
        private void OnGoalScored(Team scorer)
        {
            if (IsPlaying && localTeam.HasValue && scorer == localTeam.Value)
            {
                PlayerProgress.RecordGoal();
            }
        }

        /// <summary>Writes the whole-second part of the accumulated play time to the record, keeping the
        /// sub-second remainder for the next flush so nothing is repeatedly rounded away.</summary>
        private void FlushPlayTime()
        {
            int whole = Mathf.FloorToInt(playClock);
            if (whole > 0)
            {
                PlayerProgress.AddPlayTime(whole);
                playClock -= whole;
            }
        }

        /// <summary>Persist play time when the app is backgrounded, so a match closed from the task
        /// switcher rather than the menu does not lose the time spent in it.</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                FlushPlayTime();
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
            progressCounted = false;
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

            if (start != null) start.Show(AfterTitle);
            else AfterTitle();
        }

        /// <summary>
        /// What the title tap lands on. A brand new player is shown the sign-in / guest choice once
        /// (<see cref="OnboardingScreen"/>), which opens the menu itself when they are through; everyone
        /// else goes straight to the menu. The world stays frozen throughout — onboarding is front end,
        /// and its network calls are Tasks that a stopped clock does not touch.
        ///
        /// Kept as its own step rather than folded into Boot because the loading-bar fallback path
        /// (no title screen built) has to reach it too.
        /// </summary>
        private void AfterTitle()
        {
            if (start == null)
            {
                // No title screen to hold the boot, so the loading bar covered the sign-in round trip.
                // Run it before deciding, exactly as Boot used to open the menu behind the bar.
                loading.Show(bootLoadSeconds, ShowOnboardingOrMenu);
            }
            else
            {
                ShowOnboardingOrMenu();
            }
        }

        private void ShowOnboardingOrMenu()
        {
            if (onboarding != null && OnboardingScreen.NeedsOnboarding)
            {
                onboarding.Show(OpenMenu);
            }
            else
            {
                OpenMenu();
            }
        }

        /// <summary>Leaves play and returns to the menu, behind the loading curtain.</summary>
        public void ReturnToMenu()
        {
            // First, before anything can raise a result on this screen. Leaving is this player's own
            // doing: nothing that follows from it — their opponent's forfeit win, their own recorded
            // loss — is news they should be handed a scoreboard about on their way to the menu.
            leavingDeliberately = true;

            // Bank the time spent in the match just left, before IsPlaying drops and the clock stops.
            FlushPlayTime();

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
                if (rankedMatch) Ladder.SubmitResult(0, false);
            }

            // The same loss counts against progression — silently, since ResultsHidden means no
            // post-match screen is shown on a deliberate exit.
            if (IsPlaying && onlineMatch && !progressCounted)
            {
                progressCounted = true;
                PlayerProgress.RecordMatch(won: false);
            }

            // The same door, for quests. No MatchWon will fire here — the opponent's does, on their
            // machine — so without this the match would go uncounted and, worse, the win run would
            // survive a quit, making leaving the cheapest way to protect it.
            if (IsPlaying && questTracker != null)
            {
                questTracker.AbandonMatch();
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

        /// <summary>
        /// Tells the figure skinner which side the player here is holding, so their own equipped kit
        /// lands on their own figures and the opposition keeps its default colour.
        ///
        /// Routed through GameFlow because this is the only class that knows which team the local
        /// player has — it differs between vs-AI, local PvP and online — and the skinner is
        /// deliberately ignorant of match modes.
        /// </summary>
        private void TellFigureSkinnerLocalTeam(Team team)
        {
            var skinner = FindAnyObjectByType<FigureSkinner>();
            if (skinner != null) skinner.SetLocalTeam(team);
        }

        private void OpenSettings()
        {
            if (pause != null) pause.OpenSettingsStandalone();
        }

        /// <summary>
        /// The daily quests. Frozen, unlike the friends and online screens: nothing behind it is
        /// waiting on a transport tick, so there is no reason to leave the table running under it.
        /// Its own countdown reads unscaled time and does not care.
        /// </summary>
        private void OpenQuests()
        {
            if (menu != null) menu.Close();
            if (friends != null) friends.Close();
            if (profile != null) profile.Close();
            if (friendProfile != null) friendProfile.Close();
            if (hud != null) hud.SetVisible(false);

            Freeze();

            if (quests != null) quests.Open();
        }

        private void OpenMenu()
        {
            Freeze();
            // On for the whole front end. It stays on through Online/Friends/Profile (they are reached
            // from here and return here) and is only switched off when a match actually goes live.
            if (stage != null) stage.SetActive(true);
            if (hud != null) hud.SetVisible(false);
            // Follows hud's own visibility exactly: a pause icon means nothing before a match exists,
            // and every front-end screen is reached through here.
            if (pause != null) pause.SetPauseButtonVisible(false);
            if (online != null) online.Close();
            if (league != null) league.Close();
            if (store != null) store.Close();
            if (levelPath != null) levelPath.Close();
            if (friends != null) friends.Close();
            if (profile != null) profile.Close();
            if (friendProfile != null) friendProfile.Close();
            if (quests != null) quests.Close();
            if (menu != null) menu.Open();
            GameSfx.PlayMenuMusic();
        }

        /// <summary>
        /// Runs the ranked rewards forward against the latest standing: banks the payout if the ladder
        /// week has turned over, and unlocks the badge for whatever league the player is now in.
        ///
        /// Both are idempotent, which is what makes it safe to hang off an event that fires on every
        /// refresh — the payout only pays when the week index actually moves, and a badge already
        /// owned is granted no second time.
        /// </summary>
        private void SyncRankedRewards()
        {
            RankedRewards.Sync();
            LeagueBadges.Sync();
        }

        /// <summary>
        /// The cosmetics store. A pure front-end viewer with no session behind it, so it sits frozen
        /// like the league screen rather than running the world the way the online flow has to.
        /// </summary>
        private void OpenStore()
        {
            if (menu != null) menu.Close();
            if (levelPath != null) levelPath.Close();
            if (hud != null) hud.SetVisible(false);

            Freeze();

            if (store != null) store.Open();
        }

        /// <summary>The level path — frozen, for the same reason the store is.</summary>
        private void OpenLevelPath()
        {
            if (menu != null) menu.Close();
            if (store != null) store.Close();
            if (hud != null) hud.SetVisible(false);

            Freeze();

            if (levelPath != null) levelPath.Open();
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
            if (league != null) league.Close();
            if (hud != null) hud.SetVisible(false);

            Unfreeze();

            if (online != null) online.Open();
        }

        /// <summary>
        /// The ranked / league screen, reached from the online menu's own Ranked button. A front-end
        /// viewer — no session is running yet, so it is safe to sit frozen like the rest of the menus.
        /// </summary>
        private void OpenRanked()
        {
            leagueFromMainMenu = false;
            ShowLeague();
        }

        /// <summary>
        /// The same screen, reached directly from the main menu's trophy button — the door that did
        /// not exist before the ranked chip was added there. Skips the online screen entirely rather
        /// than opening and immediately closing it.
        /// </summary>
        private void OpenRankedFromMenu()
        {
            leagueFromMainMenu = true;
            ShowLeague();
        }

        private void ShowLeague()
        {
            if (menu != null) menu.Close();
            if (online != null) online.Close();
            if (hud != null) hud.SetVisible(false);

            Freeze();

            if (league != null) league.Open();
        }

        /// <summary>
        /// Starts a ranked search from the league screen. Runs unfrozen like every path into a session
        /// (Netcode needs a clock), and reuses the online screen's connect machinery — its spinner,
        /// player-count wait and timeout — so ranked and casual share one connection flow.
        /// </summary>
        private void PlayRanked()
        {
            if (league != null) league.Close();
            if (hud != null) hud.SetVisible(false);

            Unfreeze();

            if (online != null)
            {
                online.Open();             // arms the connect machinery (and clears RankedSearch)
                online.StartRankedSearch();  // flags this search ranked and begins matchmaking
            }
        }

        private void StartLocalMatch(bool aiOpponent)
        {
            Team playerTeam = ApplyMode(aiOpponent);

            if (menu != null) menu.Close();
            if (stage != null) stage.SetActive(false); // the top-down gameplay camera takes over now
            if (quests != null) quests.Close();
            if (hud != null) hud.SetVisible(true);
            if (pause != null) pause.SetPauseButtonVisible(true);
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
            // Mirrors hud.LocalTeam: vs-AI the player has a side to credit, local PvP has none.
            localTeam = aiOpponent ? playerTeam : (Team?)null;

            // The figure skinner needs a side even where progression does not have one. In local PvP
            // nobody owns the RESULT, but the account on this device still has figures to dress, and
            // they should be the ones this player is holding — playerTeam is that side in both modes.
            TellFigureSkinnerLocalTeam(playerTeam);

            onlineMatch = false;
            resultCounted = false;
            progressCounted = false;
            waitingForRematch = false;

            IsPlaying = true;

            // Everything quests need that MatchManager does not carry. Offline only the Hard-AI
            // quest can come of it, and the tracker decides that from what is handed over here.
            if (questTracker != null)
            {
                questTracker.BeginMatch(new MatchContext
                {
                    Online = false,
                    LocalTeam = playerTeam,
                    AiOpponent = aiOpponent,
                    Difficulty = GameAudio.Difficulty,
                    OpponentIsFriend = false,
                });
            }

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
            if (stage != null) stage.SetActive(false); // the top-down gameplay camera takes over now
            if (quests != null) quests.Close();
            if (hud != null) hud.SetVisible(true);
            if (pause != null) pause.SetPauseButtonVisible(true);
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
            // The local player always has a side online — credit their result.
            localTeam = onlineLocalTeam;
            TellFigureSkinnerLocalTeam(onlineLocalTeam);
            onlineMatch = true;
            // Ranked-ness is decided by the search that formed this session, read here at the one
            // moment the match actually begins — so it is always this match's own answer.
            rankedMatch = online != null && online.RankedSearch;
            resultCounted = false;
            progressCounted = false;
            waitingForRematch = false;

            Unfreeze();
            IsPlaying = true;

            // Resolved now, while the session is unambiguously alive, for the same reason the local
            // team is: by the time a result lands the host may be gone and the roster with it.
            if (questTracker != null)
            {
                questTracker.BeginMatch(new MatchContext
                {
                    Online = true,
                    LocalTeam = onlineLocalTeam,
                    AiOpponent = false,
                    Difficulty = GameAudio.Difficulty,
                    OpponentIsFriend = FriendsHub.IsFriend(OnlineSession.OpponentId),
                });
            }

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
