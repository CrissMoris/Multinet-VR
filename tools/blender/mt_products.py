"""Product models (one FBX per catalog product id). Axes: X right, Y forward (garment collar / shoe toe), Z up."""
import math

from mathutils import Vector

import bmesh
from mtlib import (arc_points, box, bake, cylinder, export_asset, extrude_outline, from_bmesh, mirror_x, rotate,
                   rounded_rect, soft_body, sphere, sweep, text, torus, translate, add_bevel, add_subsurf)

BUILDERS = {}


def builder(name):
    def wrap(fn):
        BUILDERS[name] = fn
        return fn
    return wrap


def lerp_profile(t, keys, values):
    for i in range(len(keys) - 1):
        if keys[i] <= t <= keys[i + 1]:
            f = (t - keys[i]) / (keys[i + 1] - keys[i])
            f = f * f * (3 - 2 * f)
            return values[i] + (values[i + 1] - values[i]) * f
    return values[-1]


# --------------------------------------------------------------------------------------------- garments

def folded_top(name, mat, width=0.27, length=0.36, thick=0.035, collar="shirt", buttons=0, button_mat="button_pearl",
               pocket=False, lapels=False, lapel_mat=None, flap_pockets=False, cuff=None):
    """Retail-folded upper garment: shirt, blouse, t-shirt, jacket."""
    w, l, t = width, length, thick
    parts = [box("body", (w, l, t), (0, 0, t / 2))]
    # shoulder roll of the sleeves folded behind, and the bottom hem folded under
    for s in (-1, 1):
        parts.append(box("shoulder", (0.035, l * 0.78, t * 0.35), (s * (w / 2 - 0.02), 0.02, t + t * 0.05)))
    parts.append(box("hem", (w * 0.97, 0.05, t * 0.25), (0, -l / 2 + 0.03, t)))
    neck_y = l / 2 - 0.07
    if collar == "shirt":
        parts.append(torus("collar", 0.058, 0.010, (0, neck_y, t + 0.006), mat=mat, scale=(1, 2.0), segments=10))
        for s in (-1, 1):
            parts.append(box("collar_point", (0.045, 0.055, 0.007), (s * 0.03, neck_y - 0.06, t + 0.005), (0, 0, math.radians(s * 28))))
        parts.append(box("placket", (0.024, l - 0.14, 0.004), (0, -0.07, t + 0.001)))
    elif collar == "crew":
        parts.append(torus("rib", 0.062, 0.008, (0, neck_y + 0.01, t + 0.002), mat=mat, scale=(1.6, 1.0), segments=10))
    elif collar == "vneck":
        for s in (-1, 1):
            parts.append(box("vneck_edge", (0.012, 0.11, 0.006), (s * 0.03, neck_y - 0.02, t + 0.002), (0, 0, math.radians(-s * 20))))
        parts.append(torus("neck_back", 0.055, 0.007, (0, neck_y + 0.01, t + 0.002), mat=mat, start=10, end=170, steps=20, scale=(1.4, 1)))
    if lapels:
        for s in (-1, 1):
            parts.append(box("lapel", (0.06, 0.17, 0.008), (s * 0.045, neck_y - 0.075, t + 0.004), (0, 0, math.radians(-s * 18))))
        parts.append(torus("jacket_collar", 0.06, 0.012, (0, neck_y + 0.005, t + 0.006), mat=mat, start=0, end=180, steps=20, scale=(1, 1.8)))
    if pocket:
        parts.append(box("pocket", (0.06, 0.065, 0.004), (0.065, l / 2 - 0.17, t + 0.001)))
    if flap_pockets:
        for s in (-1, 1):
            parts.append(box("flap", (0.075, 0.025, 0.006), (s * 0.075, -l / 2 + 0.12, t + 0.002)))
        parts.append(box("chest_welt", (0.055, 0.012, 0.004), (-0.07, l / 2 - 0.16, t + 0.001)))
    body = soft_body(name + "_fabric", parts, mat, voxel=0.0028, smooth=5, wrinkle=0.0022, wrinkle_scale=0.035, target_tris=7000)
    body.name = name
    top = t + 0.0035
    if collar == "shirt":
        for i in range(buttons):
            cylinder("button", 0.0052, 0.0028, (0, neck_y - 0.07 - i * 0.055, top), mat=button_mat, segments=14, bevel=0.0008)
    elif buttons:
        for i in range(buttons):
            y = neck_y - 0.17 - i * 0.07 if lapels else neck_y - 0.06 - i * 0.05
            cylinder("button", 0.0085 if lapels else 0.005, 0.003, (0, y, top + (0.002 if lapels else 0)), mat=button_mat, segments=16, bevel=0.0008)


