/*
 * Mini Football — ranked ladder (server-authoritative).
 *
 * ONE Cloud Code script, "ladder", dispatched by an `action` param. The client
 * (Net/Ladder/UgsLadderService.cs) calls it for getStanding / getPod / submitResult; the weekly
 * promote/relegate is the same script under action "rollover", called by a UGS Scheduler trigger.
 *
 * Why the server owns this: fifteen-player pods and a weekly top-3-up / bottom-2-down cannot be
 * trusted to a client — points, pod membership and the rollover all have to be authoritative.
 *
 * STORAGE (UGS Cloud Save):
 *   - Per player, in their OWN player data (key "ladder"): { league, podId, points, week }.
 *   - Per pod, in game-scoped custom data (key `pod:<league>:<week>:<index>`):
 *         { members: [ { id, name, points } ] }
 *     The pod doc is the only way to enumerate a pod's fifteen without a cross-player query, and it
 *     is what the weekly rollover ranks. Keep the two in step: submitResult writes both.
 *
 * The pure logic (ELO deltas, promote/relegate) matches Net/Ladder/EloRating.cs and League.cs — keep
 * the numbers in sync if you tune them.
 *
 * VERIFY ON DEPLOY: the Cloud Save access below (`context`/module APIs) is written to the documented
 * shape; confirm the exact calls against your installed Cloud Code + Cloud Save server SDK, and see
 * cloudcode/README.md for the deploy + scheduler steps.
 */

const POD_SIZE = 15;
const PROMOTE = 3;
const RELEGATE = 2;
const LEAGUES = ["Bronze", "Silver", "Gold", "Diamond"]; // index 0..3, matches the C# enum
const EPOCH_MS = Date.UTC(2024, 0, 1);
const WEEK_MS = 7 * 24 * 60 * 60 * 1000;

// Per-league (win, loss) — must match EloRating.cs. Loss is a positive magnitude.
const WIN = [32, 28, 24, 20];
const LOSS = [8, 14, 18, 20];

const clampLeague = (l) => Math.max(0, Math.min(LEAGUES.length - 1, l));
const currentWeek = () => Math.floor((Date.now() - EPOCH_MS) / WEEK_MS);
const secondsToRollover = () => Math.max(0, Math.floor((EPOCH_MS + (currentWeek() + 1) * WEEK_MS - Date.now()) / 1000));

// ---- storage helpers (adapt to your Cloud Save server SDK) -----------------------------------

async function loadPlayer(ctx, playerId) {
  const doc = await ctx.cloudSave.getPlayerItem(playerId, "ladder"); // { league, podId, points, week }
  return doc || null;
}
async function savePlayer(ctx, playerId, data) {
  await ctx.cloudSave.setPlayerItem(playerId, "ladder", data);
}
async function loadPod(ctx, league, week, index) {
  const doc = await ctx.cloudSave.getCustomItem(`pod:${league}:${week}:${index}`);
  return doc || { members: [] };
}
async function savePod(ctx, league, week, index, pod) {
  await ctx.cloudSave.setCustomItem(`pod:${league}:${week}:${index}`, pod);
}
// A pending or resolved result claim, keyed by a per-match id both players share (see submitResult).
async function loadMatch(ctx, matchId) {
  const doc = await ctx.cloudSave.getCustomItem(`match:${matchId}`);
  return doc || { claims: {}, resolved: false };
}
async function saveMatch(ctx, matchId, doc) {
  await ctx.cloudSave.setCustomItem(`match:${matchId}`, doc);
}
// A game-scoped counter of how many pods exist in a league this week, so a new player fills the last
// non-full pod or opens the next one.
async function nextPodIndex(ctx, league, week) {
  const key = `podcount:${league}:${week}`;
  const c = (await ctx.cloudSave.getCustomItem(key)) || { open: 0, count: 0 };
  return { key, c };
}

// ---- placement --------------------------------------------------------------------------------

