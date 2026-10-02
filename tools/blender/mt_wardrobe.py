"""Wardrobe U around the participant (v2 stand): carcass, hanging module, folded module, two open door leaves and the
two console tables. Everything is authored at its final world position (participant origin, facing +Y); export
origin 'none'. Slot positions come from mt_layout (audited against OVERHAUL_PLAN §1).

Left wing (theta < 0): business console + riser, hanging rail under the top shelf, shoe-rack door.
Right wing (theta > 0): leisure console with wicker basket, folded shelves, accessory/jewellery door.
"""
import math

import bpy
from mathutils import Vector

import mt_layout as L
from mtlib import (arc_points, arc_slab, assign_node, box, cylinder, empty, extrude_outline, rotate, rounded_rect, sphere,
                   sweep, torus, translate, scene_meshes, soft_body)

BUILDERS = {}


def builder(name):
    def wrap(fn):
        BUILDERS[name] = fn
        return fn
    return wrap


def radial_panel(name, t, r0, r1, z0, z1, thick, mat, bevel=0.0):
    """Vertical panel lying in the radial plane at azimuth t, between radii r0..r1."""
    a = math.radians(t)
    rc = (r0 + r1) / 2
    o = box(name, (thick, r1 - r0, z1 - z0), (rc * math.sin(a), rc * math.cos(a), (z0 + z1) / 2), (0, 0, -a), mat=mat, bevel=bevel)
    return o


def tangential_box(name, r, t, width, depth, z0, z1, mat, bevel=0.0):
    """Box centred at (r, t) with its width tangential and depth radial."""
    a = math.radians(t)
    return box(name, (width, depth, z1 - z0), (r * math.sin(a), r * math.cos(a), (z0 + z1) / 2), (0, 0, -a), mat=mat, bevel=bevel)


def slot_empties(slots, extra_rot_x=0.0):
    for s in slots:
        empty(s.name, s.pos, rot=(s.tilt + extra_rot_x, 0, s.yaw))


# --------------------------------------------------------------------------------------------- carcass
@builder("wardrobe-carcass")
def wardrobe_carcass():
    t0, t1 = L.SIDE_FROM, L.SIDE_TO
    rb = L.CARCASS_BACK_R
    h = 2.20
    for side in (-1, 1):
        a0, a1 = side * t0, side * t1
        lo, hi = min(a0, a1), max(a0, a1)
        # back wall: white lacquer outside, warm oak inside
        arc_slab("back_outer", rb, rb + 0.025, lo, hi, 0.0, h + 0.06, "lacquer_white", steps=24)
        arc_slab("back_oak", rb - 0.012, rb, lo + 0.3, hi - 0.3, 0.10, h, "oak_warm", steps=24)
        # inner side wall (towards the stage) and outer wing (door hinge side)
        radial_panel("side_inner", a0, 0.42, rb + 0.025, 0.0, h + 0.06, 0.03, "lacquer_white", bevel=0.003)
        radial_panel("side_inner_oak", a0 + side * 1.5, 0.44, rb - 0.01, 0.10, h, 0.006, "oak_warm")
        radial_panel("wing", a1, 0.60, rb + 0.025, 0.0, h + 0.06, 0.03, "lacquer_white", bevel=0.003)
        radial_panel("wing_oak", a1 - side * 1.5, 0.62, rb - 0.01, 0.10, h, 0.006, "oak_warm")
        # top, cornice LED, plinth, carcass floor
        arc_slab("top", 0.42, rb + 0.025, lo, hi, h, h + 0.06, "lacquer_white", steps=24, bevel=0.004)
        arc_slab("EMISSIVE.led_crown", 0.455, 0.49, lo + 1.5, hi - 1.5, h - 0.012, h, "led_strip", steps=24)
        arc_slab("crown_lip", 0.42, 0.46, lo, hi, h - 0.05, h, "lacquer_white", steps=24)
        extrude_outline("plinth", L.clipped_sector(0.46, rb, t0 + 0.5, t1 - 0.5, side), 0.10, "lacquer_greige")
        extrude_outline("floor", L.clipped_sector(0.44, rb, t0, t1, side), 0.02, "oak_warm", loc=(0, 0, 0.10))
        # upper cubby shelf (decor) with its own LED strip
        arc_slab("hat_shelf", 0.50, rb, lo + 0.5, hi - 0.5, 1.90, 1.922, "oak_warm", steps=24, bevel=0.002)
        arc_slab("EMISSIVE.led_hat", 0.52, 0.55, lo + 2, hi - 2, 1.888, 1.90, "led_strip", steps=24)
        # under-console drawer fronts (decor) below the console tops: two drawers per side
        for i, (z0, z1) in enumerate(((0.14, 0.44), (0.47, 0.80))):
            extrude_outline("drawer_front", L.clipped_sector(0.60, 0.625, t0 + 4, t1 - 4, side), z1 - z0, "lacquer_white", loc=(0, 0, z0), bevel=0.003)
            # brass pull
            tangential_box("pull", 0.595, side * (t0 + t1) / 2, 0.16, 0.012, (z0 + z1) / 2 + 0.08, (z0 + z1) / 2 + 0.092, "brass", bevel=0.003)
        empty(f"LIGHT.wing_{'left' if side < 0 else 'right'}", L.pol(0.75, side * 58, h - 0.05), rot=(0, 0, 0), display="CONE", size=0.1)


