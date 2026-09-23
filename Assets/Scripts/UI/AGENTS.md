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

## The look — "Arcade Neon"

Dark, deep background; a few saturated neon accents; soft rounded panels that lift off the backdrop
on a wide faint shadow; motion that is sprung but quiet. Restrained, not flashy — accents are
feedback, not decoration. It's landscape, mobile-first, and safe-area aware.

## Palette (`ArcadeTheme`)

Backgrounds go deep→raised: `BgDeep #0A0E14`, `BgPanel #141A24`, `BgRaised #1E2735`, hairlines
`Line #2A3547`. Text: `Ink #EAF0F7`, muted `InkMuted #8A97A8`. Accents: `Red #FF3355`,
`Blue #22A7FF`, `Gold #FFB63D` (primary action / brand), `OnGold #241800` (ink on gold buttons).

Two greens, kept strictly apart — don't mix them:
- **`Go #3DFF88`** — online-status **only**: a live-presence dot. A "light that turns on."
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

- Variants: `Primary` (gold fill, ink label — the main action), `Ghost` (raised fill, blue hover
  accent), `Danger` (red accent — destructive, e.g. Delete Player), `Neutral`, `IconGold`.
- **Hover is deliberately quiet**: it does *not* repaint the border or light a glow — the highlight
  is carried by the scale lift and the label alone. A lit outline reads as a box drawn around the
  thing rather than a response to the cursor. Don't "improve" this by adding hover glows.
- **Press** dips scale to `PressScale` *and* darkens the fill (reads as pushed into the panel).
- **Exactly one control per screen** may glow at rest — mark it with `SetAccent(color, restingGlow)`
  ("start here"). More than one and the accent stops meaning anything.
- Selected state intentionally does **not** light up (a touchscreen never sends pointer-exit, so a
  selection glow would strand on the last-tapped button). Only genuine hover/press light.
- Clicks self-wire `GameSfx.PlayUiClick()` — don't add click SFX at call sites.
- Full-width list rows call `SetNoScale()` so hovering one doesn't shove its neighbours.

## Component library (`UIFactory`) — reuse these first

Layout primitives: `Child(parent, name)`, `Rt(go)`, `Stretch(rt, inset)` / `Stretch(rt, l,b,r,t)`,
`Spacer`, `ClearChildren`, `ScrollList`.
Surfaces: `Panel` (shadow + border + `Fill` child — put content under `transform.Find("Fill")`),
`Backdrop` (opaque radial floor), `StageScrim` (translucent floor over the live 3D table),
`RoundedImage`, `GlowImage`.
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

Progression widgets specifically: `XpBar` (track + `UIFillBar` fill + label), the level-pill pattern in
`ProfileChip`, and `CoinPill` (a self-refreshing balance in a bordered slab — drop one in and it keeps
itself current off `Wallet.OnChanged`; put the component on a CHILD of the cell, not the cell, because
`Build` reparents its own GameObject). A level, an XP bar or a balance should look the same everywhere
— reuse them, and read data from `Net/` (`PlayerProgress`, `Wallet`, `LevelPath`, `Inventory`); never
compute progression or economy in the UI.

## Building a new full-screen screen

Every screen is a `MonoBehaviour` following the same shape (see `MainMenu`, `ProfileMenu`,
`FriendsMenu`):

1. Fields: `GameObject root; CanvasGroup group; public bool IsOpen => root != null && root.activeSelf;`
2. `Build(Transform canvasRoot, …)`: `root = UIFactory.Child(canvasRoot, "Name");
   UIFactory.Stretch(Rt(root)); group = root.AddComponent<CanvasGroup>();` → a `Backdrop`/`StageScrim`,
   a title, one or more `Panel`s populated via `Find("Fill")`, a Back button; end with
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
