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

- **Anti‑cheat:** `submitResult` now requires BOTH players to report the same match before either is
  credited — it holds a claim under a shared `matchId` (the Multiplayer Sessions id, not
  client‑chosen) until the id named as the opponent reports a matching, opposite outcome, then
  credits both at once. This defeats a single modified client calling the endpoint on its own with no
  opponent and no match. It does NOT defeat two colluding, authenticated accounts fabricating a
  matchId and agreeing with each other — closing that needs a match server issuing a token neither
  client can forge, which this relay‑hosted topology does not have. Documented in `ladder.js` and in
  AGENTS.md next to the equivalent, already‑accepted host‑authoritative‑scoring residual.
- **Pod aggregation** is the one genuinely hard part on UGS: enumerating a pod's 15 needs the pod
  document above rather than a cross‑player query. If you outgrow Cloud Save game data, move the pod
  store to a real DB behind the same `ladder.js` helpers — the endpoints and the client stay the same.

# Player progression (coins, cosmetics, XP) — server deploy

`cloudcode/progress.js` is what `Assets/Scripts/Net/CloudSaveBackend.cs` calls instead of writing
Unity Cloud Save's player‑writable data directly. That distinction is the whole point: player data is
self‑service by design (any signed‑in client can write its own), so a raw `Data.Player.SaveAsync` of
the whole progress document — coins, owned cosmetics, XP, claimed levels — let a modified client
upload any numbers it liked, and `CloudSync.cs`'s own monotonic merge (every counter takes the LARGER
value across devices, so progress is never lost to a stale device) would then keep them forever. This
script moves the document to **game‑scoped custom data** (`progress:<playerId>`), which the client
SDK cannot write at all — the same storage class `ladder.js` already uses for `pod:`/`podcount:` — so
only this script can touch it, and it bounds the two fields that are actually worth forging (coins,
and newly‑owned cosmetic ids — see the constants at the top of the file) before persisting.

**Deploy:**

1. Packages: same `com.unity.services.cloudcode` and Cloud Save as the ladder, already in the
   manifest — no new package.
2. Publish `progress.js` as a Cloud Code script named **`progress`** (the client calls
   `CallEndpointAsync("progress", { action })` for both `load` and `save`). Give it player access for
   both actions; neither is a scheduler‑only endpoint.
3. Nothing to turn on — `CloudSaveBackend` calls this unconditionally (progression sync is not gated
   behind `LADDER_UGS`; only ranked is). Until the script is deployed, calls fail and are caught the
   same way every other `Net/` call degrades — the local, PlayerPrefs‑backed save keeps working exactly
   as it does offline, it just does not reach the cloud yet.

**What this does not fix:** a tampered LOCAL save can still show an inflated number on that one
device until its next flush — nothing server‑side can stop a rooted device lying to itself. What it
stops is that lie ever being *persisted* to the cloud or *reaching another device*: `save` always
returns the server's own clamped document, and `CloudSync` applies that back over the optimistic
local merge. The clamps are also deliberately generous (see the constants in `progress.js`) — they
exist to catch a forged jump by orders of magnitude, not to police normal play, so a legitimate
player should never notice them.