# --------------------------------------------------------------------------------------------- consoles
def clipped_slab(name, r_in, r_out, t_lo, t_hi, side, z0, z1, mat, bevel=0.0):
    return extrude_outline(name, L.clipped_sector(r_in, r_out, t_lo, t_hi, side), z1 - z0, mat, loc=(0, 0, z0), bevel=bevel)


def console(side, zone_slots, name_prefix):
    """Crescent console: top 0.92 (r 0.42..0.58, clipped beside the suitcase), brass legs; hutch top shelf at 1.55."""
    t0, t1 = L.SIDE_FROM + 1.5, L.SIDE_TO - 1.5
    r0, r1 = L.CONSOLE_R
    top = L.CONSOLE_TOP
    clipped_slab("console_top", r0, r1, t0, t1, side, top - 0.03, top, "lacquer_white_gloss", bevel=0.006)
    clipped_slab("apron", r0 + 0.012, r1 - 0.012, t0 + 1.5, t1 - 1.5, side, top - 0.10, top - 0.03, "lacquer_white")
    for (r, t) in ((r0 + 0.035, t1 - 2.5), (r1 - 0.035, t1 - 2.5), (r1 - 0.035, 52.0)):
        p = L.pol(r, side * t, 0)
        cylinder("leg", 0.014, top - 0.10, (p[0], p[1], (top - 0.10) / 2), mat="brass", segments=14, radius2=0.011)
    p = (side * (L.X_CLEAR + 0.035), 0.17, 0)
    cylinder("leg", 0.014, top - 0.10, (p[0], p[1], (top - 0.10) / 2), mat="brass", segments=14, radius2=0.011)
    # top display shelf (hutch) at 1.55 - above the lid volume, so it may run to the inner side wall
    ts = L.TOP_SHELF
    tr0, tr1 = L.TOP_SHELF_R
    lo, hi = sorted((side * t0, side * t1))
    arc_slab("top_shelf", tr0, tr1, lo, hi, ts - L.TOP_SHELF_T, ts, "oak_warm", steps=20, bevel=0.003)
    arc_slab("top_shelf_lip", tr0, tr0 + 0.012, lo, hi, ts - 0.04, ts, "brass", steps=20)
    arc_slab("EMISSIVE.led_shelf", tr0 + 0.02, tr0 + 0.05, lo + 1.5, hi - 1.5, ts - L.TOP_SHELF_T - 0.008, ts - L.TOP_SHELF_T, "led_strip", steps=20)
    p = L.pol(tr0 + 0.03, side * (t1 - 1.2), 0)
    cylinder("upright", 0.012, ts - top, (p[0], p[1], top + (ts - top) / 2), mat="brass", segments=14)
    p = L.pol(r1 - 0.03, side * 52.0, 0)
    cylinder("upright", 0.012, ts - top, (p[0], p[1], top + (ts - top) / 2), mat="brass", segments=14)
    slot_empties(zone_slots)
    empty(f"LIGHT.spot_{name_prefix}", L.pol(0.55, side * 58, 2.05), rot=(0, 0, 0), display="CONE", size=0.1)


