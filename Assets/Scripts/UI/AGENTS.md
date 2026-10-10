# Mini Football — UI style guide

Scope: everything under `Assets/Scripts/UI/`. This is the style contract for the game's front end.
Read the repo-root `AGENTS.md` first for build/compile/device workflow; this file is only about how
the UI looks and how to build more of it in the same style. Follow it when adding or changing any
screen, control, or visual.

## The one rule

**The entire UI is built in code at runtime, from one design system, with zero imported art.**

- `TableFootballUI` builds the Canvas + EventSystem + every screen at play time. There is **no scene
  Canvas, no UI prefabs, no sprites/textures/fonts imported as assets.** Rounded corners and neon
  glows are baked procedurally into 9-sliced sprites at runtime (`ArcadeTheme.RoundedSolid`, `Glow`,
  `Disc`, …). Don't introduce prefabs, scene UI objects, or imported art.
- `ArcadeTheme` is the single source of truth for every colour, size, radius, glow, and duration.
  **No component may hard-code a hex value or a time.** If a value isn't a token in `ArcadeTheme`,
  add it there first, then use it. (The class doc calls this out: "if it isn't in this file, it
  doesn't ship.")
- Build controls with `UIFactory`, not raw `AddComponent<Image>()`/`Text`. **Reuse an existing
  factory piece before making a new one**; if you need a new reusable piece, add it to `UIFactory`.

## Two UI systems, mid-migration

The front end is moving from uGUI (the `Canvas` built by `TableFootballUI`) to **UI Toolkit**, one
screen at a time, so it can match the design canvas closely. Moved so far: **`StartScreen`**, **`MainMenu`**,
**`SettingsScreen`**, **`ProfileMenu`**, **`CollectionScreen`**, **`LevelPathMenu`** (the Level Road),
**`LeagueMenu`** (Ranked), **`StoreMenu`**, **`FriendsMenu`**, **`QuestsMenu`** and **`ChestOpening`**.
What is still uGUI — the online lobby, the friend's profile, the HUD and result banner, onboarding,
loading, the countdown — follows the rest of this guide and only takes the token recolours.

A migrated screen keeps its class name and its whole public surface (`Build`, `Open`/`Close`/`Show`,
the `On…` callbacks), so `GameFlow` and `TableFootballUI` do not change when one moves. Inside it:

- **Host** — `Toolkit/UiToolkitHost` builds one `PanelSettings` + `UIDocument` at runtime (nothing in
  the scene). Screens add a full-screen element to `UiToolkitHost.Root`, which is inset to the safe
  area. Reference 1280×760 with match = 1: the height is always 760 units, so lay out for 760 tall and
  let the width flex (~1690 at 20:9, ~1350 at 16:9).
- **Styles** — `Resources/UI/Styles/*.uss`: `Base.uss` holds the palette variables and shared
  components (`.btn--gold/blue/ghost/danger`, `.panel`, `.chip`, `.bar`, `.header`, `.enter`); each
  screen adds its own sheet. Looks live in USS, never in C#. The hex values mirror `ArcadeTheme`.
- **Fonts** — Oxanium (display) and Barlow (body) from Google Fonts (OFL), in `Resources/UI/Fonts`.
  Set from code by class through `Toolkit/UiFonts` (`f-display`, `f-display-semi`, `f-body-medium`,
  `f-body-semi`); call `UiFonts.Apply(root)` once a screen is built. One file per weight — UI
  Toolkit's synthesised bold does not look like the real one.
- **Shape** — `Toolkit/UiFrame`, a box with its top-left and bottom-right corners cut, drawn with the
  vector API (USS can round a corner but not cut one). Panels and buttons are frames: give the element
  a class that sets `--frame-fill`, `--frame-edge`, `--frame-edge-width`, `--frame-cut` and optionally
  `--frame-tab`; `.frame--panel` and the `.btn--*` variants in `Base.uss` are the examples. The uGUI
  counterpart is `ArcadeTheme.Chamfer(cut)` and `UIFactory.ChamferImage`, which `Panel` and `Button` use.
- **Backdrop** — `Toolkit/UiBackdrop.AddTo(screenRoot)` puts the Arena behind a screen. It reaches the
  true screen edge using `UiToolkitHost.SafeInsets`.
- **Icons** — `Toolkit/UiIcon`, line glyphs drawn with the vector API on a 24-unit grid. Add a glyph
  there rather than importing an image.
- **Building** — `Toolkit/UiKit`: `El`, `Text`, `Button`, `OnTap` (plays the UI click), `Show`,
  `Enter` (the standard rise-and-fade entrance, via the `.enter` transition), `Loop` (per-frame
  ambient motion on unscaled time), `Fade`. All of it honours `ArcadeTheme.ReducedMotion`.

Two rules that fail silently:

- **Only a displayed screen may take the pointer.** `RodTouchInput` asks the EventSystem whether a
  touch is over UI, and UI Toolkit panels answer too. The panel root and `UiToolkitHost.Root` are
  `PickingMode.Ignore`; a screen blocks touches only while `display` is not `none`. A picking element
  left visible over the table makes every rod dead, with no error.
- **Never rely on which system draws on top.** A uGUI panel opened over a UI Toolkit screen may draw
  underneath it. The one case today — Settings opened from the profile's gear — closes the profile
  first, and Back reopens it (`GameFlow.OpenSettings` / `ShowProfile`). Do the same for any new
  overlap until both sides have moved.
- **Settings** — `SettingsScreen` (UI Toolkit): Account, Game Settings and About in one vertical
  list with a rail that jumps between them. Anything that can lose the player something (change
  password, log out, delete) is a sheet over the list; delete is hold-to-confirm. The energy cells
  (`UiCells`), switch (`UiSwitch`) and hex avatar (`UiHex`) live in `Toolkit/UiWidgets`. Reduced
  motion is `ArcadeTheme.ReducedMotion` (saved); `TableFootballUI.reducedMotion` can only force it on.
- **Chest opening** — `ChestOpening.Play(tier, item, coins, onClosed)` is the one place a chest is
  opened on screen, for any caller (the level road, bought store chests and the daily deal): drop,
  wait for a tap, charge, burst, prize. The roll has already been granted by `ChestLoot.Open` before
  it plays; it is presentation only. Its animation is deliberately unchanged from the old design.
- **Reward fly-in** — `Toolkit/RewardFly.Play(kind, fromElement, counter, amount, onDone)`: a burst of
  coins (or trophies) flies from where a reward came from to a counter chip while the number runs up,
  pulses and ticks a rising note per hit. The counter is an `IRewardCounter` — `UiCoinChip` (the
  balance) or `UiPointsChip` (this week's ranked points), both built on `UiCounterChip`. **Call it after
  the reward is credited**: the count ends at the counter's real `Target` and starts `amount` below
  it, and the counter is frozen for the flight and snapped to the truth at the end. When a reward
  arrives behind another animation (a chest), `Freeze(true)` + `SetNow(Target - amount)` first to hold
  the old number, then fly on close. Under reduced motion nothing flies; the chip just counts up.
  Wired to: level road claims and CLAIM ALL, chest coins, the store's coin ad, the weekly ranked payout
  (COLLECT) and ranked points gained since the player last looked (`LeagueMenu.CheckPointsGain`).
  **Not wired**: the match result banner (uGUI — a fly-in from it would rely on which system draws on
  top) and quests (they bank themselves, there is no claim).
- **Shelves and sections** — the store is a horizontal `ScrollView` in four sections with a bottom bar
  that jumps between them and follows the scroll; the level road is the same shape. Buy buttons go grey
  (`is-poor`) rather than disabled when a price cannot be met, so tapping still explains why.

## The look — Holo Arena

Dark navy glass on a lit stadium floor. **One light per meaning**: cyan (`Pulse`) is go / confirm /
focus / progress, gold (`Reward`) is "you get something" and nothing else, red (`Danger`) is "you lose
something". Panels and buttons have their **top-left and bottom-right corners cut** on the diagonal
(never rounded), a 1px edge, and — on panels — a short cyan tab on the top edge. Labels are one to
three words in caps, icon first. Motion is sprung but quiet. It's landscape, mobile-first, and
safe-area aware. The design is on the Holo Arena canvas; `docs/ui-handoff.md` lists every value.

## Palette (`ArcadeTheme`)

Surfaces: `BgDeep #04070D` (floor), `BgPanel #0A1322` (panels), `Row #0B1628` (list rows), `Plate
#0F1C31` (tiles, quiet buttons), `BgRaised #16294A` (icon tiles, unlit cells, disabled fill). Edges:
`Line #1D3557` (quiet), `LineLit #24456F` (panels over the Arena), `BlueLine #2C5A8E` (button edges).
Text: `Ink #EAF4FF`, `InkMuted #8EA4C2`, `Dim #56698A` (disabled only). Meaning: `Pulse #3DE3FF` with
`OnPulse #03161C` ink; `Reward #FFC23D` with `OnReward #1E1400` ink; `Danger #FF5C72` (outlined over
`DangerFill #1A0B11`, never filled solid). `Gold`/`OnGold` are the old names and now alias
`Reward`/`OnReward`; `BlueSoft` is the icon/accent colour on a panel and is cyan. Team colours `Red
#FF4F6D`, `Blue #3D8BFF` are for team identity, not UI chrome. The title screen's sky runs `SplashTop
#02060F` → `SplashBottom #28609A`.

**Background: the Arena** (`ArcadeTheme.ArenaBase`/`ArenaCone`): a dark sky, a glowing horizon, a
grid floor, two drifting light cones (cyan left, violet right) and a cool-white rim light
(`EdgeLight`, 12%) at the true screen edge. uGUI screens get it from `UIFactory.Backdrop`, UI Toolkit
screens from `UiBackdrop.AddTo(screenRoot)`. Both size themselves to the whole screen, not the safe
area — a fixed bleed past the safe area would push the rim light off screen. It is opaque, so the live
table does not show behind a front-end screen. `ScrimDim` remains for the pause overlay (over a frozen
match) and small confirmation panels.

Two greens, kept strictly apart — don't mix them:
- **`Go #4CC38A`** — online-status **only**: a live-presence dot. A "light that turns on."
- **`Pitch #2FBF6B`** / `PitchDark #134A2C` — the football/turf accent: a surface, a mode's identity,
  a placeholder-stat tint ("coming soon"). Never use Pitch for online status, or Go for anything else.

Currency and rarity have their own steps, and they are not the brand gold:
- **`Coin #FFD24A`** / `CoinDark #A96E12` — gold coins only. Brighter and greener than `Gold`, because
  the coin pill and the ranked pill sit two centimetres apart in the main-menu header and at the same
  hue one would read as a dimmer version of the other.
- **`RarityCommon #8FA0B4`, `RarityRare #3DA5FF`, `RarityEpic #B15CFF`, `RarityLegendary #FF8A2B`** —
  a cosmetic's card edge, glow and label, and its chest. Common is a neutral so it reads as "no
  rarity" rather than a fifth tier. Map through `UIFactory.RarityColor`, never by hand.
- League tiers (`Bronze`, `Silver`, `Gold`, `Diamond`) map through `UIFactory.LeagueColor`.

Tint procedural sprites via `Image.color`; fade with the `WithAlpha(this Color, float)` extension.

## Type, spacing, radii, elevation

- **Type scale** (px at the 1600×900 reference): `FsScore 88`, `FsTitle 50`, `FsButton 26`,
  `FsBody 22`, `FsTeam 18`, `FsCaption 17`. Weighted, not uniform — captions are lifted because
  small text is what fails on a phone. Build text with `UIFactory.Text(...)`.
- **Spacing** — 4px base: `Xs 4`, `Sm 8`, `Md 12`, `Lg 16`, `Xl 24`, `Xl2 32`, `Xl3 48`, `Xl4 64`.
  Use these tokens for padding/spacing, never literals.
- **Radii**: `RadSm 8`, `RadMd 14`, `RadLg 20`.
- **Elevation**: one shadow for every panel — `ShadowFeather 40`, `ShadowAlpha 0.35` (wide + faint,
  so it reads as a lift, not a black rectangle nudged down-right). `Panel` already applies it.
- **Brand**: the name lives only in `ArcadeTheme.GameName` / `GameNameA "MINI"` / `GameNameB
  "FOOTBALL"` (the two-tone lockup). Never re-spell it inline.

## Motion (`UITween` + `ArcadeTheme`)

Two hard rules, both already handled by the helpers — keep them:
1. **Everything runs on unscaled time** (`Time.unscaledDeltaTime`). The menus sit at `timeScale 0`,
   so scaled-time animation would freeze. Use `UITween`, don't roll your own coroutine on `deltaTime`.
2. **Honour `ArcadeTheme.ReducedMotion`** — when on, every tween snaps to its final state. The
   `UITween` helpers do this for you; any new animation must too.

- Durations: `TFast 0.12`, `TNormal 0.20`, `TSlow 0.32`, `TFlash 0.70`; `Stagger 0.04`.
- Magnitudes: `HoverScale 1.04`, `PressScale 0.97`, `ScorePop 1.28`, `MenuFrom 0.92`.
- Easings (analytic, no `AnimationCurve` assets): `EaseOut` (quintic), `EaseIn`, `EaseSnap` (cubic
  out), `EaseOutBack` (mild overshoot — entrances and release-to-rest).
- Helpers: `ScaleTo`, `Fade`, `Pop` (a 1→peak→1 punch), `PopIn` (the standard entrance: 0.86→1 +
  fade), `Stagger` (pop children in turn — "a menu that assembles reads as built"), `CountUp`
  (sound lands on the digit), `Pulse` (tiny endless breathing), `Flash`.

Micro-interactions are subtle and fast. No flashing, no big bounces (the `EaseOutBack` constant is
deliberately mild because it runs on every button).

## Buttons (`MenuButton`)

All controls are `MenuButton : Selectable` (keyboard/gamepad nav for free; own visuals via
`DoStateTransition`). Build with `UIFactory.Button(parent, label, variant, onClick)`.

- Variants say what the button does, by colour: `Primary` (**cyan fill** — go, confirm: Play, Save,
  Next), `Reward` (**gold fill** — take a reward: Claim, Collect, a free chest), `Blue` (**cyan
  outline** — open a screen, or an ordinary action: Host, Join, Rename), `Ghost` (dark plate, dim
  edge — Back, Cancel, Later), `Neutral` (raised surface — cards, tiles), `Danger` (**red outline**,
  never filled — you lose something), `IconGold`. Do not use `Primary` for a reward; it is cyan now.
- UI Toolkit buttons are `UiKit.Button(label, variant, onClick)` with the same names in lower case —
  `pulse`, `reward` (`gold` is the old name), `blue`, `ghost`, `danger`. They are `UiFrame`s: the look
  is `--frame-*` custom properties on the variant class in `Base.uss`, written as literal colours.
- **Disabled** greys to the panel blue with a muted label, whatever the variant, so a disabled gold
  button no longer reads as the thing to press.
- **Shine**: the screen's one gold "start here" control may carry `UIShine.AddTo(button)` — a soft
  band of light that sweeps across it every few seconds (`TShine`, `ShineRest`). Hidden under
  `ReducedMotion` and while the button is not interactable. One per screen, like the resting glow.
- **Hover is deliberately quiet**: it does *not* repaint the border or light a glow — the highlight
  is carried by the scale lift and the label alone. A lit outline reads as a box drawn around the
  thing rather than a response to the cursor. Don't "improve" this by adding hover glows.
- **Press** dips scale to `PressScale` *and* darkens the fill (reads as pushed into the panel).
- **Exactly one control per screen** may glow at rest — mark it with `SetAccent(color, restingGlow)`
  ("start here"). More than one and the accent stops meaning anything.
- Selected state intentionally does **not** light up (a touchscreen never sends pointer-exit, so a
  selection glow would strand on the last-tapped button). Only genuine hover/press light.
- Every tap makes a sound, and the sound is picked for you: `MenuButton` and `UiKit.OnTap`/`Button`
  call `GameSfx.PlayUi(UiSound)` by variant (Primary → Confirm, Reward → Reward, Ghost → Back, Danger →
  Warn, the rest → Tap; a tap on a disabled `MenuButton` plays Deny). Don't add click SFX at call
  sites; pass a `UiSound` to `UiKit.OnTap` only when the default is wrong (a back button that is not
  a `Ghost` button, say). The sounds are synthesised in `SfxLibrary` and overridden by a recording
  named `UiTap`, `UiBack`, `UiConfirm`, `UiOpen`, `UiClose`, `UiToggle`, `UiStep`, `UiReward`,
  `UiWarn` or `UiDeny` in `Resources/Audio`.
- Full-width list rows call `SetNoScale()` so hovering one doesn't shove its neighbours.

## Component library (`UIFactory`) — reuse these first

Layout primitives: `Child(parent, name)`, `Rt(go)`, `Stretch(rt, inset)` / `Stretch(rt, l,b,r,t)`,
`Spacer`, `ClearChildren`, `ScrollList`.
Surfaces: `Panel` (shadow + border + `Fill` child — put content under `transform.Find("Fill")`),
`Backdrop` (opaque radial floor), `StageScrim` (translucent floor over the live 3D table),
`RoundedImage`, `GlowImage`.
Screen furniture: `ScreenHeader(root, title, onBack)` — the shared top row, title centred and Back in
the top-left corner, on every full-screen menu. `ActionCard(parent, title, subtitle, icon, onClick,
callToAction)` — a large tappable card with an icon tile; give exactly one per screen a
`callToAction` and it becomes the gold headline with the shine. `StatTile(caption, color)` — a
`StatBlock` on a raised tile, for stat grids.
Content: `Text`, `Button`, `IconButton`, `Tile` (image mode card), `StatBlock(caption, color)`
(returns the value label), `SectionRule(caption)`, `XpBar(parent, fraction, label, height)`,
`AvatarDisc`, `AvatarButton`, `RowIdentity`, `OnlinePip(parent, online, size)` (presence dot — pulsing
`Go` when online, static muted grey when not), `Segmented`,
`Loader`, `LogoLockup`.

Rewards and cosmetics: `CoinGlyph(parent, size)` and `CoinAmount(parent, text, height, color)` — every
coin in the game comes from these, so the currency is one object the player learns once. `ChestGlyph`,
`LeagueBadgeGlyph(parent, league, size)` (a ring in the tier metal around the same `TrophyGlyph` the
ranked pill draws), `CosmeticSwatch(parent, item)`, `CountPip`/`SetCountPip` (the "3 rewards waiting"
disc). Colour mappings live here too — `RarityColor`, `LeagueColor`, `ChestColor` — and there must be
exactly one of each: `LeagueMenu`'s own `LeagueColor` delegates to `UIFactory`'s rather than repeating
the switch. **`CosmeticSwatch` is the placeholder art for every skin.** When real art lands it is the
one function that changes, and every card, reward chip and reveal picks it up.

Progression widgets specifically: `XpBar` (track + `UIFillBar` fill + label) and `CoinPill` (a
self-refreshing balance in a bordered slab — drop one in and it keeps itself current off
`Wallet.OnChanged`; put the component on a CHILD of the cell, not the cell, because `Build` reparents
its own GameObject). On UI Toolkit screens the balance is `UiCoinChip`. A level, an XP bar or a balance should look the same everywhere
— reuse them, and read data from `Net/` (`PlayerProgress`, `Wallet`, `LevelPath`, `Inventory`); never
compute progression or economy in the UI.

## Building a new full-screen screen

Every screen is a `MonoBehaviour` following the same shape (see `MainMenu`, `ProfileMenu`,
`FriendsMenu`):

1. Fields: `GameObject root; CanvasGroup group; public bool IsOpen => root != null && root.activeSelf;`
2. `Build(Transform canvasRoot, …)`: `root = UIFactory.Child(canvasRoot, "Name");
   UIFactory.Stretch(Rt(root)); group = root.AddComponent<CanvasGroup>();` → a `Backdrop`/`StageScrim`,
   `UIFactory.ScreenHeader` (title + Back top-left — never a Back button of the screen's own), one or
   more `Panel`s populated via `Find("Fill")`, kept clear of the header on a 20:9 canvas (~805 tall);
   end with
   `root.SetActive(false)`.
3. `Open()`: `SetActive(true)`, `transform.SetAsLastSibling()`, `group.blocksRaycasts = true`, then a
   `UITween.Fade`/`PopIn`/`Stagger` entrance; refresh data here.
4. `Close()`: `group.blocksRaycasts = false; SetActive(false)`.
5. Talk to the outside only through `public Action` callbacks (e.g. `OnBack`). Screens don't sequence
   each other — **`GameFlow` owns navigation order**; don't hard-wire one screen to open another.

Register a new screen in `TableFootballUI` (AddComponent + Build in order) and pass it into
`GameFlow.Build(...)` so the flow can open it.

## Layout & responsiveness

- Canvas: `ScreenSpaceOverlay`, `sortingOrder 100`, `CanvasScaler` reference **1600×900**,
  `MatchWidthOrHeight = 0.5`. Effective canvas runs ~1385 wide at 4:3 to ~1790 at 20:9.
- Everything hangs off `SafeAreaRoot` (insets to `Screen.safeArea` for notches/gesture bars).
  Full-screen backdrops/dims bleed back out past it by `ArcadeTheme.Bleed (200)` so opaque menus
  still cover screen edge-to-edge.
- **Landscape, mobile.** Use anchor-stretch with constant margins; **don't pick magic pixel widths**
  for one resolution. Wide/short panels (like `ProfileMenu` 1080×430) suit landscape.

## Invariants — don't break these

- No scene Canvas, no UI prefabs, no imported art/fonts/sprites — runtime + procedural only.
- No hard-coded hex or duration; every value is an `ArcadeTheme` token.
- A `Glow` sprite's solid core must stop `feather` short of its texture edge (`ArcadeTheme.Bake`). When
  it did not, the fade fell outside the texture and every "soft" panel shadow rendered as a hard dark
  rectangle around its panel — with no error anywhere.
- All motion on unscaled time and `ReducedMotion`-aware.
- `Go` = online status only; `Pitch` = football accent. One resting glow per screen.
- Progression/state logic stays out of the UI (`Net/PlayerProgress`, `Net/MatchStats`); screens read
  and redraw on `OnChanged`.
- Surgical changes: match the surrounding style; reuse before adding; don't refactor working code to
  taste.

## Fonts

`ArcadeTheme.DisplayFont` / `UiFont` are optional slots — assign a condensed bold TMP font
(Oswald / Anton / Bebas) for the full arcade look. Both fall back to the TMP default, so the UI
renders before anything is assigned. `ResolveDisplay()` / `ResolveUi()` pick the right one.
