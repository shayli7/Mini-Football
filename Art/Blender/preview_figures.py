"""Renders the figure skins on the real figure mesh, a red and a blue of each, so the team
split can be judged at the same time as the skin.

Run: blender --background --factory-startup --python preview_figures.py -- <fbx> <texdir> <blend> <png>
"""
import bpy
import sys
import math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, TEX, BLEND, PNG = argv[0], argv[1], argv[2], argv[3]

SKIN = (0.855, 0.68, 0.48, 1.0)
RED = (0.52, 0.035, 0.045, 1.0)
BLUE = (0.045, 0.085, 0.52, 1.0)


def rgb(hexs):
    h = hexs.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)) + (1.0,)


# name, body(tex|colour), kit(tex-stem|None -> flat team colour), head(tex|colour)
ENTRIES = [
    ("Yellow",          None,               rgb("#E8C21C"),         SKIN),
    ("Black",           None,               rgb("#1E1E22"),         SKIN),
    ("Green",           None,               rgb("#2E8B3A"),         SKIN),
    ("Stripes",         rgb("#8A5F33"),     "Stripes",              SKIN),
    ("Gladiators",      "Gladiators_Body",  "Gladiators",           "Gladiators_Head"),
    ("Robots",          "Robots_Body",      "Robots",               "Robots_Head"),
    ("Trojan Warriors", "Trojan_Body",      "Trojan",               "Trojan_Head"),
    ("Trojan  Keeper",  "TrojanKeeper_Body", "TrojanKeeper",        "TrojanKeeper_Head"),
]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)

templates = [bpy.data.objects["FigB.001"]]
# Unparent keeping the transform: the FBX root empty scales by 0.01 (mesh data is in centimetres),
# so deleting it outright leaves everything 100x too big and out of frame.
bpy.ops.object.select_all(action="DESELECT")
for t in templates:
    t.select_set(True)
bpy.context.view_layer.objects.active = templates[0]
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")

for o in list(bpy.data.objects):
    if o not in templates:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.update()

fig = templates[0]
# Recentre the mesh on its own origin. The figure's vertices carry its position on the table, so
# rotating the object would swing the geometry on a long arm and out of frame — the same trap the
# ball skins hit. Centred first, rotation and placement behave normally.
c = sum((v.co for v in fig.data.vertices), Vector()) / len(fig.data.vertices)
fig.data.transform(Matrix.Translation(-c))
fig.location = (0, 0, 0)
# The object's ROTATION is what stands the figure upright — its local up is not Z. Zeroing it lays
# the figure on its side (measured: world height drops from 0.1255 to 0.054), so it is left alone
# and only the translation is used for the grid below.
bpy.context.view_layer.update()
# WORLD height, not Object.dimensions — that reports the local bounding box scaled, ignoring
# rotation, so it returned the figure's 0.054 depth rather than its 0.126 height.
wb = [fig.matrix_world @ Vector(b) for b in fig.bound_box]
height = max(v.z for v in wb) - min(v.z for v in wb)
print(f"figure world height {height:.4f} m, verts {len(fig.data.vertices)}")
assert height > 0.10, f"figure is {height:.4f} m tall — expected ~0.126, orientation is wrong"


def image_mat(name, stem):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (500, 0)
    b = nt.nodes.new("ShaderNodeBsdfPrincipled"); b.location = (250, 0)
    nt.links.new(b.outputs["BSDF"], out.inputs["Surface"])
    t = nt.nodes.new("ShaderNodeTexImage"); t.location = (-250, 0)
    t.image = bpy.data.images.load(f"{TEX}/FigureSkin_{stem}.png")
    t.image.colorspace_settings.name = "sRGB"
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    lower = stem.lower()
    metal = 0.85 if ("robot" in lower or "keeper" in lower) else (0.5 if "trojan" in lower or "glad" in lower else 0.0)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = 0.30 if metal > 0.6 else 0.55
    return m


def flat_mat(name, colour, metal=0.0, rough=0.55):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = colour
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    return m


# The model's own three slot colours, used when a skin dresses only some of them.
BODY_DEFAULT = (0.6, 0.44, 0.26, 1.0)      # Foos_PlayerWood


