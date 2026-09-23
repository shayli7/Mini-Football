"""
Renders a store thumbnail for every cosmetic that has real art.

One scene holds all three subjects — a ball, a field with its pitch lines, and a red/blue figure
pair — each with its own camera. Every thumbnail is then a material swap plus a render, rather than
three scenes built three times.

512x256 with a TRANSPARENT background, matching the store card's 2:1 picture area. Transparent so
the card keeps drawing its rarity-tinted backdrop behind the art: the tint is how a wall of cards
stays scannable, and a thumbnail with its own opaque background would hide it.

Run: blender --background --factory-startup --python make_thumbnails.py -- <fbx> <texroot> <outdir>
"""
import bpy
import sys
import math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, TEXROOT, OUT = argv[0], argv[1], argv[2]
W, H = 512, 256

BALLS = [
    ("ball.classic", None), ("ball.retro_orange", "RetroOrange"),
    ("ball.solid_red", "SolidRed"), ("ball.solid_blue", "SolidBlue"),
    ("ball.soccer", "Soccer"), ("ball.basketball", "Basketball"), ("ball.camo", "Camo"),
    ("ball.disco", "Disco"), ("ball.ice", "Ice"), ("ball.golden", "Golden"),
]
BALL_PACKED = {"Disco", "Ice", "Golden"}
BALL_FINISH = {
    "RetroOrange": (0.0, 0.42), "SolidRed": (0.0, 0.38), "SolidBlue": (0.0, 0.38),
    "Soccer": (0.0, 0.34), "Basketball": (0.0, 0.62), "Camo": (0.0, 0.55),
    "Disco": (1.0, 0.15), "Ice": (0.0, 0.26), "Golden": (1.0, 0.12),
}

FIELDS = [
    ("field.classic", None), ("field.deep_blue", "DeepBlue"), ("field.court", "IndoorCourt"),
    ("field.wild_west", "WildWest"), ("field.pitch_perfect", "PitchPerfect"),
    ("field.neon_rave", "NeonRave"), ("field.chessboard", "Chessboard"),
]

# id, body(tex stem | hex | None), kit stem | None, head tex stem | None
FIGURES = [
    ("figure.classic",    None,                None,           None),
    ("figure.yellow",     None,                "#E8C21C",      None),
    ("figure.black",      None,                "#1E1E22",      None),
    ("figure.green",      None,                "#2E8B3A",      None),
    ("figure.stripes",    "#8A5F33",           "Stripes",      None),
    ("figure.gladiators", "Gladiators_Body",   "Gladiators",   "Gladiators_Head"),
    ("figure.robots",     "Robots_Body",       "Robots",       "Robots_Head"),
    ("figure.trojan",     "Trojan_Body",       "Trojan",       "Trojan_Head"),
]
FIG_FINISH = {"Gladiators": (0.50, 0.55), "Robots": (0.85, 0.72), "Trojan": (0.50, 0.62)}


def hexrgba(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4)) + (1.0,)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)

ball = bpy.data.objects["Ball"]
field = bpy.data.objects["Field"]
lines = [o for o in bpy.data.objects
         if o.type == "MESH" and o.data.materials and o.data.materials[0]
         and o.data.materials[0].name == "Foos_Line"]
figR = bpy.data.objects["FigR.001"]
figB = bpy.data.objects["FigB.001"]

keep = [ball, field, figR, figB] + lines
# The FBX root empty scales by 0.01 (mesh data is in centimetres); deleting it outright leaves
# everything 100x too big. Unparent keeping the transform first.
bpy.ops.object.select_all(action="DESELECT")
for o in keep:
    o.select_set(True)
bpy.context.view_layer.objects.active = keep[0]
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
for o in list(bpy.data.objects):
    if o not in keep:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.update()

BALL_ORIG = ball.data.materials[0]
FIELD_ORIG = field.data.materials[0]
FIG_ORIG = {"Red": [m for m in figR.data.materials], "Blue": [m for m in figB.data.materials]}


def recentre(obj):
    """Move the mesh onto its own origin so the object can be rotated and placed freely. These
    meshes carry their position on the table in their vertex data, so rotating without this swings
    the geometry on a long arm and out of frame."""
    c = sum((v.co for v in obj.data.vertices), Vector()) / len(obj.data.vertices)
    obj.data.transform(Matrix.Translation(-c))
    obj.location = (0, 0, 0)


