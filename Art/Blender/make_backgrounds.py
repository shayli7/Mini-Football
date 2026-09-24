"""
Renders the six background (arena) skins as equirectangular panoramas.

WHY PANORAMAS
The scene has no environment geometry at all — the table floats in Unity's default procedural sky —
so a background skin is a SKYBOX. Each of these is real 3D built from primitives and rendered with
Cycles through an equirectangular camera, rather than painted flat: a room, a stadium bowl and a
dungeon all need real perspective and real light falloff to read, and painting that convincingly by
hand into a lat-long map is far harder than building it.

The camera sits at the ORIGIN with the floor 0.8 m below, which is about eye level for someone stood
at a foosball table. Unity's Skybox/Panoramic shader puts the image's vertical centre on the horizon,
which is what that height then means.

2048x1024. The menu camera's field of view is 34 degrees, so it samples roughly a tenth of the
panorama's width — detail beyond this is wasted on a background that sits behind a sharp table.

Run: blender --background --factory-startup --python make_backgrounds.py -- <outdir> [name ...]
"""
import bpy
import sys
import math
import time
import random
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]
_rest = [a for a in argv[1:] if a != "--floors"]
FLOORS = "--floors" in argv[1:]
ONLY = set(_rest) if _rest else None

W, H = 2048, 1024
SAMPLES = 48
FLOOR = -0.8          # metres below the eye

# --- the top-down floor pass ---------------------------------------------------------------------
# A room has to be visible during a MATCH, and the gameplay camera is orthographic pointing straight
# down. A skybox is useless to it — every pixel of an orthographic camera shares one view direction,
# so a panorama collapses to a flat patch. The only thing that camera can see is geometry under the
# table, so each room also renders its floor from above.
FLOOR_PX = 1024
FLOOR_SPAN = 4.0        # metres covered; matches BackgroundSkinner.FloorScale's 4-unit plane 1:1
FLOOR_HIDE_ABOVE = 1.1  # anything whose base sits higher than this is hidden from the floor camera


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)) + (1.0,)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, colour, rough=0.7, metal=0.0, emit=None, emit_strength=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = colour
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit is not None:
        b.inputs["Emission Color"].default_value = emit
        b.inputs["Emission Strength"].default_value = emit_strength
    return m


