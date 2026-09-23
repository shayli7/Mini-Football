"""
Generates the six field (table-top) skins for Mini Football.

Matched to the EXISTING Field mesh in FoosballTable.fbx, which is a 6-poly box whose two large
faces each own about half the texture:

    face normal (0,-1,0) -> u[0.003, 0.475] v[0.003, 0.860]
    face normal (0,+1,0) -> u[0.481, 0.952] v[0.003, 0.860]

Both rects are the same shape (0.471 x 0.857 uv), and 0.857/0.471 = 1.82 = 1.2/0.66, the pitch's
real aspect — so within a rect the table's LONG axis runs along V and the short axis along U.

The pattern is painted into BOTH rects. Only one of them is the playing surface, but they are
indistinguishable in shape and painting both means the skin cannot come out upside down or land on
the underside if the model is ever re-exported with the faces swapped. Everything outside the rects
is flooded with the skin's base colour so the box's thin edges pick up a sensible tone instead of
black.

1024x1024 to match the existing FB_Field.png exactly. That is ~730 px per metre; bump SIZE to 2048
if the pattern reads soft on device.

The white pitch markings (centre circle, corner pips) are SEPARATE geometry with their own
Foos_Line material and are not drawn here — they will still be laid over whatever skin is worn.

Run: blender --background --factory-startup --python make_field_textures.py -- <outdir>
"""
import bpy
import sys
import numpy as np

OUT = sys.argv[sys.argv.index("--") + 1]
SIZE = 1024

# The two large faces, with a small outward bleed so filtering at the rect edge never samples the
# flood colour and leaves a seam along the pitch boundary.
BLEED = 0.004
RECTS = [
    (0.003 - BLEED, 0.475 + BLEED, 0.003 - BLEED, 0.860 + BLEED),
    (0.481 - BLEED, 0.952 + BLEED, 0.003 - BLEED, 0.860 + BLEED),
]

PITCH_LONG = 1.20   # metres, runs along V within a rect
PITCH_SHORT = 0.66  # metres, runs along U


