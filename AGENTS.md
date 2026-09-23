# AGENTS.md

This file provides guidance to AI coding agents working in this repository.

**MINI FOOTBALL** — a 3D foosball game for Android with local and online multiplayer.
Unity **6000.0.78f1**, URP, **new Input System only** (`activeInputHandler: 1`; legacy `Input.GetKey`
throws). The table is Blender-authored and imported as `Assets/Models/FoosballTable.fbx`.

## The project path is load-bearing

Must live at an **ASCII-only path outside OneDrive** — currently `C:\Unity-projects\Mini-Football`.
Android's SDK/NDK/Gradle reject non-ASCII paths outright (`UnityException: Invalid project path`),
and Unity's `Library/` churn fights OneDrive sync. Two older copies are stale backups — never edit
them: `C:\Users\liaid\Dev\TableFootball` (the previous ASCII location) and one under
`...\תכנות שי-לי\Table Football` (the original, on OneDrive).

Only `Assets/`, `Packages/` and `ProjectSettings/` matter (~6 MB). `Library/` is ~1.8 GB and
regenerates.

## Commands

**There is no test suite.** To verify changes without opening Unity, compile the scripts against
Unity's own assemblies with the bundled Roslyn (`Editor/Data/DotNetSdkRoslyn/csc.dll`, run via
`Editor/Data/NetCoreRuntime/dotnet.exe`). It is the only fast feedback available and it catches the
API mismatches that matter.

References needed beyond `UnityEngine`/`UnityEngine.CoreModule`: `PhysicsModule`, `UIModule`,
`AudioModule`, `TextRenderingModule`, `ImageConversionModule`, `ScreenCaptureModule`, `UnityEditor`,
and from `Library/ScriptAssemblies` — `Unity.InputSystem`, `UnityEngine.UI`, `Unity.TextMeshPro`,
`Unity.Netcode.Runtime`, `Unity.Services.Multiplayer`, `Unity.Services.Core`,
`Unity.Services.Authentication`.

Three flags matter:

- Compile **twice**, with and without `/define:UNITY_EDITOR` — several files wrap `UnityEditor.Undo`
  in `#if UNITY_EDITOR` and would break a player build.
- Add `/define:ENABLE_INPUT_SYSTEM`; some UI is guarded on it.
- Pass `/nowarn:0649`, or every `[SerializeField]` warns for being "never assigned" — Unity assigns
  them, not code.

**Driving csc from the Bash tool has three separate traps, and every one of them fails by
reporting success.** Budget for getting this wrong before it works:

- Use `-r:` / `-define:` / `-out:`, **not** the `/r:` forms. Git Bash rewrites a leading `/` into a
  path and each reference becomes a bogus relative filename (`CS2021`).
- **`MSYS_NO_PATHCONV=1` is not the fix for that.** It also suppresses the POSIX→Windows conversion
  that makes the *executable* path work, and dotnet then reports the compiler itself as missing.
  `dotnet.exe` and `csc.dll` are Windows programs: every **argument** needs a Windows path
  (`C:/...`, from `pwd -W`), while bash's own `[ -f ]` tests and the launch of the exe need POSIX
  (`/c/...`). Both forms are required at once.
- `C:/Program Files` contains a space, so arguments cannot be word-split out of one big string.
  Put every argument on its own line in a **response file** and pass `@file`.

**Check the source count before believing a pass.** A response file that ends up with no sources in
it compiles an empty assembly and exits 0 — the failure mode that looks exactly like a clean build.
Assert non-zero sources and refs, confirm the output assembly exists, and sanity-check the harness
once by feeding it a deliberately broken file and watching it fail.

**A missing module reference reads as a bogus "does not contain a definition for…" against perfectly
correct code.** Add the reference; do not edit the file.

