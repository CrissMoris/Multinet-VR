"""Hanging garment variants (<id>-hanging) and the reusable hanger. Authoring frame: hook top at the origin,
garment hangs down (-Z), chest faces +Y (= Unity +Z = slot forward). Export origin 'none' (pivot at the hook top).

Garments are lofted torso shells with sleeves, collars, plackets, buttons and noise-displaced folds; total length
(hook top -> hem) = mt_layout.GARMENT_LEN so the hems stay above the business riser.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import mt_layout as L
from mtlib import (bake, box, cylinder, extrude_outline, from_bmesh, soft_body, sphere, sweep, torus, translate)
from mt_wardrobe import build_hanger

BUILDERS = {}


def builder(name):
    def wrap(fn):
        BUILDERS[name] = fn
        fn.origin = "none"
        return fn
    return wrap


def smooth01(t):
    return t * t * (3 - 2 * t)


def loft(name, rings, mat, cap_top=True, cap_bottom=False, close=True):
    """Mesh from rings of 3D points (same count each); rings[0] = top."""
    bm = bmesh.new()
    vr = [[bm.verts.new(p) for p in ring] for ring in rings]
    n = len(rings[0])
    for i in range(len(rings) - 1):
        a, b = vr[i], vr[i + 1]
        for k in range(n if close else n - 1):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    if cap_top:
        bm.faces.new(list(reversed(vr[0])))
    if cap_bottom:
        bm.faces.new(vr[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return from_bmesh(name, bm, mat)


def wrinkle(obj, strength=0.004, scale=0.06, solidify=0.004):
    tex = bpy.data.textures.new(obj.name + "_w", "CLOUDS")
    tex.noise_scale = scale
    tex.noise_depth = 2
    disp = obj.modifiers.new("wrinkle", "DISPLACE")
    disp.texture = tex
    disp.strength = strength
    disp.mid_level = 0.5
    if solidify:
        sol = obj.modifiers.new("solid", "SOLIDIFY")
        sol.thickness = solidify
        sol.offset = -1
    bake(obj)
    return obj


def torso(name, mat, z_top, z_hem, w_top, w_chest, w_waist, w_hem, depth, fold_amp=0.008, folds=9, segs=40, rows=20,
          open_front=0.0, flare_from=0.0):
    """Lofted torso: width profile (half-widths) from the shoulder line to the hem; chest towards +Y.
    open_front > 0 leaves a V opening at the front (jackets); flare_from in (0..1) starts the skirt flare."""
    rings = []
    rnd = random.Random(7)
    phases = [rnd.random() * 6.28 for _ in range(3)]
    for r in range(rows + 1):
        t = r / rows
        z = z_top + (z_hem - z_top) * t
        if t < 0.3:
            w = w_top + (w_chest - w_top) * smooth01(t / 0.3)
        elif t < 0.65:
            w = w_chest + (w_waist - w_chest) * smooth01((t - 0.3) / 0.35)
        else:
            w = w_waist + (w_hem - w_waist) * smooth01((t - 0.65) / 0.35)
        if flare_from and t > flare_from:
            w += (w_hem - w_waist) * 0.5 * smooth01((t - flare_from) / (1 - flare_from))
        d = depth * (1.0 - 0.35 * t)
        # shoulder slope: the top ring droops towards the sides like fabric over hanger arms
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            c, s_ = math.cos(a), math.sin(a)
            amp = fold_amp * smooth01(min(1, t * 1.6))
            fold = 1 + amp / max(w, 1e-3) * (math.sin(folds * a + phases[0]) + 0.5 * math.sin(2 * folds * a + phases[1] + t * 3))
            x = w * math.copysign(abs(c) ** 0.8, c) * fold
            y = d * math.copysign(abs(s_) ** 0.9, s_) * fold
            zz = z
            if t < 0.08:
                zz -= 0.03 * (abs(c) ** 2) * (1 - t / 0.08)   # shoulder drop at the sides
            ring.append((x, y, zz))
        rings.append(ring)
    obj = loft(name, rings, mat, cap_top=True)
    if open_front > 0:
        # cut a V in the front, from the neck to open_front (fraction of the length)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        doomed = []
        for f in bm.faces:
            c = f.calc_center_median()
            t = (c.z - z_top) / (z_hem - z_top)
            if c.y > 0 and t < open_front and abs(c.x) < 0.012 + 0.07 * (1 - t / open_front):
                doomed.append(f)
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        bm.to_mesh(obj.data)
        bm.free()
    return obj


def sleeve(name, mat, side, shoulder, length, r0=0.036, r1=0.03, drop=0.02, cuff=True, cuff_mat=None, bend=0.012):
    """Hanging sleeve from the shoulder point; droops down with a slight outward curve."""
    x0, y0, z0 = shoulder
    pts = [(x0 + side * 0.0, y0, z0 + 0.012), (x0 + side * 0.018, y0 + 0.004, z0 - 0.03),
           (x0 + side * 0.026, y0 + bend, z0 - length * 0.55), (x0 + side * 0.02, y0 + bend * 0.6, z0 - length)]
    o = sweep(name, pts, r0, mat, segments=12, scale=(1.0, 0.75), radius_fn=lambda t: 1 - (1 - r1 / r0) * t)
    objs = [o]
    if cuff:
        objs.append(sweep(name + "_cuff", [(x0 + side * 0.021, y0 + bend * 0.62, z0 - length + 0.004), (x0 + side * 0.019, y0 + bend * 0.58, z0 - length - 0.012)],
                          r1 * 1.05, cuff_mat or mat, segments=14, scale=(1.0, 0.75)))
    return objs


def shirt_collar(mat, neck=(0, 0.0, 0), r=0.052, band_h=0.022, points=True, button_mat="button_pearl"):
    nx, ny, nz = neck
    objs = [torus("collar_band", r, band_h / 2, (nx, ny + 0.004, nz), mat=mat, start=-200, end=20, steps=28, segments=8, scale=(0.5, 1.2))]
    if points:
        for s in (-1, 1):
            objs.append(box("collar_point", (0.052, 0.006, 0.06), (nx + s * 0.034, ny + r - 0.012, nz - 0.036), (math.radians(-18), math.radians(s * 8), math.radians(s * 24)), mat=mat))
    return objs


def placket(mat, y_front, z_top, z_bot, buttons, button_mat="button_pearl", width=0.022):
    objs = [box("placket", (width, 0.004, z_top - z_bot), (0, y_front + 0.002, (z_top + z_bot) / 2), mat=mat)]
    for i in range(buttons):
        z = z_top - 0.03 - i * (z_top - z_bot - 0.05) / max(1, buttons - 1)
        objs.append(cylinder("button", 0.0055, 0.0025, (0, y_front + 0.0055, z), (math.radians(90), 0, 0), mat=button_mat, segments=12))
    return objs


def hanging_top(name, mat, length_total=None, w_top=0.158, w_chest=0.15, w_waist=0.138, w_hem=0.145, depth=0.06,
                collar="shirt", buttons=6, button_mat="button_pearl", sleeves=True, sleeve_len=0.22, pocket=False,
                lapels=False, flap_pockets=False, fold_amp=0.008, yoke=False, cuff_mat=None, hanger_mat="hanger_walnut"):
    total = length_total or L.GARMENT_LEN
    build_hanger((0, 0, 0), hanger_mat)
    z_top = -0.095
    z_hem = -total
    body = torso(name + "_body", mat, z_top, z_hem, w_top, w_chest, w_waist, w_hem, depth, fold_amp=fold_amp,
                 open_front=0.45 if lapels else 0.0)
    wrinkle(body, strength=0.0035 if not lapels else 0.002, scale=0.07)
    parts = []
    if sleeves:
        for s in (-1, 1):
            parts += sleeve(name + "_sleeve", mat, s, (s * (w_top - 0.012), 0.0, z_top - 0.004), sleeve_len, cuff_mat=cuff_mat)
    y_front = depth * 0.98
    if collar == "shirt":
        parts += shirt_collar(mat, (0, 0.0, z_top + 0.012), button_mat=button_mat)
        parts += placket(mat, y_front, z_top - 0.02, z_hem + 0.02, buttons, button_mat)
    elif collar == "vneck":
        for s in (-1, 1):
            parts.append(box("v_edge", (0.012, 0.005, 0.12), (s * 0.03, y_front - 0.004, z_top - 0.045), (math.radians(0), math.radians(-s * 22), 0), mat=mat))
        parts.append(torus("neck_back", 0.05, 0.006, (0, 0.004, z_top + 0.01), mat=mat, start=10, end=170, steps=20, scale=(0.5, 1)))
        if buttons:
            parts += placket(mat, y_front, z_top - 0.11, z_hem + 0.03, buttons, button_mat, width=0.014)
    if lapels:
        for s in (-1, 1):
            lap = box("lapel", (0.055, 0.006, 0.19), (s * 0.045, y_front - 0.01, z_top - 0.09), (math.radians(-6), math.radians(s * 10), math.radians(-s * 16)), mat=mat, bevel=0.002)
            parts.append(lap)
        parts.append(torus("jacket_collar", 0.055, 0.009, (0, 0.006, z_top + 0.012), mat=mat, start=-200, end=20, steps=24, segments=8, scale=(0.6, 1.4)))
        # inner facing visible in the V (lining colour) + buttons
        parts.append(box("facing", (0.08, 0.004, 0.2), (0, y_front - 0.03, z_top - 0.11), mat="lining_satin"))
        for i in range(buttons):
            parts.append(cylinder("button", 0.009, 0.003, (0.012, y_front + 0.004, z_top - 0.2 - i * 0.06), (math.radians(90), 0, 0), mat=button_mat, segments=14))
    if pocket:
        parts.append(box("pocket", (0.055, 0.004, 0.06), (0.07, y_front + 0.002, z_top - 0.1), mat=mat))
    if flap_pockets:
        for s in (-1, 1):
            parts.append(box("flap", (0.07, 0.006, 0.025), (s * 0.085, y_front + 0.002, z_hem + 0.09), mat=mat, bevel=0.002))
        parts.append(box("welt", (0.05, 0.004, 0.012), (-0.07, y_front + 0.002, z_top - 0.09), mat=mat))
    if yoke:
        parts.append(box("yoke_seam", (w_top * 1.9, 0.004, 0.003), (0, -depth * 0.9, z_top - 0.03), mat=mat))
    return body, parts


@builder("shirt-hanging")
def shirt_hanging():
    hanging_top("shirt", "shirt_blue", collar="shirt", buttons=6, pocket=True, yoke=True)


@builder("blouse-hanging")
def blouse_hanging():
    body, _ = hanging_top("blouse", "blouse_silk", w_top=0.15, w_chest=0.142, w_waist=0.128, w_hem=0.15, depth=0.05,
                          collar="vneck", buttons=3, sleeve_len=0.17, fold_amp=0.011)
    # neck bow
    for s in (-1, 1):
        sphere("bow", 0.018, (s * 0.02, 0.052, -0.125), (1.2, 0.6, 0.5), mat="blouse_silk")
    sweep("bow_tail", [(0.0, 0.052, -0.13), (0.01, 0.054, -0.2), (0.02, 0.05, -0.26)], 0.006, "blouse_silk", scale=(1.6, 0.4))


@builder("jacket-hanging")
def jacket_hanging():
    hanging_top("jacket", "suit_navy", w_top=0.165, w_chest=0.158, w_waist=0.148, w_hem=0.152, depth=0.075,
                collar="none", buttons=2, button_mat="button_horn", lapels=True, flap_pockets=True, fold_amp=0.004,
                sleeve_len=0.24, hanger_mat="hanger_walnut")


@builder("blazer-hanging")
def blazer_hanging():
    hanging_top("blazer", "blazer_camel", w_top=0.16, w_chest=0.152, w_waist=0.142, w_hem=0.148, depth=0.07,
                collar="none", buttons=1, button_mat="button_horn", lapels=True, flap_pockets=True, fold_amp=0.004,
                sleeve_len=0.23)


@builder("dress-hanging")
def dress_hanging():
    build_hanger((0, 0, 0))
    z_top, z_hem = -0.125, -L.GARMENT_LEN
    body = torso("dress_body", "dress_burgundy", z_top, z_hem, 0.105, 0.115, 0.095, 0.15, 0.05, fold_amp=0.012, folds=11, flare_from=0.6)
    wrinkle(body, strength=0.004, scale=0.06)
    # straps up to the hanger arms, bodice neckline band, belt
    for s in (-1, 1):
        sweep("strap", [(s * 0.075, 0.01, z_top + 0.005), (s * 0.105, 0.0, -0.105)], 0.005, "dress_burgundy", segments=8, scale=(1.6, 0.5))
    torus("neckline", 0.11, 0.006, (0, 0.0, z_top + 0.002), mat="dress_burgundy", start=0, end=180, steps=24, segments=8, scale=(0.5, 1))
    zb = z_top + (z_hem - z_top) * 0.47
    torus("belt", 0.105, 0.012, (0, 0.0, zb), mat="leather_black", steps=36, segments=8, scale=(0.5, 1.0))
    box("buckle", (0.024, 0.006, 0.024), (0, 0.054, zb), mat="gold", bevel=0.003)


def clip_hanger():
    return build_hanger((0, 0, 0), clips=True)


@builder("swimsuit-hanging")
def swimsuit_hanging():
    clip_hanger()
    half = [(0.0, 0.0), (0.03, 0.0), (0.045, -0.03), (0.06, -0.045), (0.07, -0.12), (0.065, -0.17), (0.075, -0.215), (0.025, -0.245), (0.0, -0.245)]
    outline = half + [(-x, y) for x, y in reversed(half) if x > 1e-6]
    sh = extrude_outline("suit", outline, 0.012, "swim_coral")
    body = soft_body("swimsuit", [sh], "swim_coral", voxel=0.003, smooth=5, wrinkle=0.0015, wrinkle_scale=0.03, target_tris=3000)
    body.rotation_euler = (math.radians(90), 0, 0)   # stand it up (outline Y -> Z), facing +Y
    body.location = (0, 0.0, -0.175)
    # straps reaching the clips
    for s in (-1, 1):
        sweep("strap", [(s * 0.10, 0.0, -0.172), (s * 0.08, 0.0, -0.18)], 0.004, "swim_coral", segments=8)


@builder("bikini-hanging")
def bikini_hanging():
    clip_hanger()
    tri = [(0.0, 0.045), (0.05, -0.04), (-0.05, -0.04)]
    for s in (-1, 1):
        cup = extrude_outline("cup", [(x + s * 0.05, y) for x, y in tri], 0.01, "swim_tropical")
        c = soft_body("cup", [cup], "swim_tropical", voxel=0.0025, smooth=5, wrinkle=0.001, target_tris=1200)
        c.rotation_euler = (math.radians(90), 0, 0)
        c.location = (0, 0.0, -0.215)
    sweep("band", [(-0.11, 0.0, -0.255), (0, 0.004, -0.258), (0.11, 0.0, -0.255)], 0.003, "swim_tropical")
    for s in (-1, 1):
        sweep("halter", [(s * 0.10, 0.0, -0.172), (s * 0.05, 0.0, -0.175), (s * 0.02, 0.0, -0.18)], 0.0025, "swim_tropical")
    bottom = [(-0.085, 0.0), (0.085, 0.0), (0.03, -0.09), (-0.03, -0.09)]
    b = extrude_outline("bottom", bottom, 0.01, "swim_tropical")
    b = soft_body("bottom", [b], "swim_tropical", voxel=0.0025, smooth=5, wrinkle=0.001, target_tris=1500)
    b.rotation_euler = (math.radians(90), 0, 0)
    b.location = (0, -0.012, -0.29)
    for s in (-1, 1):   # side ties looped over the hanger bar
        sweep("tie", [(s * 0.085, -0.012, -0.29), (s * 0.09, -0.006, -0.2), (s * 0.085, 0.0, -0.142)], 0.0025, "swim_tropical")