def box(name, loc, size, material, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object
    o.name = name
    # size=1 makes a cube of SIDE 1 (vertices at +/-0.5), so the scale is the wanted size directly.
    # Halving it built every room at half scale, which left the walls floating clear of the floor
    # and the ceiling — the bright bands that leaked through those gaps.
    o.scale = (size[0], size[1], size[2])
    o.rotation_euler = rot
    o.data.materials.append(material)
    return o


def sphere(name, loc, r, material, segs=32):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=segs, ring_count=segs // 2)
    o = bpy.context.object
    o.name = name
    o.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return o


def world_colour(colour, strength=1.0):
    w = bpy.data.worlds.new("W")
    bpy.context.scene.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = colour
    bg.inputs[1].default_value = strength
    return w


def world_gradient(top, bottom, strength=1.0):
    """A sky that is brighter overhead than at the horizon — the cheapest thing that stops an
    outdoor panorama reading as a flat wall of colour."""
    w = bpy.data.worlds.new("W")
    bpy.context.scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes["Background"]
    bg.inputs[1].default_value = strength
    coord = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = bottom
    ramp.color_ramp.elements[1].position = 0.75
    ramp.color_ramp.elements[1].color = top
    nt.links.new(coord.outputs["Generated"], sep.inputs["Vector"])
    nt.links.new(sep.outputs["Z"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bg.inputs[0])
    return w


def sun(name, rot, energy, colour=(1, 1, 1), angle=0.02):
    """Distance-independent, unlike an area light — the only thing that lights geometry tens of
    metres away in the space and sky scenes."""
    d = bpy.data.lights.new(name, "SUN")
    d.energy = energy
    d.color = colour
    d.angle = angle
    o = bpy.data.objects.new(name, d)
    o.rotation_euler = rot
    bpy.context.collection.objects.link(o)
    return o


def point(name, loc, energy, colour=(1, 1, 1), radius=0.15):
    d = bpy.data.lights.new(name, "POINT")
    d.energy = energy
    d.color = colour
    d.shadow_soft_size = radius
    o = bpy.data.objects.new(name, d)
    o.location = loc
    bpy.context.collection.objects.link(o)
    return o


def area(name, loc, rot, size, energy, colour=(1, 1, 1)):
    d = bpy.data.lights.new(name, "AREA")
    d.energy = energy
    d.size = size
    d.color = colour
    o = bpy.data.objects.new(name, d)
    o.location = loc
    o.rotation_euler = rot
    bpy.context.collection.objects.link(o)
    return o


# ================================================================= scenes

def living_room():
    """A warm domestic room: painted walls, a wood floor, a sofa, a rug, a screen and a lamp."""
    world_colour(rgb("#1a1712"), 0.35)
    wall = mat("wall", rgb("#C9B79C"), rough=0.9)
    floor_m = mat("floor", rgb("#8A5A32"), rough=0.55)
    ceil = mat("ceil", rgb("#EDE7DD"), rough=0.95)

    R = 3.4  # room half-width
    box("Floor", (0, 0, FLOOR), (R * 2, R * 2, 0.1), floor_m)
    box("Ceil", (0, 0, FLOOR + 2.7), (R * 2, R * 2, 0.1), ceil)
    for i, (x, y, sx, sy) in enumerate([(-R, 0, 0.1, R * 2), (R, 0, 0.1, R * 2),
                                        (0, -R, R * 2, 0.1), (0, R, R * 2, 0.1)]):
        box(f"Wall{i}", (x, y, FLOOR + 1.35), (sx, sy, 2.7), wall)

    sofa = mat("sofa", rgb("#5C6B7A"), rough=0.85)
    box("SofaBase", (-1.6, 2.2, FLOOR + 0.28), (2.2, 0.9, 0.55), sofa)
    box("SofaBack", (-1.6, 2.6, FLOOR + 0.72), (2.2, 0.25, 0.9), sofa)
    # Pulled back off centre and shrunk: at 3.0 x 2.0 the rug covered the whole band the camera can
    # see past the table, so the room read as one flat red rectangle with none of its wood floor.
    box("Rug", (0, 0.95, FLOOR + 0.06), (2.2, 1.4, 0.02), mat("rug", rgb("#8C3B3B"), rough=0.95))

    # A screen on the wall, lightly emissive so the room has a second, cooler source.
    box("TV", (2.6, -0.4, FLOOR + 1.35), (0.08, 1.7, 1.0),
        mat("tv", rgb("#0A0C10"), emit=rgb("#3A6FA8"), emit_strength=1.6))
    box("Shelf", (-3.1, -1.8, FLOOR + 1.0), (0.35, 1.8, 1.9), mat("shelf", rgb("#6B4A2E"), rough=0.7))

    lamp = box("LampShade", (2.2, 2.4, FLOOR + 1.5), (0.42, 0.42, 0.45),
               mat("shade", rgb("#F2E2C0"), emit=rgb("#FFD9A0"), emit_strength=5.0))
    point("LampLight", (2.2, 2.4, FLOOR + 1.45), 42, rgb("#FFD9A0")[:3], 0.3)
    point("Fill", (-1.0, 0.5, FLOOR + 2.2), 20, (1.0, 0.92, 0.82), 0.6)
    return lamp


def dark_room():
    """The same room shape, stripped and lit by one hanging lamp — a late-night garage game."""
    world_colour(rgb("#05060A"), 0.15)
    wall = mat("wall", rgb("#2A2E36"), rough=0.95)
    # Lifted off near-black. The floor render is drawn UNLIT in game, so whatever brightness is baked
    # here is the whole of what the player sees — at the original value the room arrived as a black
    # void indistinguishable from having no background at all.
    floor_m = mat("floor", rgb("#282B34"), rough=0.7)

    R = 3.2
    box("Floor", (0, 0, FLOOR), (R * 2, R * 2, 0.1), floor_m)
    box("Ceil", (0, 0, FLOOR + 3.0), (R * 2, R * 2, 0.1), mat("ceil", rgb("#14161B"), rough=0.95))
    for i, (x, y, sx, sy) in enumerate([(-R, 0, 0.1, R * 2), (R, 0, 0.1, R * 2),
                                        (0, -R, R * 2, 0.1), (0, R, R * 2, 0.1)]):
        box(f"Wall{i}", (x, y, FLOOR + 1.5), (sx, sy, 3.0), wall)

    # One hard light directly overhead is the whole look: a bright pool on the table and everything
    # past it falling away into the dark.
    box("Cord", (1.15, 0.55, FLOOR + 2.5), (0.03, 0.03, 1.0), mat("cord", rgb("#0A0A0C")))
    box("Shade", (1.15, 0.55, FLOOR + 1.95), (0.55, 0.55, 0.22),
        mat("shade", rgb("#101216"), emit=rgb("#FFE6B8"), emit_strength=3.2))
    point("Bulb", (1.15, 0.55, FLOOR + 1.85), 70, (1.0, 0.90, 0.74), 0.12)
    point("Bounce", (0, 0, FLOOR + 0.2), 18, (0.5, 0.6, 0.8), 1.2)
    box("Crate", (2.4, -2.2, FLOOR + 0.35), (0.9, 0.9, 0.7), mat("crate", rgb("#3A2E22"), rough=0.9))
    box("Crate2", (-2.6, -2.5, FLOOR + 0.25), (0.7, 0.7, 0.5), mat("crate2", rgb("#2E2822"), rough=0.9))
    box("Bench", (-2.8, 1.6, FLOOR + 0.22), (0.6, 1.8, 0.44), mat("bench", rgb("#33302B"), rough=0.9))
    return None


def outer_space():
    """Starfield, a nebula wash and a few planets. The 'drifting' is a slow skybox rotation at
    wire-up time, not something baked into the image."""
    rnd = random.Random(7)
    world_gradient(rgb("#05060F"), rgb("#0A0818"), 1.0)

    # Stars as tiny emissive spheres on a far shell — real geometry so they get the panorama's
    # perspective rather than sitting in a painted layer.
    star_mats = [mat(f"star{i}", rgb("#FFFFFF"), emit=c, emit_strength=e)
                 for i, (c, e) in enumerate([(rgb("#FFFFFF"), 22.0), (rgb("#CFE0FF"), 16.0),
                                             (rgb("#FFE4C4"), 14.0)])]
    for i in range(420):
        u, v = rnd.random(), rnd.random() * 2 - 1
        th = u * math.tau
        ph = math.asin(v)
        d = 60.0
        p = (d * math.cos(ph) * math.cos(th), d * math.cos(ph) * math.sin(th), d * math.sin(ph))
        sphere(f"S{i}", p, rnd.uniform(0.05, 0.16), rnd.choice(star_mats), segs=8)

    sphere("Planet1", (18, 26, 6), 6.0, mat("p1", rgb("#B4643C"), rough=0.9))
    sphere("Planet2", (-30, 12, -8), 3.2, mat("p2", rgb("#5C7FB8"), rough=0.85))
    sphere("Moon", (-12, -22, 9), 1.6, mat("p3", rgb("#B9BCC4"), rough=0.95))
    sun("Star", (math.radians(52), 0, math.radians(40)), 3.2, (1.0, 0.96, 0.90))
    return None


def stadium_lights():
    """A packed bowl at night. The crowd is tiers of small emissive-flecked blocks — at panorama
    scale a stand reads as texture and colour, never as faces."""
    rnd = random.Random(11)
    world_colour(rgb("#04060C"), 0.5)

    box("Pitch", (0, 0, FLOOR), (44, 30, 0.1), mat("grass", rgb("#2F6B33"), rough=0.9))

    stand = mat("stand", rgb("#242A33"), rough=0.9)
    crowd_cols = [rgb("#8C93A8"), rgb("#B8804A"), rgb("#6E7F9C"), rgb("#A8A2B4")]
    crowd = [mat(f"crowd{i}", c, rough=0.95, emit=c, emit_strength=0.35)
             for i, c in enumerate(crowd_cols)]

    # Four banks of raked terracing around the pitch.
    for side, (nx, ny) in enumerate([(0, 1), (0, -1), (1, 0), (-1, 0)]):
        for tier in range(7):
            t = tier / 6.0
            dist = 17.0 + tier * 1.5
            h = FLOOR + 0.6 + tier * 1.15
            length = 40 if nx == 0 else 26
            box(f"Tier{side}_{tier}", (nx * dist, ny * dist, h),
                (length if nx == 0 else 1.6, 1.6 if nx == 0 else length, 1.0),
                rnd.choice(crowd) if tier % 2 == 0 else stand)

    # Floodlight pylons at the corners.
    for cx, cy in ((-26, -26), (26, -26), (-26, 26), (26, 26)):
        box(f"Mast{cx}{cy}", (cx, cy, FLOOR + 9), (1.0, 1.0, 18), mat("mast", rgb("#1A1E24"), metal=0.6, rough=0.5))
        head = box(f"Head{cx}{cy}", (cx, cy, FLOOR + 18.5), (5.0, 1.2, 3.0),
                   mat(f"lamp{cx}{cy}", rgb("#FFFFFF"), emit=rgb("#EAF2FF"), emit_strength=90.0))
        area(f"Flood{cx}{cy}", (cx * 0.75, cy * 0.75, FLOOR + 17),
             (math.radians(35 if cy < 0 else 145), 0, math.radians(-45 if cx < 0 else 45)),
             8, 4000, (0.92, 0.96, 1.0))
    return None


def medieval_dungeon():
    """Stone chamber with torches. Warm pools of light on cold stone is the whole read."""
    world_colour(rgb("#06070A"), 0.2)
    stone = mat("stone", rgb("#6E6A62"), rough=0.95)
    dark_stone = mat("stone2", rgb("#4A4740"), rough=0.95)

    R = 4.0
    box("Floor", (0, 0, FLOOR), (R * 2, R * 2, 0.1), dark_stone)
    box("Ceil", (0, 0, FLOOR + 3.6), (R * 2, R * 2, 0.1), dark_stone)

    # Walls built as courses of blocks so the panorama shows real masonry, offset per row.
    for i, (nx, ny) in enumerate([(-1, 0), (1, 0), (0, -1), (0, 1)]):
        for row in range(6):
            z = FLOOR + 0.3 + row * 0.6
            span = 8.0
            n = 8
            for k in range(n):
                off = (k - (n - 1) / 2) * (span / n) + (0.25 if row % 2 else 0)
                loc = (nx * R, off, z) if nx else (off, ny * R, z)
                size = (0.25, span / n * 0.92, 0.55) if nx else (span / n * 0.92, 0.25, 0.55)
                box(f"B{i}_{row}_{k}", loc, size, stone if (row + k) % 3 else dark_stone)

    flame = mat("flame", rgb("#FF8A2B"), emit=rgb("#FF7A18"), emit_strength=45.0)
    for tx, ty in ((-3.6, -2.0), (-3.6, 2.0), (3.6, -2.0), (3.6, 2.0)):
        box(f"Bracket{tx}{ty}", (tx * 0.95, ty, FLOOR + 1.5), (0.18, 0.18, 0.5),
            mat("iron", rgb("#22201C"), metal=0.7, rough=0.6))
        sphere(f"Flame{tx}{ty}", (tx * 0.95, ty, FLOOR + 1.85), 0.16, flame, segs=12)
        point(f"Torch{tx}{ty}", (tx * 0.9, ty, FLOOR + 1.9), 95, (1.0, 0.55, 0.18), 0.25)
    return None


def floating_islands():
    """Daylight sky with distant rocks hanging in it. The table's own island is implied just below
    the horizon rather than modelled, since the skybox never shows what you are standing on."""
    rnd = random.Random(3)
    world_gradient(rgb("#4A8FD8"), rgb("#CFE4F5"), 1.1)

    grass = mat("grass", rgb("#4E8B3C"), rough=0.9)
    rock = mat("rock", rgb("#6B5F52"), rough=0.95)

    def island(cx, cy, cz, r):
        sphere(f"Rock{cx}{cy}", (cx, cy, cz - r * 0.55), r, rock, segs=16)
        box(f"Top{cx}{cy}", (cx, cy, cz + r * 0.05), (r * 1.9, r * 1.9, r * 0.18), grass)

    island(16, 22, -2.5, 3.2)
    island(-20, 14, 3.0, 2.2)
    island(-14, -24, -4.0, 4.0)
    island(26, -18, 5.5, 1.8)
    island(4, 34, 8.0, 2.6)

    # A few clouds, well below the zenith so they sit in the band the menu camera actually frames.
    cloud = mat("cloud", rgb("#FFFFFF"), rough=1.0, emit=rgb("#FFFFFF"), emit_strength=0.25)
    for i in range(14):
        a = rnd.random() * math.tau
        d = rnd.uniform(22, 45)
        sphere(f"C{i}", (math.cos(a) * d, math.sin(a) * d, rnd.uniform(-2, 10)),
               rnd.uniform(2.0, 4.5), cloud, segs=12)

    sun("Sun", (math.radians(48), 0, math.radians(38)), 3.6, (1.0, 0.96, 0.88))
    return None


def disco_room():
    """A club room: a lit dance floor, a mirror ball and a couple of coloured washes.

    Deliberately restrained. The brief was lights that are not distracting, so the tiles GLOW rather
    than strobe and the colour arrives as two wide soft washes instead of hard beams. The table has
    to stay the brightest and most readable thing on screen — a background that competes with the
    ball for attention is a worse background, however good it looks in isolation.
    """
    world_colour(rgb("#07060C"), 0.18)
    wall = mat("wall", rgb("#17151F"), rough=0.9)

    R = 3.4
    box("Base", (0, 0, FLOOR - 0.03), (R * 2, R * 2, 0.1), mat("base", rgb("#08080C"), rough=0.8))
    box("Ceil", (0, 0, FLOOR + 3.0), (R * 2, R * 2, 0.1), mat("ceil", rgb("#0C0B12"), rough=0.95))
    for i, (x, y, sx, sy) in enumerate([(-R, 0, 0.1, R * 2), (R, 0, 0.1, R * 2),
                                        (0, -R, R * 2, 0.1), (0, R, R * 2, 0.1)]):
        box(f"Wall{i}", (x, y, FLOOR + 1.5), (sx, sy, 3.0), wall)

    # The dance floor is this room's entire identity from above, so it is real tiles rather than a
    # painted pattern: alternating dark glass and two soft glows, kept low enough to read as a lit
    # floor instead of a light source.
    # Muted on purpose, and this is the setting worth protecting: at full saturation the cyan and
    # magenta squares out-shouted the ball, which is the one thing on screen that must never lose a
    # fight for attention. Low emission and greyed-down hues keep it a lit floor, not a light show.
    dark_tile = mat("tileDark", rgb("#101018"), rough=0.25)
    glow_a = mat("tileA", rgb("#241A30"), rough=0.30, emit=rgb("#7A54A0"), emit_strength=0.45)
    glow_b = mat("tileB", rgb("#14242E"), rough=0.30, emit=rgb("#357991"), emit_strength=0.45)

    # Small enough that the strip of floor left visible past the table rails still reads as a
    # chequered dance floor rather than as one ambiguous coloured block.
    n, span = 16, 6.4
    step = span / n
    for ix in range(n):
        for iy in range(n):
            x = (ix - (n - 1) / 2) * step
            y = (iy - (n - 1) / 2) * step
            if (ix + iy) % 2:
                tile = dark_tile
            else:
                tile = glow_a if (ix // 2 + iy // 2) % 2 else glow_b
            box(f"T{ix}_{iy}", (x, y, FLOOR + 0.02), (step * 0.96, step * 0.96, 0.04), tile)

    sphere("MirrorBall", (0, 0, FLOOR + 2.35), 0.34,
           mat("mirror", rgb("#C8CCD6"), rough=0.12, metal=1.0), segs=24)

    box("Booth", (0, -3.0, FLOOR + 0.45), (2.0, 0.6, 0.9), mat("booth", rgb("#1A1822"), rough=0.7))
    area("WashL", (-2.2, -1.0, FLOOR + 2.6), (math.radians(35), 0, math.radians(-35)),
         2.5, 55, (0.62, 0.46, 0.80))
    area("WashR", (2.2, -1.0, FLOOR + 2.6), (math.radians(35), 0, math.radians(35)),
         2.5, 55, (0.40, 0.68, 0.80))
    point("Key", (0, 0.6, FLOOR + 2.5), 60, (1.0, 0.95, 0.90), 0.5)
    return None


def royal_palace():
    """A palace hall: marble underfoot, gilded columns, a red runner and chandelier light."""
    world_colour(rgb("#14100A"), 0.30)

    R = 4.0
    wall = mat("wall", rgb("#D8C9A8"), rough=0.75)
    gold = mat("gold", rgb("#C9A227"), rough=0.25, metal=0.9)

    box("Ceil", (0, 0, FLOOR + 4.2), (R * 2, R * 2, 0.1), mat("ceil", rgb("#E6DCC2"), rough=0.9))
    for i, (x, y, sx, sy) in enumerate([(-R, 0, 0.1, R * 2), (R, 0, 0.1, R * 2),
                                        (0, -R, R * 2, 0.1), (0, R, R * 2, 0.1)]):
        box(f"Wall{i}", (x, y, FLOOR + 2.1), (sx, sy, 4.2), wall)
        box(f"Trim{i}", (x * 0.98, y * 0.98, FLOOR + 0.18), (sx * 1.2, sy * 1.2, 0.36), gold)

    # The marble chequer is the hall's signature from above, so it is laid as real tiles.
    light_m = mat("marbleL", rgb("#EDE6D4"), rough=0.18)
    dark_m = mat("marbleD", rgb("#4A3B33"), rough=0.18)
    box("Base", (0, 0, FLOOR - 0.03), (R * 2, R * 2, 0.1), dark_m)

    n, span = 14, 7.0
    step = span / n
    for ix in range(n):
        for iy in range(n):
            x = (ix - (n - 1) / 2) * step
            y = (iy - (n - 1) / 2) * step
            box(f"M{ix}_{iy}", (x, y, FLOOR + 0.02), (step * 0.97, step * 0.97, 0.04),
                light_m if (ix + iy) % 2 else dark_m)

    # A runner down the hall, kept NARROW deliberately. The table is 1.12 wide and the camera sees
    # only about 0.9 either side of centre, so a wide runner fills that whole margin and the hall
    # reads as "red carpet" with the marble never visible at all. At 1.4 the table covers its middle,
    # a band of red and its gold edge show past the rails, and marble still reaches the screen edge.
    box("Runner", (0, 0, FLOOR + 0.05), (1.4, span, 0.02), mat("runner", rgb("#7E1B22"), rough=0.85))
    for sx in (-0.62, 0.62):
        box(f"RunnerTrim{sx}", (sx, 0, FLOOR + 0.06), (0.09, span, 0.02), gold)

    col = mat("col", rgb("#E2D6BC"), rough=0.6)
    for cx in (-2.9, 2.9):
        for cy in (-2.6, 0.0, 2.6):
            box(f"Col{cx}_{cy}", (cx, cy, FLOOR + 2.0), (0.52, 0.52, 4.0), col)
            box(f"Plinth{cx}_{cy}", (cx, cy, FLOOR + 0.13), (0.76, 0.76, 0.26), gold)

    box("Chain", (0, 0, FLOOR + 3.6), (0.05, 0.05, 1.2), gold)
    sphere("Chandelier", (0, 0, FLOOR + 2.8), 0.45,
           mat("chand", rgb("#FFF0C0"), emit=rgb("#FFE2A8"), emit_strength=6.0), segs=16)
    point("ChandLight", (0, 0, FLOOR + 2.7), 130, (1.0, 0.90, 0.72), 0.5)
    point("Fill", (0, -2.2, FLOOR + 2.0), 38, (1.0, 0.94, 0.84), 0.8)
    return None


SCENES = [
    ("LivingRoom", living_room),
    ("DarkRoom", dark_room),
    ("DiscoRoom", disco_room),
    ("RoyalPalace", royal_palace),
    ("OuterSpace", outer_space),
    ("StadiumLights", stadium_lights),
    ("MedievalDungeon", medieval_dungeon),
    ("FloatingIslands", floating_islands),
]


def render(name, build):
    t0 = time.time()
    reset()
    build()

    cam_data = bpy.data.cameras.new("Pano")
    cam_data.type = "PANO"
    # Blender 4.x+ moved the panorama type onto the cycles settings; older builds keep it on the
    # camera. Set whichever exists so this is not version-locked.
    try:
        cam_data.panorama_type = "EQUIRECTANGULAR"
    except (AttributeError, TypeError):
        pass
    try:
        cam_data.cycles.panorama_type = "EQUIRECTANGULAR"
    except AttributeError:
        pass

    cam = bpy.data.objects.new("Pano", cam_data)
    # Rotated upright: an equirectangular camera wraps around its own local Y, so the panorama's
    # horizon only lands on the world horizon when the camera is stood up like this.
    cam.location = (0, 0, 0)
    cam.rotation_euler = (math.radians(90), 0, 0)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    try:
        sc.cycles.device = "CPU"
    except AttributeError:
        pass
    sc.render.resolution_x = W
    sc.render.resolution_y = H
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    sc.render.filepath = f"{OUT}/BgSkin_{name}"

    bpy.ops.render.render(write_still=True)
    print(f"  rendered BgSkin_{name}.png in {time.time() - t0:.0f}s")


def render_floor(name, build):
    """Renders the room's floor straight down, for the plane the table stands on during a match."""
    t0 = time.time()
    reset()
    build()

    # Overhead geometry is hidden from CAMERA rays only, never with hide_render. The hanging lamp IS
    # the light in some of these rooms, and the pool it throws on the floor is most of what makes the
    # room recognisable from above — removing the object outright would take that pool with it.
    hidden = 0
    for o in list(bpy.context.scene.objects):
        if o.type != "MESH":
            continue
        base = min((o.matrix_world @ Vector(c)).z for c in o.bound_box)
        if base > FLOOR + FLOOR_HIDE_ABOVE:
            o.visible_camera = False
            hidden += 1

    cam_data = bpy.data.cameras.new("Top")
    cam_data.type = "ORTHO"
    # The render is square, so this sizes both axes: exactly FLOOR_SPAN metres of floor.
    cam_data.ortho_scale = FLOOR_SPAN
    cam = bpy.data.objects.new("Top", cam_data)
    # Above the (now camera-invisible) ceiling, looking straight down: a camera's local -Z is its
    # forward, so an unrotated camera already points down the world -Z axis.
    cam.location = (0, 0, FLOOR + 6.0)
    cam.rotation_euler = (0, 0, 0)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    try:
        sc.cycles.device = "CPU"
    except AttributeError:
        pass
    sc.render.resolution_x = FLOOR_PX
    sc.render.resolution_y = FLOOR_PX
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    sc.render.filepath = f"{OUT}/BgFloor_{name}"

    bpy.ops.render.render(write_still=True)
    print(f"  rendered BgFloor_{name}.png in {time.time() - t0:.0f}s ({hidden} overhead hidden)")


for name, fn in SCENES:
    if ONLY and name not in ONLY:
        continue
    if FLOORS:
        render_floor(name, fn)
    else:
        render(name, fn)

print("DONE")