@builder("console-table-business")
def console_business():
    side = -1
    console(side, L.business_slots(), "business")
    t0, t1 = L.SIDE_FROM + 1.5, L.SIDE_TO - 1.5
    # leather blotter with stitched edge on the console top
    clipped_slab("blotter", L.CONSOLE_R[0] + 0.02, L.CONSOLE_R[1] - 0.02, t0 + 2, t1 - 2, side, L.CONSOLE_TOP, L.CONSOLE_TOP + 0.004, "leather_tan", bevel=0.002)
    # riser shelf behind the console (under the hanging garments), oak with brass lip and a leather top
    r0, r1 = L.RISER_R
    clipped_slab("riser", r0, r1, t0, t1, side, L.RISER_TOP - 0.022, L.RISER_TOP, "oak_warm", bevel=0.003)
    clipped_slab("riser_front", r0, r0 + 0.012, t0, t1, side, L.RISER_TOP - 0.05, L.RISER_TOP, "brass")
    clipped_slab("riser_leather", r0 + 0.02, r1 - 0.02, t0 + 1, t1 - 1, side, L.RISER_TOP, L.RISER_TOP + 0.003, "leather_tan")
    for t in (47.0, t1 - 2):
        p = L.pol((r0 + r1) / 2, side * t, 0)
        box("riser_leg", (0.02, r1 - r0 - 0.03, L.RISER_TOP - 0.022 - 0.12), (p[0], p[1], 0.12 + (L.RISER_TOP - 0.022 - 0.12) / 2), (0, 0, math.radians(-side * t)), mat="oak_warm")
    # desk pad accessories: pen cup (decor) at the inner end
    p = L.pol(0.53, side * 74, L.CONSOLE_TOP)
    cylinder("pen_cup", 0.028, 0.08, (p[0], p[1], p[2] + 0.04), mat="leather_cognac", segments=20)


