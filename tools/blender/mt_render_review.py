"""Review renders of the exported FBX models (Eevee, headless).

  blender -b --factory-startup --python tools/blender/mt_render_review.py -- --out docs/qa/<date> [--views eye,top,suitcase,contact]

Imports Art/Models/**.fbx, rebuilds palette materials (tint x Poly Haven diffuse when present), places sample
products on the SLOT empties (zone mapping of OVERHAUL_PLAN §3) and renders fixed cameras:
  art-eye-front / art-eye-left / art-eye-right (player eye 0,1.62,-0.05), art-top, art-suitcase-open / -closed,
  art-contact-hanging, art-contact-jewellery, art-contact-stage.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mt_layout as L  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "MultiTravelValizChallenge", "Assets", "MultiTravel", "Art")
TEX = os.path.join(REPO, "MultiTravelValizChallenge", "Assets", "MultiTravel", "ThirdParty", "PolyHaven", "Textures")
MODELS = os.path.join(ART, "Models")

ENV_SCENE = ["stage-backdrop", "stage-floor", "floor-mat", "stopwatch", "scoreboard", "luggage-rack", "suitcase-open",
             "wardrobe-carcass", "wardrobe-hanging-module", "wardrobe-folded-module", "wardrobe-door-left",
             "wardrobe-door-right", "console-table-business", "console-table-leisure"]
ZONE_ITEMS = {
    "hanging": ["shirt-hanging", "blouse-hanging", "jacket-hanging", "blazer-hanging", "dress-hanging", "swimsuit-hanging", "bikini-hanging"],
    "folded": ["men-trousers", "women-trousers", "men-tshirt", "women-tshirt", "socks", "swim-shorts", "beach-towel"],
    "shoes": ["men-shoes", "women-shoes", "flip-flops"],
    "accessories": ["tie", "toiletry-bag", "tie", "toiletry-bag"],
    "jewellery": ["wristwatch", "cufflinks", "pearl-earrings", "minimal-necklace", "shell-necklace", "party-tiara"],
    "business": ["laptop", "laptop-bag", "laptop-charger", "phone-cable", "phone", "notebook", "pen", "id-card", "passport", "headphones"],
    "leisure": ["snorkel-mask", "kids-book", "neck-pillow", "travel-bag", "beach-towel"],
}
PALETTE = json.load(open(os.path.join(ART, "materials.json"), encoding="utf-8"))["materials"]
_images = {}


def hex_rgb(h):
    return (int(h[0:2], 16) / 255, int(h[2:4], 16) / 255, int(h[4:6], 16) / 255)


def srgb_to_linear(c):
    return tuple((v / 12.92) if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)


def setup_material(mat):
    key = mat.name.split(".")[0]
    spec = PALETTE.get(key)
    if spec is None or mat.get("mt_done"):
        return
    mat["mt_done"] = True
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tint = srgb_to_linear(hex_rgb(spec["tint"]))
    bsdf.inputs["Base Color"].default_value = (*tint, 1)
    bsdf.inputs["Metallic"].default_value = spec.get("metallic", 0)
    bsdf.inputs["Roughness"].default_value = 1 - spec.get("smoothness", 0.4)
    if spec.get("emission"):
        bsdf.inputs["Emission Color"].default_value = (*srgb_to_linear(hex_rgb(spec["emission"])), 1)
        bsdf.inputs["Emission Strength"].default_value = spec.get("emissionIntensity", 1) * 2
    if spec.get("alpha", 1) < 1:
        bsdf.inputs["Alpha"].default_value = spec["alpha"]
        mat.surface_render_method = "BLENDED"
    texid = spec.get("texture")
    if texid and spec.get("albedo", True):
        path = os.path.join(TEX, texid, f"{texid}_diff_1k.jpg")
        if os.path.exists(path):
            img = _images.get(path)
            if img is None:
                img = bpy.data.images.load(path)
                _images[path] = img
            tex = nt.nodes.new("ShaderNodeTexImage")
            tex.image = img
            mapping = nt.nodes.new("ShaderNodeMapping")
            uvn = nt.nodes.new("ShaderNodeUVMap")
            s = 1.0 / max(1e-3, spec.get("tile", 1))
            mapping.inputs["Scale"].default_value = (s, s, s)
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = "MULTIPLY"
            mix.inputs["Factor"].default_value = 1.0
            nt.links.new(uvn.outputs["UV"], mapping.inputs["Vector"])
            nt.links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
            nt.links.new(tex.outputs["Color"], mix.inputs[6])
            mix.inputs[7].default_value = (*tint, 1)
            # desaturate strongly-coloured scans when a tint is given (importer does the same)
            if spec["tint"].upper() != "FFFFFF":
                hs = nt.nodes.new("ShaderNodeHueSaturation")
                hs.inputs["Saturation"].default_value = 0.15
                hs.inputs["Value"].default_value = 1.25
                nt.links.new(tex.outputs["Color"], hs.inputs["Color"])
                nt.links.new(hs.outputs["Color"], mix.inputs[6])
            nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])


def import_fbx(name, folder=None):
    """Imports one model; returns its objects parented under a 180-deg Z rotation so they sit in the authoring frame."""
    folder = folder or ("Environment" if os.path.exists(os.path.join(MODELS, "Environment", name + ".fbx")) else "Products")
    path = os.path.join(MODELS, folder, name + ".fbx")
    if not os.path.exists(path):
        print("MISSING", path)
        return None
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    objs = [o for o in bpy.data.objects if o not in before]
    root = bpy.data.objects.new(name + ".root", None)
    bpy.context.scene.collection.objects.link(root)
    root.rotation_euler = (0, 0, math.pi)
    for o in objs:
        if o.parent is None:
            o.parent = root
        for slot in o.material_slots:
            if slot.material:
                setup_material(slot.material)
    return root


def find(root, prefix):
    out = []
    stack = list(root.children)
    while stack:
        o = stack.pop()
        if o.name.split(".0")[0].startswith(prefix) or o.name.startswith(prefix):
            out.append(o)
        stack.extend(o.children)
    return out


def place_products(roots):
    bpy.context.view_layer.update()
    counters = {}
    for root in roots:
        for e in find(root, "SLOT."):
            zone = e.name.split(".")[1]
            items = ZONE_ITEMS.get(zone)
            if not items:
                continue
            i = counters.get(zone, 0)
            counters[zone] = i + 1
            pid = items[i % len(items)]
            prod = import_fbx(pid, "Products")
            if prod is None:
                continue
            # imported empties keep Unity axes (local Y = up, Z = forward); the product's import root wraps
            # children that carry the importer's X+90 conversion, so align the wrapper with slot @ Rx(-90).
            prod.matrix_world = e.matrix_world @ Matrix.Rotation(-math.pi / 2, 4, "X")
    return counters


def add_camera(name, loc, look_dir, fov_deg=95, ortho=None):
    cam = bpy.data.cameras.new(name)
    if ortho:
        cam.type = "ORTHO"
        cam.ortho_scale = ortho
    else:
        cam.lens_unit = "FOV"
        cam.angle = math.radians(fov_deg)
    cam.clip_start = 0.02
    obj = bpy.data.objects.new(name, cam)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = loc
    d = Vector(look_dir).normalized()
    obj.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return obj


def add_light(name, kind, loc, energy, color=(1, 1, 1), size=1.0, look=None, spot=60):
    li = bpy.data.lights.new(name, kind)
    li.energy = energy
    li.color = color
    if kind == "AREA":
        li.size = size
    if kind == "SPOT":
        li.spot_size = math.radians(spot)
        li.spot_blend = 0.5
    obj = bpy.data.objects.new(name, li)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = loc
    if look is not None:
        obj.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    return obj


def setup_scene(res=(1600, 1000)):
    _images.clear()
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.image_settings.file_format = "PNG"
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.eevee.taa_render_samples = 24
    sc.view_settings.exposure = -0.6
    sc.eevee.use_shadows = True
    world = bpy.data.worlds.new("w")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.02, 0.025, 0.035, 1)
    bg.inputs[1].default_value = 1.0


def stage_lights():
    add_light("key", "AREA", (0, 0.35, 2.75), 260, (1, 0.96, 0.9), size=1.2, look=(0, 0.4, 0.6))
    add_light("fill_l", "AREA", (-1.3, 0.1, 2.4), 80, (0.9, 0.95, 1), size=1.0, look=(-0.6, 0.4, 1.0))
    add_light("fill_r", "AREA", (1.3, 0.1, 2.4), 80, (0.9, 0.95, 1), size=1.0, look=(0.6, 0.4, 1.0))
    add_light("wash", "AREA", (0, 1.0, 2.7), 60, (0.8, 0.9, 1), size=2.5, look=(0, 1.9, 1.4))
    add_light("suit", "SPOT", (0, -0.3, 2.5), 400, (1, 0.97, 0.92), look=(0, 0.4, 0.7), spot=50)
    add_light("back", "AREA", (0, -1.5, 2.2), 60, (1, 1, 1), size=2.0, look=(0, 0, 1))


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("MT_RENDER", path)


def view_scene(out, views):
    setup_scene()
    stage_lights()
    roots = [r for r in (import_fbx(n, "Environment") for n in ENV_SCENE) if r is not None]
    for r in roots:
        if r.name.startswith("suitcase-open"):
            r.location = (0, L.SUITCASE_CENTRE_Y, L.RACK_TOP)
    counts = place_products(roots)
    print("placed", counts)
    eye = (0, -0.05, 1.62)
    if "eye" in views:
        for nm, yaw in (("front", 0), ("left", -60), ("right", 60)):
            a = math.radians(yaw)
            cam = add_camera("eye_" + nm, eye, (math.sin(a), math.cos(a), -0.12), fov_deg=100)
            bpy.context.scene.camera = cam
            render(os.path.join(out, f"art-eye-{nm}.png"))
    if "top" in views:
        cam = add_camera("top", (0, 0.5, 6), (0, 0.0001, -1), ortho=4.2)
        bpy.context.scene.camera = cam
        hidden = [o for o in bpy.data.objects if o.type == "MESH" and (o.name.startswith("top") or o.name.startswith("crown") or o.name.startswith("hat_shelf") or o.name.startswith("cornice"))]
        for o in hidden:
            o.hide_render = True
        render(os.path.join(out, "art-top.png"))
        for o in hidden:
            o.hide_render = False
        cam = add_camera("iso", (-2.6, -2.6, 2.6), (2.6, 3.1, -1.8), fov_deg=55)
        bpy.context.scene.camera = cam
        render(os.path.join(out, "art-overview.png"))
    if "doors" in views:
        for nm, pos, look in (("door-left", (0.1, -0.1, 1.5), (-0.75, -0.1, -0.35)), ("door-right", (-0.1, -0.1, 1.5), (0.75, -0.1, -0.35)),
                              ("wing-left", (0.25, -0.2, 1.55), (-0.7, 0.55, -0.3)), ("wing-right", (-0.25, -0.2, 1.55), (0.7, 0.55, -0.3))):
            cam = add_camera(nm, pos, look, fov_deg=70)
            bpy.context.scene.camera = cam
            render(os.path.join(out, f"art-{nm}.png"))
    if "suitcase" in views:
        cam = add_camera("suit", (0.35, -0.55, 1.45), (-0.3, 0.95, -0.85), fov_deg=60)
        bpy.context.scene.camera = cam
        render(os.path.join(out, "art-suitcase-open.png"))
        suit = next(r for r in roots if r.name.startswith("suitcase-open"))
        piv = find(suit, "PIVOT.lid")[0]
        piv.matrix_basis = piv.matrix_basis @ Matrix.Rotation(math.radians(100), 4, "X")
        render(os.path.join(out, "art-suitcase-closed.png"))
        piv.matrix_basis = piv.matrix_basis @ Matrix.Rotation(math.radians(-100), 4, "X")


def contact_scene(out, names, fname, cols=4, cell=0.5, hanging=False):
    setup_scene((1800, 1200))
    add_light("k", "AREA", (1, -2, 3), 900, size=2, look=(0.8, 0.5, 0.2))
    add_light("f", "AREA", (-2, -1, 2), 400, size=2, look=(0.8, 0.5, 0.2))
    add_light("r", "AREA", (0, 3, 2), 300, size=2, look=(0.8, 0.5, 0.2))
    rows = math.ceil(len(names) / cols)
    row_h = cell * (1.25 if hanging else 1.0)
    for i, n in enumerate(names):
        r, c = divmod(i, cols)
        x, y = c * cell, -r * row_h
        root = import_fbx(n, "Products")
        if root is None:
            continue
        root.location = (x, y, cell * 0.95 if hanging else 0)
        curve = bpy.data.curves.new("t" + n, "FONT")
        curve.body = n
        curve.size = cell * 0.09
        curve.align_x = "CENTER"
        t = bpy.data.objects.new("t" + n, curve)
        bpy.context.scene.collection.objects.link(t)
        if hanging:
            t.location = (x, y - 0.05, 0.01)
            t.rotation_euler = (math.radians(90), 0, 0)
        else:
            t.location = (x, y - cell * 0.4, 0.001)
    cx = (cols - 1) * cell / 2
    cy = -(rows - 1) * row_h / 2
    bpy.ops.mesh.primitive_plane_add(size=30, location=(cx, cy, -0.001))
    if hanging:
        target = Vector((cx, cy, cell * 0.5))
        cam_pos = Vector((cx, cy - cols * cell * 1.6, cell * 0.6))
    else:
        target = Vector((cx, cy - cell * 0.1, 0.0))
        cam_pos = Vector((cx, cy - cols * cell * 0.9, cols * cell * 0.95))
    cam = add_camera("c", tuple(cam_pos), tuple(target - cam_pos), fov_deg=45)
    bpy.context.scene.camera = cam
    render(os.path.join(out, fname))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(REPO, "docs", "qa", "review")
    views = argv[argv.index("--views") + 1].split(",") if "--views" in argv else ["eye", "top", "doors", "suitcase", "contact"]
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if any(v in views for v in ("eye", "top", "suitcase", "doors")):
        view_scene(out, views)
    if "contact" in views:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        contact_scene(out, ZONE_ITEMS["hanging"] + ["hanger"], "art-contact-hanging.png", cols=4, cell=0.55, hanging=True)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        contact_scene(out, ZONE_ITEMS["jewellery"] + ["practice-tag", "laptop", "laptop-bag", "phone"], "art-contact-jewellery.png", cols=5, cell=0.26)
    print("MT_RENDER_DONE")


main()
