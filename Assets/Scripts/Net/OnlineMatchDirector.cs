using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Turns a connected session into a playable match: decides who plays which team, hands each
    /// player their rods, and keeps the two scoreboards saying the same thing.
    ///
    /// Teams are derived, not negotiated — the host is Red and the guest is Blue, on both machines,
    /// with nothing sent to agree it. A message could be lost or arrive late; <c>IsHost</c> cannot.
    ///
    /// Scoring is the host's call alone. Goal triggers are switched off on the guest, because a
    /// kinematic ball being driven into the goal volume by NetworkTransform still fires
    /// OnTriggerEnter there, and the guest would score a second, private goal off the host's own.
    /// </summary>
    [DisallowMultipleComponent]
    public class OnlineMatchDirector : NetworkBehaviour
    {
        /// <summary>
        /// The host defends and attacks as Red; the guest is always Blue.
        ///
        /// A missing NetworkManager also lands on Blue, which is a real answer to the wrong question —
        /// so <see cref="OnNetworkSpawn"/> reports that case rather than letting it read as a team.
        /// It is worth the noise: when this component was left out of the scene entirely, the
        /// resulting "both players are Blue and the AI still plays Red" looked like a gameplay bug
        /// for far longer than it should have.
        /// </summary>
        public static Team LocalTeam =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost ? Team.Red : Team.Blue;

        /// <summary>
        /// Both players may start now. Raised on both machines off one message from the host, sent
        /// only once the guest is genuinely connected.
        ///
        /// This exists because "the session says there are two players" and "the two players can
        /// actually see each other" are different moments, and the gap between them is the length of
        /// a Relay allocation plus an NGO handshake plus a scene sync — ten seconds over a phone
        /// network. Starting on the first of those gave whoever won that race a free run at an
        /// undefended goal.
        /// </summary>
        public static event Action OnMatchShouldStart;

        /// <summary>
        /// The other player's connection has gone, whether they left, crashed or lost signal.
        ///
        /// The transport notices this immediately. The session's own PlayerLeaving is a lobby-level
        /// courtesy that may be slow and may not arrive at all, which is why a forfeit went unnoticed
        /// and left the remaining player on a table that never finished.
        /// </summary>
        public static event Action OnOpponentGone;

        /// <summary>Both players agreed to another match. Raised on both machines off one message.</summary>
        public static event Action OnRematchStarting;

        /// <summary>Nobody is coming: the other player left, or never answered.</summary>
        public static event Action OnRematchDeclined;

        /// <summary>How long the other player has to agree before it counts as a no.</summary>
        private const float RematchTimeoutSeconds = 20f;

        /// <summary>Gap between one unanswered request to start and the next.</summary>
        private const float ReadyRetrySeconds = 0.5f;

        /// <summary>
        /// How often the host puts the match clock on the wire. The guest runs its own clock down
        /// between these and is only corrected by them, so this is a correction rate rather than a
        /// frame rate — four a second is far more than enough to keep the two in step.
        /// </summary>
        private const float ClockPublishSeconds = 0.25f;

        /// <summary>How many times the guest asks before giving up and saying so.</summary>
        private const int ReadyAttempts = 20;

        /// <summary>
        /// The live director, so the UI can ask for a rematch without holding a reference to a scene
        /// object that comes and goes with the session.
        /// </summary>
        private static OnlineMatchDirector instance;

        private readonly HashSet<ulong> rematchVotes = new();
        private Coroutine rematchTimer;

        /// <summary>
        /// The match clock, owned by the host like the ball and the score.
        ///
        /// It used to be owned by nobody: <see cref="MatchManager"/> counts down against local frame
        /// time, so each machine ran its own stopwatch and the two drifted apart over a match — and a
        /// phone that stopped running for a while came back with a clock its opponent's had left
        /// behind. Sent as a NetworkVariable rather than an event because it is state, not news: a
        /// guest that misses one update is corrected by the next instead of staying wrong.
        /// </summary>
        private readonly NetworkVariable<float> clock = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>When the host next puts the clock on the wire, in unscaled time.</summary>
        private float nextClockPublish;

        /// <summary>
        /// The TABLE's cosmetics for this match, chosen by the host.
        ///
        /// The pitch and the room are one shared thing both players look at, so they cannot be each
        /// player's own choice: two machines rendering different pitches would be two different
        /// games, and a screenshot would not match what the opponent saw. The host owns them for the
        /// same reason it owns the clock and the ball — its answer is already authoritative.
        ///
        /// The FIGURES are different; see <see cref="netFiguresRed"/>.
        ///
        /// (The ball skin is host-owned too, but it lives on <see cref="NetworkedBall"/> alongside
        /// the rest of the ball's networked state rather than here.)
        ///
        /// Fixed strings because Netcode needs a bounded size on the wire; catalogue ids are short
        /// by design, and one that ever outgrew this would be truncated and resolve to the default.
        /// </summary>
        private readonly NetworkVariable<FixedString64Bytes> netField = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> netBackground = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// One figure skin PER TEAM, because the figures are not shared scenery — they are each
        /// player's own eleven, and each player dresses their own.
        ///
        /// Red is the host's side and Blue the guest's (see <see cref="LocalTeam"/>). Both are still
        /// server-written, since NetworkVariable write permission here is Server: the guest cannot
        /// publish directly and instead reports its choice with
        /// <see cref="ReportFigureSkinRpc"/>, which the host copies into the Blue slot. Routing it
        /// through the host keeps one writer and means a late joiner gets both values as part of the
        /// spawn, exactly like every other piece of match state.
        /// </summary>
        private readonly NetworkVariable<FixedString64Bytes> netFiguresRed = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> netFiguresBlue = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>The table's skinners, if the scene has them. Optional: a table without them just
        /// keeps the materials the model ships with.</summary>
        private FieldSkinner fieldSkinner;
        private FigureSkinner figureSkinner;
        private BackgroundSkinner backgroundSkinner;

        /// <summary>The guest's standing request to be started, retried until it is answered.</summary>
        private Coroutine readyHandshake;

        /// <summary>Set on both machines by the kick-off message, so the guest knows to stop asking.</summary>
        private bool matchBegun;

        /// <summary>
        /// True while this machine is restarting because it was TOLD to, by a message every machine
        /// received. The host relays its own restarts to the guest, and relaying one of these would
        /// restart the guest a second time off a message it had already acted on.
        /// </summary>
        private bool restartFromMessage;

        private MatchManager match;

        private void Awake()
        {
            match = FindAnyObjectByType<MatchManager>();
        }

        public override void OnNetworkSpawn()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError($"{name}: no NetworkManager — every player will be assigned Blue and " +
                               "the AI will keep playing. Add one to the scene.", this);
            }

            instance = this;

            ConfigureTable();

            // The table's cosmetics, published by the host and worn by BOTH sides — the host wears
            // its own published value rather than reading its inventory a second time, so the two
            // machines cannot drift apart. A NetworkVariable's current value is delivered to a late
            // joiner as part of the spawn, so a guest arriving afterwards still gets it with no RPC.
            fieldSkinner = FindAnyObjectByType<FieldSkinner>();
            figureSkinner = FindAnyObjectByType<FigureSkinner>();
            backgroundSkinner = FindAnyObjectByType<BackgroundSkinner>();
            netField.OnValueChanged += OnFieldSkinChanged;
            netFiguresRed.OnValueChanged += OnFigureSkinChanged;
            netFiguresBlue.OnValueChanged += OnFigureSkinChanged;
            netBackground.OnValueChanged += OnBackgroundSkinChanged;

            string myFigures = Inventory.EquippedId(CosmeticKind.FigureSkin) ?? string.Empty;

            if (IsServer)
            {
                netField.Value = new FixedString64Bytes(
                    Inventory.EquippedId(CosmeticKind.FieldSkin) ?? string.Empty);
                netBackground.Value = new FixedString64Bytes(
                    Inventory.EquippedId(CosmeticKind.Background) ?? string.Empty);
                // The host dresses its own side directly; the guest's arrives by RPC below.
                netFiguresRed.Value = new FixedString64Bytes(myFigures);
            }
            else
            {
                // The guest cannot write a server-owned variable, so it tells the host what it is
                // wearing and the host publishes it for both machines.
                ReportFigureSkinRpc(new FixedString64Bytes(myFigures));
            }

            ApplyNetSkins();

            // Subscribed by BOTH roles, unlike everything else here. On the host it reports the guest
            // leaving; on the guest it reports being cut off from the host. One player walking out has
            // to end the match on the other's screen no matter which of them it was.
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }

            if (IsServer)
            {
                if (match != null)
                {
                    match.GoalScored += OnHostGoal;
                    match.MatchRestarted += OnHostRestart;
                }

                return;
            }

            // The guest takes the clock from the host from here on, rather than running its own.
            clock.OnValueChanged += OnClockChanged;
            if (match != null && clock.Value > 0f)
            {
                match.SyncClock(clock.Value);
            }

            // The guest, and only the guest, says when the match may begin.
            //
            // Asked repeatedly rather than announced once. OnNetworkSpawn is the first moment this
            // object exists on the guest's machine, but it is not reliably a moment the guest can
            // SEND from: the server is still finishing that client's synchronisation, and a single
            // message posted into that window can be dropped without trace. Losing it cost the whole
            // match — no kick-off, and no rods handed over, so the guest sat watching a table it
            // could not touch while the host played on.
            readyHandshake = StartCoroutine(ReadyHandshake());
        }

        private void OnFieldSkinChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            ApplyNetSkins();
        }

        private void OnFigureSkinChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            ApplyNetSkins();
        }

        /// <summary>
        /// The guest telling the host which figure skin it is wearing, so the host can publish it for
        /// both machines. Server-only write permission is why this is an RPC rather than the guest
        /// simply setting its own variable.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void ReportFigureSkinRpc(FixedString64Bytes id, RpcParams rpcParams = default)
        {
            netFiguresBlue.Value = id;
        }

        private void OnBackgroundSkinChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            ApplyNetSkins();
        }

        /// <summary>
        /// Wears whatever the host published. An empty value means the host had nothing equipped, in
        /// which case the override is CLEARED rather than set to "" — the skinners read empty as
        /// "no override", so setting it explicitly would be the same thing said twice.
        /// </summary>
        private void ApplyNetSkins()
        {
            if (fieldSkinner != null)
            {
                string id = netField.Value.ToString();
                if (string.IsNullOrEmpty(id)) fieldSkinner.ClearOverride();
                else fieldSkinner.SetOverride(id);
            }

            if (figureSkinner != null)
            {
                // Each side dressed independently, so the host's eleven wear the host's skin and the
                // guest's wear theirs — the same two teams on both screens.
                figureSkinner.SetTeamSkin(Team.Red, netFiguresRed.Value.ToString());
                figureSkinner.SetTeamSkin(Team.Blue, netFiguresBlue.Value.ToString());
            }

            if (backgroundSkinner != null)
            {
                string id = netBackground.Value.ToString();
                if (string.IsNullOrEmpty(id)) backgroundSkinner.ClearOverride();
                else backgroundSkinner.SetOverride(id);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            netField.OnValueChanged -= OnFieldSkinChanged;
            netFiguresRed.OnValueChanged -= OnFigureSkinChanged;
            netFiguresBlue.OnValueChanged -= OnFigureSkinChanged;
            netBackground.OnValueChanged -= OnBackgroundSkinChanged;

            // The local player's own choices come back the moment the online match ends. Without
            // this the next local match would still be wearing the last host's table — cosmetics
            // this player may not even own.
            if (fieldSkinner != null) fieldSkinner.ClearOverride();
            if (figureSkinner != null) figureSkinner.ClearOverride();
            if (backgroundSkinner != null) backgroundSkinner.ClearOverride();

            StopRematchTimer();
            rematchVotes.Clear();

            if (readyHandshake != null)
            {
                StopCoroutine(readyHandshake);
                readyHandshake = null;
            }

            matchBegun = false;

            if (instance == this)
            {
                instance = null;
            }

            if (IsServer && match != null)
            {
                match.GoalScored -= OnHostGoal;
                match.MatchRestarted -= OnHostRestart;
            }

            if (!IsServer)
            {
                clock.OnValueChanged -= OnClockChanged;
            }

            // Whatever comes next — a local match, a match against the AI — runs its own clock again.
            if (match != null)
            {
                match.ClearRemoteClock();
            }

            // Give the table back the things ConfigureTable took away for the session's duration.
            //
            // The goal triggers are the ones that bite: they are switched off on the guest so that a
            // kinematic ball driven through the goal volume cannot score a second, private goal off
            // the host's. Nothing switched them back on, so a player who had been a guest once could
            // play the AI all day and never score again — the ball crossed the line and the game
            // simply did not react.
            foreach (GoalTrigger goal in FindObjectsByType<GoalTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (goal != null) goal.enabled = true;
            }

            // Restrictions belong to a mode, and this one is over. Whatever starts next says who may
            // touch what — ApplyMode for a local match, ConfigureTable for the next online one.
            foreach (RodTouchInput input in FindObjectsByType<RodTouchInput>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (input != null) input.SetTeamRestriction(false, Team.Red);
            }
        }

        /// <summary>
        /// Host only: keeps the match clock on the wire.
        ///
        /// Unscaled time, like the rest of the online handshakes here, because the world sits at
        /// timeScale 0 behind the pause menu and a win banner — and the clock the guest is being
        /// corrected against has to go on being reported through those, not freeze on one machine.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || !IsServer || match == null)
            {
                return;
            }

            if (Time.unscaledTime < nextClockPublish)
            {
                return;
            }

            nextClockPublish = Time.unscaledTime + ClockPublishSeconds;
            clock.Value = match.TimeRemaining;
        }

        /// <summary>Guest only: the host's clock arriving.</summary>
        private void OnClockChanged(float previous, float current)
        {
            if (match != null)
            {
                match.SyncClock(current);
            }
        }

        // ---------- table setup ----------

        /// <summary>
        /// Puts the local table into online shape. Runs on both machines, and everything it does is
        /// the same question asked twice with a different answer for <see cref="LocalTeam"/>.
        /// </summary>
        private void ConfigureTable()
        {
            int aisDisabled = 0;
            int inputsRestricted = 0;

            // Nothing is live until the kick-off message says so. The host reaches this the moment it
            // starts hosting and then waits — for a code to be read out, for matchmaking, for an
            // invited friend — and every second of that wait was a second of match: clock running,
            // rods answering, goals counting, on one machine only.
            if (match != null)
            {
                match.Park();
            }

            // No AI in an online match: a second brain driving the guest's rods would fight them for
            // the same transforms and publish the result as if it were the player's own input.
            foreach (TeamAI ai in FindObjectsByType<TeamAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ai != null)
                {
                    ai.enabled = false;
                    aisDisabled++;
                }
            }

            // Each player may only touch their own team — the same restriction the AI match uses, so
            // no new input path had to be written for online.
            foreach (RodTouchInput input in FindObjectsByType<RodTouchInput>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (input != null)
                {
                    input.SetTeamRestriction(true, LocalTeam);
                    inputsRestricted++;
                }
            }

            // Only the host's triggers score. See the class summary for why the guest's must not.
            foreach (GoalTrigger goal in FindObjectsByType<GoalTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (goal != null) goal.enabled = IsServer;
            }

            // Says what was actually applied rather than that it ran. Every symptom of this component
            // being absent or half-wired is visible in these three numbers.
            Debug.Log($"Online: playing as {LocalTeam} ({(IsServer ? "host" : "guest")}) — " +
                      $"{aisDisabled} AI disabled, {inputsRestricted} input(s) restricted.", this);

            int rods = FindObjectsByType<NetworkedRod>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            if (rods == 0)
            {
                Debug.LogWarning($"{name}: no NetworkedRod in the scene — no rod will move on the " +
                                 "other player's screen. Add NetworkObject + NetworkedRod to all " +
                                 "eight rods.", this);
            }
        }

        /// <summary>
        /// The guest asking to be started, and then asking again until it is.
        ///
        /// Two separate promises are being waited on, and they are checked separately because they
        /// fail separately. The kick-off is a message; the rods are an ownership change that travels
        /// on its own and can still be in flight when the whistle has already gone. A guest who got
        /// one without the other is in the match on paper and unable to move a single rod in it —
        /// which is precisely what "the blue team is missing on one device" looked like.
        /// </summary>
        private IEnumerator ReadyHandshake()
        {
            int asked = 0;
            while (!matchBegun && asked < ReadyAttempts)
            {
                ReportReadyRpc();
                asked++;
                // Unscaled, like everything else that has to survive a stopped world: the front end
                // sits at timeScale 0 and the guest may still be on the connecting screen.
                yield return new WaitForSecondsRealtime(ReadyRetrySeconds);
            }

            if (!matchBegun)
            {
                Debug.LogError($"{name}: the host never started the match after {asked} requests. " +
                               "The guest is connected but has not been kicked off.", this);
                readyHandshake = null;
                yield break;
            }

            int claimed = 0;
            while (!OwnsOurRods() && claimed < ReadyAttempts)
            {
                RequestRodsRpc();
                claimed++;
                yield return new WaitForSecondsRealtime(ReadyRetrySeconds);
            }

            if (!OwnsOurRods())
            {
                Debug.LogError($"{name}: still do not own the {LocalTeam} rods after {claimed} " +
                               "requests — this player cannot move their own team.", this);
            }

            readyHandshake = null;
        }

        /// <summary>
        /// Whether every rod this player is supposed to be driving actually answers to them.
        ///
        /// A rod nobody here owns is dragged back to the other machine's pose every frame by
        /// <see cref="NetworkedRod"/>, so it reads on screen as a rod that simply will not move.
        /// </summary>
        private bool OwnsOurRods()
        {
            bool found = false;

            foreach (NetworkedRod rod in FindObjectsByType<NetworkedRod>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                RodController controller = rod != null ? rod.GetComponent<RodController>() : null;
                if (controller == null || controller.Team != LocalTeam)
                {
                    continue;
                }

                found = true;
                if (!rod.IsOwner)
                {
                    return false;
                }
            }

            return found;
        }

        /// <summary>
        /// The guest reporting that its table is built and it can be played against.
        ///
        /// The host does no work to decide this and no longer watches for connections at all — being
        /// connected and being ready are different things, and only the guest can tell them apart.
        ///
        /// Safe to receive more than once: granting rods to their existing owner does nothing, and a
        /// second kick-off lands on a flow that ignores a match already in progress.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void ReportReadyRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;

            // Rods first, kick-off second. Arriving to find a table whose Blue rods still answer to
            // the host would be a worse head start than the one this replaces.
            GrantRods(clientId);

            Debug.Log($"Online: guest {clientId} is ready — starting the match on both machines.");
            BeginMatchRpc();
        }

        /// <summary>The guest chasing an ownership change that did not arrive. Idempotent, like the above.</summary>
        [Rpc(SendTo.Server)]
        private void RequestRodsRpc(RpcParams rpcParams = default) =>
            GrantRods(rpcParams.Receive.SenderClientId);

        /// <summary>
        /// Starts play on both machines off a single message.
        ///
        /// Everyone rather than NotServer, deliberately: the host obeys the same message it sends, so
        /// the two sides start on one event with only the wire between them, instead of each starting
        /// off whatever local signal happened to reach it first.
        ///
        /// That only holds because of where this is sent FROM. The sender runs an Everyone RPC on
        /// itself immediately, without waiting for anyone to receive it — so sending this the moment
        /// a connection was approved simply moved the head start rather than removing it. Sent in
        /// answer to the guest's own readiness, the two starts are a round trip apart.
        /// </summary>
        [Rpc(SendTo.Everyone)]
        private void BeginMatchRpc()
        {
            if (matchBegun)
            {
                return;
            }

            matchBegun = true;
            rematchVotes.Clear();

            // Re-asserted here rather than trusted from spawn time. Whatever the table was left in by
            // the last local match, this is the moment it has to be in online shape, and it costs
            // three loops to be certain instead of hopeful.
            ConfigureTable();

            // UI first, table second: the flow puts the HUD up and marks itself playing, and the
            // restart below immediately fires the events that HUD is there to draw.
            OnMatchShouldStart?.Invoke();

            StartFromMessage();
        }

        /// <summary>
        /// Restarts this machine's match off a message every machine received.
        ///
        /// Both sides restart from the same message rather than the host restarting and telling the
        /// guest afterwards. That second hop was its own failure: a repeat of the kick-off — which is
        /// exactly what a guest retrying produces — reached a host already playing, which had nothing
        /// left to restart and so relayed nothing, leaving the guest kicked off on paper and parked
        /// in fact.
        /// </summary>
        private void StartFromMessage()
        {
            if (match == null)
            {
                return;
            }

            restartFromMessage = true;
            match.RestartMatch();
            restartFromMessage = false;
        }

        // ---------- rematch ----------

        /// <summary>
        /// This player wants another match. Nothing restarts until the other one says so too.
        ///
        /// The old Play Again restarted the match then and there on whichever machine pressed it,
        /// which on the guest desynced the two tables and, once an opponent had left, dealt a fresh
        /// kick-off to somebody playing alone.
        /// </summary>
        public static void RequestRematch()
        {
            if (instance == null)
            {
                // No session left to ask. Treated as a refusal rather than ignored, or the player
                // would be left watching a "waiting" message that nothing can ever answer.
                OnRematchDeclined?.Invoke();
                return;
            }

            instance.SubmitVote();
        }

        private void SubmitVote()
        {
            // The host votes straight into the tally rather than through the RPC. A server sending
            // itself a SendTo.Server message is legal, but naming the voter explicitly here removes
            // any question of who a locally-invoked RPC reports as its sender.
            if (IsServer)
            {
                RegisterVote(NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                RequestRematchRpc();
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestRematchRpc(RpcParams rpcParams = default) =>
            RegisterVote(rpcParams.Receive.SenderClientId);

        /// <summary>
        /// Counts one vote, and starts the rematch once everybody still connected has cast one.
        ///
        /// Counted against the live connection list rather than a hardcoded two, so a player who left
        /// while the other was deciding cannot hold the vote open forever — they stop being someone
        /// whose agreement is required the moment they stop being connected.
        /// </summary>
        private void RegisterVote(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            rematchVotes.Add(clientId);

            if (rematchVotes.Count >= NetworkManager.Singleton.ConnectedClientsIds.Count)
            {
                StopRematchTimer();
                rematchVotes.Clear();
                StartRematchRpc();
                return;
            }

            // First vote in: the other player now has a limited time to agree. Without a clock a
            // player who simply puts their phone down leaves the other staring at "waiting" for good.
            rematchTimer ??= StartCoroutine(RematchTimeout());
        }

        private IEnumerator RematchTimeout()
        {
            // Unscaled: the win banner may be up with the world frozen behind it, and a countdown
            // that stops when the game does would never fire.
            yield return new WaitForSecondsRealtime(RematchTimeoutSeconds);

            rematchTimer = null;
            rematchVotes.Clear();
            RematchDeclinedRpc();
        }

        private void StopRematchTimer()
        {
            if (rematchTimer != null)
            {
                StopCoroutine(rematchTimer);
                rematchTimer = null;
            }
        }

        [Rpc(SendTo.Everyone)]
        private void StartRematchRpc()
        {
            OnRematchStarting?.Invoke();
            StartFromMessage();
        }

        [Rpc(SendTo.Everyone)]
        private void RematchDeclinedRpc() => OnRematchDeclined?.Invoke();

        private void OnClientDisconnected(ulong clientId)
        {
            // On the host this is the guest going. On the guest it is the guest's own client being
            // cut off, which means the host went. Neither machine has to work out which case it is:
            // if a connection dropped and this player is still here, the other one is not.
            if (IsServer && clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            Debug.Log($"Online: connection to client {clientId} lost — the opponent is gone.");
            OnOpponentGone?.Invoke();
        }

        /// <summary>
        /// Hands the guest ownership of the Blue rods, which is what lets their inputs drive those
        /// rods directly instead of being relayed through the host. The ball stays the host's.
        /// </summary>
        private void GrantRods(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            foreach (NetworkedRod rod in FindObjectsByType<NetworkedRod>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                RodController controller = rod != null ? rod.GetComponent<RodController>() : null;
                if (controller == null || controller.Team != Team.Blue)
                {
                    continue;
                }

                if (rod.NetworkObject.IsSpawned && rod.NetworkObject.OwnerClientId != clientId)
                {
                    rod.NetworkObject.ChangeOwnership(clientId);
                }
            }

            Debug.Log($"Online: client {clientId} now owns the Blue rods.");
        }

        // ---------- score ----------

        private void OnHostGoal(Team scorer)
        {
            ScoreGoalRpc(scorer == Team.Red);
        }

        /// <summary>
        /// Relays a restart the host decided on by itself — the Inspector's Restart match, say.
        ///
        /// A restart that came from a message is skipped: the guest ran that same message and has
        /// already restarted, and telling it again would wipe the kick-off it just took.
        /// </summary>
        private void OnHostRestart()
        {
            if (restartFromMessage)
            {
                return;
            }

            RestartRpc();
        }

        /// <summary>
        /// Replays the host's goal on the guest through the very same <see cref="MatchManager"/> call
        /// the trigger would have made, so the guest's HUD, sounds and kick-off all follow from it
        /// exactly as they do offline. Sending the event rather than the resulting score is what keeps
        /// that true — a bare number would light up the scoreboard and nothing else.
        /// </summary>
        [Rpc(SendTo.NotServer)]
        private void ScoreGoalRpc(bool redScored)
        {
            if (match != null)
            {
                match.ScoreGoal(redScored ? Team.Red : Team.Blue);
            }
        }

        [Rpc(SendTo.NotServer)]
        private void RestartRpc()
        {
            if (match != null)
            {
                match.RestartMatch();
            }
        }
    }
}
