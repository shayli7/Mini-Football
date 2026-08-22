"""MINI FOOTBALL app icon -- the 3D pass.

    blender.exe -b --factory-startup -P render.py

Writes three 1024x1024 frames into out/render/:

    field_wide.png        the green field alone, opaque        | wide framing
    figure_wide.png       figure + rod + contact shadow, alpha | wide framing
    composite_tight.png   field + figure + rod, opaque         | tight framing

The first two are the two halves of an Android adaptive icon and MUST come from
one camera, or they stop registering when the launcher stacks them. The third is
a separate, closer camera for the legacy / round / store images, which are never
cropped to 66.7% and would look emptily zoomed-out at the wide framing.

Colours come from out/palette.json (`node build.mjs palette`), which is dumped
from lib/palette.mjs, which mirrors Assets/Scripts/UI/ArcadeTheme.cs. The
renderer does not get to invent a colour any more than the SVG marks do.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out", "render")
FBX = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "Models", "FoosballTable.fbx"))

# The one figure and the rod it rides. FigR.006 is the centre man on Rod_3 --
# picked because it is the only red figure with clear table on both sides of it,
# so nothing from the deleted geometry has to be faked back in.
FIGURE = "FigR.006"
ROD = "Rod_3"

# The figure ships with three materials -- Foos_Red, Foos_Skin, Foos_PlayerWood.
# The icon takes only the team colour and paints the whole man in it: a skin-tone
# head and a tan grip are two more shapes to resolve, and at 48px they turn the
# head into a smudge instead of reading as a head.
FIGURE_MAT = "Foos_Red"
ROD_MAT = "Foos_Metal"

RES = 1024
SAMPLES = 160

# How far under the figure's feet the pitch sits. On the real table the Field
# slab is 15mm below the men -- foosball figures hover, they do not stand -- and
# at icon scale that gap swallowed the contact shadow entirely and left the
# figure looking pasted on. The pitch is brought up to just under the feet so the
# shadow closes; 3mm keeps a hairline of air rather than fusing the two.
FOOT_CLEARANCE = 0.003

# Share of the frame the figure's bounding box is allowed to span.
#   wide  -- Android shows only the centre 66.7% of an adaptive layer, so the
#            figure has to live well inside that. 0.52 lands it at ~78% of the
#            visible tile after the crop, matching the flat pipeline's 0.78.
#   tight -- no such crop, so the subject can come forward.
FRAME_WIDE = 0.52
FRAME_TIGHT = 0.74

# A three-quarter view. Azimuth is measured off +X, the direction the red team
# attacks, so the camera sits in front of the figure rather than behind it, and
# the rod (which runs along Y) crosses the frame on a diagonal instead of lying
# flat like a horizon line.
AZIMUTH = 38.0
ELEVATION = 41.0
LENS_MM = 55.0

# The pitch has to outrun the frame in every direction. At this elevation a small
# plane shows its own far edge as a horizon, and a horizon turns a full-bleed
# green tile back into a photograph of a table.
FIELD_SIZE = 14.0
FIELD_FALLOFF = 0.42                      # metres from the figure to the darkest green


# --------------------------------------------------------------------- colour
def _load_palette():
    path = os.path.join(HERE, "out", "palette.json")
    if not os.path.exists(path):
        raise SystemExit("out/palette.json missing -- run: node build.mjs palette")
    with open(path, encoding="utf-8") as fh:
        raw = json.load(fh)
    return {**raw["P"], **raw["D"]}


PAL = _load_palette()


def lin(name, alpha=1.0):
    """A palette colour as linear RGBA. Blender's inputs are linear; the palette is sRGB."""
    h = PAL[name].lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (out[0], out[1], out[2], alpha)


# ---------------------------------------------------------------------- scene
def wipe():
    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.materials,
                 bpy.data.lights, bpy.data.cameras):
        for item in list(coll):
            coll.remove(item, do_unlink=True)