def slot_mat(tag, spec, fallback):
    """spec: None -> the model's own colour for that slot, a str -> a texture stem, a tuple -> a
    flat colour. The fallback is passed in PER SLOT — the previous version defaulted every slot to
    the team colour, which was only ever right for the kit."""
    if spec is None:
        return flat_mat(f"{tag}_def", fallback)
    if isinstance(spec, str):
        return image_mat(f"{tag}_{spec}", spec)
    return flat_mat(f"{tag}_flat", spec)


COLS, GX, GZ, PAIR = 4, 0.185, 0.235, 0.062
label_mat = bpy.data.materials.new("Label")
label_mat.use_nodes = True
lb = label_mat.node_tree.nodes["Principled BSDF"]
lb.inputs["Base Color"].default_value = (0.95, 0.97, 1.0, 1.0)
lb.inputs["Emission Color"].default_value = (0.95, 0.97, 1.0, 1.0)
lb.inputs["Emission Strength"].default_value = 2.0

for i, (label, body, kit, head) in enumerate(ENTRIES):
    col, row = i % COLS, i // COLS
    cx = (col - (COLS - 1) / 2) * GX
    cz = ((len(ENTRIES) // COLS - 1) / 2 - row) * GZ

    for k, team in enumerate(("Red", "Blue")):
        o = fig.copy()
        o.data = fig.data.copy()
        o.location = (cx + (k - 0.5) * PAIR, 0, cz)
        bpy.context.collection.objects.link(o)

        # A str kit is a texture stem and needs the team suffix; a tuple is a flat jersey colour and
        # is passed through untouched.
        kit_spec = f"{kit}_Kit{team}" if isinstance(kit, str) else kit

        # Assigned INTO the existing three slots. materials.clear() was measured to reset every
        # polygon's material_index to 0 (histogram went from {0:38, 1:180, 2:160} to {0:378}),
        # which collapsed the kit and head onto the body material and made the whole figure one
        # flat colour.
        o.data.materials[0] = slot_mat(f"{label}{team}_b", body, BODY_DEFAULT)
        o.data.materials[1] = slot_mat(f"{label}{team}_k", kit_spec,
                                       RED if team == "Red" else BLUE)
        o.data.materials[2] = slot_mat(f"{label}{team}_h", head, SKIN)

    bpy.ops.object.text_add(location=(cx, 0.25, cz - height * 0.78))
    t = bpy.context.object
    t.data.body = label
    t.data.align_x = "CENTER"
    t.data.size = 0.017
    t.rotation_euler = (math.radians(90), 0, 0)
    t.data.materials.append(label_mat)

bpy.data.objects.remove(fig, do_unlink=True)

world = bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.07, 1.0)


def area(name, loc, rot, size, energy, colour=(1, 1, 1)):
    d = bpy.data.lights.new(name, "AREA")
    d.energy = energy; d.size = size; d.color = colour
    o = bpy.data.objects.new(name, d)
    o.location = loc; o.rotation_euler = rot
    bpy.context.collection.objects.link(o)


area("Key",  (-0.5, -0.8, 0.7), (math.radians(48), 0, math.radians(-32)), 1.2, 24)
area("Fill", (0.7, -0.7, 0.1),  (math.radians(85), 0, math.radians(42)),  1.0, 8, (0.85, 0.92, 1.0))
area("Rim",  (0.0, 0.8, 0.5),   (math.radians(-55), 0, 0),                1.2, 11, (1.0, 0.94, 0.85))

cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 0.86
cam = bpy.data.objects.new("Cam", cam_data)
cam.location = (0.0, -1.2, -0.018)
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.collection.objects.link(cam)
bpy.context.scene.camera = cam

sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
try:
    sc.eevee.taa_render_samples = 96
    sc.eevee.use_raytracing = True
except AttributeError:
    pass
sc.render.resolution_x = 1700
sc.render.resolution_y = 1080
sc.view_settings.view_transform = "Standard"
sc.render.image_settings.file_format = "PNG"
sc.render.filepath = PNG
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
bpy.ops.render.render(write_still=True)
print("DONE")