def folded_trousers(name, mat, width=0.22, length=0.40, thick=0.036, belt_loops=True):
    w, l, t = width, length, thick
    parts = [box("legs", (w, l, t), (0, 0, t / 2)),
             box("waistband", (w, 0.045, 0.008), (0, l / 2 - 0.0225, t + 0.002)),
             box("fold_edge", (w * 0.98, 0.04, 0.006), (0, -l / 2 + 0.02, t + 0.001)),
             box("crease", (0.006, l * 0.8, 0.003), (w * 0.22, -0.03, t + 0.001)),
             box("fly", (0.018, 0.11, 0.003), (-0.03, l / 2 - 0.1, t + 0.002))]
    if belt_loops:
        for x in (-0.085, -0.03, 0.03, 0.085):
            parts.append(box("loop", (0.009, 0.05, 0.006), (x, l / 2 - 0.023, t + 0.007)))
    for s in (-1, 1):
        parts.append(box("pocket_slant", (0.008, 0.07, 0.003), (s * (w / 2 - 0.03), l / 2 - 0.08, t + 0.001), (0, 0, math.radians(s * 20))))
    soft_body(name, parts, mat, voxel=0.0028, smooth=5, wrinkle=0.002, wrinkle_scale=0.04, target_tris=6000)
    cylinder("button", 0.0065, 0.003, (0, l / 2 - 0.022, t + 0.0085), mat="button_horn", segments=16, bevel=0.0008)


def flat_garment(name, mat, outline, thick=0.02, wrinkle=0.002, extra=None, target_tris=5000):
    """Flat-lay garment from a 2D outline (dress, swimwear, socks, tie)."""
    parts = [extrude_outline("shape", outline, thick, mat)]
    if extra:
        parts += extra
    return soft_body(name, parts, mat, voxel=0.0025, smooth=6, wrinkle=wrinkle, wrinkle_scale=0.03, target_tris=target_tris)


def mirror_outline(half):
    """Symmetric outline from its right half (x >= 0, listed top to bottom)."""
    left = [(-x, y) for x, y in reversed(half) if x > 1e-6]
    return half + left


@builder("shirt")
def shirt():
    folded_top("shirt", "shirt_blue", collar="shirt", buttons=5, pocket=True)


@builder("blouse")
def blouse():
    folded_top("blouse", "blouse_silk", width=0.25, length=0.33, thick=0.028, collar="vneck", buttons=3, button_mat="button_pearl")
    # soft tie bow at the neckline
    sphere("bow_l", 0.016, (-0.018, 0.33 / 2 - 0.08, 0.034), (1.3, 0.8, 0.45), mat="blouse_silk")
    sphere("bow_r", 0.016, (0.018, 0.33 / 2 - 0.08, 0.034), (1.3, 0.8, 0.45), mat="blouse_silk")


@builder("men-tshirt")
def men_tshirt():
    folded_top("men-tshirt", "tshirt_navy", width=0.26, length=0.30, thick=0.03, collar="crew")


@builder("women-tshirt")
def women_tshirt():
    folded_top("women-tshirt", "tshirt_white", width=0.24, length=0.28, thick=0.026, collar="vneck")


@builder("jacket")
def jacket():
    folded_top("jacket", "suit_navy", width=0.30, length=0.40, thick=0.05, collar="none", buttons=2, button_mat="button_horn",
               lapels=True, flap_pockets=True)


@builder("blazer")
def blazer():
    folded_top("blazer", "blazer_camel", width=0.28, length=0.37, thick=0.045, collar="none", buttons=1, button_mat="button_horn",
               lapels=True, flap_pockets=True)


@builder("men-trousers")
def men_trousers():
    folded_trousers("men-trousers", "suit_charcoal")


@builder("women-trousers")
def women_trousers():
    folded_trousers("women-trousers", "trousers_beige", width=0.21, length=0.38, thick=0.032)


@builder("dress")
def dress():
    half = [(0.0, 0.30), (0.045, 0.30), (0.06, 0.33), (0.085, 0.33), (0.095, 0.25), (0.085, 0.12), (0.075, 0.06),
            (0.11, -0.06), (0.16, -0.28), (0.17, -0.33), (0.0, -0.33)]
    half = [(x * 0.72, y * 0.72) for x, y in half]
    flat_garment("dress", "dress_burgundy", mirror_outline(half), thick=0.022)
    box("belt", (0.125, 0.02, 0.004), (0, 0.045, 0.024), mat="leather_black", bevel=0.001)
    cylinder("buckle", 0.009, 0.004, (0, 0.045, 0.026), mat="gold", segments=16)


@builder("swimsuit")
def swimsuit():
    half = [(0.0, 0.17), (0.035, 0.12), (0.06, 0.20), (0.075, 0.20), (0.085, 0.10), (0.075, 0.0), (0.085, -0.08),
            (0.11, -0.16), (0.03, -0.2), (0.0, -0.2)]
    flat_garment("swimsuit", "swim_coral", mirror_outline(half), thick=0.012)


