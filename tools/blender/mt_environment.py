"""Legacy hotel-room environment models (v1 room): wardrobe bay, luggage bench, bed, nightstand, lamp, mirror, curtains.

Axes: X right, Y forward (away from the participant), Z up. Units: metres. Origins are set to the bottom centre
by export_asset, so Unity places each piece by its footprint.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from mtlib import (add_bevel, add_subsurf, arc_points, box, cylinder, extrude_outline, from_bmesh, rotate, rounded_rect,
                   soft_body, sphere, sweep, torus, translate)

BUILDERS = {}


def builder(name):
    def wrap(fn):
        BUILDERS[name] = fn
        return fn
    return wrap


def hollow_shell(name, w, d, h, wall, corner, mat, inner_mat):
    """Open-top rounded shell (suitcase half): outer rounded box minus inner box, plus inner surfaces."""
    outer = extrude_outline(name, rounded_rect(w, d, corner, 8), h, mat, bevel=0.012, segments=4)
    inner = extrude_outline(name + "_cut", rounded_rect(w - 2 * wall, d - 2 * wall, max(0.005, corner - wall), 8), h + 0.02, inner_mat,
                            loc=(0, 0, wall))
    mod = outer.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = inner
    mod.solver = "EXACT"
    from mtlib import bake
    bake(outer)
    bpy.data.objects.remove(inner)
    return outer


def shell_ribs(w, d, h, z0, mat, count=3):
    """Horizontal ribs moulded into the polycarbonate shell (both long sides and ends)."""
    for i in range(count):
        z = z0 + h * (0.3 + 0.2 * i)
        for s in (-1, 1):
            box("rib", (w * 0.86, 0.006, 0.01), (0, s * (d / 2 + 0.001), z), mat=mat, bevel=0.003)
            box("rib", (0.006, d * 0.8, 0.01), (s * (w / 2 + 0.001), 0, z), mat=mat, bevel=0.003)


def suitcase_open_v1():  # superseded by mt_stage.suitcase_open (v2); kept for reference, not built
    """Medium spinner (70 x 46 cm), lying open: base towards the participant, lid hinged at the far side (+Y)."""
    w, d = 0.70, 0.46
    base_h, lid_h = 0.14, 0.11
    wall = 0.006
    wheel_h = 0.055
    z0 = wheel_h
    base = hollow_shell("base", w, d, base_h, wall, 0.06, "shell_navy", "shell_inner")
    base.location.z = z0
    shell_ribs(w, d, base_h, z0, "shell_navy")
    # fabric lining (floor + walls) and zipper rim
    box("lining_floor", (w - 0.03, d - 0.03, 0.006), (0, 0, z0 + wall + 0.003), mat="lining_jacquard", bevel=0.004)
    for s in (-1, 1):
        box("lining_wall", (w - 0.03, 0.004, base_h - 0.03), (0, s * (d / 2 - wall - 0.004), z0 + base_h / 2 + 0.005), mat="lining_jacquard")
        box("lining_wall", (0.004, d - 0.03, base_h - 0.03), (s * (w / 2 - wall - 0.004), 0, z0 + base_h / 2 + 0.005), mat="lining_jacquard")
    rim = extrude_outline("rim", rounded_rect(w + 0.004, d + 0.004, 0.06, 8), 0.012, "zipper_tape", loc=(0, 0, z0 + base_h - 0.006))
    cut = extrude_outline("rim_cut", rounded_rect(w - 0.012, d - 0.012, 0.055, 8), 0.03, "zipper_tape", loc=(0, 0, z0 + base_h - 0.01))
    mod = rim.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cut
    from mtlib import bake
    bake(rim)
    bpy.data.objects.remove(cut)
    # compression straps lying open on the floor, buckles
    for x in (-0.17, 0.17):
        box("strap", (0.035, d - 0.05, 0.003), (x, -0.02, z0 + wall + 0.0075), mat="strap_grey")
        box("buckle", (0.045, 0.03, 0.008), (x, d / 2 - 0.07, z0 + wall + 0.01), mat="plastic_black", bevel=0.002)
        box("strap_end", (0.035, 0.12, 0.003), (x, -d / 2 + 0.09, z0 + base_h - 0.02), (math.radians(-70), 0, 0), mat="strap_grey")
    # spinner wheels (double) at the four corners, under the base
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (w / 2 - 0.05), sy * (d / 2 - 0.05)
            box("wheel_housing", (0.05, 0.045, 0.022), (cx, cy, z0 - 0.009), mat="plastic_black", bevel=0.008)
            cylinder("swivel", 0.008, 0.012, (cx, cy, z0 - 0.022), mat="chrome", segments=12)
            for o in (-0.012, 0.012):
                cylinder("wheel", 0.021, 0.012, (cx + o, cy, 0.021), (0, math.radians(90), 0), mat="wheel_rubber", segments=20, bevel=0.003)
    # carry handle on the near long side, TSA-style lock (unbranded)
    box("handle_base", (0.16, 0.02, 0.02), (0, -d / 2 - 0.008, z0 + base_h * 0.55), mat="plastic_black", bevel=0.006)
    sweep("handle_grip", [(-0.06, -d / 2 - 0.012, z0 + base_h * 0.55), (-0.05, -d / 2 - 0.035, z0 + base_h * 0.55), (0.05, -d / 2 - 0.035, z0 + base_h * 0.55),
                          (0.06, -d / 2 - 0.012, z0 + base_h * 0.55)], 0.008, "plastic_black", scale=(1, 1.6))
    box("lock", (0.06, 0.015, 0.03), (0.22, -d / 2 - 0.005, z0 + base_h - 0.02), mat="space_grey", bevel=0.004)
    # lid: hinged at the far edge, opened ~100 degrees so the inside faces the participant
    lid_parts = []
    lid = hollow_shell("lid", w, d, lid_h, wall, 0.06, "shell_navy", "shell_inner")
    lid_parts.append(lid)
    lid_parts.append(box("lid_lining", (w - 0.03, d - 0.03, 0.005), (0, 0, wall + 0.003), mat="lining_jacquard", bevel=0.004))
    lid_parts.append(box("divider", (w - 0.05, d - 0.06, 0.004), (0, 0, lid_h - 0.025), mat="mesh_pocket", bevel=0.003))
    lid_parts.append(box("divider_zip", (w - 0.08, 0.008, 0.004), (0, d / 2 - 0.06, lid_h - 0.022), mat="zipper_tape"))
    lid_parts.append(box("divider_zip_pull", (0.012, 0.025, 0.004), (0.2, d / 2 - 0.075, lid_h - 0.018), mat="zipper_metal", bevel=0.001))
    for i in range(3):
        z = lid_h * (0.3 + 0.2 * i)
        for s in (-1, 1):
            lid_parts.append(box("rib", (w * 0.86, 0.006, 0.01), (0, s * (d / 2 + 0.001), z), mat="shell_navy", bevel=0.003))
            lid_parts.append(box("rib", (0.006, d * 0.8, 0.01), (s * (w / 2 + 0.001), 0, z), mat="shell_navy", bevel=0.003))
    lid_parts.append(box("telescopic_housing", (0.2, 0.02, 0.012), (0, 0, -0.004), mat="plastic_black", bevel=0.004))
    # lid built upside-down (opening up); flip it so the opening faces down, then hinge it open
    rotate(lid_parts, 180, "X")
    translate(lid_parts, (0, 0, z0 + base_h + lid_h))
    hinge = (0, d / 2, z0 + base_h)
    # the lid, flipped closed over the base, rotates about the far edge by -100 degrees (opening towards +Y)
    translate(lid_parts, (0, 0, 0))
    rotate(lid_parts, -100, "X", hinge)
    for x in (-0.22, 0.22):
        cylinder("hinge_pin", 0.006, 0.05, (x, d / 2 + 0.002, z0 + base_h), (0, math.radians(90), 0), mat="space_grey", segments=12)


@builder("wardrobe-bay")
def wardrobe_bay():
    """One 45 cm bay of the built-in dressing-room wardrobe: shelves at 0.68 / 1.08 / 1.48 m (top surfaces),
    two drawers below, LED strip under each shelf, crown at 2.05 m. Front edge at y = -0.19 (towards the participant)."""
    w, d, h = 0.45, 0.38, 2.05
    t = 0.018
    for s in (-1, 1):
        box("side", (t, d, h - 0.06), (s * (w / 2 - t / 2), 0, 0.06 + (h - 0.06) / 2), mat="oak_veneer", bevel=0.0015)
    box("back", (w - 2 * t, 0.012, h - 0.1), (0, d / 2 - 0.006, 0.08 + (h - 0.1) / 2), mat="lacquer_greige")
    box("plinth", (w - 0.02, d - 0.05, 0.06), (0, -0.015, 0.03), mat="lacquer_greige")
    box("crown", (w, d, 0.05), (0, 0, h - 0.025), mat="oak_veneer", bevel=0.002)
    box("crown_led", (w - 2 * t - 0.01, 0.012, 0.004), (0, -d / 2 + 0.03, h - 0.052), mat="led_strip")
    for top in (0.68, 1.08, 1.48):
        box("shelf", (w - 2 * t, d - 0.02, 0.025), (0, -0.005, top - 0.0125), mat="oak_veneer", bevel=0.0015)
        box("led", (w - 2 * t - 0.02, 0.01, 0.004), (0, -d / 2 + 0.035, top - 0.027), mat="led_strip")
    box("drawer_top", (w - 2 * t, d - 0.02, 0.022), (0, -0.005, 0.65), mat="oak_veneer")
    for i, (z, hh) in enumerate(((0.37, 0.24), (0.165, 0.17))):
        box("drawer_front", (w - 2 * t - 0.006, 0.02, hh - 0.006), (0, -d / 2 + 0.01, z + 0.0), mat="lacquer_white", bevel=0.002)
        box("pull", (0.12, 0.012, 0.012), (0, -d / 2 - 0.006, z + hh / 2 - 0.03), mat="brass", bevel=0.003)


@builder("luggage-bench")
def luggage_bench():
    """Upholstered bench the suitcase rests on: seat top at 0.42 m, 0.86 x 0.56 m."""
    w, d, top = 0.86, 0.56, 0.42
    cushion = box("cushion", (w, d, 0.09), (0, 0, top - 0.045))
    soft_body("cushion_soft", [cushion], "upholstery_boucle", voxel=0.006, smooth=6, wrinkle=0.0015, wrinkle_scale=0.08, target_tris=3000)
    box("frame", (w - 0.02, d - 0.02, 0.05), (0, 0, top - 0.11), mat="oak_veneer", bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cylinder("leg", 0.018, top - 0.13, (sx * (w / 2 - 0.06), sy * (d / 2 - 0.06), (top - 0.13) / 2), mat="oak_veneer", radius2=0.013, segments=16)
            cylinder("foot", 0.016, 0.01, (sx * (w / 2 - 0.06), sy * (d / 2 - 0.06), 0.005), mat="brass", segments=16)


@builder("bed")
def bed():
    """Hotel queen bed (1.6 x 2.0 m mattress), headboard at +Y."""
    w, l = 1.6, 2.0
    box("base", (w + 0.06, l + 0.04, 0.3), (0, 0, 0.17), mat="upholstery_navy", bevel=0.02, segments=3)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cylinder("foot", 0.025, 0.02, (sx * (w / 2 - 0.05), sy * (l / 2 - 0.05), 0.01), mat="brass", segments=16)
    mattress = box("mattress", (w, l, 0.24), (0, 0, 0.32 + 0.12))
    soft_body("mattress_soft", [mattress], "bedding_white", voxel=0.012, smooth=4, target_tris=2500)
    duvet = [box("duvet_top", (w + 0.08, l * 0.72, 0.07), (0, -l * 0.14, 0.59)),
             box("duvet_side", (0.05, l * 0.72, 0.28), (-(w / 2 + 0.03), -l * 0.14, 0.46)),
             box("duvet_side", (0.05, l * 0.72, 0.28), ((w / 2 + 0.03), -l * 0.14, 0.46)),
             box("duvet_foot", (w + 0.08, 0.05, 0.28), (0, -l / 2 - 0.0, 0.46)),
             box("duvet_fold", (w + 0.08, 0.16, 0.05), (0, l * 0.21, 0.63))]
    soft_body("duvet", duvet, "bedding_white", voxel=0.012, smooth=6, wrinkle=0.012, wrinkle_scale=0.18, target_tris=6000)
    throw = [box("throw", (w + 0.12, 0.45, 0.025), (0, -l / 2 + 0.3, 0.64)),
             box("throw_side", (0.025, 0.45, 0.3), (-(w / 2 + 0.06), -l / 2 + 0.3, 0.5)),
             box("throw_side", (0.025, 0.45, 0.3), ((w / 2 + 0.06), -l / 2 + 0.3, 0.5))]
    soft_body("throw", throw, "bedding_throw", voxel=0.01, smooth=5, wrinkle=0.008, wrinkle_scale=0.15, target_tris=3000)
    for x in (-0.38, 0.38):
        p = box("pillow", (0.68, 0.42, 0.16), (x, l / 2 - 0.28, 0.66))
        soft_body("pillow", [p], "bedding_white", voxel=0.01, smooth=12, wrinkle=0.006, wrinkle_scale=0.1, target_tris=1500)
    for x in (-0.3, 0.3):
        p = box("cushion", (0.5, 0.14, 0.32), (x, l / 2 - 0.42, 0.74), (math.radians(-15), 0, 0))
        soft_body("cushion", [p], "upholstery_navy", voxel=0.01, smooth=10, target_tris=1200)
    # channel-tufted headboard
    box("headboard_back", (w + 0.4, 0.08, 1.25), (0, l / 2 + 0.06, 0.625), mat="upholstery_navy", bevel=0.01)
    for i in range(7):
        x = -(w + 0.3) / 2 + (w + 0.3) * (i + 0.5) / 7
        c = box("channel", ((w + 0.3) / 7 - 0.01, 0.05, 0.75), (x, l / 2 + 0.0, 0.9))
        soft_body("channel", [c], "upholstery_navy", voxel=0.008, smooth=8, target_tris=500)


@builder("nightstand")
def nightstand():
    w, d, h = 0.5, 0.4, 0.55
    box("body", (w, d, h - 0.08), (0, 0, 0.08 + (h - 0.08) / 2), mat="oak_veneer", bevel=0.004)
    box("drawer", (w - 0.03, 0.015, 0.16), (0, -d / 2 - 0.006, h - 0.12), mat="lacquer_white", bevel=0.002)
    box("pull", (0.1, 0.012, 0.012), (0, -d / 2 - 0.018, h - 0.12), mat="brass", bevel=0.003)
    box("niche_shadow", (w - 0.04, 0.01, 0.2), (0, -d / 2 + 0.02, 0.22), mat="lacquer_greige")
    for sx in (-1, 1):
        for sy in (-1, 1):
            cylinder("leg", 0.012, 0.08, (sx * (w / 2 - 0.04), sy * (d / 2 - 0.04), 0.04), mat="brass", segments=12)


@builder("table-lamp")
def table_lamp():
    cylinder("base", 0.07, 0.025, (0, 0, 0.0125), mat="brass", segments=32, bevel=0.004)
    cylinder("stem", 0.008, 0.32, (0, 0, 0.18), mat="brass", segments=12)
    cylinder("shade", 0.15, 0.22, (0, 0, 0.42), mat="lamp_shade", radius2=0.12, segments=40)


@builder("floor-mirror")
def floor_mirror():
    """Full-length leaning mirror, 0.7 x 1.9 m, slight lean (top towards +Y)."""
    w, h = 0.7, 1.9
    frame = extrude_outline("frame", rounded_rect(w, h, 0.03), 0.035, "oak_veneer", bevel=0.004)
    hole = extrude_outline("hole", rounded_rect(w - 0.08, h - 0.08, 0.015), 0.06, "oak_veneer", loc=(0, 0, -0.01))
    mod = frame.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = hole
    from mtlib import bake
    bake(frame)
    bpy.data.objects.remove(hole)
    extrude_outline("glass", rounded_rect(w - 0.078, h - 0.078, 0.015), 0.006, "mirror_glass", loc=(0, 0, 0.012))
    box("back", (w - 0.02, h - 0.02, 0.01), (0, 0, 0.004), mat="lacquer_greige")
    objs = list(bpy.context.scene.objects)
    rotate(objs, -90, "X")  # stand it up: mirror faces -Y (towards the room)
    translate(objs, (0, 0, h / 2))
    rotate(objs, 6, "X", (0, 0, 0))


@builder("curtains")
def curtains():
    """Pair of pleated linen curtains, 1.2 m wide each, 2.6 m tall, with a brass rod."""
    for side in (-1, 1):
        bm = bmesh.new()
        cols, rows = 48, 20
        width, height = 1.1, 2.55
        verts = []
        for r in range(rows + 1):
            z = height * r / rows
            row = []
            for c in range(cols + 1):
                u = c / cols
                x = side * (0.12 + u * width * (0.75 + 0.25 * (1 - r / rows)))
                y = 0.035 * math.sin(u * math.pi * 14) * (0.6 + 0.4 * r / rows)
                row.append(bm.verts.new((x, y, z + 0.05)))
            verts.append(row)
        for r in range(rows):
            for c in range(cols):
                bm.faces.new((verts[r][c], verts[r][c + 1], verts[r + 1][c + 1], verts[r + 1][c]))
        o = from_bmesh("curtain", bm, "curtain_linen")
        sol = o.modifiers.new("solid", "SOLIDIFY")
        sol.thickness = 0.004
    cylinder("rod", 0.012, 2.9, (0, 0.0, 2.65), (0, math.radians(90), 0), mat="brass", segments=16)
    for x in (-1.45, 1.45):
        sphere("finial", 0.022, (x, 0, 2.65), mat="brass")


@builder("rug")
def rug():
    r = box("rug", (2.6, 2.6, 0.012), (0, 0, 0.006), mat="rug_wool", bevel=0.004)
    box("border", (2.62, 2.62, 0.004), (0, 0, 0.002), mat="upholstery_navy", bevel=0.002)


@builder("hanger-rail-decor")
def hanger_rail_decor():
    """Decorative hanging garments (not interactive) for the hanging section of the wardrobe."""
    cylinder("rail", 0.012, 1.3, (0, 0, 1.85), (0, math.radians(90), 0), mat="chrome", segments=12)
    for x in (-0.62, 0.62):  # free-standing rack frame
        cylinder("upright", 0.015, 1.85, (x, 0, 0.925), mat="chrome", segments=12)
        box("foot", (0.05, 0.5, 0.025), (x, 0, 0.0125), mat="chrome", bevel=0.006)
    colors = ["suit_navy", "shirt_white", "blazer_camel", "suit_charcoal", "shirt_blue", "dress_burgundy"]
    for i, mat in enumerate(colors):
        x = -0.45 + i * 0.18
        sweep("hook", arc_points((x, 0, 1.85), 0.015, -40, 220, 10, "yz"), 0.0025, "chrome", segments=6)
        sweep("hanger", [(x, -0.2, 1.75), (x, 0, 1.81), (x, 0.2, 1.75)], 0.008, "hanger_wood", scale=(1, 0.4))
        length = 0.95 if mat != "dress_burgundy" else 1.1
        g = [box("garment", (0.05, 0.44, length), (x, 0, 1.76 - length / 2)),
             box("shoulders", (0.06, 0.42, 0.08), (x, 0, 1.73))]
        soft_body("garment", g, mat, voxel=0.01, smooth=6, wrinkle=0.006, wrinkle_scale=0.12, target_tris=900)


@builder("room-shell")
def room_shell():
    """Hotel-room shell: floor, four walls (window opening on the left, door on the back), skirting, ceiling.
    Interior: x -3.3..3.6, y -2.9..2.8 (y = Unity z), height 2.9 m."""
    x0, x1, y0, y1, h, t = -3.3, 3.6, -2.9, 2.8, 2.9, 0.12
    cx, cy, w, d = (x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0
    box("floor", (w + 2 * t, d + 2 * t, 0.1), (cx, cy, -0.05), mat="parquet")
    box("ceiling", (w + 2 * t, d + 2 * t, 0.1), (cx, cy, h + 0.05), mat="ceiling_white")
    box("wall_front", (w + 2 * t, t, h), (cx, y1 + t / 2, h / 2), mat="plaster_wall")
    box("wall_right", (t, d, h), (x1 + t / 2, cy, h / 2), mat="plaster_wall")
    # back wall with a door opening (x 1.6..2.5)
    box("wall_back_a", (1.6 - x0, t, h), ((x0 + 1.6) / 2, y0 - t / 2, h / 2), mat="plaster_wall")
    box("wall_back_b", (x1 - 2.5, t, h), ((2.5 + x1) / 2, y0 - t / 2, h / 2), mat="plaster_wall")
    box("wall_back_c", (0.9, t, h - 2.15), (2.05, y0 - t / 2, 2.15 + (h - 2.15) / 2), mat="plaster_wall")
    box("door", (0.86, 0.04, 2.12), (2.05, y0 + 0.0, 1.06), mat="oak_veneer", bevel=0.004)
    box("door_frame", (1.0, 0.06, 0.06), (2.05, y0 + 0.01, 2.17), mat="lacquer_white")
    for x in (1.57, 2.53):
        box("door_jamb", (0.06, 0.06, 2.17), (x, y0 + 0.01, 1.085), mat="lacquer_white")
    cylinder("door_handle", 0.01, 0.13, (2.35, y0 + 0.05, 1.0), (0, math.radians(90), 0), mat="brass", segments=12)
    # left wall with a window opening (y -0.9..1.3, z 0.85..2.45)
    wy0, wy1, wz0, wz1 = -0.9, 1.3, 0.85, 2.45
    box("wall_left_a", (t, wy0 - y0, h), (x0 - t / 2, (y0 + wy0) / 2, h / 2), mat="plaster_wall")
    box("wall_left_b", (t, y1 - wy1, h), (x0 - t / 2, (wy1 + y1) / 2, h / 2), mat="plaster_wall")
    box("wall_left_c", (t, wy1 - wy0, wz0), (x0 - t / 2, (wy0 + wy1) / 2, wz0 / 2), mat="plaster_wall")
    box("wall_left_d", (t, wy1 - wy0, h - wz1), (x0 - t / 2, (wy0 + wy1) / 2, wz1 + (h - wz1) / 2), mat="plaster_wall")
    box("window_glass", (0.02, wy1 - wy0, wz1 - wz0), (x0 - t * 0.7, (wy0 + wy1) / 2, (wz0 + wz1) / 2), mat="window_glass")
    for y in (wy0, (wy0 + wy1) / 2, wy1):
        box("mullion", (0.08, 0.05, wz1 - wz0), (x0 - t / 2, y, (wz0 + wz1) / 2), mat="lacquer_white")
    for z in (wz0, wz1):
        box("transom", (0.1, wy1 - wy0 + 0.05, 0.05), (x0 - t / 2 + 0.01, (wy0 + wy1) / 2, z), mat="lacquer_white")
    box("sill", (0.2, wy1 - wy0 + 0.1, 0.03), (x0 + 0.06, (wy0 + wy1) / 2, wz0 - 0.015), mat="lacquer_white")
    # skirting
    sk = 0.08
    box("skirt", (w, 0.015, sk), (cx, y1 - 0.0075, sk / 2), mat="lacquer_white")
    box("skirt", (0.015, d, sk), (x1 - 0.0075, cy, sk / 2), mat="lacquer_white")
    box("skirt", (0.015, d, sk), (x0 + 0.0075, cy, sk / 2), mat="lacquer_white")
    box("skirt", (1.6 - x0, 0.015, sk), ((x0 + 1.6) / 2, y0 + 0.0075, sk / 2), mat="lacquer_white")
    box("skirt", (x1 - 2.5, 0.015, sk), ((2.5 + x1) / 2, y0 + 0.0075, sk / 2), mat="lacquer_white")
    # recessed ceiling downlights (emissive discs)
    for x in (-1.6, 0.0, 1.8):
        for y in (-1.6, 0.4, 2.0):
            cylinder("downlight", 0.06, 0.01, (x, y, h - 0.004), mat="led_strip", segments=20)