async function placeIntoPod(ctx, playerId, name, league, week, startPoints) {
  const { key, c } = await nextPodIndex(ctx, league, week);
  let index = c.open;
  let pod = await loadPod(ctx, league, week, index);

  if (pod.members.length >= POD_SIZE) {
    index = c.count; // open a fresh pod
    pod = { members: [] };
    c.count = index + 1;
    c.open = index;
  }

  pod.members.push({ id: playerId, name: name || "Player", points: startPoints });
  if (pod.members.length >= POD_SIZE) c.open = c.count; // this one is full; next player opens another

  await savePod(ctx, league, week, index, pod);
  await ctx.cloudSave.setCustomItem(key, c);
  return index;
}

async function ensurePlayer(ctx, playerId, name) {
  const week = currentWeek();
  let p = await loadPlayer(ctx, playerId);

  if (!p) {
    const league = 0; // everyone starts in Bronze
    const podId = await placeIntoPod(ctx, playerId, name, league, week, 0);
    p = { league, podId, points: 0, week };
    await savePlayer(ctx, playerId, p);
    return p;
  }

  // A new week they have not been re-podded into yet: the rollover normally handles this, but if a
  // player returns after the job ran, drop them into a fresh pod for the current week at 0.
  if (p.week !== week) {
    const podId = await placeIntoPod(ctx, playerId, name, p.league, week, 0);
    p = { league: p.league, podId, points: 0, week };
    await savePlayer(ctx, playerId, p);
  }
  return p;
}

function rankIn(pod, playerId, points) {
  let above = 0;
  for (const m of pod.members) {
    const pts = m.id === playerId ? points : m.points;
    if (pts > points) above++;
  }
  return above + 1;
}

// ---- endpoints --------------------------------------------------------------------------------

async function getStanding(ctx, playerId, name) {
  const p = await ensurePlayer(ctx, playerId, name);
  const pod = await loadPod(ctx, p.league, p.week, p.podId);
  const rank = rankIn(pod, playerId, p.points);
  return {
    league: p.league,
    podId: p.podId,
    rankInPod: rank,
    podSize: POD_SIZE,
    points: p.points,
    promo: rank <= PROMOTE && p.league < LEAGUES.length - 1,
    releg: rank > POD_SIZE - RELEGATE && p.league > 0,
    week: p.week,
    secondsToRollover: secondsToRollover(),
  };
}

async function getPod(ctx, playerId, name) {
  const p = await ensurePlayer(ctx, playerId, name);
  const pod = await loadPod(ctx, p.league, p.week, p.podId);
  const entries = pod.members
    .map((m) => ({ name: m.id === playerId ? "You" : m.name, points: m.points, you: m.id === playerId }))
    .sort((a, b) => b.points - a.points);
  return { entries };
}

// The actual point application, unchanged from before this player id — factored out so both
// participants can be credited in one call once submitResult below has corroborated them.
async function applyDelta(ctx, playerId, name, won) {
  const p = await ensurePlayer(ctx, playerId, name);
  const delta = won ? WIN[p.league] : -LOSS[p.league];
  p.points = Math.max(0, p.points + delta);
  await savePlayer(ctx, playerId, p);

  // Mirror into the pod doc so standings and the rollover see it.
  const pod = await loadPod(ctx, p.league, p.week, p.podId);
  const me = pod.members.find((m) => m.id === playerId);
  if (me) me.points = p.points;
  else pod.members.push({ id: playerId, name: name || "Player", points: p.points });
  await savePod(ctx, p.league, p.week, p.podId, pod);

  return p.points;
}

// How long a claim waits for its corroborating half before it is simply pending forever (never
// auto-credited on its own — see submitResult).
const MATCH_CLAIM_WINDOW_MS = 10 * 60 * 1000;