@builder("bikini")
def bikini():
    tri = [(0.0, 0.05), (0.055, -0.04), (-0.055, -0.04)]
    for s in (-1, 1):
        flat_garment("cup", "swim_tropical", [(x + s * 0.06, y + 0.1) for x, y in tri], thick=0.01, wrinkle=0.001, target_tris=1500)
    sweep("strap", [(-0.12, 0.06, 0.006), (-0.06, 0.058, 0.007), (0.0, 0.065, 0.006), (0.06, 0.058, 0.007), (0.12, 0.06, 0.006)], 0.003, "swim_tropical")
    for s in (-1, 1):
        sweep("halter", [(s * 0.06, 0.15, 0.006), (s * 0.03, 0.19, 0.006), (0.0, 0.21, 0.006)], 0.0025, "swim_tropical")
    bottom = [(-0.1, -0.02), (0.1, -0.02), (0.035, -0.13), (-0.035, -0.13)]
    flat_garment("bottom", "swim_tropical", bottom, thick=0.01, wrinkle=0.001, target_tris=2000)


@builder("swim-shorts")
def swim_shorts():
    half = [(0.0, 0.14), (0.15, 0.14), (0.165, -0.1), (0.17, -0.13), (0.02, -0.13), (0.0, -0.06)]
    flat_garment("swim-shorts", "swim_print", mirror_outline(half), thick=0.022)
    box("waist", (0.30, 0.03, 0.004), (0, 0.125, 0.023), mat="flipflop_foam", bevel=0.001)
    for s in (-1, 1):
        sweep("cord", [(s * 0.01, 0.12, 0.026), (s * 0.02, 0.07, 0.027), (s * 0.035, 0.04, 0.026)], 0.0025, "flipflop_strap")
        box("side_stripe", (0.01, 0.2, 0.003), (s * 0.15, 0.0, 0.0225), mat="flipflop_foam")


@builder("socks")
def socks():
    sock = [(-0.035, 0.11), (0.035, 0.11), (0.035, -0.04), (0.07, -0.08), (0.08, -0.1), (0.07, -0.125), (0.0, -0.13), (-0.035, -0.11)]
    for s in (-1, 1):
        o = flat_garment("sock", "socks_knit", [(x + s * 0.045, y) for x, y in sock], thick=0.014, wrinkle=0.0012, target_tris=3000)
        if s > 0:
            o.location.z += 0.012
        cuff = box("cuff", (0.072, 0.03, 0.004), (s * 0.045, 0.095, 0.014 + (0.012 if s > 0 else 0)), mat="socks_knit", bevel=0.0015)


@builder("tie")
def tie():
    outline = [(-0.018, 0.2), (0.018, 0.2), (0.032, -0.12), (0.0, -0.17), (-0.032, -0.12)]
    flat_garment("tie", "tie_silk", outline, thick=0.008, wrinkle=0.0006)
    # knot-side fold
    box("keeper", (0.03, 0.012, 0.002), (0, -0.06, 0.0085), mat="tie_silk", bevel=0.0008)


# --------------------------------------------------------------------------------------------- shoes