# ---- ball: recentre, scale to radius 1, tilt off the pole ------------------------------------
recentre(ball)
bpy.context.view_layer.update()
wb = [ball.matrix_world @ Vector(b) for b in ball.bound_box]
r = (max(v.x for v in wb) - min(v.x for v in wb)) / 2
# MULTIPLIED into the existing scale, not assigned over it. After parent_clear the ball already
# carries the root empty's 0.01 (its mesh is in centimetres); assigning 1/r discarded that and made
# the ball radius 100, which filled the frame and rendered every ball thumbnail fully opaque.
ball.scale = tuple(s * (1.0 / r) for s in ball.scale)
bpy.context.view_layer.objects.active = ball
bpy.ops.object.select_all(action="DESELECT")
ball.select_set(True)
bpy.ops.object.transform_apply(scale=True)
bpy.context.view_layer.update()
wb2 = [ball.matrix_world @ Vector(b) for b in ball.bound_box]
r2 = (max(v.x for v in wb2) - min(v.x for v in wb2)) / 2
assert 0.9 < r2 < 1.1, f"ball radius is {r2:.3f}, expected ~1 — thumbnail framing will be wrong"
# Rotated as mesh data: the pole points at the camera by default, which is every equirect map's
# worst angle (latitude rings converge to a point).
ball.data.transform(Matrix.Rotation(math.radians(-72), 4, "X")
                    @ Matrix.Rotation(math.radians(24), 4, "Z"))
ball.location = (0, 0, 0)
bpy.ops.object.shade_smooth()

# ---- figures: recentre, place as a pair, keep their upright rotation --------------------------
for f, dx in ((figR, -0.045), (figB, 0.045)):
    recentre(f)
    f.location = (20 + dx, 0, 0)
bpy.context.view_layer.update()
fwb = [figR.matrix_world @ Vector(b) for b in figR.bound_box]
fig_h = max(v.z for v in fwb) - min(v.z for v in fwb)
fig_mid = (max(v.z for v in fwb) + min(v.z for v in fwb)) / 2

# ---- field: shift out of the way of the other two ---------------------------------------------
field.location = field.location + Vector((10, 0, 0))
for ln in lines:
    ln.location = ln.location + Vector((10, 0, 0))
bpy.context.view_layer.update()
fb = [field.matrix_world @ Vector(b) for b in field.bound_box]
field_cx = (max(v.x for v in fb) + min(v.x for v in fb)) / 2
field_cy = (max(v.y for v in fb) + min(v.y for v in fb)) / 2
field_top = max(v.z for v in fb)


def mat_image(name, path, metallic, smooth, emission=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (500, 0)
    b = nt.nodes.new("ShaderNodeBsdfPrincipled"); b.location = (250, 0)
    nt.links.new(b.outputs["BSDF"], out.inputs["Surface"])
    t = nt.nodes.new("ShaderNodeTexImage"); t.location = (-250, 100)
    t.image = bpy.data.images.load(path)
    t.image.colorspace_settings.name = "sRGB"
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = 1.0 - smooth
    if emission:
        e = nt.nodes.new("ShaderNodeTexImage"); e.location = (-250, -220)
        e.image = bpy.data.images.load(emission)
        e.image.colorspace_settings.name = "sRGB"
        nt.links.new(e.outputs["Color"], b.inputs["Emission Color"])
        b.inputs["Emission Strength"].default_value = 1.6
    return m


def mat_flat(name, colour, metallic=0.0, smooth=0.45):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = colour
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = 1.0 - smooth
    return m


# ---- world + lights ---------------------------------------------------------------------------
world = bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
wnt = world.node_tree
bg = wnt.nodes["Background"]
coord = wnt.nodes.new("ShaderNodeTexCoord")
sep = wnt.nodes.new("ShaderNodeSeparateXYZ")
ramp = wnt.nodes.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].position = 0.2
ramp.color_ramp.elements[0].color = (0.05, 0.06, 0.08, 1.0)
ramp.color_ramp.elements[1].position = 0.85
ramp.color_ramp.elements[1].color = (0.62, 0.70, 0.82, 1.0)
wnt.links.new(coord.outputs["Generated"], sep.inputs["Vector"])
wnt.links.new(sep.outputs["Z"], ramp.inputs["Fac"])
wnt.links.new(ramp.outputs["Color"], bg.inputs[0])


def area(name, loc, rot, size, energy, colour=(1, 1, 1)):
    d = bpy.data.lights.new(name, "AREA")
    d.energy = energy; d.size = size; d.color = colour
    o = bpy.data.objects.new(name, d)
    o.location = loc; o.rotation_euler = rot
    bpy.context.collection.objects.link(o)
    return o


# Ball rig (subject at the origin, radius 1).
area("BKey",  (-3.0, -4.0, 3.5), (math.radians(48), 0, math.radians(-36)), 7.0, 900)
area("BFill", (3.5, -3.0, 0.4),  (math.radians(85), 0, math.radians(46)),  5.0, 320, (0.85, 0.92, 1.0))
area("BRim",  (0.0, 3.5, 2.2),   (math.radians(-58), 0, 0),                6.0, 560, (1.0, 0.93, 0.82))
# Field rig (overhead).
sun = bpy.data.lights.new("Sun", "SUN"); sun.energy = 3.0
so = bpy.data.objects.new("Sun", sun); so.rotation_euler = (math.radians(20), math.radians(14), 0)
bpy.context.collection.objects.link(so)
# Figure rig.
area("FKey",  (19.5, -0.8, fig_mid + 0.6), (math.radians(48), 0, math.radians(-30)), 1.2, 26)
area("FFill", (20.7, -0.7, fig_mid),       (math.radians(85), 0, math.radians(42)),  1.0, 9, (0.85, 0.92, 1.0))
area("FRim",  (20.0, 0.8, fig_mid + 0.4),  (math.radians(-55), 0, 0),                1.2, 12, (1.0, 0.94, 0.85))


