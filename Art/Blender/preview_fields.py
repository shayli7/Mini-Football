"""Renders the six field skins on the real Field mesh with the table's own pitch-line geometry
over them. Up is +Z (measured: field's thin axis is Z, figures sit above it), so this is a plain
overhead shot.

Run: blender --background --factory-startup --python preview_fields.py -- <fbx> <texdir> <blend> <png>
"""
import bpy, sys, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, TEX, BLEND, PNG = argv[0], argv[1], argv[2], argv[3]

SKINS = [
    ("Deep Blue",     "DeepBlue",     False),
    ("Indoor Court",  "IndoorCourt",  False),
    ("Wild West",     "WildWest",     False),
    ("Pitch Perfect", "PitchPerfect", False),
    ("Neon Rave",     "NeonRave",     True),
    ("Chessboard",    "Chessboard",   False),
]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)

field = bpy.data.objects["Field"]
lines = [o for o in bpy.data.objects
         if o.type == "MESH" and o.data.materials and o.data.materials[0]
         and o.data.materials[0].name == "Foos_Line"]

fb = [field.matrix_world @ Vector(c) for c in field.bound_box]
top_z = max(v.z for v in fb)
print(f"field top z = {top_z:.4f}, {len(lines)} pitch-line objects")

keep = [field] + lines

# The FBX hangs everything off a root empty that scales by 0.01 — the mesh data is in
# CENTIMETRES. Deleting that parent outright silently makes every kept object 100x too big and
# they sail straight out of frame. Clearing the parent while KEEPING the transform bakes the
# metre-scale world matrix into each object first.
bpy.ops.object.select_all(action="DESELECT")
for o in keep:
    o.select_set(True)
bpy.context.view_layer.objects.active = keep[0]
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")

for o in list(bpy.data.objects):
    if o not in keep:
        bpy.data.objects.remove(o, do_unlink=True)

bpy.context.view_layer.update()
fb = [field.matrix_world @ Vector(c) for c in field.bound_box]
span_x = max(v.x for v in fb) - min(v.x for v in fb)
assert 1.0 < span_x < 1.5, f"field is {span_x:.3f} across, expected ~1.2 m — scale went wrong"
top_z = max(v.z for v in fb)
print(f"after unparent: field spans {span_x:.3f} m, top z = {top_z:.4f}")


def make_material(name, emissive):
    mat = bpy.data.materials.new("FieldSkin_" + name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (600, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location = (300, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.location = (-300, 200)
    tex.image = bpy.data.images.load(f"{TEX}/FieldSkin_{name}_Albedo.png")
    tex.image.colorspace_settings.name = "sRGB"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.55
    if emissive:
        em = nt.nodes.new("ShaderNodeTexImage"); em.location = (-300, -240)
        em.image = bpy.data.images.load(f"{TEX}/FieldSkin_{name}_Emission.png")
        em.image.colorspace_settings.name = "sRGB"
        nt.links.new(em.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 1.6
        bsdf.inputs["Roughness"].default_value = 0.25
    return mat


COLS, GX, GY = 3, 1.34, 1.06
label_mat = bpy.data.materials.new("Label")
label_mat.use_nodes = True
lb = label_mat.node_tree.nodes["Principled BSDF"]
lb.inputs["Base Color"].default_value = (0.95, 0.97, 1.0, 1.0)
lb.inputs["Emission Color"].default_value = (0.95, 0.97, 1.0, 1.0)
lb.inputs["Emission Strength"].default_value = 2.2

for i, (label, tex_name, emissive) in enumerate(SKINS):
    col, row = i % COLS, i // COLS
    dx = (col - (COLS - 1) / 2) * GX
    dy = ((len(SKINS) // COLS - 1) / 2 - row) * GY

    f = field.copy(); f.data = field.data.copy()
    f.location = field.location + Vector((dx, dy, 0))
    f.data.materials.clear()
    f.data.materials.append(make_material(tex_name, emissive))
    bpy.context.collection.objects.link(f)

    for ln in lines:
        c = ln.copy(); c.data = ln.data.copy()
        c.location = ln.location + Vector((dx, dy, 0))
        bpy.context.collection.objects.link(c)

    bpy.ops.object.text_add(location=(dx, dy - 0.42, top_z + 0.01))
    t = bpy.context.object
    t.data.body = label
    t.data.align_x = "CENTER"
    t.data.size = 0.072
    t.data.materials.append(label_mat)   # text faces +Z already; camera is overhead

for o in keep:
    bpy.data.objects.remove(o, do_unlink=True)

world = bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.035, 0.04, 0.05, 1.0)

sun = bpy.data.lights.new("Sun", "SUN")
sun.energy = 3.0
so = bpy.data.objects.new("Sun", sun)
so.rotation_euler = (math.radians(22), math.radians(16), 0)
bpy.context.collection.objects.link(so)

fill = bpy.data.lights.new("Fill", "AREA")
fill.energy = 22; fill.size = 7
fo = bpy.data.objects.new("Fill", fill)
fo.location = (0, 0, top_z + 2.0)
bpy.context.collection.objects.link(fo)

cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 4.35
cam = bpy.data.objects.new("Cam", cam_data)
cam.location = (0, 0, top_z + 3.0)     # straight down; default camera looks along -Z
bpy.context.collection.objects.link(cam)
bpy.context.scene.camera = cam

sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
try:
    sc.eevee.taa_render_samples = 64
    sc.eevee.use_raytracing = True
except AttributeError:
    pass
sc.render.resolution_x = 1700
sc.render.resolution_y = 880
sc.view_settings.view_transform = "Standard"
sc.render.image_settings.file_format = "PNG"
sc.render.filepath = PNG
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
bpy.ops.render.render(write_still=True)
print("DONE")
