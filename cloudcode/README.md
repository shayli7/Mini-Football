# Ranked ladder — server deploy

The ranked ladder (four leagues, pods of 15, weekly top‑3‑promote / bottom‑2‑relegate) is
**server‑authoritative**. The Unity client (`Assets/Scripts/Net/Ladder/`) runs on the offline
**mock** backend until you deploy the pieces below and turn on the `LADDER_UGS` scripting define —
so nothing here blocks local, casual or Blitz play.

## What ships in the game already

- Full client + UI, running on `MockLadderService` (a real simulation) — testable now with no server.
- `UgsLadderService` — the real backend client, compiled only when `LADDER_UGS` is defined.
- `RankedMatchAsync` in `OnlineSession` — uses the Matchmaker queue when `LADDER_UGS` is on, else
  falls back to open pairing so ranked matches still form.
- `cloudcode/ladder.js` — the server logic (this folder).

## Deploy steps (Unity Dashboard / UGS CLI)

1. **Packages** — already added to `Packages/manifest.json`: `com.unity.services.cloudcode`. Open the
   project once so the editor resolves it. (Add `com.unity.services.leaderboards` too only if you also
   want the separate friends win/loss board — it is not required by the ladder.)
2. **Cloud Save** — enable it for the project. The ladder stores:
   - per‑player item `ladder` = `{ league, podId, points, week }` (player data),
   - per‑pod item `pod:<league>:<week>:<index>` = `{ members: [ { id, name, points } ] }` (game data),
   - per‑league counter `podcount:<league>:<week>` = `{ open, count }` (game data).
   > The pod/counter items are **game‑scoped custom data**. Confirm your Cloud Save server SDK exposes
   > the `getCustomItem/setCustomItem` (game data) and `getPlayerItem/setPlayerItem` calls used in
   > `ladder.js`, and adapt the four storage helpers at the top of that file if the method names differ
   > in your installed version. This is the one part written to the documented shape, not verified.
3. **Cloud Code** — publish `ladder.js` as a script named **`ladder`** (the client calls
   `CallEndpointAsync("ladder", { action })`). Give it player access for `getStanding` / `getPod` /
   `submitResult`.
4. **Scheduler** — create a weekly trigger that calls the `ladder` script with `{ "action":
   "rollover" }` (e.g. every Monday 00:00 UTC). Restrict `rollover` to the scheduler (an access rule
   or a shared secret check at the top of the function), so a client can never trigger a promotion.
5. **Matchmaker (optional, for skill pooling)** — create a queue named **`ranked`** over a
   Relay‑backed session. Without it, ranked still works via open pairing (step in `OnlineSession`).
   Add a rating ticket attribute later to pool by skill.
6. **Turn it on** — add `LADDER_UGS` to *Project Settings → Player → Scripting Define Symbols*. The
   client now uses `UgsLadderService`; the mock is used only when the player is not signed in.

## Tuning

The ELO win/loss table lives in **two** places that must stay in sync: `WIN`/`LOSS` in `ladder.js`
and `EloRating.cs`. Pod size and promote/relegate counts likewise: `ladder.js` constants and
`Leagues` in `League.cs`.

## Notes / next

- **Anti‑cheat:** `submitResult` currently trusts the caller's win/loss. Harden by validating the
  match result server‑side (e.g. the host reports both players' outcome, or the match token is checked)
  before applying points. Deferred.
- **Pod aggregation** is the one genuinely hard part on UGS: enumerating a pod's 15 needs the pod
  document above rather than a cross‑player query. If you outgrow Cloud Save game data, move the pod
  store to a real DB behind the same `ladder.js` helpers — the endpoints and the client stay the same.
