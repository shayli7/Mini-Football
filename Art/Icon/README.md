# App icon

Source for the MINI FOOTBALL launcher icon and the Play Store assets. Lives outside `Assets/` on
purpose — Unity has no importer for `.mjs`, `.py`, `.html` or `.svg` here and would carry each file
as an opaque `DefaultAsset` with a `.meta` alongside it.

**The shipped mark is `render3d` ("The Figure"):** one red foosball man on his chrome rod, seen from
a three-quarter angle, on a full-bleed green pitch. No text. It is rendered in Blender from the
game's own `FoosballTable.fbx`, so the man on the launcher tile is literally the man in the game.

The earlier flat marks (`glyph`, `glyph-rod`, the five direction concepts) are still buildable and
their sources are still in `out/` — they are history now, not the shipped icon.

## Commands

```bash
node build.mjs palette        # out/palette.json   -- colours, for render.py to read
```

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P render.py
```

```bash
node build.mjs ship           # the export set (defaults to render3d)
node verify.mjs               # out/verify.png     -- proof pass on the exported PNGs
```

Add `-- preview` to the Blender command for one small, cheap frame (`out/render/preview.png`, 512px,
48 samples, ~20s) while judging framing, exposure or colour. The full pass is three frames at 1024px
and takes a few minutes on CPU.

Still buildable, no longer shipped:

```bash
node build.mjs glyph          # out/glyph-sheet.png -- the three flat polarity variants
node build.mjs sheet          # out/sheet.png       -- the five original directions
node build.mjs ship glyph-rod # go back to the flat gold mark
```

Rasterising the export set goes through headless Chrome. It is the only renderer already on this
machine that produces an exact pixel size with a genuinely transparent background — there is no
`magick`, no `cwebp`, no `sips`, and no Python outside Blender's own.

## Where things land

| Path | What |
|---|---|
| `out/render/*.png` | the three Blender frames — the actual artwork |
| `../../Assets/Images/Icon/*.png` | the four files Player Settings reads |
| `out/ship/*.svg` | editable source for each of those four |
| `out/store/` | Play Store listing icon and feature graphic |

## The three frames

`render.py` shoots three, not one:

| Frame | Becomes | Why separate |
|---|---|---|
| `field_wide.png` | adaptive **background** | |
| `figure_wide.png` | adaptive **foreground** (alpha) | Android animates the two layers independently |
| `composite_tight.png` | legacy, round, store | never cropped to 66.7%, so it can be framed closer |

The two `_wide` frames come from **one camera position**. They must, or they stop registering the
moment a launcher parallaxes one against the other.

## Rules the render has to obey

- **The figure's colour comes from the FBX, not from `lib/palette.mjs`.** `ArcadeTheme.Red`
  (`#FF3355`) is a HUD accent — a rose, meant for borders and glows. Painted onto a physical object
  it renders visibly pink. The team colour of a foosball man is `Foos_Red` (`#BF353C`) and it lives
  in the model, so `render.py` reads it back from there and cannot drift from what the player sees.
  The pitch green is still palette (`pitch` / `pitchLit`) — the FBX's own field material is an
  untextured placeholder.
- **`lib/palette.mjs` still mirrors `Assets/Scripts/UI/ArcadeTheme.cs`,** and `out/palette.json` is
  dumped from it. If a value changes in the C#, change it here too, or the launcher tile and the
  game's first screen drift apart.
- **View transform is Standard, not AgX.** AgX would desaturate the team red into something the
  game's UI does not contain. The cost is that there is no highlight rolloff, so the lights have to
  stay dim — an early pass at 26 W clipped the man to pink and the rod to paper white.
- **Everything important stays inside the centre 52% of a `_wide` frame.** Android shows the centre
  66.7% of an adaptive layer and only *guarantees* the inner circle. This is `FRAME_WIDE`, and
  `frame_on()` solves the camera distance for it rather than trusting a hand-placed camera.
- **The pitch has to outrun the frame.** A small ground plane shows its own far edge as a horizon,
  and a horizon turns a full-bleed green tile back into a photograph of a table.
- **The man has to touch the pitch.** On the real table the field sits 15 mm below the figures —
  foosball men hover — and at icon scale that gap swallows the contact shadow and leaves the figure
  looking pasted on. `FOOT_CLEARANCE` brings the pitch up to 3 mm under his feet.
- **The key light sits a quarter-turn off the lens.** Placed near the camera axis its shadow falls
  behind the figure, hidden by the figure, which reads as no shadow at all. Lights are positioned in
  the same polar terms as the camera so that relationship stays visible in the code.
- **The rod is load-bearing.** A figure without one reads as a pedestrian sign. It runs corner to
  corner and off both edges of the frame.

## Not approved copy

`TAGLINE` in `build.mjs` ("Rods in your thumbs. Table in your pocket.") is placeholder store copy
written to fill the feature graphic. Replace it before the listing goes live.