Device checks (`adb` ships with Unity's Android module):

```bash
"C:\Program Files\Unity\Hub\Editor\6000.0.78f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" devices -l
```

Building is GUI-only: `File → Build Profiles` → Android → `Ctrl+B`, output to `C:\Builds` (also
ASCII, also outside OneDrive). The test device (Xiaomi/MIUI) blocks USB installs until
**Developer options → Install via USB** is enabled; it fails as `INSTALL_FAILED_USER_RESTRICTED`,
not as a Unity error.

## Architecture

`RodController` is the hub. Everything that plays the game — touch input, the AI, match resets, the
network — drives rods through its input-agnostic API (`SetSlide01`, `FlickSpin`, `SetSpinAngle`,
`ResetRod`), and none of them know about each other. **New control schemes should be new callers,
not new branches inside `RodController`.**

| Area | Role |
|---|---|
| `Gameplay/RodController` | One rod: slides along its bar, spins about it. Owns the spin sign convention. |
| `Gameplay/TableReferences` | Registry of red/blue rods + ball; how everything else finds rods. |
| `Gameplay/BallController` | The only dynamic body. Owns the speed cap, fall-through net, dead-ball rescue and strike impulse. |
| `Gameplay/TableSurfaces`, `TablePhysicsSettings` | Per-surface materials, and PhysX retuned for tabletop scale. |
| `Ai/TeamAI` | Plays one team through the same API. Never touches the ball. |
| `Managers/MatchManager` | Score, clock, goal → pause → kick-off, sudden death, restart. |
| `UI/*` | The whole interface, built **in code at runtime**. No Canvas in the scene, no UI prefabs. |
| `Net/*` | UGS Sessions + Netcode for GameObjects. Host-authoritative. |
| `Net/Ladder/*` | Ranked: leagues, pods, weekly promote/relegate. Mock backend today, UGS behind the same interface later. |
| `Net/Cosmetics/*` | The catalogue of skins and badges, what is owned and worn, and the chest that rolls them. |
| `Gameplay/BallSkins`, `BallSkinner` | Turns an equipped ball-skin id into the material the ball wears. Art in `Resources/BallSkins`, source in `Art/Blender`. |
| `Net/Wallet`, `LevelPath`, `RankedRewards` | Gold coins, the 60-level reward path, and the weekly ranked payout. |

**The rewards economy is four statics and one rule: coins are earned, never bought.** `Wallet` is the
only balance; exactly two things credit it — `RankedRewards` at the weekly ladder rollover and
`LevelPath` when a level's reward is claimed — and exactly one thing spends it, the store. Screens
read these synchronously and redraw on their `OnChanged`, the same shape as `PlayerProgress`.
`CosmeticCatalog` is the single list of skins; adding one is a row there and nothing else, because
the store grid, the chest pool and the level path's named rewards all read it.

**Cloud Save is a sync layer over the six PlayerPrefs stores, never between them and the UI.**
`Wallet`, `Inventory`, `PlayerProgress`, `LevelPath`, `RankedRewards` and `MatchStats` stay
synchronous and PlayerPrefs-backed; `CloudSync` sits on top. Each store owns its own keys and exposes
only an `internal ExportTo`/`ImportFrom(CloudSync.SaveDoc)` pair — `CloudSync` never sees a key name.
Every write funnel calls `CloudSync.MarkDirty()`; a debounced runner (`CloudSyncRunner`, flushing on
idle, app-pause and quit) uploads. `ImportFrom` writes PlayerPrefs **directly**, not through the
store's own `Add`/`Grant`/etc., so applying a cloud value never re-marks the store dirty — breaking
that is a write loop. The merge is **monotonic**: owned skins union, claim masks OR, every counter
takes the max, so a stale device can never take something away. The one field max is not strictly
right for is **coins** — a spend can be refunded by a staler device — accepted because coins are
earned, never bought; the fix, if ever needed, is a lifetime-earned/spent pair. **All SDK calls live
in `CloudSaveBackend` and nowhere else** — it is the only file that depends on
`com.unity.services.cloudsave`, so it is also the only one that will not compile until that package is
imported. Keep it that way: the compile-check harness stubs its two signatures.

**Events, not polling.** `MatchManager` exposes `GoalScored`, `ScoreChanged`, `MatchWon`,
`MatchRestarted`, `KickedOff`, `TimeChanged`, `FullTime` and `SuddenDeathStarted`, plus a `PlayLive`
flag. HUD and audio subscribe; the AI idles on `PlayLive`. `MatchManager` knows about none of them —
keep it that way.

**`UI/GameFlow` owns the sequence** (boot → loading → menu → match → loading → menu) and the front
end's `timeScale`. Menus report choices through callbacks and never start matches themselves.

**Physics and audio are configured from code rather than Project Settings**, so the values are
version-controlled and hold on device. Sound effects are synthesised in `Audio/SfxLibrary` and
overridden by recordings in `Assets/Resources/Audio/`; resolution runs inspector slot → recording →
synthesised.

**Online** (`Net/`) uses the unified **UGS Sessions API** (`CreateSessionAsync` with
`WithRelayNetwork()`), not hand-rolled Lobby + Relay. `GameServices` and `OnlineSession` **never
throw** — they report status, because a phone with no signal must boot exactly like one with a
working connection, and local play depends on none of it. A `NetworkManager` with `UnityTransport`
must already be in the scene before any of it is used.

## Invariants that bite

Every one of these has already caused a real bug here, and most fail **silently**.

**Physics material combine modes resolve by PRIORITY, not by averaging intent:**
`Average(0) < Minimum(1) < Multiply(2) < Maximum(3)`. The higher-priority mode decides how *both*
values are combined. So each surface declares the mode that gives it its character (figures: Minimum
bounce, Maximum friction; rails: the reverse), and **the ball must carry a neutral `Average` baseline
sitting numerically between the Minimum-mode and Maximum-mode targets** — a Minimum surface can only
pull *down* from it, a Maximum surface only *up*. Break that and an inspector field labelled "grip"
sits there doing nothing. `TableSurfaces.WarnIfBallOutranksSurfaces` guards it.

**`Physics.bounceThreshold` is measured against the impact's NORMAL component, not its speed.**
Unity's default of 2 m/s kills nearly every bounce on a 1.3 m table, and even a few tenths silently
destroys *glancing* hits first — a ball meeting a rail at 10° presents only ~0.26 m/s. Keep it near
zero, or bank shots slide along the rail instead of rebounding.

**Power and control are deliberately separate dials.** Figures are dead (low bounce, high grip) so
the ball can be trapped and dribbled; shots come from an explicit strike impulse applied when a rod
is genuinely *swinging*. A ball bouncy enough to fly off a figure would fly off every accidental
touch, and close control would be impossible.

**Read `RodController.MeasuredSpinSpeed`, not `CurrentSpinVelocity`, to detect a swing** — the latter
is pinned at zero while a drag drives the angle directly.

**`BarPivot` is the rod's *resting* pivot** and never moves with the slide. Compute slide targets as
**absolute** positions; feeding an offset back as a per-frame delta accumulates and slams the rod
between its limits.

**`MovePosition`/`MoveRotation` do nothing while `timeScale` is 0.** They are requests PhysX carries
out on the next *physics* step, and a frozen world runs none — so a rod repositioned for a frozen
table sits queued and invisible until play resumes, then snaps into place at the worst possible
moment. Rods carry kinematic bodies, so this is every `RodController.ApplyPose` call: reposition a
rod for anything the player is meant to *see* while stopped (a reset, a countdown, a menu) via
`ApplyPose(teleport: true)`, which writes the transform and the Rigidbody's own copy together.
`BallController.ResetBall` already teleports for the same reason.

**A serialized flag that reads like one rule usually governs several moments.** `resetRodsOnGoal`
gated the rod reset at a goal kick-off, at `Park`, *and* at the start of a new match; turning it off
to stop rods being yanked mid-match silently left every fresh match holding the previous one's rod
positions. Each of those moments now passes what it wants explicitly.

**Spin direction belongs to `RodController.ForwardKickDirection`** — the world direction a foot
travels under a positive spin. Derive kick sign from it. Guessing from which half a team defends is
an unrelated fact and will be wrong for one of the two teams.

**Anything reading rod geometry must build in `Start`, not `Awake`.** `BarAxis`/`BarPivot` exist only
after `RodController.Awake` caches the rest pose, and Awake order between components is not
guaranteed. A zero axis collapses every rod to depth 0, so the AI locks onto one rod forever — with
no error anywhere.

**Netcode's `NetworkRigidbody` forces the ball kinematic from its own `Awake`, with no session
running.** It sits after `BallController` on the Ball, so it overwrites `ApplyPhysics`'s
`isKinematic = false` and every *local* match gets a ball that no figure can move — the ball still
rests correctly on the pitch and every collider is present, enabled and correctly sized, so this
presents as a collider bug and is not one. `BallController.Start` re-asserts the dynamic body after
all Awakes; `NetworkedBall.OnNetworkDespawn` does the same on the way out of an online match, where
`AutoSetKinematicOnDespawn` freezes the ball on **both** machines, host included.

**Figure colliders are extended down to the pitch** in `TablePhysicsBuilder`; a collider fitted to
the mesh inherits the model's gap above the pitch and the ball rolls under the players.
**Goal backs must never take a solid `BoxCollider`** — an axis-aligned box reaches across the goal
mouth and wedges the ball before it can score.

**`RodTouchInput` bypasses the EventSystem** (it reads `Pointer.current` directly), so it must
raycast the canvas explicitly to avoid grabbing rods through UI — testing by **screen position**, not
pointer id, since EnhancedTouch ids do not match EventSystem ids and an id-based check fails silently
on device.

**`Resources.Load` only reads from a folder named exactly `Resources`.** Audio placed anywhere else
is invisible to it and every sound falls back to the synthesised one — the fallback working exactly
as designed is what makes this hard to spot.

**Online:** the guest's ball is kinematic with `BallController` switched off wholesale; goal triggers
are **disabled on the guest** (a NetworkTransform-driven ball still fires `OnTriggerEnter`, and the
guest would score a second private goal off the host's); and teams are **derived from `IsHost`**
rather than negotiated, because a message can be lost or arrive late and `IsHost` cannot.
`NetworkedRod` sends two floats — slide and spin angle — rather than a `NetworkTransform`, which
would cost three times the bandwidth and whose interpolation would fight `RodController`'s own
`ApplyPose` for the same transform every frame.

**The ladder never announces its own rollover, so ranked rewards are DETECTED, not received.**
`ILadderService` has no "the week ended" event and cannot have a useful one: the mock rolls over
inside its own `Sync`, and a real server rolls over while the app is closed. `RankedRewards` therefore
keeps a snapshot of the last week it saw — index, league, rank — and pays out for *that* week the
moment a standing comes back carrying a different index. `LeagueBadges` works the same way, granting
from the standing rather than from a promotion. Both are idempotent and both hang off
`Ladder.OnChanged` in `GameFlow`; a screen must never be the thing that pays a player, or a player
who does not open Ranked is never paid.

**`PlayerProgress.MaxLevel` and the level path are one fact in two files.** The path pays a reward per
level up to the cap and finishes on a chest at 60, so `LevelPath` reads the cap from `PlayerProgress`
rather than holding a copy — and `PlayerProgress.ResetLocal` wipes the path with it. Clearing the XP
while leaving the claim mask behind hands the next player a path already marked collected, and every
level they earn back pays nothing, with no error anywhere. XP still accrues past the cap, so the bar
is pinned full inside `FractionForXp` (not at the `XpFraction` property) — a `MatchOutcome` carries
raw before/after fractions to the results banner, and capping only at the property would have the
banner creeping forward while the profile chip sat full.

**A `CoinPill` stays subscribed to `Wallet.OnChanged` while its menu is hidden**, and every screen has
one. `StartCoroutine` on an inactive GameObject is a Unity error, not a no-op, so the pill snaps
instead of animating when `!isActiveAndEnabled` — the same guard `UIFillBar` uses. This is not
hypothetical: claiming on the level path credits coins while the main menu's own pill is switched off.

**Cosmetic skins are materials, never meshes, and every write goes through `SkinMaterials`.** Writing
a shader property a material does not have is a SILENT no-op in Unity — no error, no warning — so the
URP/built-in name table (`_BaseMap`/`_MainTex`, `_Smoothness`/`_Glossiness`, `_GlossMapScale`) lives in
exactly one place and `SetAlbedo` warns by shader name when neither exists. A skin clones the model's
own material rather than calling `Shader.Find`, which cannot pick the wrong pipeline or come back
magenta in a build. Every failure — stale id, missing texture, unexpected shader — falls back to the
original material, so the worst case is a plain ball on a plain pitch. Textures live under
`Assets/Resources/<Kind>Skins/`; `Resources.Load` only reads from a folder named exactly `Resources`.
Because none of this touches a collider, no skin can change how the game plays.

**A figure skin dresses its OWN team only, and the kit slot is the shirt.** Each figure has three
material slots — body (46%), kit (45%), head (9%) — and the kit is the jersey. A plain skin
("Yellow") repaints that slot outright, which is safe *because* a skin never touches the opposing
side: `FigureSkinner.CurrentIdFor` returns the local player's equipped id for their own team and null
for the other, so the opposition keeps the model's red or blue and the two sides stay tellable apart.
Textured skins still ship a `_KitRed` and a `_KitBlue` — a red player's Stripes must read as red —
authored per team rather than tinted from one greyscale map, because a multiply tint cannot produce
WHITE stripes on a red shirt. Only the 0.50–0.85 height band is actually visible in play (the body
covers the rest), so anything carrying team identity must live there; a sash below it made red and
blue gladiators identical. The goalkeeper is derived from his rod carrying exactly one figure, not
from the rod list's documented goalie-first ordering, which an editor command re-establishes.

**Figures are the one cosmetic the host does NOT own online — each player dresses their own eleven.**
`OnlineMatchDirector` therefore carries `netFiguresRed` and `netFiguresBlue` rather than one value.
Both are still server-written (write permission is Server), so the guest cannot publish directly and
reports its choice with `ReportFigureSkinRpc`, which the host copies into the Blue slot; one writer,
and a late joiner still gets both values as part of the spawn. Which side is "yours" comes from
`GameFlow` — the only class that knows, since it differs between vs-AI, local PvP and online.

**`Material.materials.clear()` resets every polygon's `material_index` to 0.** On a figure the
histogram goes from `{0:38, 1:180, 2:160}` to `{0:378}`, collapsing kit and head onto the body and
making the whole figure one flat colour. Assign into the existing slots instead. Related: reading
`Renderer.material`/`materials` CLONES, so skinning writes `sharedMaterial`/`sharedMaterials` (and
`sharedMaterials` returns a copy — the whole array has to be assigned back, not mutated in place).

**Store previews are rendered, not the skin's own texture.** A skin's texture is a uv layout, not a
picture of it — a soccer ball's equirectangular map shown raw reads as a stretched blob. So
`Art/Blender/make_thumbnails.py` renders the real ball, the real pitch with its lines and the real
red/blue figure pair into `Resources/SkinThumbs/`, transparent so the card keeps its rarity tint
behind them. `CosmeticThumbnails` caches the misses as well as the hits: the store rebuilds its whole
grid on any wallet or inventory change, and badges legitimately have no render.

**A room shows in a MATCH as a floor, never as a skybox.** The gameplay camera is orthographic and
points straight down, so every pixel shares one view direction and a skybox collapses to a flat
patch — `RenderSettings.skybox` can be set perfectly and still show nothing in play. `BackgroundSkinner`
therefore lays an unlit plane under the table, positioned from the table's measured renderer bounds
(never typed-in coordinates — those are what left the menu stage camera filming the table's
underside after the rig moved). Its art is `Resources/BackgroundSkins/BgFloor_<Stem>.png`, rendered
top-down by `Art/Blender/make_backgrounds.py --floors` at 4 m across to match the 4-unit plane 1:1.
The floor draws unlit, so the brightness baked into that image is all the player gets.

**`BallTrail` measures speed from the TRANSFORM, never the Rigidbody.** On the guest in an online
match the ball is kinematic and driven by NetworkTransform, so its rigidbody velocity is permanently
zero — gating the trail on that gives the host a trail and the guest none. The transform moves on
both machines. Trail and particle materials come from `SkinMaterials.CreateTrailMaterial`, which
tries unlit shaders first (only those honour the vertex colours that make a trail FADE) and falls
back to a transparent clone of the ball's own material, because `Shader.Find` returns null in a build
for any shader that was stripped. No usable shader costs the sparkle, never the match.

**A background skin is a SKYBOX, and its default is no skybox at all.** The scene has no environment
geometry — the table floats in Unity's default sky — so `BackgroundSkins` resolves an id to a
`Skybox/Panoramic` material, and `background.classic` resolves to **null**. That null is the whole
safety property: the front end keeps the flat backdrop it has always had, so a player who buys no
room sees no change. Two things break it silently if forgotten: `MenuStageCamera` clears to
`SolidColor` and draws *over* the gameplay camera, so it must be told `UseSkybox(true)` or it paints
straight over the sky; and `Skybox/Panoramic` defaults `_Mapping` to 6-frames layout, so a lat-long
panorama needs `_Mapping = 1` or one sixth of the image is stretched across the entire sky. The
panoramas are rendered from eye level with the floor 0.8 m below, which is what puts their vertical
centre on the horizon where that shader expects it. Nothing overhead may sit at the camera's own
position — a lamp at the origin lands on the zenith, where equirect distortion smears it across the
whole top of the image.

**Anything iterating `CosmeticKind` must iterate the ENUM, not a hand-written bound.**
`Inventory.ResetLocal` used to stop at `Badge`; adding `Background` after it would have left that
slot's equipped id behind on a player change, so the next person inherits a room they never unlocked.

**The host owns the table's cosmetics online**, for the same reason it owns the clock and the ball:
two machines rendering different pitches would be two different games. Ball skin rides on
`NetworkedBall`; field and figure skins ride on `OnlineMatchDirector`. Both sides apply the published
value — the host wears its own broadcast rather than re-reading its inventory, so they cannot drift —
and `OnNetworkDespawn` clears the override so local play returns to the player's own choices.

**A ball skin is a MATERIAL, never a model.** The ball in the FBX is a UV sphere of radius 0.0165 —
the `SphereCollider`'s exact radius — with a clean lat-long unwrap (pole axis Y,
`V = asin(y/R)/pi + 0.5`), so every skin is an equirectangular texture on that one mesh.
`Gameplay/BallSkins` clones **the ball's own material** rather than calling `Shader.Find`, so it
inherits the render pipeline's shader and cannot come back magenta in a build that stripped a
name-looked-up one. Never ship a skin as geometry: the mesh is purely visual (the collider does the
physics), so a material can never change how the ball plays, and a second sphere is a second chance
to get the radius wrong. Regenerating the art is `Art/Blender/` — see its README.

**URP MULTIPLIES the smoothness map by `_Smoothness`; it does not replace it** (`specGloss.a *=
_Smoothness`). A packed `_MetallicGlossMap` therefore only reads back verbatim with `_Smoothness = 1`
— leave the fallback float in place and every authored value is silently scaled by it. The map also
does nothing at all unless `_METALLICSPECGLOSSMAP` is enabled, which fails *almost* invisibly because
the float fallback looks nearly right. And the packed maps must import with **sRGB off**: they are
data, not colour.

**Online, the ball skin is the HOST's**, published as a `NetworkVariable<FixedString64Bytes>` on
`NetworkedBall` and applied by *both* machines through the same `BallSkinner.SetOverride` — the host
wears its own published value rather than reading its inventory directly, which is what stops the two
screens drifting apart. `OnNetworkDespawn` clears the override, or the next local match keeps wearing
a skin the player may not own. `BallSkinner` is deliberately input-agnostic in the manner of
`RodController`: new sources of a skin are new callers, not new branches inside it.

## Working style in this repo

**Write runtime scripts and give manual Inspector instructions — do not add `[ContextMenu]` or Editor
builder tools.** Automated scene surgery has repeatedly caused problems here (a figure-stretching
tool mangled the models; a collider builder silently wedged the ball in the goal), and it hides what
is actually changing. Deliver a script, then a short checklist: which GameObject, which component,
which values.

`TablePhysicsBuilder` predates this rule and still carries context menus. Leave them; do not extend
them. Global settings applied at runtime (`Physics.*`) are the reasonable exception — they have no
Inspector equivalent.

**Serialized values already saved in the scene beat changed code defaults.** Editing a default does
nothing to a component that already exists in the scene, so say so explicitly and give the Inspector
step alongside the code change.

The corollary is the way out: a **newly added** serialized field has no saved value, so its code
default *does* take effect on the existing scene. Retuning something already saved — the whole
`TeamAI` difficulty tree is — is therefore done by adding a new field applied on top of the
resolved value (`reactionHandicap`, `blockErrorHandicap`), not by editing the saved ranges,
which would need an Inspector visit per number and silently do nothing until then.

Scene setup lives on the `FoosballTable` root: `TableReferences`, `RodTouchInput`,
`TablePhysicsSettings`, `TableSurfaces`, `MatchManager`, `TeamAI`. Each `Rod_0`…`Rod_7` carries a
`RodController`. One empty object carries `TableFootballUI`, which builds the Canvas, EventSystem,
audio object and every screen at runtime.
