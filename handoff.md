# Handoff — online netcode work (as of 2026-10-04)

Read `AGENTS.md` first (loaded via `CLAUDE.md`); it now documents every rule below in its Online
section. This file is the **status**: what was built, what is verified, and what is next.
The full design plan is `~/.claude/plans/while-there-has-been-ticklish-acorn.md`.

## The goal

Online is host + client over Unity Relay (UGS Sessions, `WithRelayNetwork()`), Netcode for GameObjects
2.13.1. The owner reported two problems: **(1)** the match freezes when the host's phone backgrounds,
and **(2)** the guest's ball stutters and reacts a round trip late to their own swings. A dedicated
server was considered and rejected (it would put the lag on BOTH players and cost money). The plan is
three phases built in order **A → C → B**.

## Status

| Item | State |
|---|---|
| Phase A — ball sample stream (`BallSample`, `NetClock`, `BallStream`), NetworkTransform removed | Built; tested once on one PC |
| Buffer fix — starvation nudge bug removed, buffer capped at 35 ms | Built, **not yet retested** |
| **Phase C — lane authority** (C1–C4) | **Built, compiles, NEVER RUN** |
| Phase C5 — delete guest own-kick sound prediction | Not done (small) |
| Phase C6 — draw opponent's slow dribbles in the past | Not done (polish) |
| Phase B — player away = forced authority + referee handover | Not started |
| Versus screen before online matches (2 s, diagonal red/blue) | Built, not seen running |
| Host-suspend handling (pause notice, 20 s forfeit, self-forfeit) | Built earlier, superseded by Phase B later |

Every code change passes the compile check (0 errors, ~135 sources). **None of the Phase C behaviour
has been observed in Unity** — expect bugs on the first run.

## Phase C in one paragraph

Rows alternate by team, so `BallLanes` divides the table into lanes; whoever owns the lane the ball is
in simulates it with the real `BallController`, so each player's own touches are instant. The other
machine draws the stream projected to the present and starts simulating *provisionally* when its drawn
ball enters its own lane; the old simulator then hands off (`BallHandoffRpc`) and the new one confirms,
rebases or aborts. Terms have a growing `epoch`. The host is referee: new term (epoch + 2) at every
goal/restart/kick-off, on guest silence (0.5 s) and on validation failure (`AuthorityValidator`); goals
inside the guest's simulation are judged from the guest's real samples (`GoalJudge`). Off switch: the
host's **`laneAuthority`** field on the Ball's `NetworkedBall`.

Key files: `Net/NetworkedBall.cs` (state machine), `Net/BallStream.cs`, `Net/NetClock.cs`,
`Net/BallSample.cs`, `Net/GoalJudge.cs`, `Net/AuthorityValidator.cs`, `Gameplay/BallLanes.cs`,
`Gameplay/BallController.cs` (`BeginSimulatingFrom`/`EndSimulating`), `Gameplay/GoalTrigger.cs`
(ignores a ball whose `BallController` is disabled), `Net/OnlineMatchDirector.cs` (clock ping/pong,
stall relay from whichever machine simulates, versus kick-off hold, player cards).

## What to do next

1. **Owner tests Phase C** and pastes logs. Diagnostic lines, every 2 s on both machines:
   - `[BallLanes]` once at spawn: worst figure reach past a lane boundary. Design assumes **< ~1 cm**;
     if larger, raise `laneHysteresis` and expect late judging of touches near boundaries.
   - `[BallAuthority]`: `handoffs`/`provisional` should climb; `aborts`/`rebases` stay low; `forced`
     stays 0 (`lastForce='...'` says why if not); `goalsJudged` counts goals scored in the guest's sim.
   - With `laneAuthority` off, `[BallStream]` (guest) and `[BallHost]` lines report delay/buffer/fps.
2. Fix what the test shows. Likely suspects if something is off: provisional start firing too
   early/late (`laneHysteresis`, `abortGrace`), handoffs ping-ponging at a boundary
   (`handoffDwell`, `ownContactDwell`), validator false positives forcing takeovers (`maxOverstay`).
3. Then C5, C6, then **Phase B**: symmetric heartbeats, forced authority when the simulating player goes
   silent, referee handover to the guest when the host is away (goals via `GoalJudge`, clock via
   `match.ClearRemoteClock()`), validated match-state handback through a new
   `MatchManager.RestoreState(...)`, and an online pause menu that does not set `timeScale = 0`
   (`GameMenu.cs:168` — it currently freezes the ball for both players).
4. Remove the temporary diagnostics (`logStreamStats`, `[BallLanes]` log) once things are confirmed.

## Facts learned the hard way (this session)

- **Two instances on one PC still go through Relay on the internet** (~200 ms RTT measured) and share
  one CPU (guest fell to ~26 fps), which inflates every delay. Realistic latency tests need two phones.
- `runInBackground` was 0, which throttled the unfocused test instance; it is now 1.
- `Application.targetFrameRate` was never set (Android ran at 30 fps); `Gameplay/DisplaySettings.cs`
  pins it to the panel's refresh rate, 60–120, via `[RuntimeInitializeOnLoadMethod]`.
- The scene's `TickRate` got silently reverted to 50 by an Editor save; it is now set in code
  (`OnlineSession.ConfigureNetcode`, 100 Hz, matching the 100 Hz physics step).
- `RodAutoLift` exists in code but is **not on any GameObject** — the auto-lift assist has never run.
- Unused packages `com.unity.netcode` (Netcode for Entities) and `com.unity.entities.graphics` were
  removed from `Packages/manifest.json`; the real one is `com.unity.netcode.gameobjects`.
- The `progress` Cloud Code script (`cloudcode/progress.js`) is **not deployed**, so cloud save 404s;
  `CloudSync` now backs off failed flushes (4 s doubling to 2 min) instead of retrying every frame.
  Deploy it from the Unity Dashboard as script `progress` with player access.
- `gh` (GitHub CLI) is not installed on this machine.

## Owner decisions to respect

- When a player is away, **play continues** against their frozen rods until the 20 s forfeit — not a
  pause for both.
- The owner prefers "cut the delay, keep it simple" changes first, but approved the full lane
  authority design for distance play (60–150 ms RTT matters to them).
- Owner tests in Unity between steps; never assume something works until they have run it.

## Compile check

No test suite. The working Roslyn invocation is in memory (`compile-check-harness`) and `AGENTS.md`;
the scratchpad script from this session will not survive, so rebuild it from there. Assert a non-zero
source count and that the output DLL exists before trusting a pass.