def make_cam(name, loc, rot, ortho):
    d = bpy.data.cameras.new(name); d.type = "ORTHO"; d.ortho_scale = ortho
    o = bpy.data.objects.new(name, d); o.location = loc; o.rotation_euler = rot
    bpy.context.collection.objects.link(o)
    return o


cam_ball = make_cam("CamBall", (0, -8, 0), (math.radians(90), 0, 0), 5.2)
cam_field = make_cam("CamField", (field_cx, field_cy, field_top + 3.0), (0, 0, 0), 1.45)
cam_fig = make_cam("CamFig", (20, -3, fig_mid), (math.radians(90), 0, 0), 0.34)

sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
try:
    sc.eevee.taa_render_samples = 64
    sc.eevee.use_raytracing = True
except AttributeError:
    pass
sc.render.resolution_x = W
sc.render.resolution_y = H
sc.view_settings.view_transform = "Standard"
sc.render.image_settings.file_format = "PNG"
sc.render.image_settings.color_mode = "RGBA"
sc.render.film_transparent = True   # the card draws its own rarity tint behind this


def hide_all_but(visible):
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in visible


def render(cam, cosmetic_id):
    sc.camera = cam
    sc.render.filepath = f"{OUT}/Thumb_{cosmetic_id.replace('.', '_')}"
    bpy.ops.render.render(write_still=True)
    print(f"  wrote Thumb_{cosmetic_id.replace('.', '_')}.png")


print("\n===== ball thumbnails =====")
for cid, stem in BALLS:
    hide_all_but({ball})
    if stem is None:
        ball.data.materials[0] = BALL_ORIG
    else:
        metal, smooth = BALL_FINISH[stem]
        m = mat_image(f"T_{stem}", f"{TEXROOT}/BallSkins/BallSkin_{stem}_Albedo.png", metal, smooth)
        ball.data.materials[0] = m
    render(cam_ball, cid)

print("\n===== field thumbnails =====")
for cid, stem in FIELDS:
    hide_all_but({field} | set(lines))
    if stem is None:
        field.data.materials[0] = FIELD_ORIG
    else:
        emis = (f"{TEXROOT}/FieldSkins/FieldSkin_{stem}_Emission.png"
                if stem == "NeonRave" else None)
        field.data.materials[0] = mat_image(
            f"T_{stem}", f"{TEXROOT}/FieldSkins/FieldSkin_{stem}_Albedo.png", 0.0, 0.4, emis)
    render(cam_field, cid)

print("\n===== figure thumbnails =====")
for cid, body, kit, head in FIGURES:
    hide_all_but({figR, figB})
    for obj, team in ((figR, "Red"), (figB, "Blue")):
        orig = FIG_ORIG[team]
        metal, smooth = FIG_FINISH.get(kit, (0.0, 0.45))
        # Slot 0 body, 1 kit, 2 head — assigned in place, never via materials.clear(), which
        # resets every polygon's material_index to 0 and collapses all three onto the body.
        if body is None:
            obj.data.materials[0] = orig[0]
        elif body.startswith("#"):
            obj.data.materials[0] = mat_flat(f"T_{cid}_b", hexrgba(body))
        else:
            obj.data.materials[0] = mat_image(
                f"T_{cid}_b", f"{TEXROOT}/FigureSkins/FigureSkin_{body}.png", metal, smooth)

        # The kit is the SHIRT: either an authored per-team texture, or — for the plain skins — a
        # flat jersey colour. A hex here is a colour, not a texture stem, and feeding it to the
        # image loader would ask for "FigureSkin_#E8C21C_KitRed.png".
        if kit is None:
            obj.data.materials[1] = orig[1]
        elif kit.startswith("#"):
            obj.data.materials[1] = mat_flat(f"T_{cid}_k", hexrgba(kit))
        else:
            obj.data.materials[1] = mat_image(
                f"T_{cid}_k{team}", f"{TEXROOT}/FigureSkins/FigureSkin_{kit}_Kit{team}.png",
                metal, smooth)

        obj.data.materials[2] = orig[2] if head is None else mat_image(
            f"T_{cid}_h", f"{TEXROOT}/FigureSkins/FigureSkin_{head}.png", metal, smooth)
    render(cam_fig, cid)

print("DONE")
