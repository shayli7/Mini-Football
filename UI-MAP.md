# UI-MAP.md — which files make the UI

Read this first when a task touches the interface. Everything is built **in code at runtime** (no
scene UI, no prefabs). Branch to work on: `claude/dazzling-cori-l6c9e0`. You cannot compile in a
cloud session, so say so when you hand work back and ask the user to compile in Unity.

For the style rules (colours, buttons, motion) read `Assets/Scripts/UI/AGENTS.md` after this.

## Two UI systems side by side

The UI is halfway through a move from **uGUI** (old) to **UI Toolkit** (new). Each screen is one or
the other. Check the table below before editing, and never mix the two in one screen.

| Screen | File in `Assets/Scripts/UI/` | System | Styles (UI Toolkit only) |
|---|---|---|---|
| Start / title | `StartScreen.cs` | UI Toolkit | `Resources/UI/Styles/StartScreen.uss` |
| Main menu | `MainMenu.cs` | UI Toolkit | `MainMenu.uss` |
| Pause, leave, Settings | `GameMenu.cs` (its pause button is still uGUI) | UI Toolkit | `GameMenu.uss` |
| Daily quests | `QuestsMenu.cs` | UI Toolkit | `Quests.uss` |
| Chest opening animation | `ChestOpening.cs` | UI Toolkit | `ChestOpening.uss` |
| Store (chests, daily deal, coin ads) | `StoreMenu.cs` | uGUI | — |
| Level path | `LevelPathMenu.cs` | uGUI | — |
| Friends / friend profile | `FriendsMenu.cs`, `FriendProfileMenu.cs` | uGUI | — |
| Online | `OnlineMenu.cs` | uGUI | — |
| Ranked league | `LeagueMenu.cs` | uGUI | — |
| Account / profile | `ProfileMenu.cs`, `ProfileChip.cs` | uGUI | — |
| Match score HUD | `ScoreHud.cs` | uGUI | — |
| Countdown, loading, onboarding | `CountdownScreen.cs`, `LoadingScreen.cs`, `OnboardingScreen.cs` | uGUI | — |
| Quest result ledger (after a match) | `QuestLedger.cs`, `QuestIcons.cs` | uGUI | — |

**Migration order still to do:** Level Path, Friends, Online, Account, League, then the match HUD.

## Shared pieces

**UI Toolkit** (`Assets/Scripts/UI/Toolkit/`)
- `UiToolkitHost.cs` — creates the one panel every Toolkit screen hangs off. Safe-area handling.
- `UiKit.cs` — helpers: `El`, `Text`, `Button`, `OnTap`, `Header`, `Show`, `Enter`, `Fade`, `Loop`.
- `UiIcon.cs` — all icons, drawn in code. To add an icon, add a `Glyph` and a `case`.
- `UiFonts.cs` — applies the fonts by class name (`f-display`, `f-body-semi`, ...).
- `Assets/Resources/UI/Styles/Base.uss` — shared colours (CSS variables), buttons, chips, bars,
  header. Fonts are in `Assets/Resources/UI/Fonts/`.

**uGUI** (old)
- `UIFactory.cs` — builders for every old-style element (buttons, panels, text, chest glyph, coins).
- `MenuButton.cs` — old button behaviour. `UITween.cs` — old animations.
- `UIFillBar.cs`, `UIPulse.cs`, `UIShine.cs`, `UIBurst.cs`, `UISpinner.cs`, `UIAmbientDrift.cs`,
  `CoinPill.cs` — small old-style effects and widgets.

**Both systems**
- `ArcadeTheme.cs` — the palette (black / blue / gold), sizes, timings. Colours also exist as CSS
  variables in `Base.uss`; **change both together**.
- `TableFootballUI.cs` — builds every screen at startup. Add a new screen here.
- `GameFlow.cs` — decides which screen is showing (boot, menu, match, loading). Screens report
  choices back to it through callbacks; they never start a match themselves.
- `SafeArea.cs`, `MenuStageCamera.cs`, `GameAudio.cs` — layout, menu camera, volume settings.

## Where to edit for common requests

- **Change how a screen looks:** its `.uss` file (Toolkit) or its `.cs` file (uGUI).
- **Change a colour everywhere:** `ArcadeTheme.cs` and the matching variable in `Base.uss`.
- **Add or change an icon (Toolkit):** `Toolkit/UiIcon.cs`.
- **Chest animation:** `ChestOpening.cs` (timing and motion) and `ChestOpening.uss` (layout). It is
  started by `StoreMenu.cs` (bought and daily chests) and `LevelPathMenu.cs` (level rewards) through
  `ChestOpening.Play(...)`. The chest is drawn like the store's card chest, `UIFactory.ChestGlyph`.
- **Store items, prices, ads:** the data lives in `Assets/Scripts/Net/` (`ChestLoot.cs`,
  `ShopOffers.cs`, `Wallet.cs`, `AdService.cs`); `StoreMenu.cs` only draws it.
- **Quest content:** `Assets/Scripts/Progression/` (`QuestDefinition.cs`, `DailyQuests.cs`);
  `QuestsMenu.cs` only draws it.
- **Design mock-ups:** the Design canvas artifact
  https://claude.ai/artifact/R3R3iBYLf69txbAWn1DHdE has one artboard per screen. Match it.

## Rules that save time

- A migrated screen keeps its class name and public members (`Build`, `Open`, `Close`, `OnBack`,
  `IsOpen`), so `GameFlow` and `TableFootballUI` do not need to change when a screen moves over.
- **Never rely on which system draws on top.** If a Toolkit screen opens over a uGUI one (the chest
  opening over the store or level path does), hide or fade the uGUI screen first and restore it after.
- Screens read data from `Net/` and `Progression/` and redraw on their `OnChanged` events. Do not put
  game rules in a screen file.
- Unity only loads `Resources.Load` files from a folder named exactly `Resources`.
- Do not add Editor tools or `[ContextMenu]` builders. Give the user Inspector steps instead.
