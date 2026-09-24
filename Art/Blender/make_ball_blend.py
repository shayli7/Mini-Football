"""
Builds the ball-skin source .blend and renders a preview contact sheet.

The preview spheres are the ACTUAL game ball, pulled out of FoosballTable.fbx — same 222-vert
mesh, same UV layout — so what the render shows is what Unity will show, rather than a stand-in
sphere with a different unwrap that would flatter or distort the patterns.

Run: blender --background --factory-startup --python make_ball_blend.py -- <fbx> <texdir> <blend> <png>
"""
import bpy
from mathutils import Matrix, Vector
import sys
import math

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, TEX, BLEND, PNG = argv[0], argv[1], argv[2], argv[3]

# name, albedo, metalsmooth or None, (metallic, roughness) fallback when there is no map
SKINS = [
    ("Retro Orange",        "RetroOrange", None,         (0.0, 0.42)),
    ("Solid Red",           "SolidRed",    None,         (0.0, 0.38)),
    ("Solid Blue",          "SolidBlue",   None,         (0.0, 0.38)),
    ("Soccer Ball",         "Soccer",      None,         (0.0, 0.34)),
    ("Basketball",          "Basketball",  None,         (0.0, 0.62)),
    ("Camouflage",          "Camo",        None,         (0.0, 0.55)),
    ("Disco Ball",          "Disco",       "Disco",      None),
    ("Ice Cube",            "Ice",         "Ice",        None),
    ("Golden Trophy Ball",  "Golden",      "Golden",     None),
]

bpy.ops.wm.read_factory_settings(use_empty=True)

# ---- pull the real ball out of the table -------------------------------------------------
bpy.ops.import_scene.fbx(filepath=FBX)
ball = bpy.data.objects["Ball"]
for o in list(bpy.data.objects):
    if o is not ball:
        bpy.data.objects.remove(o, do_unlink=True)

# Radius 0.0165 is unworkable next to lights and a camera; scale is cosmetic here and touches
# neither UVs nor materials, so the preview works at radius 1.
ball.scale = (1.0 / 0.0165 * 0.01,) * 3   # fbx units are cm -> radius 1
bpy.context.view_layer.objects.active = ball
ball.select_set(True)
bpy.ops.object.transform_apply(scale=True)
ball.data.materials.clear()
bpy.ops.object.shade_smooth()

# Recentre the mesh data on its own origin. The FBX ball's vertices are NOT centred on the
# object origin — the model carries the ball's position on the table in the vertex data — so
# rotating about the origin swings the geometry on a ~76-unit arm and throws it out of frame.
# Unrotated that offset is invisible (the object transform happens to cancel it), which is why
# it only surfaced as "the balls disappeared when I tilted them".
_c = sum((v.co for v in ball.data.vertices), Vector()) / len(ball.data.vertices)
print(f"ball mesh centroid before recentre = {tuple(round(v, 3) for v in _c)}")
ball.data.transform(Matrix.Translation(-_c))
_c2 = sum((v.co for v in ball.data.vertices), Vector()) / len(ball.data.vertices)
print(f"ball mesh centroid after  recentre = {tuple(round(v, 3) for v in _c2)}")
_r = max((v.co.length for v in ball.data.vertices))
print(f"ball mesh radius = {_r:.4f}")


