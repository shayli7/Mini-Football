# Holo Arena UI — build brief

This is the hand-off for rebuilding Mini Football's front end in the approved "Holo Arena" style.
The design was done with the owner over several rounds; this file is the final, merged result.
**Nothing in it has been built yet.** No game code was changed during the design work.

Read these first, in this order:

1. `AGENTS.md` (repo root) — project rules, the compile check, the invariants.
2. `Assets/Scripts/UI/AGENTS.md` — the UI style contract. This brief changes parts of it (see
   "Docs to update").
3. The design canvas: https://claude.ai/artifact/AuAibcuki4PCdnjTFa8wsE
   Read a board with the Artifact tool: `action: "read"`, that `url`, `path: "project/<Board>.dc.html"`.
   The boards are HTML mockups at 1600×760. They are the visual reference: sizes, colours, layout and
   copy come from them. Sample names and numbers in them (STRIKER, 2,450 coins, rank #5) are
   placeholder data, not values to hard-code.

## How to work

- **One step at a time.** After each step: run the compile check from `AGENTS.md` (both passes), then
  stop and ask the owner to look at it in Unity. Nobody has seen any of this running in the game.
- The owner cannot compile from a cloud session; say so if that applies.
- Follow the repo's working style: runtime scripts only, no `[ContextMenu]`, no Editor tools, no
  scene Canvas, no UI prefabs, no imported UI art. Looks live in USS / `ArcadeTheme`, not in screen code.
- A rebuilt screen keeps its class name and public surface (`Build`, `Open`/`Close`, `On…` callbacks)
  so `GameFlow` and `TableFootballUI` change as little as possible.
- Never let a uGUI panel and a UI Toolkit screen overlap: close one before opening the other.
- Every new UI Toolkit screen must be `display: none` when closed, or it eats rod touches.
- Surgical changes. Do not refactor things this brief does not mention.

## Decisions (final — later rounds already merged in)

| Topic | Decision |
|---|---|
| Look | "Holo Arena": dark navy floor, panels with two cut corners (top-left, bottom-right) and a 1px edge, hex avatars, energy-cell sliders. Icon first, 1–3 word labels in caps. |
| Button meaning | **Cyan fill** = go / confirm. **Gold fill** = take a reward (claim, collect, free). **Cyan outline** = open a screen. **Chevron square** = back. **Red outline** = you lose something. One glowing control per screen. |
| Gold | Gold is for rewards and coins only. It is no longer the main button colour. |
| Fonts | Oxanium (display) + Barlow (body), already in `Resources/UI/Fonts`. No new fonts. |
| Background | "A · Arena" on every screen: two stadium light cones, a glowing horizon line, a perspective grid floor, plus a soft cool-white edge light around the screen rim. Moves slowly in game; static under reduced motion. |
| Sound | Every tap makes a faint sound; different actions have different sounds (see Sound palette). |
| Settings | Opened from a **gear on Profile**. Not on the main-menu nav. Not in the pause menu. |
| AI difficulty | Removed from Settings. It is still chosen on the VS AI cards (`GameAudio.Difficulty` stays). |
| Pause menu | Resume + Main Menu only. No Settings button. |
| Game settings | Music, Sound FX, Vibration, Touch sensitivity, Reduce motion. (Graphics and Notifications were rejected.) |
| Chest opening | **Do not change the chest opening animation** (`ChestOpening.cs` / `.uss`). |
| Chest name | Player-facing text says **Lucky Chest**. Code names (`ChestLoot`, `ChestTier`) stay. |
| Chest art | New, taller, more detailed chest drawing. No star, no gem on top. |
| Level path | Its **own full screen**, horizontal ("Level Road"), opened from a button on Profile. Removed from the main-menu nav. |
| XP | Levels cost less XP (see New logic). |
| Profile | Identity panel left, RECORD + WEARING right. Header has COLLECTION and the gear. |
| League names | Legend / Premier / Challenger / Academy League (display names only). |
| Ranked | Podium for the top 3, the player's own row pops out. |
| Store | Four sections with a bottom section bar; a new "Daily items" section sells 3 skins directly. |
| Friends | Same features as today, restyled. The ACCOUNT button is removed. |
| Offline | Red wifi-off "OFFLINE" chip on the main menu; the ONLINE card locks. |
| Reward animation | Coins and ranked trophies fly to their counter, which counts up fast. |

## Tokens

Change values, keep names, so existing readers keep working. Mirror every value in
`Assets/Resources/UI/Styles/Base.uss` `:root`.

| Token (`ArcadeTheme`) | Value | Use |
|---|---|---|
| `BgDeep` | `#04070D` | floor base |
| `BgPanel` | `#0A1322` | panels |
| `Row` (new) | `#0B1628` | list rows |
| `Plate` (new) | `#0F1C31` | tiles, back button fill |
| `BgRaised` | `#16294A` | icon tiles, unlit cells, disabled fill |
| `Line` | `#1D3557` | quiet edges |
| `LineLit` (new) | `#24456F` | panel edges on the Arena background |
| `BlueLine` | `#2C5A8E` | secondary button edge, input edge |
| `Pulse` (new) | `#3DE3FF` | the go colour; focus; progress |
| `OnPulse` (new) | `#03161C` | ink on cyan |
| `Reward` (new) | `#FFC23D` | reward buttons, coins, claimable edge |
| `OnReward` (new) | `#1E1400` | ink on gold |
| `Danger` | `#FF5C72` | destructive |
| `Go` | `#4CE0A0` | online status only (unchanged rule) |
| `Ink` | `#EAF4FF` | text |
| `InkMuted` | `#8EA4C2` | secondary text |
| `Dim` (new) | `#56698A` | disabled text only |
| `Red` / `Blue` (teams) | `#FF4F6D` / `#3D8BFF` | team identity |
| Rarity | `#9AAABF` / `#3DA5FF` / `#B15CFF` / `#FF8A2B` | common / rare / epic / legendary |
| League | Legend `#7FE3FF`, Premier `#FFC23D`, Challenger `#C4CFDC`, Academy `#D08A55` | tier colour |
| Cut sizes (new) | `CutSm 8`, `Cut 12`, `CutLg 16` | corner cut, in reference px |
| Edge light (new) | cool white `rgb(214,232,255)` at 12% | screen rim |

Existing `Gold`/`OnGold` stay as aliases of `Reward`/`OnReward`; `.btn--gold` becomes an alias of
the new `.btn--reward`; add `.btn--pulse`. `MenuButton.Variant.Primary` becomes the cyan go button;
add a `Reward` variant for gold.

Arena background layers (from the canvas): base gradient `#0B1A36 → #070E1F (52%) → #03050B`; a cyan
light cone from the top-left (`rgba(61,227,255,0.20)` fading out by 46%) and a violet one from the
top-right (`rgba(122,110,255,0.20)`); horizon line at 62% height, cyan, glowing; grid floor below
it (90px cells, cyan at 34%, tilted in perspective); bottom 26% fades to `#03050B`.

## Shared pieces to build first

| Piece | Where | What |
|---|---|---|
| Cut-corner frame | new `UI/Toolkit/UiFrame.cs` | `VisualElement` drawing a polygon with TL + BR corners cut, optional 1px edge and a short cyan accent tab on the top edge. `generateVisualContent` + `Painter2D`, colours from custom USS properties. |
| uGUI chamfer | `ArcadeTheme` | a baked 9-sliced `Chamfer(cut)` sprite next to `RoundedSolid`; `UIFactory.Panel` and `MenuButton` use it, so screens not yet rebuilt match. |
| Backdrop | new `UI/Toolkit/UiBackdrop.cs` | the Arena background + edge light, drawn once and shared by every screen. Slow drift on unscaled time; static under `ReducedMotion`. uGUI screens get the same through `UIFactory.Backdrop`. |
| Icons | `UI/Toolkit/UiIcon.cs` | add the glyphs the boards use: Gear, Key, LogOut, WifiOff, Mail, Doc, Shield, Info, LinkOut, Pencil, Grid, Gift, Vibrate, Touch, Motion, ChevronRight, ArrowUp, ArrowDown, Crown. |
| Chest drawing | `UiIcon`-style painter | the new Lucky Chest (domed lid, two metal bands, rivets, bright rim, shield lock with keyhole, base trim, soft floor shadow), tier colour as input. See any chest on the Store board. |
| Hex avatar | `UiKit` | flat-top hexagon with a coloured edge and an initial. |
| Energy cells | `UiKit` | a row of skewed cells (10 for volume, 5 for sensitivity); tap or drag; lit = cyan with glow. |
| Switch | `UiKit` | 84×38 cut-corner track, square knob, ON/OFF text inside. |
| Reward fly-in | new `UI/Toolkit/RewardFly.cs` | see Reward animation. |

## Sound palette

Add synthesised clips to `Audio/SfxLibrary.cs` (each still overridable by a recording in
`Resources/Audio/`), `enum UiSound` + `GameSfx.PlayUi(UiSound)`; `PlayUiClick()` stays and maps to Tap.
`UiKit.OnTap(el, action, UiSound sound = Tap)`; `MenuButton` picks by variant. Call sites never play
UI sounds themselves.

| Sound | Shape (from the canvas "Feel" board) | Used for |
|---|---|---|
| Tap | sine 1900→1300 Hz, 50 ms | any button |
| Back | sine 1300→650 Hz, 70 ms | back, close |
| Confirm | triangle 880 then 1320 Hz, two notes | play, save, next, cyan buttons |
| Open / Close | sine sweep 500→1400 / 1400→500 Hz, ~120 ms | screens and sheets |
| Toggle | triangle ~2600 Hz, 25 ms | switches, segmented |
| Step | sine ~1000 Hz, 35 ms (pitch rises with level) | slider cells, each reward landing |
| Reward | triangle arpeggio 1047, 1319, 1568, 2093 Hz | claim, collect |
| Warn | triangle 330 then 260 Hz | opening a destructive sheet |
| Deny | triangle 220 Hz, 90 ms | a disabled action |

All quiet (the mockups use gain ≈ 0.05). Vibration: `Gameplay/Haptics.cs` already exists — reuse it;
gate it on the new `GameAudio.Vibration`.

## Screens

Every screen: Arena backdrop; header row with the back chevron top-left (56×56 cut square) and the
title centred with a short cyan bar under it.

### Main menu — boards `MainMenu`, `MainMenuOffline` (`UI/MainMenu.cs`, already UI Toolkit)
- Header: profile chip (hex avatar, name, LV pill, gold badge with `LevelPath.UnclaimedCount`, XP
  bar) → opens Profile; coin pill; ranked chip (trophy, league short name, rank) → opens Ranked.
- Three mode cards with the existing sprites (`playerVsPlayerArt`, `aiVsPlayerArt`, `onlineArt`)
  full-bleed on top: scan-line overlay, fade into the card, two cyan corner brackets, icon badge.
  Title over the picture's foot, one short line, cyan PLAY strip. The difficulty cards follow the same look.
- Bottom nav: **Friends, Store, Quests** only (Settings and Levels are gone).
- Offline: red OFFLINE chip with a wifi-off icon in the header; the ONLINE card's picture goes grey
  and dark, a large wifi-off icon sits on it, and PLAY is replaced by a grey "NO CONNECTION" strip.

### Profile — board `Profile` (`UI/ProfileMenu.cs`, rebuild in UI Toolkit)
- Header right: COLLECTION (cyan outline, opens the store's Collection overlay) and the gear
  (cyan-edged square, opens Settings).
- Left panel: large hex avatar, name + rename button, LV pill + XP text + 10-segment XP bar, the
  gold-edged **LEVEL ROAD** button with the waiting-rewards badge, and the league row → Ranked.
- Right: RECORD (4 stat tiles from `MatchStats` / `PlayerProgress`) and WEARING (the 4 equipped
  cosmetics with `CosmeticThumbnails`, rarity-coloured edge).
- Create Account / Sign In / Delete Player leave this screen; they live in Settings.

### Level Road — board `LevelRoad` (`UI/LevelPathMenu.cs`, rebuild in UI Toolkit)
- Back goes to Profile. Top strip: LV pill, XP bar, XP text, coin pill. Then title row + gold CLAIM ALL
  (`LevelPath.ClaimAllCoins`) with its count.
- One horizontal `ScrollView`. Coin levels are small nodes (coin + amount). Levels where
  `LevelPath.Reward.IsMilestone` is true are big cards with a large picture: `CosmeticThumbnails`
  for a named skin, the chest drawing in its tier colour for a Lucky Chest.
- States: owned (dim, check), claimable (gold edge, glow, CLAIM), locked (lock, "LEVEL n").
- A rail under the nodes fills cyan up to the current level; hex markers carry the level number; the
  current level's marker is larger and white. Opens scrolled to the current level.
- A chest claim still plays `ChestOpening.Play` unchanged.

### Settings — boards `Settings`, `SettingsGuest`, `SettingsPassword`, `SettingsDelete` (new `UI/SettingsScreen.cs` + `Settings.uss`)
- Left rail of 3 tabs (Account / Game / About) that jump the list and follow the scroll.
- One vertical `ScrollView`, three sections, each with a numbered cyan heading and a rule:
  - **Account**: identity row with a SIGNED IN or GUEST chip. Guest (`!PlayerAccount.HasAccount`):
    a glowing **Sign Up** row. Signed in: **Change Password**, **Log Out**. **Delete Account** is
    always last, in red.
  - **Game Settings**: Music (10 cells + %), Sound FX (10 cells + %), Vibration (switch), Touch
    sensitivity (5 cells, LOWEST…MAX), Reduce motion (switch).
  - **About**: Contact Us, Terms of Service, Privacy Policy (placeholder URLs via
    `Application.OpenURL`, kept in one const block), Version (`Application.version`, not tappable).
- Sheets over the list:
  - Change password, 2 steps: current password → NEXT; then new + confirm with live rule chips
    (8+, A–Z, a–z, 0–9, MATCH — the same rules as `PlayerAccount.ValidatePassword`) → SAVE.
  - Sign up: username, password, confirm, the same chips → CREATE (`PlayerAccount.CreateAccountAsync`).
  - Log out: confirm.
  - Delete: red sheet; KEEP IT is the cyan button; the red button is **hold to delete** (1.5 s fill,
    a Step tick every quarter, releasing early cancels) → `PlayerAccount.DeleteAccountAsync`.
- `UI/GameMenu.cs`: remove the Settings button, the settings group, the difficulty control,
  `OpenSettingsStandalone` and `OnStandaloneClosed`.

### Ranked — board `Ranked` (`UI/LeagueMenu.cs`, rebuild in UI Toolkit)
- Left panel: league badge, league name, the four leagues listed with the current one marked YOU,
  this week's payout (`RankedRewards`), "Top 3 go up to …", cyan PLAY RANKED.
- Middle: podium for ranks 1–3 (1st tallest, gold, crown, glow; 2nd silver; 3rd bronze), each with a
  green up-arrow (the top `Leagues.PromoteCount` promote).
- Right: ranks 4–15 as slim rows. The player's row is about twice the height, cyan-edged, glowing,
  wider than the list, with a YOU tag and the points gap to the top 3. The last
  `Leagues.RelegateCount` rows are red with a down arrow. If the player is in the top 3, their
  podium step gets the cyan edge and YOU tag instead.
- Data stays from `Ladder`. The editor-only DEBUG buttons stay.

### Quests — board `Quests` (`UI/QuestsMenu.cs`)
- Left: level ring, streak pips. Right: quest rows with icon tile, tier diamonds, segmented progress,
  and the reward on the right: gold CLAIM when ready, dashed "+XP" while in progress, DONE when claimed.
  Bottom row: the clear-all-three bonus. Reset timer chip in the header.

### Store — boards `Store`, `StoreItems` (`UI/StoreMenu.cs`, rebuild in UI Toolkit)
- Header right: COLLECTION (opens the existing Collection overlay) and the coin pill.
- One horizontal `ScrollView` with four sections; a bottom section bar jumps between them and
  follows the scroll:
  1. **Lucky Chests** — four tiers at `ChestLoot.Price`. Four dots show which rarities a chest leans toward.
  2. **Daily reward** — the existing free chest for an ad (`ShopOffers` → `AdService.ShowRewarded`). Gold, glowing.
  3. **Coins** — the existing coin ad (`ShopOffers.AdCoins`, `AdsPerDay`), shown as pips with "n LEFT".
  4. **Daily items** — new, see New logic. Reset timer in the section heading.
- Price button: cyan when affordable, grey when not, gold when free.

### Friends — board `Friends` (`UI/FriendsMenu.cs`, rebuild in UI Toolkit)
- Left: YOUR CODE card (hex avatar, code, COPY CODE → "COPIED") and ADD A FRIEND (code field + cyan ADD).
- Right: the list. Requests first, gold-edged, with ACCEPT and a decline square. Online friends:
  green bar and dot, cyan INVITE. At a table: JOIN (outline). Offline: dimmed. Header chip: "n ONLINE".
- Empty state: two overlapping hex avatars, "NO FRIENDS YET", one line.
- Same data and flows as today (`FriendsHub`). The ACCOUNT button is removed.

### Reward animation — board `Rewards` (new `UI/Toolkit/RewardFly.cs`)
- `RewardFly.Play(kind, fromElement, toElement, amount, onDone)` on a top-most overlay.
- 8–12 pieces burst out of the source (ease-out, the first ~28% of the flight), then accelerate home
  on a slightly bowed path, shrinking a little.
- The counter runs up continuously between the first and last landing, scales to 1.14 and glows on
  each hit, and each hit plays Step a note higher. A "+amount" label fades under the counter.
- Coins first, then ranked trophies (the ranked points counter).
- Unscaled time. Under `ReducedMotion`: no flight, the number only counts up.
- Callers: match result, quest claim, level road claim, chest coins, the weekly ranked payout, and
  the ranked points change after a ranked match. `CoinPill` keeps its count-up and gains the pulse.

Not designed yet (keep working, restyled only by the shared tokens): online lobby, match HUD, result
banner layout, onboarding, loading, start screen, friend profile.

## New logic (not just restyling)

| What | Where | Detail |
|---|---|---|
| Change password | `Net/PlayerAccount.cs` | `ChangePasswordAsync(current, next)` → `AuthenticationService.Instance.UpdatePasswordAsync(Harden(current), Harden(next))`. Same never-throw / `LastError` / `IsBusy` pattern. SDK calls stay in this class. |
| Settings values | `UI/GameAudio.cs` | `Vibration` (bool, default on), `TouchSensitivity` (1–5, default 3), `ReducedMotion` applied to `ArcadeTheme.ReducedMotion` in `ApplyAll`. PlayerPrefs, same shape as `Music`/`Sfx`. |
| Touch sensitivity | `RodTouchInput` | multiply its spin gain by a factor from `TouchSensitivity`. A caller-side multiplier — no new branch in `RodController`. |
| Connectivity | new `Net/Connectivity.cs` | static `IsOnline` + `OnChanged`, polled about once a second on unscaled time from `Application.internetReachability`. Never throws. |
| XP curve | `Net/PlayerProgress.cs` `LevelXp` | cumulative `{0, 150, 325, 525, 750, 1000, 1275, 1575}`, then +300 per level. Existing players keep their XP, land on a higher level, and find the newly reached rewards claimable. |
| League display names | next to `UIFactory.LeagueColor` | one table: Diamond → "Legend League", Gold → "Premier League", Silver → "Challenger League", Bronze → "Academy League". The `League` enum is unchanged. |
| Daily items | `Net/ShopOffers.cs` | 3 random unowned skins per device-local day (seeded like the daily deal); `BuyDailyItem(id)` spends through `Wallet` and grants through `Inventory`. Prices: **ask the owner** — the canvas prices are placeholders; `CosmeticItem.Price` is the natural source. |
| Nav wiring | `UI/GameFlow.cs`, `UI/TableFootballUI.cs` | Profile → gear → Settings → back to Profile. Profile → LEVEL ROAD → back to Profile. Profile/Store → COLLECTION. Menu loses `OnOpenSettings` and `OnOpenLevelPath`. |

## Build order

Each step ends with: compile check (both passes) → owner checks in Unity → next step.

1. Tokens: `ArcadeTheme` + `Base.uss`, button variants (cyan go, gold reward). Whole game recolours.
2. Cut-corner shape: `UiFrame`, uGUI `Chamfer`; panels and buttons switch to it.
3. Arena backdrop + edge light on every screen.
4. Sound palette + `UiSound` wiring + vibration gate.
5. Settings screen, `ChangePasswordAsync`, the new `GameAudio` values, pause-menu cleanup, gear on Profile.
6. Main menu: pictures on cards, 3-item nav, `Connectivity` + offline state.
7. XP curve + league display names (small, logic only).
8. Profile rebuild (with COLLECTION and LEVEL ROAD buttons).
9. Level Road rebuild (horizontal) + the new chest drawing.
10. Ranked rebuild.
11. Store rebuild + Daily items.
12. Friends rebuild.
13. Quests restyle.
14. Reward fly-in, then hook it into each caller.

## Build status (2026-09-30)

All 14 steps are built and compile-checked (player 0 errors; editor only the 5 known CS0012). None has been
seen running in Unity yet. Deliberate differences from the boards:

- **Quests have no CLAIM button.** Quests bank themselves on the result screen; rows show "+XP" while in
  progress and DONE once banked. A manual claim would change the awarding logic.
- **No fly-in from the match result banner** (still uGUI). The fly-in is wired to level road claims and
  CLAIM ALL, chest coins, the store coin ad, the weekly ranked COLLECT, and ranked points gained since the
  player last opened Ranked.
- **Daily item prices** are `CosmeticItem.Price` (`ShopOffers.DailyItemPrice`) until the owner sets them.
- Quest icons reuse the shared `UiIcon` glyphs; the old bespoke uGUI quest art still drives the result-screen ledger.

## Docs to update as you go

- `Assets/Scripts/UI/AGENTS.md`: the palette, the "black, blue and gold 60/30/10" section, the button
  table (gold is no longer the primary), the list of screens moved to UI Toolkit, the new shared pieces.
- Root `AGENTS.md`: "The store sells chests, not skins" is no longer fully true once Daily items
  ships; `CosmeticItem.Price` sells again. Also note `Connectivity` and the Settings entry point.

## Open questions for the owner

- Daily item prices (and whether an owned item can appear).
- Real URLs / address for Contact Us, Terms of Service, Privacy Policy.
- Whether the difficulty cards and the screens listed under "Not designed yet" should get their own mockups.