def world_bounds(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def fbx_base_color(material_name):
    """The base colour the model itself carries, as linear RGBA.

    The figure is not painted from lib/palette.mjs. ArcadeTheme's Red (#FF3355)
    is a HUD accent -- a rose used for borders and glows -- and putting it on a
    physical object rendered it pink. The team colour of an actual foosball man
    is Foos_Red (#BF353C), and it lives in the FBX. Reading it back from there
    means the icon cannot drift from the model the player sees in the game.
    """
    mat = bpy.data.materials.get(material_name)
    if mat is None:
        raise SystemExit("material %s not found in %s" % (material_name, FBX))
    if mat.use_nodes:
        for node in mat.node_tree.nodes:
            if node.type == "BSDF_PRINCIPLED":
                return tuple(node.inputs["Base Color"].default_value)
    return tuple(mat.diffuse_color)


def import_subject():
    """Import the table, keep the one figure and its rod, drop the other 152 objects."""
    bpy.ops.import_scene.fbx(filepath=FBX)
    keep = [bpy.data.objects.get(FIGURE), bpy.data.objects.get(ROD)]
    if not all(keep):
        raise SystemExit("%s / %s not found in %s" % (FIGURE, ROD, FBX))

    # Read the model's own colours before the slots are cleared below.
    colors = {name: fbx_base_color(name) for name in (FIGURE_MAT, ROD_MAT)}

    bpy.ops.object.select_all(action="DESELECT")
    for obj in keep:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = keep[0]
    # The FBX hangs everything off a Foosball_Table empty carrying the 0.01 scale
    # and the Y-up correction. Baking that in lets the empty be deleted with the rest.
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")

    for obj in list(bpy.data.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    for obj in keep:
        obj.data.materials.clear()
        # The FBX ships flat-shaded low-poly parts. Smoothing by angle rounds the
        # rod and softens the torso without melting the crisp edges.
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    return keep[0], keep[1], colors


def principled(name, values):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    for key, value in values.items():
        bsdf.inputs[key].default_value = value
    return mat


def field_material(target, z):
    """Pitch green, brighter under the figure and falling off to the tile edge.

    The gradient is doing real work: a flat green makes a full-bleed tile read as
    a sticker and the figure loses its footing. The falloff puts a soft pool of
    light exactly where the subject stands.

    It is driven off world position, not the plane's own UVs, so FIELD_SIZE can
    grow to whatever it takes to clear the frame without stretching the pool of
    light along with it. The falloff radius stays in metres either way.
    """
    mat = bpy.data.materials.new("Icon_Field")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.74
    bsdf.inputs["Specular IOR Level"].default_value = 0.18

    geo = nt.nodes.new("ShaderNodeNewGeometry")
    offset = nt.nodes.new("ShaderNodeVectorMath")
    offset.operation = "SUBTRACT"
    offset.inputs[1].default_value = (target.x, target.y, z)
    dist = nt.nodes.new("ShaderNodeVectorMath")
    dist.operation = "LENGTH"
    scale = nt.nodes.new("ShaderNodeMath")
    scale.operation = "DIVIDE"
    scale.inputs[1].default_value = FIELD_FALLOFF
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = lin("pitchLit")
    ramp.color_ramp.elements[1].position = 1.0
    ramp.color_ramp.elements[1].color = lin("pitch")

    nt.links.new(geo.outputs["Position"], offset.inputs[0])
    nt.links.new(offset.outputs["Vector"], dist.inputs[0])
    nt.links.new(dist.outputs["Value"], scale.inputs[0])
    nt.links.new(scale.outputs["Value"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def build_field(target, z):
    """A plane of our own, not the FBX Field slab.

    The slab is 1.2 x 0.66 and its far edge reads as a horizon at these camera
    angles. The brief asks for green to the tile edge, so the ground has to
    outrun the frame in every direction.
    """
    bpy.ops.mesh.primitive_plane_add(size=FIELD_SIZE, location=(0, 0, z))
    field = bpy.context.active_object
    field.name = "Icon_Field"
    field.data.materials.append(field_material(target, z))
    return field


def build_lights(target):
    """Soft key, cool fill, low rim. Matte toy shading: shape from light, not gloss.

    Lights are placed in the same polar terms as the camera and offset from
    AZIMUTH, because where a light sits *relative to the lens* is what decides
    whether its shadow is visible at all. An earlier pass put the key a few
    degrees off the camera axis; the figure's shadow fell squarely behind the
    figure, which read -- wrongly -- as the figure having no shadow.

    Energies are low on purpose. Under the Standard view transform there is no
    highlight rolloff to rescue an over-lit frame: the first render clipped the
    red to pink and the rod to paper white.
    """
    def area(name, d_az, elevation, distance, size, energy, color=(1.0, 1.0, 1.0)):
        data = bpy.data.lights.new(name, type="AREA")
        data.size = size
        data.energy = energy
        data.color = color
        obj = bpy.data.objects.new(name, data)
        az, el = math.radians(AZIMUTH + d_az), math.radians(elevation)
        obj.location = target + Vector((
            math.cos(el) * math.cos(az),
            math.cos(el) * math.sin(az),
            math.sin(el),
        )) * distance
        obj.rotation_euler = (obj.location - target).to_track_quat("Z", "Y").to_euler()
        bpy.context.collection.objects.link(obj)
        return obj

    # Key a quarter-turn off the lens, so the shadow rakes across open pitch
    # beside the figure instead of hiding underneath it. Small, for a shadow with
    # a defined edge -- a broad key gave an even wash and no contact at all.
    area("Key", -68.0, 38.0, 0.50, 0.26, 7.0)
    # Fill returns from the opposite side and from high up: enough to open the
    # shade, never low enough to fill in the key's shadow.
    area("Fill", 96.0, 52.0, 0.60, 0.70, 1.6, color=(0.80, 0.88, 1.0))
    # Rim sits behind the figure and catches the crown of the head and one
    # shoulder -- the two edges that keep the silhouette legible against green.
    area("Rim", 172.0, 26.0, 0.45, 0.34, 4.0)

    world = bpy.data.worlds.new("Icon_World")
    world.use_nodes = True
    # A dim, slightly cool ambient so the shadow side never reaches pure black --
    # a black-cored figure disappears against a dark launcher wallpaper.
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.035, 0.045, 0.055, 1.0)
    bg.inputs["Strength"].default_value = 1.0
    bpy.context.scene.world = world


def build_camera():
    data = bpy.data.cameras.new("Icon_Cam")
    data.lens = LENS_MM
    data.sensor_width = 36.0
    cam = bpy.data.objects.new("Icon_Cam", data)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam


def aim(cam, target, distance):
    az, el = math.radians(AZIMUTH), math.radians(ELEVATION)
    cam.location = target + Vector((
        math.cos(el) * math.cos(az),
        math.cos(el) * math.sin(az),
        math.sin(el),
    )) * distance
    cam.rotation_euler = (cam.location - target).to_track_quat("Z", "Y").to_euler()
    bpy.context.view_layer.update()


def frame_on(cam, target, subject, fraction):
    """Dolly the camera until `subject` spans `fraction` of the square frame.

    Solved rather than hand-placed: the figure's size is fixed by the FBX, so a
    hand-tuned distance would silently drift the day the model changes.
    """
    scene = bpy.context.scene
    lo, hi = world_bounds(subject)
    corners = [Vector((x, y, z))
               for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]

    distance = 0.45
    for _ in range(24):
        aim(cam, target, distance)
        ndc = [world_to_camera_view(scene, cam, c) for c in corners]
        span = 2.0 * max(max(abs(p.x - 0.5), abs(p.y - 0.5)) for p in ndc)
        if abs(span - fraction) < 0.002:
            break
        distance *= span / fraction
    return distance


# --------------------------------------------------------------------- render
def configure_render(preview):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48 if preview else SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.denoiser = "OPENIMAGEDENOISE"
    scene.render.resolution_x = 512 if preview else RES
    scene.render.resolution_y = 512 if preview else RES
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.compression = 90
    # Standard, not AgX. AgX would desaturate #FF3355 into a red the game's UI
    # does not contain, and the icon has to match ArcadeTheme exactly.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"


def shoot(name, transparent=False):
    scene = bpy.context.scene
    scene.render.film_transparent = transparent
    scene.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)
    print("  out/render/%s  %dx%d%s" % (name, scene.render.resolution_x,
                                        scene.render.resolution_y,
                                        "  (alpha)" if transparent else ""))


def main(preview):
    os.makedirs(OUT, exist_ok=True)
    wipe()
    configure_render(preview)

    figure, rod, colors = import_subject()
    lo, hi = world_bounds(figure)
    target = (lo + hi) / 2.0

    figure.data.materials.append(principled("Icon_Figure", {
        "Base Color": colors[FIGURE_MAT],
        "Roughness": 0.52,
        "Metallic": 0.0,
        "Specular IOR Level": 0.32,
    }))
    rod.data.materials.append(principled("Icon_Rod", {
        "Base Color": colors[ROD_MAT],
        "Metallic": 1.0,
        "Roughness": 0.34,
    }))

    field = build_field(target, lo.z - FOOT_CLEARANCE)
    build_lights(target)
    cam = build_camera()

    if preview:
        # One small, cheap frame for judging framing, exposure and colour. The
        # adaptive pair only matters once those three are settled.
        frame_on(cam, target, figure, FRAME_TIGHT)
        shoot("preview.png")
        return

    # --- adaptive pair: one camera, two passes ------------------------------
    frame_on(cam, target, figure, FRAME_WIDE)

    figure.hide_render = rod.hide_render = True
    field.is_shadow_catcher = False
    shoot("field_wide.png")

    figure.hide_render = rod.hide_render = False
    field.is_shadow_catcher = True          # keeps the contact shadow on an alpha frame
    shoot("figure_wide.png", transparent=True)

    # --- legacy / round / store: closer, fully composited -------------------
    field.is_shadow_catcher = False
    frame_on(cam, target, figure, FRAME_TIGHT)
    shoot("composite_tight.png")


# Blender hands the script everything after `--`; `-- preview` is the fast path.
main(preview="preview" in sys.argv)