def make_material(name, albedo, metalsmooth, fallback):
    mat = bpy.data.materials.new(f"BallSkin_{name}")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (600, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.location = (-300, 200)
    tex.image = bpy.data.images.load(f"{TEX}/BallSkin_{albedo}_Albedo.png")
    tex.image.colorspace_settings.name = "sRGB"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])

    if metalsmooth:
        ms = nt.nodes.new("ShaderNodeTexImage")
        ms.location = (-300, -220)
        ms.image = bpy.data.images.load(f"{TEX}/BallSkin_{metalsmooth}_MetalSmooth.png")
        ms.image.colorspace_settings.name = "Non-Color"
        nt.links.new(ms.outputs["Color"], bsdf.inputs["Metallic"])
        # URP packs smoothness in alpha; Blender wants roughness, so invert it.
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        inv.location = (0, -260)
        nt.links.new(ms.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    else:
        bsdf.inputs["Metallic"].default_value = fallback[0]
        bsdf.inputs["Roughness"].default_value = fallback[1]

    return mat


# ---- one ball per skin, laid out 3x3 ------------------------------------------------------
COLS, GAP = 3, 2.9
label_mat = bpy.data.materials.new("Label")
label_mat.use_nodes = True
lb = label_mat.node_tree.nodes["Principled BSDF"]
lb.inputs["Base Color"].default_value = (0.92, 0.95, 1.0, 1.0)
lb.inputs["Emission Color"].default_value = (0.92, 0.95, 1.0, 1.0)
lb.inputs["Emission Strength"].default_value = 1.4

for i, (label, albedo, ms, fallback) in enumerate(SKINS):
    col, row = i % COLS, i // COLS
    x = (col - (COLS - 1) / 2) * GAP
    z = ((len(SKINS) // COLS - 1) / 2 - row) * GAP

    o = ball.copy()
    o.data = ball.data.copy()
    o.location = (x, 0, z + 0.25)

    # Tilted off the pole. The mesh's pole axis points straight at the camera by default, which
    # shows every equirect texture at its one genuinely bad angle: the latitude rings converge to
    # a point (the disco ball reads as a spiral) and the uv pinch sits dead centre. In play the
    # ball is rolling and shows every orientation, so the preview should show a representative
    # one rather than the worst one.
    #
    # Rotated as MESH DATA, not as an object transform. Setting rotation_euler on this
    # FBX-derived object was measured to throw its geometry ~76 units away from its own origin
    # while location, scale and dimensions all still read as correct — so the balls silently
    # vanished from frame while every scene dump insisted they were where they belonged.
    # Transforming the vertices sidesteps the object transform entirely.
    o.data.transform(Matrix.Rotation(math.radians(-72), 4, "X")
                     @ Matrix.Rotation(math.radians(24), 4, "Z"))
    bpy.context.collection.objects.link(o)

    # The failure above was invisible in every property Blender reports, so it is asserted rather
    # than trusted: the geometry must actually sit within a ball's radius of where it was placed.
    # matrix_world is stale until the view layer is re-evaluated, so it is composed by hand here.
    bpy.context.view_layer.update()
    centre = sum((o.matrix_world @ v.co for v in o.data.vertices), Vector()) / len(o.data.vertices)
    assert (centre - Vector(o.location)).length < 0.05, \
        f"{label}: geometry at {tuple(round(c, 2) for c in centre)}, expected {tuple(o.location)}"
    o.data.materials.append(make_material(albedo, albedo, ms, fallback))

    bpy.ops.object.text_add(location=(x, -1.2, z - 1.30))
    t = bpy.context.object
    t.data.body = label
    t.data.align_x = "CENTER"
    t.data.size = 0.30
    t.rotation_euler = (math.pi / 2, 0, 0)
    t.data.materials.append(label_mat)

bpy.data.objects.remove(ball, do_unlink=True)

# ---- world, lights, camera ----------------------------------------------------------------
world = bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
wnt = world.node_tree
bg = wnt.nodes["Background"]
bg.inputs[1].default_value = 1.0

# A vertical gradient rather than a flat colour. Gold and the disco tiles are pure metal: they
# have no diffuse component at all and can only show what they reflect, so a uniform world makes
# them read as flat painted spheres. A bright top and dark bottom gives them a horizon to catch.
coord = wnt.nodes.new("ShaderNodeTexCoord")
coord.location = (-600, 0)
sep = wnt.nodes.new("ShaderNodeSeparateXYZ")
sep.location = (-400, 0)
ramp = wnt.nodes.new("ShaderNodeValToRGB")
ramp.location = (-200, 0)
ramp.color_ramp.elements[0].position = 0.15
ramp.color_ramp.elements[0].color = (0.02, 0.025, 0.035, 1.0)
ramp.color_ramp.elements[1].position = 0.85
ramp.color_ramp.elements[1].color = (0.55, 0.62, 0.75, 1.0)
wnt.links.new(coord.outputs["Generated"], sep.inputs["Vector"])
wnt.links.new(sep.outputs["Z"], ramp.inputs["Fac"])
wnt.links.new(ramp.outputs["Color"], bg.inputs[0])

# Metals only read as metal if there is something for them to reflect, so the key light is a
# large area source rather than a point — a small light would give gold and chrome one hot dot
# and read as plastic.
def area(name, loc, rot, size, energy, color=(1, 1, 1)):
    d = bpy.data.lights.new(name, "AREA")
    d.energy = energy
    d.size = size
    d.color = color
    o = bpy.data.objects.new(name, d)
    o.location = loc
    o.rotation_euler = rot
    bpy.context.collection.objects.link(o)

area("Key",  (-4.5, -6.0, 5.0), (math.radians(48), 0, math.radians(-38)), 9.0, 4200)
area("Fill", (5.5, -5.0, 0.5),  (math.radians(85), 0, math.radians(48)),  7.0, 1600, (0.85, 0.92, 1.0))
area("Rim",  (0.0, 5.5, 3.5),   (math.radians(-60), 0, 0),                8.0, 2600, (1.0, 0.93, 0.8))

cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
# Content spans z = -4.2 (bottom label) to +3.9 (top ball), so 8.1 units tall. At 1500x1150 the
# vertical extent is ortho_scale * 1150/1500, which needs ortho_scale >= 10.6 — the old 9.6 cut
# the top row and the bottom labels off.
cam_data.ortho_scale = 11.6
cam = bpy.data.objects.new("Cam", cam_data)
cam.location = (0, -14, -0.15)
cam.rotation_euler = (math.pi / 2, 0, 0)
bpy.context.collection.objects.link(cam)
bpy.context.scene.camera = cam

# ---- render --------------------------------------------------------------------------------
scene = bpy.context.scene
for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
    try:
        scene.render.engine = engine
        break
    except TypeError:
        continue
print(f"engine = {scene.render.engine}")

try:
    scene.eevee.taa_render_samples = 64
except AttributeError:
    pass
try:
    scene.eevee.use_raytracing = True
except AttributeError:
    pass

scene.render.resolution_x = 1500
scene.render.resolution_y = 1150
scene.render.film_transparent = False
scene.view_settings.view_transform = "Standard"
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = PNG

bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print(f"saved blend -> {BLEND}")

bpy.ops.render.render(write_still=True)
print(f"rendered -> {PNG}")
print("DONE")
