"""
Generates the nine ball-skin texture sets for Mini Football, as equirectangular maps
matched to the EXISTING ball mesh in FoosballTable.fbx.

Why equirectangular, and why no new geometry: the ball in the FBX is a 222-vert UV sphere of
radius 0.0165 whose unwrap was measured to be a clean lat-long map — pole axis Y, 13 latitude
rings, V = asin(y/R)/pi + 0.5 exactly, with a proper duplicated seam column at U = 0/1. So a
skin is a MATERIAL, not a model: the mesh and the SphereCollider both stay exactly as they are,
which is what makes "the same size as the normal ball" true by construction rather than by
careful matching.

Mapping used for every texel (must stay in step with the mesh):
    v in [0,1]  ->  latitude  phi = (v - 0.5) * pi      (v=0 south pole, v=1 north pole)
    u in [0,1]  ->  longitude theta = u * 2pi
    dir = (cos(phi)cos(theta), sin(phi), cos(phi)sin(theta))

Outputs, into Assets/Resources/BallSkins/:
    BallSkin_<Name>_Albedo.png        sRGB   RGBA
    BallSkin_<Name>_MetalSmooth.png   linear RGBA, R = metallic, A = smoothness (URP packing)

Run:  blender --background --factory-startup --python make_ball_textures.py -- <outdir>
"""
import bpy
import sys
import math
import numpy as np

OUT = sys.argv[sys.argv.index("--") + 1]
W, H = 1024, 512

# ---------------------------------------------------------------- colour helpers