def hexcol(s):
    s = s.lstrip("#")
    return np.array([int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


# ---------------------------------------------------------------- 2D value noise

LAT = 256


def _smooth(t):
    return t * t * (3.0 - 2.0 * t)


def noise2(x, y, seed):
    """Value noise over arbitrary 2D coordinates. Not tiled — the pattern lives inside a rect and
    never wraps, so a lattice big enough to cover it is all that is needed."""
    rng = np.random.default_rng(seed)
    g = rng.random((LAT, LAT))
    xi, yi = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    xf, yf = _smooth(x - xi), _smooth(y - yi)
    xi %= LAT; yi %= LAT
    x1, y1 = (xi + 1) % LAT, (yi + 1) % LAT
    c0 = g[xi, yi] * (1 - xf) + g[x1, yi] * xf
    c1 = g[xi, y1] * (1 - xf) + g[x1, y1] * xf
    return c0 * (1 - yf) + c1 * yf


def fbm2(x, y, freq, octaves, seed, gain=0.5):
    total = np.zeros_like(x)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        total += amp * noise2(x * freq * (2 ** o), y * freq * (2 ** o), seed + o * 131)
        norm += amp
        amp *= gain
    return total / norm


def stripes(t, count, softness=0.06):
    """A 0/1 band pattern with soft edges, `count` bands across t in 0..1."""
    p = (t * count) % 1.0
    edge = np.minimum(p, 1.0 - p)
    return np.clip(edge / max(1e-6, softness), 0.0, 1.0)


# ================================================================= the six skins
# Each takes (along, across) in METRES and returns (rgb, emission or None).

def deep_blue(a, c):
    base = hexcol("#16386E")
    mottle = fbm2(a, c, 3.0, 3, 11)
    grain = fbm2(a, c, 40.0, 2, 12)
    rgb = base[None, None, :] * (1.0 + (mottle[..., None] - 0.5) * 0.18)
    rgb *= (1.0 + (grain[..., None] - 0.5) * 0.07)
    return np.clip(rgb, 0, 1), None


def indoor_court(a, c):
    """Maple parquet. Planks run the LENGTH of the table, as they do on a real court, so the seams
    are lines of constant `across`."""
    planks = 8
    idx = np.floor(c / PITCH_SHORT * planks)
    rng = np.random.default_rng(21)
    tone = rng.random(planks + 2)[idx.astype(int) % (planks + 2)]

    base = hexcol("#C08344")
    rgb = base[None, None, :] * (0.90 + 0.20 * tone[..., None])

    # Grain: noise stretched hard along the plank direction so it reads as wood rather than dirt.
    grain = fbm2(a * 0.9, c * 26.0, 3.0, 4, 22)
    rgb *= (1.0 + (grain[..., None] - 0.5) * 0.22)

    seam = stripes(c / PITCH_SHORT, planks, softness=0.035)
    rgb *= (0.55 + 0.45 * seam[..., None])
    return np.clip(rgb, 0, 1), None


def wild_west(a, c):
    """Dry desert floor: warm sand, wind ripples, and darker cracked patches."""
    base = hexcol("#B98B5E")
    dunes = fbm2(a, c, 2.2, 3, 31)
    ripple = fbm2(a * 3.0, c * 0.4, 6.0, 2, 32)
    grit = fbm2(a, c, 60.0, 2, 33)

    rgb = base[None, None, :] * (0.86 + 0.28 * dunes[..., None])
    rgb *= (1.0 + (ripple[..., None] - 0.5) * 0.14)
    rgb *= (1.0 + (grit[..., None] - 0.5) * 0.10)

    # Cracked earth: the thin dark network where the noise crosses a threshold.
    cracks = fbm2(a, c, 7.0, 3, 34)
    crack = np.clip(1.0 - np.abs(cracks - 0.5) / 0.035, 0, 1)
    rgb = rgb * (1.0 - 0.45 * crack[..., None]) + hexcol("#6B4A2C")[None, None, :] * 0.45 * crack[..., None]
    return np.clip(rgb, 0, 1), None


def pitch_perfect(a, c):
    """Real turf with mower stripes. The bands run ACROSS the pitch, the way a groundsman cuts it,
    so you read them goal to goal."""
    bands = 9
    band = np.floor(a / PITCH_LONG * bands).astype(int) % 2

    light = hexcol("#4E9C42")
    dark = hexcol("#377C32")
    rgb = np.where(band[..., None] == 0, light[None, None, :], dark[None, None, :])

    # A soft transition at each mow line — a real stripe is laid grass, not a painted edge.
    blend = stripes(a / PITCH_LONG, bands, softness=0.18)
    rgb = rgb * (0.96 + 0.08 * blend[..., None])

    blades = fbm2(a * 1.0, c * 1.0, 120.0, 2, 41)
    rgb *= (1.0 + (blades[..., None] - 0.5) * 0.13)
    wear = fbm2(a, c, 2.5, 3, 42)
    rgb *= (1.0 + (wear[..., None] - 0.5) * 0.10)
    return np.clip(rgb, 0, 1), None


def neon_rave(a, c):
    """Near-black with a glowing grid. Returns an emission map too — a 'glowing' line that only
    exists in albedo is just a bright stripe, and reads as paint rather than light."""
    bg = hexcol("#080810")
    rgb = np.repeat(np.repeat(bg[None, None, :], a.shape[0], 0), a.shape[1], 1).copy()
    emis = np.zeros_like(rgb)

    cyan = hexcol("#2BF0FF")
    magenta = hexcol("#FF3BD0")

    def glow_lines(t, count, colour, width, bloom):
        p = (t * count) % 1.0
        d = np.minimum(p, 1.0 - p) / count            # distance to nearest line, in t units
        core = np.clip(1.0 - d / width, 0, 1)
        halo = np.clip(1.0 - d / bloom, 0, 1) ** 2
        strength = np.clip(core + 0.55 * halo, 0, 1)
        return strength[..., None] * colour[None, None, :]

    across_lines = glow_lines(a / PITCH_LONG, 10, cyan, 0.0016, 0.010)
    along_lines = glow_lines(c / PITCH_SHORT, 6, magenta, 0.0022, 0.013)

    lit = np.clip(across_lines + along_lines, 0, 1)
    rgb = np.clip(rgb + lit * 0.85, 0, 1)
    emis = lit

    shimmer = fbm2(a, c, 5.0, 2, 51)
    rgb *= (1.0 + (shimmer[..., None] - 0.5) * 0.10)
    return np.clip(rgb, 0, 1), np.clip(emis, 0, 1)


def chessboard(a, c):
    """8 squares across by 14 along keeps them square on a 1.82:1 pitch (0.0825 x 0.0857 m)."""
    across_n, along_n = 8, 14
    ca = np.floor(c / PITCH_SHORT * across_n).astype(int)
    al = np.floor(a / PITCH_LONG * along_n).astype(int)
    dark = ((ca + al) % 2) == 0

    light_c = hexcol("#EDEDE8")
    dark_c = hexcol("#191A1F")
    rgb = np.where(dark[..., None], dark_c[None, None, :], light_c[None, None, :])

    # A whisper of grain so the flat squares are not two dead colours.
    grain = fbm2(a, c, 50.0, 2, 61)
    rgb = rgb * (1.0 + (grain[..., None] - 0.5) * 0.06)
    return np.clip(rgb, 0, 1), None


SKINS = [
    ("DeepBlue",     deep_blue,      "#16386E"),
    ("IndoorCourt",  indoor_court,   "#A96F38"),
    ("WildWest",     wild_west,      "#A87C52"),
    ("PitchPerfect", pitch_perfect,  "#3D8537"),
    ("NeonRave",     neon_rave,      "#080810"),
    ("Chessboard",   chessboard,     "#8A8A88"),
]


def write(name, rgb, srgb=True):
    data = np.clip(np.asarray(rgb, dtype=np.float64), 0, 1)
    buf = np.empty((SIZE, SIZE, 4))
    buf[..., :3] = data
    buf[..., 3] = 1.0
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=True, float_buffer=False)
    # Written in display space; Blender stores 8-bit pixels as given and applies no transform on
    # save. The tag only tells readers how to interpret the bytes. (Linearising first was measured
    # to darken every colour — see the ball skins.)
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    img.alpha_mode = "CHANNEL_PACKED"
    img.pixels.foreach_set(np.ascontiguousarray(buf, dtype=np.float32).ravel())
    img.filepath_raw = f"{OUT}/{name}.png"
    img.file_format = "PNG"
    img.save()
    print(f"  wrote {name}.png")


def main():
    print(f"\n===== generating {len(SKINS)} field skins at {SIZE}x{SIZE} =====")
    for name, fn, base in SKINS:
        flood = hexcol(base)
        albedo = np.repeat(np.repeat(flood[None, None, :], SIZE, 0), SIZE, 1).copy()
        emission = np.zeros((SIZE, SIZE, 3))
        has_emission = False

        for (u0, u1, v0, v1) in RECTS:
            # Clamped: the rects start at u/v 0.003 and the bleed pushes that negative, where a
            # raw int() gives -1 — which numpy reads as "the last row", silently producing an empty
            # slice rather than an error.
            x0, x1 = int(np.clip(u0, 0, 1) * SIZE), int(np.clip(u1, 0, 1) * SIZE)
            y0, y1 = int(np.clip(v0, 0, 1) * SIZE), int(np.clip(v1, 0, 1) * SIZE)
            x1, y1 = min(x1, SIZE), min(y1, SIZE)
            w, h = x1 - x0, y1 - y0
            assert w > 0 and h > 0, f"{name}: empty rect {(x0, x1, y0, y1)}"

            # Rows are V (the pitch's LONG axis), columns are U (the short axis) — see the header.
            av = (np.arange(h) + 0.5) / h * PITCH_LONG
            cu = (np.arange(w) + 0.5) / w * PITCH_SHORT
            along = np.repeat(av[:, None], w, axis=1)
            across = np.repeat(cu[None, :], h, axis=0)

            rgb, emis = fn(along, across)
            albedo[y0:y1, x0:x1] = rgb
            if emis is not None:
                emission[y0:y1, x0:x1] = emis
                has_emission = True

        assert np.isfinite(albedo).all(), f"{name}: non-finite albedo"
        write(f"FieldSkin_{name}_Albedo", albedo, srgb=True)
        if has_emission:
            write(f"FieldSkin_{name}_Emission", emission, srgb=True)
    print("DONE")


main()