def shoe(length, width, height, mat, sole_mat, heel_raise=0.0, pump=False, laces=False, lace_mat="lace_black", flip=False):
    """One right shoe (toe +Y) built from lofted superellipse cross-sections."""
    n_len, n_arc = 24, 14
    keys = [0, 0.08, 0.3, 0.55, 0.72, 0.86, 0.95, 1.0]
    w_prof = [0.6, 0.72, 0.74, 0.86, 1.0, 0.86, 0.6, 0.18]
    if pump:
        h_prof = [0.85, 0.82, 0.62, 0.48, 0.38, 0.32, 0.25, 0.1]
    else:
        h_prof = [0.9, 0.96, 0.92, 0.82, 0.62, 0.48, 0.38, 0.15]

    def z_off(t):
        if heel_raise <= 0:
            return 0.0
        return heel_raise * (1 - lerp_profile(t, [0, 0.2, 0.62, 1], [0, 0, 1, 1]))

    def section(t):
        y = (t - 0.5) * length
        w = width * lerp_profile(t, keys, w_prof)
        h = height * lerp_profile(t, keys, h_prof)
        inner_bias = 0.08 * width * math.sin(math.pi * t)  # medial side straighter
        return y, w, h, inner_bias

    bm = bmesh.new()
    rings = []
    for i in range(n_len + 1):
        t = i / n_len
        y, w, h, bias = section(t)
        zo = z_off(t)
        ring = []
        for k in range(n_arc + 1):
            a = math.pi * k / n_arc
            c, s = math.cos(a), math.sin(a)
            x = (w / 2) * math.copysign(abs(c) ** 0.7, c) - bias * 0.5
            z = h * (abs(s) ** 0.55)
            ring.append(bm.verts.new((x, y, z + zo)))
        for k in (1, 2, 3):  # flat bottom chord, left -> right
            x = -w / 2 + w * k / 4 - bias * 0.5
            ring.append(bm.verts.new((x, y, zo)))
        rings.append((t, ring))
    m = len(rings[0][1])
    for i in range(n_len):
        a, b = rings[i][1], rings[i + 1][1]
        for k in range(m):
            bm.faces.new((a[k], a[(k + 1) % m], b[(k + 1) % m], b[k]))
    bm.faces.new(list(reversed(rings[0][1])))
    bm.faces.new(rings[-1][1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # opening (collar / topline)
    open_end = 0.52 if pump else 0.36
    doomed = []
    for f in bm.faces:
        c = f.calc_center_median()
        t = c.y / length + 0.5
        _, _, h, _ = section(min(max(t, 0), 1))
        local_z = c.z - z_off(min(max(t, 0), 1))
        thresh = (0.5 if pump else 0.7) * h
        if t < open_end and local_z > thresh and t > -0.02:
            doomed.append(f)
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    upper = from_bmesh("upper", bm, mat)
    sol = upper.modifiers.new("solid", "SOLIDIFY")
    sol.thickness = 0.0035
    sol.offset = -1
    add_subsurf(upper, 1)

    def strip(name, mat_key, t0, t1, width_scale, z_top, thickness):
        """Solid strip following the shoe's footprint (and heel rise) between t0 and t1."""
        bm = bmesh.new()
        rings = []
        steps = n_len
        for i in range(steps + 1):
            t = t0 + (t1 - t0) * i / steps
            y, w, h, bias = section(t)
            zo = z_off(t)
            ww = w * width_scale / 2
            cx = -bias * 0.5
            rings.append([bm.verts.new((cx + ww, y, zo + z_top)), bm.verts.new((cx - ww, y, zo + z_top)),
                          bm.verts.new((cx - ww, y, zo + z_top - thickness)), bm.verts.new((cx + ww, y, zo + z_top - thickness))])
        for i in range(steps):
            a, b = rings[i], rings[i + 1]
            for k in range(4):
                bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return from_bmesh(name, bm, mat_key)

    # outsole following the bottom outline (and the heel rise), thickness downward
    sole_t = 0.004 if pump else 0.011
    sole = strip("sole", sole_mat, 0.0, 1.0, 1.03, 0.001, sole_t + 0.001)
    add_bevel(sole, 0.0015, 2, angle=50)
    objs = [upper, sole]
    # sock lining / insole visible through the opening, following the footbed slope
    objs.append(strip("insole", "insole", 0.03, 0.72, 0.8, 0.0075, 0.002))
    if pump:
        y, _, _, _ = section(0.06)
        objs.append(cylinder("stiletto", 0.011, heel_raise, (0, y + 0.004, heel_raise / 2 - sole_t), mat=sole_mat, radius2=0.006, segments=16))
        objs.append(cylinder("heel_tip", 0.0062, 0.006, (0, y + 0.004, 0.003 - sole_t), mat="sole_rubber", segments=12))
    else:
        y, w, _, b = section(0.1)
        objs.append(box("heel_block", (w * 0.98, length * 0.22, 0.018), (-b * 0.5, y + 0.01, -sole_t - 0.009 + 0.001), mat=sole_mat, bevel=0.002))
    if laces:
        for j, t in enumerate((0.42, 0.47, 0.52, 0.57)):
            y, w, h, b = section(t)
            z = h + z_off(t) + 0.0005
            objs.append(cylinder("lace", 0.0018, w * 0.34, (-b * 0.5, y, z), (0, math.radians(90), 0), mat=lace_mat, segments=8))
        for s in (-1, 1):
            y, w, h, b = section(0.49)
            objs.append(box("facing", (0.012, length * 0.2, 0.003), (-b * 0.5 + s * w * 0.2, y, h + 0.0005), (math.radians(-12), 0, 0), mat=mat, bevel=0.001))
    # lift everything so the lowest point sits at z=0
    lowest = -sole_t - (0.018 if not pump else 0) if not pump else -sole_t
    translate(objs, (0, 0, -lowest))
    return objs


def shoe_pair(build):
    right = build()
    translate(right, (0.06, 0, 0))
    rotate(right, -4, "Z", (0.06, 0, 0))
    mirror_x(right)


@builder("men-shoes")
def men_shoes():
    shoe_pair(lambda: shoe(0.29, 0.105, 0.085, "leather_brown", "sole_leather", laces=True))


@builder("women-shoes")
def women_shoes():
    shoe_pair(lambda: shoe(0.245, 0.08, 0.06, "leather_black", "sole_rubber", heel_raise=0.075, pump=True))


@builder("flip-flops")
def flip_flops():
    def one():
        foot = [(0.0, 0.13), (0.03, 0.125), (0.045, 0.09), (0.047, 0.03), (0.038, -0.03), (0.04, -0.09), (0.03, -0.12),
                (0.0, -0.13), (-0.03, -0.12), (-0.04, -0.08), (-0.036, -0.02), (-0.042, 0.05), (-0.035, 0.11), (-0.02, 0.128)]
        sole = extrude_outline("foam", foot, 0.016, "flipflop_foam", bevel=0.004)
        straps = [sweep("strap", [(-0.04, -0.01, 0.014), (-0.025, 0.04, 0.03), (-0.008, 0.085, 0.022), (0.0, 0.095, 0.016)], 0.005, "flipflop_strap", scale=(1, 0.5)),
                  sweep("strap", [(0.042, -0.01, 0.014), (0.026, 0.04, 0.03), (0.008, 0.085, 0.022), (0.0, 0.095, 0.016)], 0.005, "flipflop_strap", scale=(1, 0.5)),
                  cylinder("toe_post", 0.004, 0.014, (0, 0.095, 0.018), mat="flipflop_strap", segments=10)]
        objs = [sole] + straps
        translate(objs, (0.055, 0, 0))
        return objs
    mirror_x(one())


# --------------------------------------------------------------------------------------------- electronics & business

@builder("phone")
def phone():
    extrude_outline("frame", rounded_rect(0.0716, 0.1475, 0.0105), 0.0079, "space_grey", bevel=0.0022)
    extrude_outline("screen", rounded_rect(0.0686, 0.1445, 0.0092), 0.0004, "glass_screen", loc=(0, 0, 0.0079))
    text("clock", "09:41", 0.012, (0, 0.035, 0.0085), "paper")
    extrude_outline("island", rounded_rect(0.02, 0.0055, 0.0027), 0.0002, "plastic_black", loc=(0, 0.064, 0.00835))
    # camera module on the back (underside)
    extrude_outline("cam_plate", rounded_rect(0.028, 0.028, 0.006), 0.0012, "space_grey", loc=(-0.018, 0.052, -0.0012), bevel=0.0004)
    for x, y in ((-0.0245, 0.0585), (-0.0115, 0.0585), (-0.018, 0.046)):
        cylinder("lens_ring", 0.0052, 0.0014, (x, y, -0.0016), mat="chrome", segments=20)
        cylinder("lens", 0.0038, 0.0014, (x, y, -0.0019), mat="lens_tinted", segments=20)
    for y in (0.035, 0.02):
        box("vol", (0.001, 0.009, 0.0025), (-0.0362, y, 0.004), mat="space_grey", bevel=0.0004)
    box("power", (0.001, 0.014, 0.0025), (0.0362, 0.03, 0.004), mat="space_grey", bevel=0.0004)


@builder("laptop")
def laptop():
    """13-inch laptop, lid open ~70 deg so it reads as a laptop at arm's length (keyboard + dark screen)."""
    w, d = 0.312, 0.221
    extrude_outline("base", rounded_rect(w, d, 0.012, 6), 0.0085, "aluminium", bevel=0.0025)
    box("keyboard", (0.27, 0.105, 0.0012), (0, 0.012, 0.0085), mat="keyboard_keys", bevel=0.0004)
    for r in range(5):
        for c in range(14):
            box("key", (0.016, 0.016, 0.0012), (-0.123 + c * 0.0189, 0.055 - r * 0.0195, 0.0098), mat="plastic_black")
    extrude_outline("trackpad", rounded_rect(0.11, 0.065, 0.004, 4), 0.0006, "space_grey", loc=(0, -0.07, 0.0085))
    lid = [extrude_outline("lid", rounded_rect(w, d - 0.002, 0.012, 6), 0.0062, "aluminium", loc=(0, 0.001, 0.0094), bevel=0.0022),
           extrude_outline("screen", rounded_rect(w - 0.016, d - 0.02, 0.006, 4), 0.0006, "glass_screen", loc=(0, 0.001, 0.0088)),
           box("notch", (0.05, 0.004, 0.0012), (0, -d / 2 + 0.001, 0.0092), mat="space_grey", bevel=0.0004)]
    # the screen faces down when closed; open the lid 70 deg about the hinge (far edge), screen towards the user
    rotate(lid, 70, "X", (0, d / 2 - 0.002, 0.0085))
    cylinder("hinge", 0.0045, w * 0.8, (0, d / 2 - 0.002, 0.0085), (0, math.radians(90), 0), mat="space_grey", segments=16)
    for x in (-1, 1):
        for y in (-1, 1):
            cylinder("foot", 0.006, 0.0012, (x * (w / 2 - 0.03), y * (d / 2 - 0.025), -0.0005), mat="rubber_feet", segments=16)
    for i, y in enumerate((0.04, 0.055)):
        box("usb_c", (0.002, 0.009, 0.003), (-w / 2 + 0.0005, y, 0.0045), mat="plastic_black", bevel=0.0008)
    box("jack", (0.002, 0.004, 0.004), (w / 2 - 0.0005, 0.05, 0.0045), mat="plastic_black")


def cable_coil(name_prefix, center, loops=3, radius=0.04, pitch=0.004, wire=0.0022, mat="plastic_white", lead=None):
    pts = []
    steps = 30 * loops
    for i in range(steps + 1):
        a = 2 * math.pi * i / 30
        r = radius * (1 + 0.06 * math.sin(a * 2.3))
        pts.append((center[0] + math.cos(a) * r, center[1] + math.sin(a) * r, center[2] + wire + (i / steps) * pitch * loops))
    if lead:
        pts = pts + lead
    return sweep(name_prefix + "_wire", pts, wire, mat, segments=10)


def usb_c_plug(loc, rot, mat="plastic_white"):
    box("plug_body", (0.011, 0.024, 0.0065), loc, rot, mat=mat, bevel=0.002)
    d = Vector((0, 0.0145, 0))
    from mathutils import Euler
    d.rotate(Euler(rot))
    box("plug_tip", (0.0084, 0.008, 0.0026), tuple(Vector(loc) + d), rot, mat="chrome", bevel=0.0009)


@builder("laptop-charger")
def laptop_charger():
    box("brick", (0.062, 0.062, 0.029), (0, 0, 0.0145), mat="plastic_white", bevel=0.007, segments=4)
    for x in (-0.0095, 0.0095):  # EU Type C pins on the side
        cylinder("pin", 0.002, 0.019, (x, -0.04, 0.0145), (math.radians(90), 0, 0), mat="chrome", segments=12)
    box("pin_base", (0.032, 0.002, 0.014), (0, -0.031, 0.0145), mat="plastic_grey", bevel=0.0008)
    box("port", (0.009, 0.002, 0.0032), (0, 0.031, 0.0145), mat="plastic_black")
    cable_coil("cable", (0.0, 0.11, 0.0), loops=3, radius=0.042, lead=[(0.035, 0.09, 0.018), (0.01, 0.045, 0.016), (0.0, 0.034, 0.0145)])
    usb_c_plug((0.06, 0.12, 0.008), (0, 0, math.radians(-60)))


@builder("phone-cable")
def phone_cable():
    cable_coil("cable", (0, 0, 0), loops=4, radius=0.036, wire=0.0018, mat="plastic_white")
    usb_c_plug((0.05, -0.012, 0.006), (0, 0, math.radians(-100)))
    box("usb_a_body", (0.016, 0.03, 0.008), (-0.048, 0.02, 0.0062), (0, 0, math.radians(70)), mat="plastic_white", bevel=0.002)
    box("usb_a_tip", (0.012, 0.012, 0.0045), (-0.064, 0.026, 0.0062), (0, 0, math.radians(70)), mat="chrome", bevel=0.0005)
    box("velcro", (0.016, 0.03, 0.004), (0.0, -0.036, 0.009), mat="plastic_black", bevel=0.0015)


@builder("headphones")
def headphones():
    band = arc_points((0, 0, 0.0), 0.088, 0, 180, 40, "xz")
    sweep("band", band, 0.009, "plastic_black", segments=12, scale=(1.0, 2.2))
    sweep("band_pad", arc_points((0, 0, 0.0), 0.079, 30, 150, 30, "xz"), 0.007, "cushion_black", segments=12, scale=(1.0, 1.8))
    for s in (-1, 1):
        cylinder("slider", 0.004, 0.035, (s * 0.088, 0, -0.012), mat="chrome", segments=12)
        cylinder("cup", 0.042, 0.028, (s * 0.098, 0, -0.06), (0, math.radians(90), 0), mat="plastic_black", segments=32, bevel=0.006, bevel_segments=3)
        torus("cushion", 0.03, 0.012, (s * 0.08, 0, -0.06), (0, math.radians(90), 0), mat="cushion_black", segments=12, steps=32)
        cylinder("cap", 0.03, 0.002, (s * 0.1125, 0, -0.06), (0, math.radians(90), 0), mat="space_grey", segments=32)
    # lie the headphones down flat (as packed)
    import bpy
    rotate(list(bpy.context.scene.objects), -90, "X")


@builder("notebook")
def notebook():
    w, d, t = 0.148, 0.21, 0.018
    box("back_cover", (w, d, 0.0025), (0, 0, 0.00125), mat="notebook_cover", bevel=0.0012)
    box("front_cover", (w, d, 0.0025), (0, 0, t - 0.00125), mat="notebook_cover", bevel=0.0012)
    cylinder("spine", t / 2, d, (-w / 2, 0, t / 2), (math.radians(90), 0, 0), mat="notebook_cover", segments=16)
    box("pages", (w - 0.006, d - 0.006, t - 0.005), (0.001, 0, t / 2), mat="paper", bevel=0.0008)
    box("elastic", (0.007, d + 0.0008, 0.0006), (w / 2 - 0.022, 0, t + 0.0003), mat="elastic_band")
    box("elastic_b", (0.007, d + 0.0008, 0.0006), (w / 2 - 0.022, 0, -0.0003), mat="elastic_band")
    for s in (-1, 1):
        box("elastic_edge", (0.007, 0.0008, t + 0.0012), (w / 2 - 0.022, s * (d / 2 + 0.0004), t / 2), mat="elastic_band")
    box("ribbon", (0.006, 0.04, 0.0006), (-0.02, -d / 2 - 0.018, 0.009), (math.radians(10), 0, 0), mat="lining_satin")


@builder("pen")
def pen():
    cylinder("barrel", 0.0052, 0.105, (0, -0.005, 0.0055), (math.radians(90), 0, 0), mat="pen_body", segments=20)
    cylinder("cone", 0.0052, 0.016, (0, -0.0655, 0.0055), (math.radians(-90), 0, 0), mat="chrome", radius2=0.0012, segments=20)
    cylinder("cap_ring", 0.0055, 0.004, (0, 0.03, 0.0055), (math.radians(90), 0, 0), mat="chrome", segments=20)
    cylinder("end", 0.0052, 0.006, (0, 0.05, 0.0055), (math.radians(90), 0, 0), mat="chrome", radius2=0.0035, segments=20)
    box("clip", (0.0022, 0.042, 0.0014), (0, 0.024, 0.0118), mat="chrome", bevel=0.0005)


@builder("toiletry-bag")
def toiletry_bag():
    parts = [box("body", (0.25, 0.13, 0.115), (0, 0, 0.0575))]
    soft_body("toiletry-bag", parts, "toiletry_fabric", voxel=0.004, smooth=10, wrinkle=0.0025, wrinkle_scale=0.05, target_tris=5000)
    box("zip_tape", (0.24, 0.016, 0.003), (0, 0, 0.113), mat="zipper_tape", bevel=0.001)
    box("zip_teeth", (0.236, 0.004, 0.0025), (0, 0, 0.1145), mat="zipper_metal")
    box("puller", (0.012, 0.022, 0.003), (0.08, -0.012, 0.116), (math.radians(20), 0, 0), mat="zipper_metal", bevel=0.001)
    sweep("loop", [(0.124, 0.0, 0.07), (0.15, 0.0, 0.09), (0.15, 0.0, 0.05), (0.124, 0.0, 0.04)], 0.008, "leather_cognac", scale=(1, 0.35))
    box("patch", (0.06, 0.002, 0.03), (0, -0.065, 0.06), mat="leather_cognac", bevel=0.001)


@builder("id-card")
def id_card():
    w, h = 0.0856, 0.054
    extrude_outline("card", rounded_rect(w, h, 0.0032), 0.0008, "card_white")
    extrude_outline("band", rounded_rect(w, 0.012, 0.0001), 0.0001, "card_blue", loc=(0, h / 2 - 0.006, 0.0008))
    text("title", "KİMLİK KARTI", 0.0055, (0.006, h / 2 - 0.006, 0.0009), "card_white")
    box("photo", (0.022, 0.028, 0.0001), (-w / 2 + 0.017, -0.004, 0.00085), mat="card_photo")
    sphere("photo_head", 0.0055, (-w / 2 + 0.017, 0.0005, 0.00092), (1, 1.2, 0.02), mat="card_ink")
    for i in range(5):
        box("line", (0.04 - (i % 2) * 0.012, 0.0022, 0.0001), (0.012 - (i % 2) * 0.006, 0.008 - i * 0.0062, 0.00085), mat="card_ink")
    extrude_outline("chip", rounded_rect(0.011, 0.009, 0.0015), 0.0001, "gold", loc=(0.03, -0.017, 0.00085))


@builder("passport")
def passport():
    w, d = 0.088, 0.125
    box("cover", (w, d, 0.0055), (0, 0, 0.00275), mat="passport_cover", bevel=0.0015, segments=3)
    box("pages", (w - 0.003, d - 0.004, 0.0035), (0.0018, 0, 0.00275), mat="paper")
    text("title", "PASAPORT", 0.0085, (0, 0.04, 0.0056), "gold")
    torus("emblem", 0.012, 0.0009, (0, 0.0, 0.0056), mat="gold", segments=6, steps=40, scale=(1, 0.3))
    text("sub", "PASSPORT", 0.0055, (0, -0.04, 0.0056), "gold")


@builder("laptop-bag")
def laptop_bag():
    parts = [box("body", (0.40, 0.075, 0.29), (0, 0, 0.145))]
    soft_body("laptop-bag", parts, "leather_black", voxel=0.004, smooth=8, wrinkle=0.0012, wrinkle_scale=0.08, target_tris=5000)
    box("pocket", (0.3, 0.012, 0.15), (0, -0.04, 0.1), mat="leather_black", bevel=0.005)
    box("pocket_zip", (0.28, 0.003, 0.006), (0, -0.0465, 0.17), mat="zipper_tape")
    box("top_zip", (0.38, 0.012, 0.004), (0, 0, 0.292), mat="zipper_tape")
    for s in (-1, 1):
        sweep("handle", [(s * 0.02 - 0.06 * s, 0.0, 0.285), (s * 0.06 - 0.06 * s + 0.0, 0.0, 0.35), (s * 0.1, 0, 0.35), (s * 0.14 - 0.0, 0.0, 0.285)],
              0.0055, "leather_black", segments=10)
    for x in (-0.12, 0.12):
        box("ring", (0.02, 0.012, 0.012), (x, 0, 0.29), mat="chrome", bevel=0.002)
    import bpy
    rotate(list(bpy.context.scene.objects), 90, "X")


@builder("travel-bag")
def travel_bag():
    body = cylinder("body", 0.13, 0.46, (0, 0, 0.12), (0, math.radians(90), 0), mat="duffel_canvas", segments=24)
    body.scale = (1.0, 1.0, 1.0)
    flat = box("flat", (0.46, 0.2, 0.05), (0, 0, 0.025))
    soft_body("travel-bag", [body, flat], "duffel_canvas", voxel=0.005, smooth=10, wrinkle=0.003, wrinkle_scale=0.06, target_tris=6000)
    for s in (-1, 1):
        box("end_trim", (0.006, 0.2, 0.2), (s * 0.228, 0, 0.12), mat="leather_cognac", bevel=0.004)
        sweep("strap", [(s * 0.09, -0.12, 0.05), (s * 0.09, -0.13, 0.17), (s * 0.09, -0.08, 0.24), (s * 0.09, 0.0, 0.25)], 0.012, "webbing_olive", scale=(0.25, 1))
    sweep("handles", [(-0.09, 0.0, 0.25), (-0.06, 0.0, 0.33), (0.06, 0.0, 0.33), (0.09, 0.0, 0.25)], 0.008, "leather_cognac")
    box("zip", (0.36, 0.014, 0.004), (0, 0.03, 0.248), (math.radians(-12), 0, 0), mat="zipper_tape")


@builder("beach-towel")
def beach_towel():
    bands = [("towel_turquoise", 0.13), ("towel_white", 0.04), ("towel_orange", 0.06), ("towel_white", 0.04), ("towel_turquoise", 0.13)]
    x = -sum(b[1] for b in bands) / 2
    for mat, wdt in bands:
        o = cylinder("roll", 0.062, wdt, (x + wdt / 2, 0, 0.062), (0, math.radians(90), 0), mat=mat, segments=28)
        x += wdt
    # spiral visible at both ends
    for s in (-1, 1):
        pts = []
        for i in range(70):
            a = i * 0.32
            r = 0.012 + a * 0.0023
            pts.append((s * 0.2005, math.cos(a) * r, 0.062 + math.sin(a) * r))
        sweep("spiral", pts, 0.0018, "towel_white", segments=6, scale=(0.4, 1))


@builder("snorkel-mask")
def snorkel_mask():
    frame = extrude_outline("skirt", rounded_rect(0.17, 0.085, 0.032), 0.035, "silicone_clear")
    sk = soft_body("skirt_soft", [frame], "silicone_clear", voxel=0.003, smooth=6, target_tris=3000)
    extrude_outline("frame", rounded_rect(0.165, 0.08, 0.03), 0.01, "plastic_black", loc=(0, 0, 0.033), bevel=0.002)
    extrude_outline("lens", rounded_rect(0.15, 0.066, 0.024), 0.004, "snorkel_glass", loc=(0, 0, 0.04))
    strap = arc_points((0, 0.0, 0.02), 0.11, 200, 340, 30, "xy")
    sweep("strap", [(x, y - 0.03, z) for x, y, z in strap], 0.012, "silicone_yellow", scale=(0.25, 1))
    tube = [(0.09, -0.01, 0.03), (0.105, 0.04, 0.035), (0.11, 0.12, 0.04), (0.11, 0.24, 0.04), (0.1, 0.29, 0.035)]
    sweep("snorkel", tube, 0.011, "silicone_yellow", segments=14)
    box("mouthpiece", (0.03, 0.025, 0.02), (0.085, -0.03, 0.025), mat="silicone_clear", bevel=0.006)


@builder("neck-pillow")
def neck_pillow():
    pts = arc_points((0, 0, 0.05), 0.085, -60, 240, 40, "xy")
    o = sweep("pillow", pts, 0.05, "velvet_grey", segments=16, scale=(1.0, 0.85))
    soft_body("neck-pillow", [o], "velvet_grey", voxel=0.004, smooth=8, wrinkle=0.002, wrinkle_scale=0.04, target_tris=5000)
    box("snap", (0.03, 0.012, 0.03), (0.0, -0.13, 0.05), mat="strap_grey", bevel=0.004)


@builder("kids-book")
def kids_book():
    w, d = 0.21, 0.28
    box("cover", (w, d, 0.007), (0, 0, 0.0035), mat="book_cover_a", bevel=0.0012)
    box("pages", (w - 0.004, d - 0.006, 0.005), (0.002, 0, 0.0035), mat="paper")
    sphere("sun", 0.03, (0.055, 0.075, 0.0071), (1, 1, 0.02), mat="book_cover_b")
    box("house", (0.07, 0.05, 0.0003), (-0.04, -0.03, 0.0071), mat="book_cover_c")
    roof = extrude_outline("roof", [(-0.08, -0.005), (0.0, -0.005), (-0.04, 0.03)], 0.0003, "book_cover_b", loc=(0, 0, 0.0071))
    box("grass", (w - 0.02, 0.03, 0.0003), (0, -0.095, 0.0071), mat="book_cover_d")
    text("title", "BOYAMA", 0.026, (0, 0.11, 0.0072), "book_cover_c")
    text("title2", "KİTABI", 0.018, (0, 0.085, 0.0072), "book_cover_b")
    sphere("dot", 0.008, (0.07, -0.05, 0.0071), (1, 1, 0.05), mat="book_cover_d")