def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def hexcol(s):
    s = s.lstrip("#")
    return np.array([int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


# ---------------------------------------------------------------- direction field

def directions():
    """Unit direction per texel, shaped (H, W, 3). Row 0 is v=0 (south pole), matching
    Blender's bottom-up pixel order."""
    v = (np.arange(H) + 0.5) / H
    u = (np.arange(W) + 0.5) / W
    phi = (v - 0.5) * math.pi
    theta = u * 2.0 * math.pi
    cp = np.cos(phi)[:, None]
    sp = np.sin(phi)[:, None]
    ct = np.cos(theta)[None, :]
    st = np.sin(theta)[None, :]
    d = np.empty((H, W, 3))
    d[..., 0] = cp * ct
    d[..., 1] = np.broadcast_to(sp, (H, W))
    d[..., 2] = cp * st
    return d


D = directions()

# ---------------------------------------------------------------- 3D value noise
# Sampled by DIRECTION, so it is seamless on the sphere by construction — there is no
# u-wrap or pole to special-case, which is the failure mode of 2D noise on an equirect map.

LAT = 64


def _lattice(seed):
    rng = np.random.default_rng(seed)
    return rng.random((LAT, LAT, LAT))


def _smooth(t):
    return t * t * (3.0 - 2.0 * t)


def noise3(p, seed):
    g = _lattice(seed)
    x, y, z = p[..., 0], p[..., 1], p[..., 2]
    xi, yi, zi = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64), np.floor(z).astype(np.int64)
    xf, yf, zf = _smooth(x - xi), _smooth(y - yi), _smooth(z - zi)
    xi %= LAT; yi %= LAT; zi %= LAT
    x1, y1, z1 = (xi + 1) % LAT, (yi + 1) % LAT, (zi + 1) % LAT

    def s(a, b, c):
        return g[a, b, c]

    c00 = s(xi, yi, zi) * (1 - xf) + s(x1, yi, zi) * xf
    c10 = s(xi, y1, zi) * (1 - xf) + s(x1, y1, zi) * xf
    c01 = s(xi, yi, z1) * (1 - xf) + s(x1, yi, z1) * xf
    c11 = s(xi, y1, z1) * (1 - xf) + s(x1, y1, z1) * xf
    c0 = c00 * (1 - yf) + c10 * yf
    c1 = c01 * (1 - yf) + c11 * yf
    return c0 * (1 - zf) + c1 * zf


def fbm(freq, octaves, seed, gain=0.5):
    total = np.zeros((H, W))
    amp = 1.0
    norm = 0.0
    for o in range(octaves):
        total += amp * noise3(D * (freq * (2 ** o)), seed + o * 101)
        norm += amp
        amp *= gain
    return total / norm


# ---------------------------------------------------------------- icosahedron (soccer ball)

def icosahedron():
    t = (1.0 + math.sqrt(5.0)) / 2.0
    raw = []
    for s1 in (-1, 1):
        for s2 in (-1, 1):
            raw += [(0, s1 * 1, s2 * t), (s1 * 1, s2 * t, 0), (s2 * t, 0, s1 * 1)]
    v = np.array(sorted(set(raw)), dtype=np.float64)
    v /= np.linalg.norm(v, axis=1)[:, None]
    # Adjacent vertices of an icosahedron sit at dot = 1/sqrt(5); the rest are further.
    edges = []
    for i in range(len(v)):
        for j in range(i + 1, len(v)):
            if abs(float(v[i] @ v[j]) - 1.0 / math.sqrt(5.0)) < 1e-6:
                edges.append((i, j))
    return v, edges


# ---------------------------------------------------------------- writing

def write(name, rgb, alpha=None, srgb=True):
    """rgb is (H,W,3) in DISPLAY space when srgb=True, raw otherwise."""
    data = np.clip(np.asarray(rgb, dtype=np.float64), 0.0, 1.0)
    # Written in DISPLAY space, not linearised. For an 8-bit image Blender stores what it is
    # given and applies no transform on save — the colorspace tag below only tells readers how
    # to interpret the bytes. Linearising first was measured to darken #CE2233 to (156,4,8):
    # srgb_to_linear(206/255)*255 = 156, exactly the value that came back out of the file.
    px = data
    a = np.ones((H, W)) if alpha is None else np.clip(alpha, 0.0, 1.0)

    buf = np.empty((H, W, 4))
    buf[..., :3] = px
    buf[..., 3] = a

    img = bpy.data.images.new(name, W, H, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    # Keeps alpha an independent channel — it carries smoothness, not transparency, and any
    # premultiply would silently corrupt both it and the colour.
    img.alpha_mode = "CHANNEL_PACKED"
    # foreach_set wants single precision and a contiguous buffer; float64 is rejected outright.
    img.pixels.foreach_set(np.ascontiguousarray(buf, dtype=np.float32).ravel())
    img.filepath_raw = f"{OUT}/{name}.png"
    img.file_format = "PNG"
    img.save()
    print(f"  wrote {name}.png")


def solid(color, scuff_amt=0.06, seed=1, freq=7.0):
    """A plain ball: flat colour, lifted off 'flat plastic' by a faint 3D scuff."""
    base = hexcol(color)
    n = fbm(freq, 3, seed)
    shade = 1.0 + (n - 0.5) * 2.0 * scuff_amt
    return np.clip(base[None, None, :] * shade[..., None], 0, 1)


# ================================================================= the nine skins

def retro_orange():
    # Mottled, slightly faded — the look of a well-used table ball rather than new plastic.
    rgb = solid("#E2661B", scuff_amt=0.10, seed=11, freq=5.0)
    blotch = fbm(2.5, 2, 77)
    rgb = rgb * (1.0 + (blotch[..., None] - 0.5) * 0.10)
    return np.clip(rgb, 0, 1), None


def solid_red():
    return solid("#CE2233", scuff_amt=0.06, seed=21), None


def solid_blue():
    return solid("#1F5FD0", scuff_amt=0.06, seed=31), None


def soccer():
    """Truncated icosahedron: 12 black pentagons on the icosahedron's vertices, white
    hexagons between them, and seam lines along the icosahedron's 30 edges — which are
    exactly the hexagon/hexagon boundaries of the real solid."""
    v, edges = icosahedron()
    flat = D.reshape(-1, 3)

    dots = flat @ v.T                      # (N, 12)
    nearest = np.argmax(dots, axis=1)
    ang = np.arccos(np.clip(np.max(dots, axis=1), -1, 1))

    # Pentagon orientation: its EDGES face the five neighbouring vertices, so a pentagon
    # vertex sits 36 degrees around from each neighbour direction.
    apothem = math.radians(17.6)
    inside = np.zeros(flat.shape[0], dtype=bool)
    for i in range(len(v)):
        sel = nearest == i
        if not sel.any():
            continue
        c = v[i]
        nb = sorted(range(len(v)), key=lambda j: -float(c @ v[j]))[1]
        e1 = v[nb] - c * float(c @ v[nb])
        e1 /= np.linalg.norm(e1)
        e2 = np.cross(c, e1)
        p = flat[sel]
        a = np.arctan2(p @ e2, p @ e1)
        # Fold into one 72-degree wedge, then a pentagon is r < apothem / cos(angle).
        wedge = np.abs(((a + math.pi / 5) % (2 * math.pi / 5)) - math.pi / 5)
        inside[sel] = ang[sel] < apothem / np.cos(wedge)

    # Seams along the icosahedron edge arcs.
    seam = np.zeros(flat.shape[0], dtype=bool)
    # Thin. At 1.5 degrees the 30 seam arcs covered ~10% of the sphere on their own and the ball
    # read as dark grey; the 12 pentagons are already ~28% of the surface, which is correct for a
    # real truncated icosahedron and is as black as it should ever get.
    half = math.radians(0.7)
    for i, j in edges:
        n = np.cross(v[i], v[j])
        n /= np.linalg.norm(n)
        dist = np.abs(np.arcsin(np.clip(flat @ n, -1, 1)))
        between = (flat @ v[i] > 0.35) & (flat @ v[j] > 0.35)
        seam |= (dist < half) & between

    white = hexcol("#F2F2EE")
    black = hexcol("#191919")
    rgb = np.where(inside[:, None], black[None, :], white[None, :])
    rgb = np.where(seam[:, None] & ~inside[:, None], hexcol("#2A2A2A")[None, :], rgb)
    rgb = rgb.reshape(H, W, 3)

    grain = fbm(9.0, 2, 5)
    rgb = np.clip(rgb * (1.0 + (grain[..., None] - 0.5) * 0.07), 0, 1)
    return rgb, None


def basketball():
    """Eight-panel seam layout: equator, one meridian, and the two 'parenthesis' curves,
    over a pebbled orange."""
    x, y, z = D[..., 0], D[..., 1], D[..., 2]
    t = math.radians(1.9)

    equator = np.abs(np.arcsin(np.clip(y, -1, 1))) < t
    meridian = np.abs(np.arcsin(np.clip(x, -1, 1))) < t
    # Two small circles centred on +/-Z give the curved seams either side of the meridian.
    c = math.radians(52.0)
    curve = (np.abs(np.arccos(np.clip(z, -1, 1)) - c) < t) | \
            (np.abs(np.arccos(np.clip(-z, -1, 1)) - c) < t)

    seam = equator | meridian | curve

    base = hexcol("#C4551C")
    pebble = fbm(70.0, 2, 9)
    rgb = base[None, None, :] * (1.0 + (pebble[..., None] - 0.5) * 0.22)
    blotch = fbm(3.0, 2, 12)
    rgb *= (1.0 + (blotch[..., None] - 0.5) * 0.10)
    rgb = np.where(seam[..., None], hexcol("#14100E")[None, None, :], rgb)
    return np.clip(rgb, 0, 1), None


def camo():
    """Four-tone blob camouflage from banded 3D noise — seamless because the noise is
    sampled by direction rather than by uv."""
    n = fbm(3.2, 4, 41)
    n = (n - n.min()) / (n.max() - n.min())
    cols = [hexcol("#2F3A22"), hexcol("#4E5C31"), hexcol("#7D8253"), hexcol("#3A2E20")]
    idx = np.clip((n * 4).astype(int), 0, 3)
    rgb = np.stack([np.choose(idx, [c[k] for c in cols]) for k in range(3)], axis=-1)
    grain = fbm(24.0, 2, 43)
    return np.clip(rgb * (1.0 + (grain[..., None] - 0.5) * 0.12), 0, 1), None


def disco():
    """A mirror-tile ball. Tile columns thin out toward the poles so the tiles stay roughly
    square instead of collapsing into slivers, which is what a real disco ball does."""
    v = (np.arange(H) + 0.5) / H
    u = (np.arange(W) + 0.5) / W
    phi = (v - 0.5) * math.pi
    rows = 26
    row_idx = np.floor(v * rows).astype(int)[:, None]
    cols = np.maximum(4, np.round(52 * np.cos(phi)).astype(int))[:, None]
    col_idx = np.floor(u[None, :] * cols).astype(int)

    fu = u[None, :] * cols - col_idx
    fv = (v[:, None] * rows) - row_idx
    grout = (np.minimum(fu, 1 - fu) < 0.09) | (np.minimum(fv, 1 - fv) < 0.09)

    rng = np.random.default_rng(7)
    jitter = rng.random((rows + 2, 64))[row_idx % (rows + 2), col_idx % 64]

    tile = hexcol("#C9D6E4")[None, None, :] * (0.72 + 0.42 * jitter[..., None])
    rgb = np.where(grout[..., None], hexcol("#20262E")[None, None, :], tile)

    metallic = np.where(grout, 0.0, 1.0)
    smooth = np.where(grout, 0.15, 0.80 + 0.18 * jitter)
    return np.clip(rgb, 0, 1), (metallic, smooth)


def ice():
    """Frosted crystalline ball: Voronoi facets on the sphere, cool blue, with the cell
    borders frosted brighter. NOTE: a literal cube would need new geometry and would break
    the shared sphere radius, so this is an ice-faceted BALL — see the report."""
    rng = np.random.default_rng(3)
    pts = rng.normal(size=(70, 3))
    pts /= np.linalg.norm(pts, axis=1)[:, None]

    flat = D.reshape(-1, 3)
    dots = flat @ pts.T
    part = np.argpartition(-dots, 2, axis=1)[:, :2]
    top = np.take_along_axis(dots, part, axis=1)
    order = np.argsort(-top, axis=1)
    f1 = np.take_along_axis(top, order[:, :1], axis=1)[:, 0]
    f2 = np.take_along_axis(top, order[:, 1:2], axis=1)[:, 0]
    cell = np.take_along_axis(part, order[:, :1], axis=1)[:, 0]

    edge = np.clip((f1 - f2) / 0.035, 0, 1)          # 0 on a cell border, 1 deep inside
    shade = 0.80 + 0.30 * rng.random(len(pts))[cell]

    base = hexcol("#BBDDF2")
    rgb = base[None, :] * shade[:, None]
    rgb = rgb * (0.86 + 0.14 * edge)[:, None] + (1 - edge)[:, None] * 0.16
    rgb = rgb.reshape(H, W, 3)

    frost = fbm(30.0, 3, 61)
    rgb = np.clip(rgb * (1.0 + (frost[..., None] - 0.5) * 0.16), 0, 1)

    smooth = (0.62 + 0.26 * edge.reshape(H, W)) * (0.9 + 0.1 * frost)
    metallic = np.zeros((H, W))
    return rgb, (metallic, np.clip(smooth, 0, 1))


def golden():
    """Polished trophy gold: near-uniform metal, lifted by fine anisotropic-looking
    micro-scratches so it catches the light instead of reading as a flat yellow ball."""
    scratch = fbm(90.0, 2, 71)
    swirl = fbm(6.0, 3, 73)
    base = hexcol("#F2C042")
    rgb = base[None, None, :] * (1.0 + (swirl[..., None] - 0.5) * 0.16)
    rgb = rgb * (1.0 + (scratch[..., None] - 0.5) * 0.10)

    metallic = np.ones((H, W))
    smooth = np.clip(0.86 + (scratch - 0.5) * 0.22 + (swirl - 0.5) * 0.08, 0, 1)
    return np.clip(rgb, 0, 1), (metallic, smooth)


SKINS = [
    ("RetroOrange", retro_orange),
    ("SolidRed", solid_red),
    ("SolidBlue", solid_blue),
    ("Soccer", soccer),
    ("Basketball", basketball),
    ("Camo", camo),
    ("Disco", disco),
    ("Ice", ice),
    ("Golden", golden),
]


def main():
    print(f"\n===== generating {len(SKINS)} ball skins at {W}x{H} =====")
    for name, fn in SKINS:
        rgb, ms = fn()
        assert rgb.shape == (H, W, 3), f"{name}: bad shape {rgb.shape}"
        assert np.isfinite(rgb).all(), f"{name}: non-finite albedo"
        write(f"BallSkin_{name}_Albedo", rgb, srgb=True)
        if ms is not None:
            metallic, smooth = ms
            packed = np.stack([metallic, metallic, metallic], axis=-1)
            write(f"BallSkin_{name}_MetalSmooth", packed, alpha=smooth, srgb=False)
    print("DONE")


main()