@builder("console-table-leisure")
def console_leisure():
    side = 1
    console(side, L.leisure_slots(), "leisure")
    # wicker basket at the outer end of the console (slot leisure.03 sits inside it)
    t = 79.0
    r = 0.51
    p = L.pol(r, t, L.CONSOLE_TOP)
    w, d, hgt = 0.18, 0.13, 0.11
    base = box("basket_floor", (w - 0.02, d - 0.02, 0.008), (p[0], p[1], p[2] + 0.004), (0, 0, math.radians(-t)), mat="wicker")
    # woven look: stacked rounded bands
    for i in range(5):
        z = p[2] + 0.012 + i * (hgt - 0.012) / 4
        ring = extrude_outline("basket_band", rounded_rect(w + 0.004 * (i % 2), d + 0.004 * (i % 2), 0.04, 5), (hgt - 0.012) / 4 - 0.002, "wicker",
                               loc=(p[0], p[1], z), rot=(0, 0, math.radians(-t)))
        cut = extrude_outline("basket_cut", rounded_rect(w - 0.016, d - 0.016, 0.034, 5), 0.2, "wicker", loc=(p[0], p[1], z - 0.05), rot=(0, 0, math.radians(-t)))
        mod = ring.modifiers.new("cut", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.object = cut
        from mtlib import bake
        bake(ring)
        bpy.data.objects.remove(cut)
    # rim + handles
    a = math.radians(t)
    from mathutils import Matrix
    rim = sweep("basket_rim", [(x, y, 0) for x, y in rounded_rect(w + 0.006, d + 0.006, 0.04, 6)], 0.006, "leather_cognac", closed=True, segments=8)
    rim.matrix_world = Matrix.Translation((p[0], p[1], p[2] + hgt)) @ Matrix.Rotation(-a, 4, "Z")
    # a folded teal towel in the basket corner (decor)
    tb = box("towel_decor", (0.06, 0.05, 0.03), (p[0], p[1], p[2] + 0.03), (0, 0, math.radians(-t)), mat="towel_turquoise", bevel=0.01)


# --------------------------------------------------------------------------------------------- modules
@builder("wardrobe-hanging-module")
def hanging_module():
    """Chrome rail under the left top shelf: brackets, 8 hooks (HOOK.nn = SLOT.hanging.nn)."""
    p0, p1 = L.rail_ends()
    p0, p1 = (p0[0], p0[1], L.RAIL_Z), (p1[0], p1[1], L.RAIL_Z)
    sweep("rail", [p0, p1], 0.011, "chrome", segments=16)
    shelf_bottom = L.TOP_SHELF - L.TOP_SHELF_T
    for p in (p0, p1):
        sweep("bracket", [(p[0], p[1], p[2]), (p[0], p[1], shelf_bottom)], 0.006, "chrome", segments=10)
        cylinder("bracket_plate", 0.02, 0.004, (p[0], p[1], shelf_bottom - 0.002), mat="chrome", segments=16)
    mid = tuple((p0[k] + p1[k]) / 2 for k in range(3))
    sweep("bracket", [mid, (mid[0], mid[1], shelf_bottom)], 0.006, "chrome", segments=10)
    for s in L.hooks():
        n = s.name.split(".")[-1]
        empty(f"HOOK.{n}", s.pos, rot=(0, 0, s.yaw))
        empty(s.name, s.pos, rot=(0, 0, s.yaw))
    empty("LIGHT.spot_hanging", L.pol(0.62, -58, 2.05), rot=(0, 0, 0), display="CONE", size=0.1)


@builder("wardrobe-folded-module")
def folded_module():
    """Three curved oak shelves (right wing) with brass lips and LED strips; SLOT.folded.nn on them."""
    lo, hi = L.SIDE_FROM + 2.5, L.SIDE_TO - 2.5
    for i, z in enumerate(L.FOLDED_SHELVES):
        r0 = 0.64 if i == 0 else 0.50
        r1 = 0.88
        arc_slab("shelf", r0, r1, lo, hi, z - 0.022, z, "oak_warm", steps=20, bevel=0.003)
        arc_slab("lip", r0, r0 + 0.012, lo, hi, z - 0.045, z, "brass", steps=20)
        arc_slab("EMISSIVE.led_folded", r0 + 0.02, r0 + 0.05, lo + 1.5, hi - 1.5, z - 0.03, z - 0.022, "led_strip", steps=20)
        # shelf dividers (oak) splitting each shelf into bays
        for t in (lo + (hi - lo) * 0.5,):
            radial_panel("divider", t, r0 + 0.02, r1 - 0.02, z, z + 0.12, 0.012, "oak_warm")
    # uprights at both ends
    for t in (lo + 0.8, hi - 0.8):
        radial_panel("upright", t, 0.50, 0.88, 0.12, L.TOP_SHELF - 0.025, 0.018, "oak_warm")
    slot_empties(L.folded_slots())
    empty("LIGHT.spot_folded", L.pol(0.62, 58, 2.05), rot=(0, 0, 0), display="CONE", size=0.1)


# --------------------------------------------------------------------------------------------- doors
def door_leaf(side):
    """Open leaf: hinge pins at the wing, lacquer outside, oak inside, brass handle. Returns (h, d, n)."""
    h, d, n = L.door_frame(side)
    w, t, z0, z1 = L.DOOR_W, 0.035, 0.06, 2.20
    cx = (h[0] + d[0] * w / 2, h[1] + d[1] * w / 2)
    yaw = math.atan2(-n[0], n[1])      # +Y of a box -> n
    box("leaf", (w, t, z1 - z0), (cx[0] - n[0] * t / 2, cx[1] - n[1] * t / 2, (z0 + z1) / 2), (0, 0, yaw), mat="lacquer_white", bevel=0.004)
    box("leaf_oak", (w - 0.02, 0.006, z1 - z0 - 0.02), (cx[0] + n[0] * 0.002, cx[1] + n[1] * 0.002, (z0 + z1) / 2), (0, 0, yaw), mat="oak_warm")
    for z in (0.35, 1.15, 1.95):
        cylinder("hinge", 0.012, 0.09, (h[0] - n[0] * t / 2, h[1] - n[1] * t / 2, z), mat="chrome_dark", segments=12)
    # handle on the outer face near the free edge
    hx = (h[0] + d[0] * (w - 0.06) - n[0] * (t + 0.02), h[1] + d[1] * (w - 0.06) - n[1] * (t + 0.02))
    box("handle", (0.02, 0.012, 0.22), (hx[0], hx[1], 1.05), (0, 0, yaw), mat="brass", bevel=0.004)
    for z in (0.96, 1.14):
        box("handle_foot", (0.012, 0.022, 0.012), (hx[0] + n[0] * 0.011, hx[1] + n[1] * 0.011, z), (0, 0, yaw), mat="brass")
    return h, d, n, yaw


def leaf_box(name, h, d, n, yaw, s0, s1, depth0, depth1, z0, z1, mat, bevel=0.0, rot_x=0.0):
    """Box on the leaf's inner face: along-leaf s0..s1, normal-offset depth0..depth1, heights z0..z1."""
    sc = (s0 + s1) / 2
    dc = (depth0 + depth1) / 2
    c = (h[0] + d[0] * sc + n[0] * dc, h[1] + d[1] * sc + n[1] * dc, (z0 + z1) / 2)
    return box(name, (s1 - s0, depth1 - depth0, z1 - z0), c, (math.radians(rot_x), 0, yaw), mat=mat, bevel=bevel)


@builder("wardrobe-door-left")
def door_left():
    side = -1
    h, d, n, yaw = door_leaf(side)
    # tilted 3-tier shoe rack + a high chrome rail (4th pair): boards 0.44 long, 0.20 deep, tilted 12 deg
    for z in L.SHOE_TIERS:
        b = leaf_box("tier", h, d, n, yaw, 0.03, 0.47, 0.02, 0.22, z - 0.012 - 0.021, z - 0.012, "oak_warm", bevel=0.003, rot_x=0)
        # tilt about the leaf axis through the back edge
        pivot = (h[0] + d[0] * 0.25 + n[0] * 0.02, h[1] + d[1] * 0.25 + n[1] * 0.02, z - 0.012)
        axis = Vector((d[0], d[1], 0))
        from mathutils import Matrix
        m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(12 * side), 4, axis) @ Matrix.Translation(-Vector(pivot))
        bpy.context.view_layer.update()
        b.matrix_world = m @ b.matrix_world
        lip = leaf_box("lip", h, d, n, yaw, 0.03, 0.47, 0.21, 0.225, z - 0.012 - 0.021, z + 0.018, "brass")
        lip.matrix_world = m @ lip.matrix_world
        for s in (0.03, 0.47):
            br = leaf_box("bracket", h, d, n, yaw, s - 0.006, s + 0.006, 0.0, 0.2, z - 0.05, z - 0.03, "chrome_dark")
            br.matrix_world = m @ br.matrix_world
    # high rail pair (shoes rest on two chrome rails)
    for dep in (0.05, 0.17):
        leaf_box("rail", h, d, n, yaw, 0.03, 0.47, dep - 0.005, dep + 0.005, 1.44 - 0.012, 1.44 - 0.002, "chrome")
    for s in (0.04, 0.46):
        leaf_box("rail_bracket", h, d, n, yaw, s - 0.006, s + 0.006, 0.0, 0.18, 1.44 - 0.03, 1.44 - 0.012, "chrome_dark")
    slot_empties(L.shoe_slots())
    empty("LIGHT.spot_shoes", L.door_point(side, 0.25, 0.3, 2.05), rot=(0, 0, 0), display="CONE", size=0.1)


