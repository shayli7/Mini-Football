/*
 * Mini Football — player progression save (server-authoritative for the fields that have real
 * value: coins and owned cosmetics).
 *
 * ONE Cloud Code script, "progress", dispatched by an `action` param, mirroring cloudcode/ladder.js.
 * The client (Assets/Scripts/Net/CloudSaveBackend.cs) calls it for `load` and `save` instead of
 * writing Unity Cloud Save's player data directly — that data is client-writable by design, which is
 * exactly the hole this closes: this document lives in GAME-SCOPED custom data instead
 * (`progress:<playerId>`), the same storage class cloudcode/ladder.js already uses for `pod:` and
 * `podcount:`, which the client SDK has no write access to at all. Only this script can touch it.
 *
 * WHY: Assets/Scripts/Net/CloudSync.cs documents a monotonic merge (every counter takes the larger
 * value across devices, "never lose progress to a stale device") and, until this script existed,
 * uploaded the merged result straight to Unity Cloud Save's player-writable data. A modified client
 * could submit ANY document — the merge rule would then keep the larger number forever, so one forged
 * flush with coins: 999999999, or an `owned` list holding every cosmetic in the game, would stick
 * permanently and sync to every device. Coins and owned cosmetics are the two fields with real value
 * (everything a player can spend gates on Wallet.TrySpend, per Assets/Scripts/UI/StoreMenu.cs), so
 * those two are bounded here.
 *
 * MERGE RULES must match CloudSync.Merge in Assets/Scripts/Net/CloudSync.cs field-for-field — this is
 * a deliberate duplicate (Cloud Code cannot share the C# class), not an independent design. If that
 * merge logic changes, mirror the change here.
 *
 * VERIFY ON DEPLOY: the Cloud Save game-data calls below are written to the documented shape, exactly
 * like the `pod:`/`podcount:` helpers in ladder.js — confirm against your installed Cloud Code +
 * Cloud Save server SDK.
 */

const SCHEMA = 1;

// The two fields with real value, and how much they may legitimately grow in one flush (~4s
// debounce, occasionally longer if the app was closed). Generous on purpose — these exist to catch a
// forged jump by orders of magnitude, not to police normal play:
//
//   coins  — the single biggest legitimate reward is a Diamond-league promotion payout (600 * 2 =
//            1200, see RankedRewards.LeagueBase/CoinsFor), and payouts ACCUMULATE across missed
//            weeks rather than being capped, so a player back after two months could plausibly cash
//            in several at once. The level path adds at most ~12,500 across every non-milestone
//            level from 2-59 (LevelPath.CoinsForLevel) if claimed in one sweep. 25,000 comfortably
//            covers both at once with headroom for future tuning.
//   owned  — normal play unlocks cosmetics one at a time (a store purchase, or a level-path
//            milestone at 10/20/30/40/50/60). A generous burst cap of 5 new ids per flush allows a
//            "claim everything" sweep after time away without letting a single forged flush unlock
//            the whole catalogue.
//
// A flush that exceeds either cap has that ONE field clamped back to the stored value rather than
// the whole save rejected — a false positive (an unusually large legitimate haul) costs the player
// one flush's delay, self-correcting on the next, rather than losing anything real.
const MAX_COINS_PER_FLUSH = 25000;
const MAX_NEW_OWNED_PER_FLUSH = 5;

function emptyDoc() {
  return {
    schema: SCHEMA,
    coins: 0, walletSeeded: false,
    owned: "", equipped: [],
    xp: 0, wins: 0, losses: 0, goals: 0, playTime: 0,
    levelClaimed: "",
    onlineWins: 0, onlineLosses: 0,
    hasSeen: false, seenWeek: 0, seenLeague: 0, seenRank: 0,
    pendingCoins: 0, pendingLeague: 0, pendingRank: 0,
  };
}

// ---- storage (game-scoped custom data; the client SDK cannot write this) -----------------------

async function loadStored(ctx, playerId) {
  const doc = await ctx.cloudSave.getCustomItem(`progress:${playerId}`);
  return doc || null;
}
async function saveStored(ctx, playerId, doc) {
  await ctx.cloudSave.setCustomItem(`progress:${playerId}`, doc);
}

// ---- merge, mirroring CloudSync.Merge in CloudSync.cs -------------------------------------------

const maxInt = (a, b) => Math.max(a || 0, b || 0);

function unionCsv(a, b) {
  const set = new Set();
  for (const s of [a, b]) {
    if (!s) continue;
    for (const id of s.split(",")) if (id) set.add(id);
  }
  return Array.from(set).join(",");
}

function orMask(a, b) {
  a = a || ""; b = b || "";
  const n = Math.max(a.length, b.length);
  let out = "";
  for (let i = 0; i < n; i++) {
    out += (a[i] === "1" || b[i] === "1") ? "1" : "0";
  }
  return out;
}

function mergeEquipped(local, remote) {
  const n = Math.max((local || []).length, (remote || []).length);
  const out = new Array(n);
  for (let i = 0; i < n; i++) {
    const l = local && local[i];
    const r = remote && remote[i];
    out[i] = l ? l : (r || "");
  }
  return out;
}

