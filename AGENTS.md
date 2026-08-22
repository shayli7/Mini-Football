# AGENTS.md

This file provides guidance to AI coding agents working in this repository.

**MINI FOOTBALL** — a 3D foosball game for Android with local and online multiplayer.
Unity **6000.0.78f1**, URP, **new Input System only** (`activeInputHandler: 1`; legacy `Input.GetKey`
throws). The table is Blender-authored and imported as `Assets/Models/FoosballTable.fbx`.

## The project path is load-bearing

Must live at an **ASCII-only path outside OneDrive** — currently `C:\Users\liaid\Dev\TableFootball`.
Android's SDK/NDK/Gradle reject non-ASCII paths outright (`UnityException: Invalid project path`),
and Unity's `Library/` churn fights OneDrive sync. An older copy under `...\תכנות שי-לי\Table Football`
is a stale backup — never edit it.

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