@builder("wardrobe-door-right")
def door_right():
    side = 1
    h, d, n, yaw = door_leaf(side)
    # organiser cabinet on the inner face (s 0.27..0.50, z 0.86..1.46), two drawers pulled out, two ledges
    cab_s0, cab_s1 = 0.27, 0.495
    leaf_box("cabinet_side", h, d, n, yaw, cab_s0, cab_s0 + 0.012, 0.0, 0.12, 0.86, 1.46, "lacquer_white", bevel=0.002)
    leaf_box("cabinet_side", h, d, n, yaw, cab_s1 - 0.012, cab_s1, 0.0, 0.12, 0.86, 1.46, "lacquer_white", bevel=0.002)
    leaf_box("cabinet_top", h, d, n, yaw, cab_s0, cab_s1, 0.0, 0.12, 1.44, 1.46, "lacquer_white", bevel=0.002)
    leaf_box("cabinet_bottom", h, d, n, yaw, cab_s0, cab_s1, 0.0, 0.12, 0.86, 0.88, "lacquer_white", bevel=0.002)
    for z in L.DRAWER_Z:
        # drawer body pulled out by 0.16; floor at z (slot height), velvet lined
        leaf_box("drawer_box", h, d, n, yaw, cab_s0 + 0.016, cab_s1 - 0.016, 0.01, 0.27, z - 0.012, z, "velvet_navy")
        leaf_box("drawer_wall", h, d, n, yaw, cab_s0 + 0.016, cab_s0 + 0.022, 0.01, 0.27, z, z + 0.06, "velvet_navy")
        leaf_box("drawer_wall", h, d, n, yaw, cab_s1 - 0.022, cab_s1 - 0.016, 0.01, 0.27, z, z + 0.06, "velvet_navy")
        leaf_box("drawer_back", h, d, n, yaw, cab_s0 + 0.016, cab_s1 - 0.016, 0.01, 0.016, z, z + 0.06, "velvet_navy")
        leaf_box("drawer_front", h, d, n, yaw, cab_s0 + 0.008, cab_s1 - 0.008, 0.27, 0.288, z - 0.02, z + 0.075, "lacquer_white_gloss", bevel=0.003)
        leaf_box("drawer_pull", h, d, n, yaw, (cab_s0 + cab_s1) / 2 - 0.05, (cab_s0 + cab_s1) / 2 + 0.05, 0.288, 0.30, z + 0.02, z + 0.032, "brass", bevel=0.003)
    for z in L.LEDGE_Z:
        leaf_box("ledge", h, d, n, yaw, cab_s0 + 0.012, cab_s1 - 0.012, 0.0, 0.14, z - 0.016, z, "oak_warm", bevel=0.002)
        leaf_box("ledge_lip", h, d, n, yaw, cab_s0 + 0.012, cab_s1 - 0.012, 0.13, 0.142, z - 0.016, z + 0.012, "brass")
    # hat pegs (walnut knobs on brass stems)
    for s in (0.12, 0.36):
        p = L.door_point(side, s, 0.0, L.PEG_Z + 0.03)
        pe = L.door_point(side, s, 0.075, L.PEG_Z + 0.03)
        sweep("peg", [p, pe], 0.008, "brass", segments=10)
        sphere("peg_knob", 0.018, pe, mat="walnut")
    # jewellery valet board: velvet-covered panel tilted 15 deg (top out), 8 recesses with pads
    s0, s1, z0, z1 = 0.03, 0.27, 0.98, 1.42
    from mathutils import Matrix
    board = leaf_box("valet_board", h, d, n, yaw, s0, s1, 0.02, 0.045, z0, z1, "walnut", bevel=0.004)
    velvet = leaf_box("valet_velvet", h, d, n, yaw, s0 + 0.012, s1 - 0.012, 0.045, 0.052, z0 + 0.012, z1 - 0.012, "velvet_wine")
    parts = [board, velvet]
    for z in L.TRAY_Z:
        for s in L.TRAY_S:
            parts.append(leaf_box("recess", h, d, n, yaw, s - 0.04, s + 0.04, 0.052, 0.056, z - 0.035, z + 0.035, "velvet_navy", bevel=0.003))
            parts.append(leaf_box("recess_ledge", h, d, n, yaw, s - 0.042, s + 0.042, 0.052, 0.062, z - 0.04, z - 0.034, "walnut", bevel=0.002))
    pivot = (h[0] + d[0] * 0.15 + n[0] * 0.02, h[1] + d[1] * 0.15 + n[1] * 0.02, z0)
    axis = Vector((d[0], d[1], 0))
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(-15 * side), 4, axis) @ Matrix.Translation(-Vector(pivot))
    bpy.context.view_layer.update()
    for o in parts:
        o.matrix_world = m @ o.matrix_world
    # support struts under the tilted board
    for s in (s0 + 0.03, s1 - 0.03):
        leaf_box("strut", h, d, n, yaw, s - 0.006, s + 0.006, 0.0, 0.1, z1 - 0.07, z1 - 0.058, "brass")
    slot_empties(L.accessory_slots())
    slot_empties(L.jewellery_slots())
    empty("LIGHT.spot_accessories", L.door_point(side, 0.25, 0.3, 2.05), rot=(0, 0, 0), display="CONE", size=0.1)