/**
 * Records this player's claimed result for one match and credits points ONLY once both
 * participants have reported it — matching, opposite outcomes, within the claim window.
 *
 * Before this, a single call with { won: true } was credited on the spot: nothing connected it to
 * an actual match, so a modified client could call this endpoint directly, with no opponent, no
 * relay session and no game running, and farm ranked points forever (see cloudcode/README.md,
 * "Anti-cheat", and AGENTS.md). Requiring a matching claim from the id named as the opponent raises
 * that from "one modified client" to "two authenticated UGS identities agreeing with each other" —
 * a real increase in cost, not a full close. A single account, or two colluding ones, can still
 * fabricate a matchId and complementary claims with no real match behind them; closing THAT needs an
 * authoritative match server issuing a signed match token neither client can forge, which this
 * relay-hosted, host-authoritative topology does not have (the same residual already accepted for
 * goal-scoring — see AGENTS.md, "a malicious host can cheat freely"). This is documented, not hidden.
 *
 * matchId is expected to be the Multiplayer Sessions id both clients already share (assigned by the
 * service when the session is created, not chosen by either client) — see
 * Assets/Scripts/Net/OnlineSession.cs (Current.Id) — which is why forging one requires acting as
 * both participants rather than just picking a string.
 */
async function submitResult(ctx, playerId, name, won, matchId, opponentId) {
  if (!matchId || !opponentId || opponentId === playerId ||
      matchId.length > 64 || opponentId.length > 64) {
    return { applied: false, reason: "invalid" };
  }

  const match = await loadMatch(ctx, matchId);

  if (match.resolved) {
    return { applied: false, reason: "already-resolved" };
  }

  if (match.claims[playerId]) {
    // This player retrying (the client's call is fire-and-forget and may be re-sent). Idempotent.
    await saveMatch(ctx, matchId, match);
    return { applied: false, reason: "already-claimed" };
  }

  match.claims[playerId] = { opponentId, won: !!won, name: name || "Player", ts: Date.now() };

  const theirs = match.claims[opponentId];
  const corroborated =
    !!theirs &&
    theirs.opponentId === playerId &&
    theirs.won !== !!won && // exactly one winner, exactly one loser
    Math.abs(theirs.ts - match.claims[playerId].ts) <= MATCH_CLAIM_WINDOW_MS;

  if (!corroborated) {
    await saveMatch(ctx, matchId, match);
    return { applied: false, reason: theirs ? "mismatch" : "pending" };
  }

  // Both sides agree on who won: credit both now, symmetrically, in this one call.
  const myPoints = await applyDelta(ctx, playerId, name, won);
  await applyDelta(ctx, opponentId, theirs.name, theirs.won);

  match.resolved = true;
  await saveMatch(ctx, matchId, match);

  return { applied: true, points: myPoints };
}

// The weekly lock. Scheduled (see README): ranks every pod of the just-finished week, moves the top
// PROMOTE up and the bottom RELEGATE down, and re-pods everyone for the new week at 0 points.
async function rollover(ctx) {
  const finished = currentWeek() - 1;
  const nextWeek = currentWeek();

  for (let league = 0; league < LEAGUES.length; league++) {
    const { c } = await nextPodIndex(ctx, league, finished);
    const podCount = c.count || 0;

    for (let index = 0; index < podCount; index++) {
      const pod = await loadPod(ctx, league, finished, index);
      const ranked = pod.members.slice().sort((a, b) => b.points - a.points);

      for (let i = 0; i < ranked.length; i++) {
        const m = ranked[i];
        let dest = league;
        if (i < PROMOTE && league < LEAGUES.length - 1) dest = clampLeague(league + 1);
        else if (i >= ranked.length - RELEGATE && league > 0) dest = clampLeague(league - 1);

        const podId = await placeIntoPod(ctx, m.id, m.name, dest, nextWeek, 0);
        await savePlayer(ctx, m.id, { league: dest, podId, points: 0, week: nextWeek });
      }
    }
  }
  return { rolledOverTo: nextWeek };
}

// ---- dispatch ---------------------------------------------------------------------------------

module.exports = async ({ params, context, logger }) => {
  const playerId = context.playerId;
  const name = (params && params.name) || "Player";
  const action = params && params.action;

  switch (action) {
    case "getStanding": return getStanding(context, playerId, name);
    case "getPod":      return getPod(context, playerId, name);
    case "submitResult":return submitResult(context, playerId, name, !!params.won,
                                            params && params.matchId, params && params.opponentId);
    case "rollover":    return rollover(context); // scheduler-only; protect with an access rule
    default:
      logger.error(`ladder: unknown action ${action}`);
      throw new Error("unknown action");
  }
};
