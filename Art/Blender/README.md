# Ball skins

Nine ball cosmetics, authored as **equirectangular textures on the game's existing ball mesh**.

## Why there is no geometry here

The ball inside `Assets/Models/FoosballTable.fbx` is a 222-vertex UV sphere of radius **0.0165**,
which is exactly the radius of the `SphereCollider` on the scene's Ball. Its unwrap was measured to
be a clean lat-long map: pole axis **Y**, 13 latitude rings, `V = asin(y/R)/pi + 0.5` to five
decimals, with a duplicated seam column at `U = 0/1`.

So a skin is a **material**, not a model. The mesh and the collider are both left alone, which is
what makes "the same size as the normal ball" true by construction rather than by matching numbers —
and since the collider does the physics while the mesh is only drawn, no skin can change how the
ball plays.

## Regenerating

```bash
BL="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
P="C:/Unity-projects/Mini-Football"

# 1. textures -> Assets/Resources/BallSkins
"$BL" --background --factory-startup --python Art/Blender/make_ball_textures.py -- \
  "$P/Assets/Resources/BallSkins"

# 2. verify colour round-trip, packing and seam continuity
"$BL" --background --factory-startup --python Art/Blender/verify_ball_textures.py -- \
  "$P/Assets/Resources/BallSkins"

# 3. rebuild the source .blend and the preview sheet
"$BL" --background --factory-startup --python Art/Blender/make_ball_blend.py -- \
  "$P/Assets/Models/FoosballTable.fbx" "$P/Assets/Resources/BallSkins" \
  "$P/Art/Blender/BallSkins.blend" "$P/Art/BallSkins_preview.png"
```

**Always run step 2.** Two real bugs were caught by it and by nothing else: the albedo was being
written linearised (`#CE2233` came back as `(156,4,8)` — Blender stores 8-bit pixels as given and
applies no transform on save), and the soccer seams were fat enough that the ball read as grey.

## Traps that cost time here

- `image.pixels.foreach_set` rejects float64. Cast to float32.
- The ball mesh's vertices are **not centred on its object origin** — the centroid is
  `(0.587, 76.1, -0.011)`, the ball's height on the table baked into the vertex data. Rotating the
  object swings the geometry on a 76-unit arm and out of frame, while `location`, `scale` and
  `dimensions` all still report correct values. `make_ball_blend.py` recentres the mesh data first
  and then asserts the result.

## Import settings

`_Albedo` maps are sRGB. `_MetalSmooth` maps must have **sRGB off** — they are data (R = metallic,
A = smoothness, URP's packing), and gamma-decoding them makes gold and disco wrong.