# --------------------------------------------------------------------------------------------- hanger (standalone)
def build_hanger(origin=(0, 0, 0), mat_wood="hanger_walnut", clips=False):
    """Walnut flat hanger with a chrome hook; the hook top is at ``origin``. Returns the objects."""
    ox, oy, oz = origin
    objs = []
    # hook: arc from the top going down into the hanger body
    hook = arc_points((ox, oy, oz - 0.03), 0.03, 90, 270, 12, "yz")
    hook = [(ox, oy + (y - oy) * 1.0, z) for x, y, z in hook]
    pts = [(ox, oy, oz)] + hook[::-1][1:] + [(ox, oy + 0.0, oz - 0.075)]
    objs.append(sweep("hook", pts, 0.0032, "chrome", segments=10))
    objs.append(cylinder("hook_collar", 0.007, 0.012, (ox, oy, oz - 0.078), mat="chrome", segments=12))
    # body: two gently curved arms with rounded ends, 0.30 wide
    arm = [(-0.15, 0.0, -0.135), (-0.10, 0.0, -0.115), (-0.05, 0.0, -0.095), (0.0, 0.0, -0.085), (0.05, 0.0, -0.095), (0.10, 0.0, -0.115), (0.15, 0.0, -0.135)]
    arm = [(ox + x, oy + y, oz + z) for x, y, z in arm]
    objs.append(sweep("arm", arm, 0.011, mat_wood, segments=12, scale=(1.0, 0.45)))
    objs.append(sweep("bar", [(ox - 0.14, oy, oz - 0.138), (ox + 0.14, oy, oz - 0.138)], 0.006, mat_wood, segments=10, scale=(1, 0.6)))
    for s in (-1, 1):
        objs.append(sphere("arm_end", 0.012, (ox + s * 0.15, oy, oz - 0.135), (1, 0.45, 1), mat=mat_wood))
    if clips:
        for s in (-1, 1):
            x = ox + s * 0.10
            objs.append(box("clip", (0.018, 0.012, 0.03), (x, oy, oz - 0.158), mat="chrome", bevel=0.003))
            objs.append(box("clip_jaw", (0.02, 0.016, 0.012), (x, oy, oz - 0.17), mat="plastic_black", bevel=0.003))
    return objs


@builder("hanger")
def hanger():
    build_hanger((0, 0, 0))


hanger.origin = "none"
