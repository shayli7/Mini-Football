import bpy, sys, numpy as np
OUT = sys.argv[sys.argv.index("--") + 1]

def load_raw(path):
    """Load forcing Non-Color so pixels come back as the stored bytes/255, with no transform."""
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    return buf.reshape(h, w, 4), w, h

checks = [
    ("BallSkin_SolidRed_Albedo.png",    "expect ~ #CE2233 (206, 34, 51)"),
    ("BallSkin_SolidBlue_Albedo.png",   "expect ~ #1F5FD0 (31, 95, 208)"),
    ("BallSkin_RetroOrange_Albedo.png", "expect ~ #E2661B (226, 102, 27)"),
    ("BallSkin_Soccer_Albedo.png",      "white field ~ (242,242,238), pentagons ~ (25,25,25)"),
]
print("\n===== ALBEDO ROUND-TRIP (bytes as stored) =====")
for name, note in checks:
    px, w, h = load_raw(f"{OUT}/{name}")
    mid = px[h // 2, :, :3]
    mean = (mid.mean(axis=0) * 255).round(1)
    lo = (px[..., :3].min() * 255).round(1)
    hi = (px[..., :3].max() * 255).round(1)
    print(f"{name}")
    print(f"   equator mean RGB = {tuple(mean)}   full range [{lo}, {hi}]")
    print(f"   {note}")

print("\n===== SOCCER COVERAGE (pentagons should be ~12-18% of area, weighted by cos-lat) =====")
px, w, h = load_raw(f"{OUT}/BallSkin_Soccer_Albedo.png")
lum = px[..., :3].mean(axis=2)
v = (np.arange(h) + 0.5) / h
weight = np.cos((v - 0.5) * np.pi)[:, None]
dark = (lum < 0.25).astype(float)
frac = float((dark * weight).sum() / (np.ones_like(dark) * weight).sum())
print(f"   dark (pentagon+seam) area fraction = {frac*100:.1f}%")

print("\n===== METALSMOOTH PACKING (R=metallic, A=smoothness, linear) =====")
for name in ("BallSkin_Disco_MetalSmooth.png", "BallSkin_Ice_MetalSmooth.png",
             "BallSkin_Golden_MetalSmooth.png"):
    px, w, h = load_raw(f"{OUT}/{name}")
    r, a = px[..., 0], px[..., 3]
    print(f"{name}: metallic[min={r.min():.2f} max={r.max():.2f} mean={r.mean():.2f}]  "
          f"smoothness[min={a.min():.2f} max={a.max():.2f} mean={a.mean():.2f}]")

print("\n===== SEAM CONTINUITY (u=0 vs u=1 column must match) =====")
for name in ("BallSkin_Soccer_Albedo.png", "BallSkin_Camo_Albedo.png",
             "BallSkin_Basketball_Albedo.png"):
    px, w, h = load_raw(f"{OUT}/{name}")
    d = np.abs(px[:, 0, :3] - px[:, -1, :3]).max() * 255
    print(f"   {name}: max |first col - last col| = {d:.1f}/255")
print("DONE")
