"""
Generates the figure (player) skins for Mini Football.

WHY THIS BAKES RATHER THAN PAINTS
The figure's unwrap is a scattered multi-island layout — neighbouring points on the body are not
neighbours in UV. Painting a stripe in UV space would scatter it over the model as unrelated
patches. So this rasterises every triangle INTO uv space and evaluates the pattern in 3D at each
texel: vertical stripes are a function of the angle around the body, armour bands a function of
height. The pattern then lands correctly whatever the unwrap does.

Each figure has three material slots, and each uses the full 0..1 uv square independently, so each
gets its own texture:
    slot 0  Foos_PlayerWood   46% of the surface — the body
    slot 1  Foos_Blue/Red     45% — the kit, which is what tells the teams apart
    slot 2  Foos_Skin          9% — the head

The kit is authored per team (a _Red and a _Blue) rather than tinted from one greyscale map,
because a multiply tint can never produce WHITE stripes on a red shirt — white x red is red. The
material tint is left at white and the texture carries the team colour.

Run: blender --background --factory-startup --python make_figure_textures.py -- <fbx> <outdir>
"""
import bpy
import sys
import math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, OUT = argv[0], argv[1]
SIZE = 512

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)


def hexcol(s):
    s = s.lstrip("#")
    return np.array([int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


# ---------------------------------------------------------------- the baker

def bake_attributes(obj, slot):
    """Rasterise one material slot's triangles into uv space.

    Returns (mask, h, ang, r, nz): coverage, normalised height 0..1, angle around the body's
    vertical axis in degrees, normalised radius, and the surface normal's z.
    """
    me = obj.data
    me.calc_loop_triangles()

    # WORLD space, not local. The figure's local up axis is not Z — its object rotation is what
    # stands it upright — so height taken from local z is really a horizontal axis and the angle is
    # measured in the wrong plane. Baked that way the stripes came out as wedges across the
    # shoulders and the armour plates as vertical streaks. In world space Z is up (measured: the
    # table's field is thin in Z and the figures stand above it).
    mw = obj.matrix_world
    nm = mw.to_3x3().inverted().transposed()
    co = np.array([(mw @ v.co)[:] for v in me.vertices])
    zlo, zhi = co[:, 2].min(), co[:, 2].max()
    cx, cy = (co[:, 0].min() + co[:, 0].max()) / 2, (co[:, 1].min() + co[:, 1].max()) / 2
    rmax = np.sqrt((co[:, 0] - cx) ** 2 + (co[:, 1] - cy) ** 2).max()

    uvl = me.uv_layers[0].data
    mask = np.zeros((SIZE, SIZE), dtype=bool)
    P = np.zeros((SIZE, SIZE, 3))
    N = np.zeros((SIZE, SIZE, 3))

    for tri in me.loop_triangles:
        if tri.material_index != slot:
            continue
        uvs = np.array([uvl[li].uv[:] for li in tri.loops]) * (SIZE - 1)
        pts = np.array([(mw @ me.vertices[vi].co)[:] for vi in tri.vertices])
        nrm = np.array((nm @ tri.normal).normalized()[:])

        x0 = max(0, int(np.floor(uvs[:, 0].min()))); x1 = min(SIZE - 1, int(np.ceil(uvs[:, 0].max())))
        y0 = max(0, int(np.floor(uvs[:, 1].min()))); y1 = min(SIZE - 1, int(np.ceil(uvs[:, 1].max())))
        if x1 < x0 or y1 < y0:
            continue

        xs = np.arange(x0, x1 + 1)
        ys = np.arange(y0, y1 + 1)
        gx, gy = np.meshgrid(xs, ys)

        # Barycentric coordinates. A degenerate (zero-area) uv triangle is skipped rather than
        # dividing by zero — a few always exist on a mesh this small.
        ax, ay = uvs[0]; bx, by = uvs[1]; cxx, cyy = uvs[2]
        den = (by - cyy) * (ax - cxx) + (cxx - bx) * (ay - cyy)
        if abs(den) < 1e-9:
            continue
        w0 = ((by - cyy) * (gx - cxx) + (cxx - bx) * (gy - cyy)) / den
        w1 = ((cyy - ay) * (gx - cxx) + (ax - cxx) * (gy - cyy)) / den
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -0.002) & (w1 >= -0.002) & (w2 >= -0.002)
        if not inside.any():
            continue

        pos = (w0[..., None] * pts[0] + w1[..., None] * pts[1] + w2[..., None] * pts[2])
        sub = (gy[inside], gx[inside])
        mask[sub] = True
        P[sub] = pos[inside]
        N[sub] = nrm

    h = np.clip((P[..., 2] - zlo) / max(1e-9, zhi - zlo), 0, 1)
    ang = (np.degrees(np.arctan2(P[..., 1] - cy, P[..., 0] - cx)) + 360.0) % 360.0
    r = np.sqrt((P[..., 0] - cx) ** 2 + (P[..., 1] - cy) ** 2) / max(1e-9, rmax)
    return mask, h, ang, r, N[..., 2]


def dilate(rgb, mask, rounds=6):
    """Bleed colour outward past the island edges so bilinear filtering never samples empty
    background and draws a dark seam around every island."""
    out = rgb.copy()
    filled = mask.copy()
    for _ in range(rounds):
        nxt = filled.copy()
        acc = np.zeros_like(out)
        cnt = np.zeros(out.shape[:2])
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            s = np.roll(np.roll(out, dy, 0), dx, 1)
            m = np.roll(np.roll(filled, dy, 0), dx, 1)
            acc += s * m[..., None]
            cnt += m
        new = (~filled) & (cnt > 0)
        out[new] = acc[new] / cnt[new][..., None]
        nxt |= new
        filled = nxt
    return out


def write(name, rgb):
    data = np.clip(np.asarray(rgb, dtype=np.float64), 0, 1)
    buf = np.empty((SIZE, SIZE, 4))
    buf[..., :3] = data
    buf[..., 3] = 1.0
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "sRGB"
    img.alpha_mode = "CHANNEL_PACKED"
    img.pixels.foreach_set(np.ascontiguousarray(buf, dtype=np.float32).ravel())
    img.filepath_raw = f"{OUT}/{name}.png"
    img.file_format = "PNG"
    img.save()
    print(f"  wrote {name}.png")


# ---------------------------------------------------------------- pattern helpers

def band(ang, count, duty=0.5):
    """Vertical stripes: `count` bands around the body."""
    return ((ang / (360.0 / count)) % 1.0) < duty


def noise(shape, seed, scale=0.06):
    rng = np.random.default_rng(seed)
    return 1.0 + (rng.random(shape) - 0.5) * 2 * scale


TEAMS = {"Red": hexcol("#B4262E"), "Blue": hexcol("#1E43B0")}
SKIN = hexcol("#DAAE7B")

# ================================================================= skin definitions
# Each entry: kit(h, ang, r, nz, team) -> rgb, body(...) -> rgb or None, head(...) -> rgb or None.
# Returning None for a slot means "no texture; use a flat material colour instead".


def kit_stripes(h, ang, r, nz, team):
    white = hexcol("#F0F0EC")
    col = TEAMS[team]
    stripe = band(ang, 8, 0.5)
    rgb = np.where(stripe[..., None], col[None, None, :], white[None, None, :])
    # Shorts below the shirt line: plain white, the classic kit.
    shorts = h < 0.42
    rgb = np.where(shorts[..., None], white[None, None, :] * 0.94, rgb)
    # Socks lower still, back to team colour.
    socks = h < 0.22
    rgb = np.where(socks[..., None], col[None, None, :] * 0.9, rgb)
    return rgb * noise(rgb.shape[:2], 7)[..., None]


def kit_gladiator(h, ang, r, nz, team):
    bronze = hexcol("#B08339")
    dark = hexcol("#4A3518")
    col = TEAMS[team]

    # Team colour is the DEFAULT here, with the armour laid over the middle of the torso. The
    # first pass put the team sash below h 0.45, which is the band the body covers — so red and
    # blue gladiators came out identical and the sides could not be told apart. Only the 0.50-0.85
    # band is really visible on this figure, so anything carrying team identity has to live there.
    rgb = np.repeat(np.repeat(col[None, None, :], h.shape[0], 0), h.shape[1], 1).copy()

    # Lorica segmentata: horizontal plates, banded across the chest only.
    chest = (h > 0.55) & (h < 0.80)
    plate = ((h * 30) % 1.0) < 0.72
    armour = np.where(plate[..., None], bronze[None, None, :], dark[None, None, :])
    rgb = np.where(chest[..., None], armour, rgb)

    # A bronze belt under the plates, so the team colour below reads as a tunic rather than a gap.
    belt = (h > 0.50) & (h < 0.55)
    rgb = np.where(belt[..., None], bronze[None, None, :] * 0.8, rgb)
    return rgb * noise(rgb.shape[:2], 8, 0.10)[..., None]


def kit_robot(h, ang, r, nz, team):
    chrome = hexcol("#C9D2DA")
    seam = hexcol("#3A424C")
    col = TEAMS[team]
    rgb = np.repeat(np.repeat(chrome[None, None, :], h.shape[0], 0), h.shape[1], 1).copy()
    # Panel seams both ways, so it reads as machined plate rather than painted metal.
    s = (((h * 22) % 1.0) < 0.10) | (((ang / 30.0) % 1.0) < 0.09)
    rgb = np.where(s[..., None], seam[None, None, :], rgb)
    # A glowing team core across the chest. Wide, because this is the only thing separating a red
    # robot from a blue one and it has to survive being seen at about sixty pixels tall.
    core = (h > 0.56) & (h < 0.74)
    rgb = np.where(core[..., None], col[None, None, :] * 1.7, rgb)
    return np.clip(rgb * noise(rgb.shape[:2], 9, 0.05)[..., None], 0, 1)


def kit_trojan(h, ang, r, nz, team):
    gold = hexcol("#D9A72C")
    col = TEAMS[team]
    rgb = np.repeat(np.repeat(col[None, None, :], h.shape[0], 0), h.shape[1], 1).copy()
    # Gold breastplate over the chest, gold hem at the bottom of the tunic.
    breast = (h > 0.52) & (h < 0.78)
    rgb = np.where(breast[..., None], gold[None, None, :], rgb)
    hem = (h > 0.34) & (h < 0.40)
    rgb = np.where(hem[..., None], gold[None, None, :] * 0.9, rgb)
    return rgb * noise(rgb.shape[:2], 10, 0.08)[..., None]


def kit_trojan_keeper(h, ang, r, nz, team):
    """The keeper is a knight: steel plate instead of the tunic, so he reads as a different
    figure at a glance even though he shares the team's colour."""
    steel = hexcol("#AEB6BF")
    dark = hexcol("#4A5058")
    col = TEAMS[team]
    plate = ((h * 18) % 1.0) < 0.80
    rgb = np.where(plate[..., None], steel[None, None, :], dark[None, None, :])
    belt = (h > 0.42) & (h < 0.48)
    rgb = np.where(belt[..., None], col[None, None, :], rgb)
    return rgb * noise(rgb.shape[:2], 11, 0.07)[..., None]


def body_gladiator(h, ang, r, nz):
    leather = hexcol("#6B4A2A")
    return np.repeat(np.repeat(leather[None, None, :], h.shape[0], 0), h.shape[1], 1) \
        * noise(h.shape, 21, 0.12)[..., None]


def body_robot(h, ang, r, nz):
    metal = hexcol("#8E98A4")
    rgb = np.repeat(np.repeat(metal[None, None, :], h.shape[0], 0), h.shape[1], 1).copy()
    s = ((h * 16) % 1.0) < 0.12
    rgb = np.where(s[..., None], hexcol("#454C55")[None, None, :], rgb)
    return rgb


def body_trojan(h, ang, r, nz):
    bronze = hexcol("#8A6520")
    return np.repeat(np.repeat(bronze[None, None, :], h.shape[0], 0), h.shape[1], 1) \
        * noise(h.shape, 23, 0.10)[..., None]


def head_helmet_gold(h, ang, r, nz):
    """Golden helmet over the top of the head, face left as skin below the brow."""
    gold = hexcol("#E3B733")
    rgb = np.repeat(np.repeat(SKIN[None, None, :], h.shape[0], 0), h.shape[1], 1).copy()
    helm = h > 0.56
    rgb = np.where(helm[..., None], gold[None, None, :], rgb)
    return rgb


def head_helmet_steel(h, ang, r, nz):
    steel = hexcol("#B9C1C9")
    rgb = np.repeat(np.repeat(SKIN[None, None, :], h.shape[0], 0), h.shape[1], 1).copy()
    helm = h > 0.50
    rgb = np.where(helm[..., None], steel[None, None, :], rgb)
    # A dark visor slit across the face.
    visor = (h > 0.56) & (h < 0.63)
    rgb = np.where(visor[..., None], hexcol("#23272C")[None, None, :], rgb)
    return rgb


def head_robot(h, ang, r, nz):
    chrome = hexcol("#C9D2DA")
    rgb = np.repeat(np.repeat(chrome[None, None, :], h.shape[0], 0), h.shape[1], 1).copy()
    visor = (h > 0.48) & (h < 0.66)
    rgb = np.where(visor[..., None], hexcol("#18E0D8")[None, None, :], rgb)
    return rgb


def head_gladiator(h, ang, r, nz):
    bronze = hexcol("#9C7233")
    rgb = np.repeat(np.repeat(SKIN[None, None, :], h.shape[0], 0), h.shape[1], 1).copy()
    helm = h > 0.60
    rgb = np.where(helm[..., None], bronze[None, None, :], rgb)
    return rgb


SKINS = [
    # name,          kit fn,             body fn,          head fn
    ("Stripes",      kit_stripes,        None,             None),
    ("Gladiators",   kit_gladiator,      body_gladiator,   head_gladiator),
    ("Robots",       kit_robot,          body_robot,       head_robot),
    ("Trojan",       kit_trojan,         body_trojan,      head_helmet_gold),
    ("TrojanKeeper", kit_trojan_keeper,  body_trojan,      head_helmet_steel),
]


def main():
    fig = bpy.data.objects["FigB.001"]
    print(f"\n===== baking figure skins at {SIZE}x{SIZE} =====")

    slots = {}
    for si, label in ((0, "Body"), (1, "Kit"), (2, "Head")):
        slots[label] = bake_attributes(fig, si)
        m = slots[label][0]
        print(f"  slot {si} ({label}): {m.sum()} texels covered ({m.mean()*100:.1f}%)")
        assert m.sum() > 500, f"slot {si} baked almost nothing — uv/rasterise problem"

    for name, kit_fn, body_fn, head_fn in SKINS:
        mask, h, ang, r, nz = slots["Kit"]
        for team in ("Red", "Blue"):
            rgb = kit_fn(h, ang, r, nz, team)
            rgb = np.where(mask[..., None], rgb, 0.0)
            write(f"FigureSkin_{name}_Kit{team}", dilate(rgb, mask))

        if body_fn is not None:
            mask, h, ang, r, nz = slots["Body"]
            rgb = np.where(mask[..., None], body_fn(h, ang, r, nz), 0.0)
            write(f"FigureSkin_{name}_Body", dilate(rgb, mask))

        if head_fn is not None:
            mask, h, ang, r, nz = slots["Head"]
            rgb = np.where(mask[..., None], head_fn(h, ang, r, nz), 0.0)
            write(f"FigureSkin_{name}_Head", dilate(rgb, mask))

    print("DONE")


main()
