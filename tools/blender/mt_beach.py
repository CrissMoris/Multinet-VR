"""Beach products added for the client's final product list: swim fins (palet), inflatable sea mattress (deniz yatağı),
straw sun hat (kadın plaj şapkası) and straw fedora (erkek hasır şapka).
Products: origin at the bottom centre (export origin 'bottom'). Axes: X right, Y forward, Z up."""
import math

import bmesh
import bpy
from mathutils import Vector

from mtlib import (box, cylinder, extrude_outline, from_bmesh, soft_body, sphere, sweep)

BUILDERS = {}


def builder(name):
    def wrap(fn):
        BUILDERS[name] = fn
        return fn
    return wrap


def lathe(name, outline, mat, segments=48):
    """Solid of revolution around Z from a closed (r, z) outline (first point on the axis)."""
    bm = bmesh.new()
    n = len(outline)
    rings = []
    for (r, z) in outline:
        if r < 1e-6:
            rings.append(bm.verts.new((0.0, 0.0, z)))
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * s / segments), r * math.sin(2 * math.pi * s / segments), z))
                          for s in range(segments)])
    for i in range(n):
        a, b = rings[i], rings[(i + 1) % n]
        for s in range(segments):
            s2 = (s + 1) % segments
            va = [a] if isinstance(a, bmesh.types.BMVert) else [a[s], a[s2]]
            vb = [b] if isinstance(b, bmesh.types.BMVert) else [b[s], b[s2]]
            if len(va) == 1 and len(vb) == 1:
                continue
            if len(va) == 1:
                tri = (va[0], vb[1], vb[0])
            elif len(vb) == 1:
                tri = (va[0], va[1], vb[0])
            else:
                tri = (va[0], va[1], vb[1], vb[0])
            try:
                bm.faces.new(tri)
            except ValueError:
                pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = from_bmesh(name, bm, mat)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def hat(brim_r, crown_h, crown_r, droop, band_mat, pinch=0.0, brim_up=0.0, name="hat"):
    t = 0.006
    outer = [(0.0, crown_h + 0.004 - pinch), (crown_r * 0.55, crown_h + 0.006), (crown_r * 0.9, crown_h - 0.01 - pinch * 0.4),
             (crown_r, crown_h * 0.55), (crown_r + 0.004, 0.012), (crown_r + 0.05, 0.004 + brim_up * 0.3),
             (brim_r * 0.8, -droop * 0.35 + brim_up * 0.7), (brim_r, -droop + brim_up)]
    inner = [(brim_r, -droop - t + brim_up), (brim_r * 0.8, -droop * 0.35 - t + brim_up * 0.7), (crown_r + 0.05, -t + brim_up * 0.3),
             (crown_r - 0.004, -t), (crown_r - 0.01, crown_h * 0.55), (crown_r * 0.86, crown_h - 0.016 - pinch * 0.4),
             (crown_r * 0.5, crown_h - 0.002)]
    lathe(name, outer + inner, "wicker")
    band = [(crown_r + 0.0045, 0.012), (crown_r + 0.0045, 0.045), (crown_r - 0.002, 0.045), (crown_r - 0.002, 0.012)]
    # ribbon band + bow as a ring (lathe of a rectangle) and a small knot
    lathe("band", [(crown_r + 0.0045, 0.014), (crown_r + 0.0045, 0.046), (crown_r + 0.001, 0.046), (crown_r + 0.001, 0.014)], band_mat)
    sphere("knot", 0.014, (crown_r + 0.008, 0.0, 0.03), (0.7, 1.0, 1.0), mat=band_mat)
    for sy in (-1, 1):
        box("tail", (0.006, 0.028, 0.012), (crown_r + 0.012, sy * 0.022, 0.026), (0, 0, 0), mat=band_mat, bevel=0.002)


@builder("beach-hat")
def beach_hat():
    """Wide floppy straw sun hat with a coral ribbon."""
    hat(brim_r=0.215, crown_h=0.1, crown_r=0.078, droop=0.03, band_mat="swim_coral", name="sun_hat")


@builder("straw-hat")
def straw_hat():
    """Straw fedora with a navy band, brim slightly turned up at the sides."""
    hat(brim_r=0.165, crown_h=0.115, crown_r=0.072, droop=0.006, band_mat="suit_navy", pinch=0.018, brim_up=0.012, name="straw_fedora")


def one_fin(side):
    """One swim fin lying flat, toe end at +Y: foot pocket + blade with side ribs. Returns nothing (objects in scene)."""
    x0 = side * 0.105
    pocket = sphere("pocket", 0.06, (x0, -0.09, 0.035), (0.95, 1.55, 0.62), mat="fin_blue")
    toe = sphere("toe", 0.052, (x0, -0.02, 0.028), (1.0, 1.2, 0.5), mat="fin_blue")
    heel = box("heel_strap", (0.1, 0.012, 0.012), (x0, -0.17, 0.055), mat="plastic_black", bevel=0.004)
    soft_body("pocket_union", [pocket, toe], "fin_blue", voxel=0.004, smooth=6, target_tris=2500)
    # blade: trapezoid, 0.007 thick, widening towards the tip
    outline = [(x0 - 0.045, 0.0), (x0 + 0.045, 0.0), (x0 + 0.095, 0.2), (x0 + 0.075, 0.24), (x0 - 0.075, 0.24), (x0 - 0.095, 0.2)]
    extrude_outline("blade", outline, 0.007, "fin_blue", loc=(0, 0, 0.012), bevel=0.0015)
    for sx in (-1, 1):
        extrude_outline("rib", [(x0 + sx * 0.04, 0.0), (x0 + sx * 0.052, 0.0), (x0 + sx * 0.098, 0.2), (x0 + sx * 0.086, 0.2)], 0.016, "plastic_black",
                        loc=(0, 0, 0.012), bevel=0.002)
    for i in range(3):
        y = 0.07 + i * 0.05
        w = 0.05 + i * 0.012
        box("vane", (w * 0.8, 0.006, 0.012), (x0, y, 0.022), mat="plastic_black", bevel=0.0015)


@builder("fins")
def fins():
    """A pair of swim fins (palet) lying side by side, blades pointing forward."""
    one_fin(-1)
    one_fin(1)


@builder("sea-bed")
def sea_bed():
    """Inflatable sea mattress (deniz yatağı), long side along X: six fused air tubes, a pillow end and a valve."""
    parts = []
    n = 6
    for i in range(n):
        x = -0.25 + i * 0.1
        parts.append(box("tube", (0.088, 0.34, 0.09), (x, 0.0, 0.045)))
    parts.append(box("pillow", (0.09, 0.34, 0.115), (0.3, 0.0, 0.0575)))
    soft_body("mattress", parts, "mattress_blue", voxel=0.0045, smooth=10, wrinkle=0.0, target_tris=7000)
    cylinder("valve", 0.012, 0.02, (0.3, 0.13, 0.12), mat="plastic_white", segments=16)
    for i in range(0, n, 2):
        x = -0.25 + i * 0.1
        box("stripe", (0.05, 0.2, 0.003), (x, 0.0, 0.092), mat="plastic_white", bevel=0.001)