// Reconciles two documents so the player keeps the best of both — the same rule CloudSync.Merge
// applies client-side, ported here because Cloud Code cannot share the C# class. Symmetric for every
// counter (max) and every collection (union / OR); asymmetric only where "the device in the player's
// hand" is the better authority (equipped choices prefer local).
function merge(local, remote) {
  local = local || emptyDoc();
  remote = remote || emptyDoc();

  const m = {
    schema: SCHEMA,
    coins: maxInt(local.coins, remote.coins),
    walletSeeded: !!local.walletSeeded || !!remote.walletSeeded,
    owned: unionCsv(local.owned, remote.owned),
    equipped: mergeEquipped(local.equipped, remote.equipped),
    xp: maxInt(local.xp, remote.xp),
    wins: maxInt(local.wins, remote.wins),
    losses: maxInt(local.losses, remote.losses),
    goals: maxInt(local.goals, remote.goals),
    playTime: maxInt(local.playTime, remote.playTime),
    levelClaimed: orMask(local.levelClaimed, remote.levelClaimed),
    onlineWins: maxInt(local.onlineWins, remote.onlineWins),
    onlineLosses: maxInt(local.onlineLosses, remote.onlineLosses),
  };

  // Ranked snapshot moves as a UNIT: the side that watched the more recent week owns the whole
  // (week, league, rank) triple, because a mixed triple could mis-detect a rollover.
  const takeRemoteSeen = !!remote.hasSeen && (!local.hasSeen || remote.seenWeek > local.seenWeek);
  const seen = takeRemoteSeen ? remote : local;
  m.hasSeen = !!local.hasSeen || !!remote.hasSeen;
  m.seenWeek = seen.seenWeek || 0;
  m.seenLeague = seen.seenLeague || 0;
  m.seenRank = seen.seenRank || 0;

  // Pending payout: never lose owed coins (max), and carry the describing league/rank from
  // whichever side actually held that larger amount.
  m.pendingCoins = maxInt(local.pendingCoins, remote.pendingCoins);
  const pend = (remote.pendingCoins || 0) > (local.pendingCoins || 0) ? remote : local;
  m.pendingLeague = pend.pendingLeague || 0;
  m.pendingRank = pend.pendingRank || 0;

  return m;
}

// ---- the bound: the one thing merge() above does not do -----------------------------------------

function ownedIds(csv) {
  const set = new Set();
  if (csv) for (const id of csv.split(",")) if (id) set.add(id);
  return set;
}

// Clamps the merged document against what was ACTUALLY stored server-side last time — never against
// what the client claimed, which is the value under attack. See the constants above for why these
// numbers.
function clampToStoredBounds(merged, stored, logger, playerId) {
  const baseline = stored || emptyDoc();

  const coinsGrowth = merged.coins - (baseline.coins || 0);
  if (coinsGrowth > MAX_COINS_PER_FLUSH) {
    if (logger) logger.error(
      `progress: ${playerId} coins grew by ${coinsGrowth} in one flush (cap ${MAX_COINS_PER_FLUSH}) — clamped`);
    merged.coins = (baseline.coins || 0) + MAX_COINS_PER_FLUSH;
  }

  const before = ownedIds(baseline.owned);
  const after = ownedIds(merged.owned);
  let newCount = 0;
  for (const id of after) if (!before.has(id)) newCount++;

  if (newCount > MAX_NEW_OWNED_PER_FLUSH) {
    if (logger) logger.error(
      `progress: ${playerId} gained ${newCount} new cosmetics in one flush (cap ${MAX_NEW_OWNED_PER_FLUSH}) — rejected`);
    merged.owned = baseline.owned || "";
  }

  return merged;
}

// ---- endpoints ------------------------------------------------------------------------------------

async function load(ctx, playerId) {
  const stored = await loadStored(ctx, playerId);
  return { doc: stored ? JSON.stringify(stored) : null };
}

async function save(ctx, playerId, docJson, logger) {
  let local;
  try {
    local = JSON.parse(docJson);
  } catch (e) {
    if (logger) logger.error(`progress: ${playerId} sent an unparseable doc — ignored`);
    const stored = await loadStored(ctx, playerId);
    return { doc: stored ? JSON.stringify(stored) : null };
  }

  const stored = await loadStored(ctx, playerId);
  let merged = merge(local, stored);
  merged = clampToStoredBounds(merged, stored, logger, playerId);

  await saveStored(ctx, playerId, merged);
  return { doc: JSON.stringify(merged) };
}

// ---- dispatch ---------------------------------------------------------------------------------

module.exports = async ({ params, context, logger }) => {
  const playerId = context.playerId;
  const action = params && params.action;

  switch (action) {
    case "load": return load(context, playerId);
    case "save": return save(context, playerId, params && params.docJson, logger);
    default:
      logger.error(`progress: unknown action ${action}`);
      throw new Error("unknown action");
  }
};
