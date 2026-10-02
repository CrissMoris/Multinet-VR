"""Shelf wall around the participant (v3 stand): a curved back wall with header bands and dividers, and one shelf unit per
display zone. Everything is authored at its final world position (participant origin, facing +Y); export origin 'none'.
Slot positions come from mt_layout (audited against OVERHAUL_PLAN §1, reach relaxed in v3).

Left (theta < 0): business, hanging rails, folded.  Right (theta > 0): leisure, shoes, accessories, jewellery.
The FBX names of v2 are kept so the Unity scene builder does not change:
  wardrobe-carcass (wall, headers, dividers), console-table-business, console-table-leisure, wardrobe-hanging-module,
  wardrobe-folded-module, wardrobe-door-left (shoes), wardrobe-door-right (accessories + jewellery).
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


# --------------------------------------------------------------------------------------------- wall
def divider(side, t, z0=0.0):
    radial_panel("divider", side * t, L.SHELF_IN - 0.02, L.BACK_OUT, z0, L.HEADER_Z[1], 0.02, "lacquer_white", bevel=0.002)


@builder("wardrobe-carcass")
def wardrobe_carcass():
    """Round cabin wall: back wall floor to 2.10 m, accent header band per zone, dividers, outer white skin, plinth, cornice."""
    spans = {-1: [], 1: []}
    for zone, (side, t0, t1, rows) in L.WEDGES.items():
        accent = L.ACCENT[zone]
        lo, hi = sorted((side * (t0 - 0.8), side * (t1 + 0.8)))
        arc_slab("wall_" + zone, L.BACK_IN, L.BACK_OUT, lo, hi, L.WALL_BOTTOM, L.WALL_TOP, "lacquer_greige", steps=16)
        arc_slab("wall_inset", L.BACK_IN - 0.006, L.BACK_IN, lo + 0.5, hi - 0.5, 0.20, L.HEADER_Z[0] - 0.06, "oak_warm", steps=16)
        arc_slab("header_" + zone, L.BACK_IN - 0.012, L.BACK_OUT + 0.012, lo, hi, L.HEADER_Z[0], L.HEADER_Z[1], accent, steps=16, bevel=0.004)
        arc_slab("EMISSIVE.led_header_" + zone, L.BACK_IN - 0.01, L.BACK_IN - 0.002, lo + 1.0, hi - 1.0, L.HEADER_Z[0] - 0.014, L.HEADER_Z[0], "led_strip", steps=16)
        divider(side, t0 - 0.8)
        divider(side, t1 + 0.8)
        mid = (t0 + t1) / 2
        empty(f"LIGHT.wardrobe_{zone}", L.pol(0.9, side * mid, 2.45), rot=(0, 0, 0), display="CONE", size=0.1)
        empty(f"UI.header_{zone}", L.pol(L.BACK_IN - 0.016, side * mid, (L.HEADER_Z[0] + L.HEADER_Z[1]) / 2), rot=(0, 0, 0),
              scale=(math.radians(t1 - t0) * L.BACK_IN, 1, 0.05))
        spans[side].append((t0 - 0.8, t1 + 0.8))
    for side, lst in spans.items():
        lo_t, hi_t = min(a for a, _ in lst), max(b for _, b in lst)
        lo, hi = sorted((side * lo_t, side * hi_t))
        # outer skin (white lacquer), plinth and cornice as seen from the hotel room
        arc_slab("skin", L.BACK_OUT, L.SKIN_OUT, lo, hi, 0.0, L.WALL_TOP + 0.04, "lacquer_white", steps=40)
        arc_slab("plinth", L.BACK_OUT, L.SKIN_OUT + 0.02, lo, hi, 0.0, 0.12, "lacquer_greige", steps=40)
        arc_slab("cornice", L.BACK_OUT, L.SKIN_OUT + 0.03, lo, hi, L.WALL_TOP + 0.04, L.WALL_TOP + 0.09, "lacquer_white", steps=40)
        arc_slab("EMISSIVE.led_crown", L.BACK_IN - 0.05, L.BACK_IN - 0.01, lo + 1.0, hi - 1.0, L.WALL_TOP - 0.02, L.WALL_TOP, "led_strip", steps=40)
        # entrance jamb: tall white post at the end of the wall
        jamb_t = side * hi_t
        radial_panel("jamb", jamb_t, L.SHELF_IN - 0.05, L.SKIN_OUT + 0.02, 0.0, L.WALL_TOP + 0.09, 0.06, "lacquer_white", bevel=0.004)


# --------------------------------------------------------------------------------------------- shelves
def shelf_unit(zone):
    """Thin curved shelves for every used row of a zone (brackets under each board)."""
    side, t0, t1, rows = L.WEDGES[zone]
    accent = L.ACCENT[zone]
    used = [i for i, r in enumerate(rows) if r]
    lo, hi = sorted((side * (t0 - 0.4), side * (t1 + 0.4)))
    for ri in used:
        z = L.ROW_Z[ri]
        arc_slab("shelf", L.SHELF_IN + 0.012, L.SHELF_OUT, lo, hi, z - 0.024, z, "oak_warm", steps=24, bevel=0.003)
        arc_slab("lip", L.SHELF_IN, L.SHELF_IN + 0.012, lo, hi, z - 0.05, z + 0.012, accent, steps=24)
        arc_slab("EMISSIVE.led_" + zone, L.SHELF_IN + 0.03, L.SHELF_IN + 0.06, lo + 0.8, hi - 0.8, z - 0.032, z - 0.024, "led_strip", steps=24)
        for t in (t0 + 3.0, (t0 + t1) / 2, t1 - 3.0):
            tangential_box("bracket", L.SHELF_OUT - 0.1, side * t, 0.016, 0.20, z - 0.1, z - 0.024, "chrome_dark", bevel=0.002)
    slot_empties(L.wedge_slots(zone))


@builder("console-table-business")
def console_business():
    shelf_unit("business")


@builder("console-table-leisure")
def console_leisure():
    shelf_unit("leisure")


@builder("wardrobe-folded-module")
def folded_module():
    shelf_unit("folded")


@builder("wardrobe-door-left")
def shoes_unit():
    shelf_unit("shoes")


@builder("wardrobe-door-right")
def accessories_unit():
    shelf_unit("accessories")
    shelf_unit("jewellery")


@builder("wardrobe-hanging-module")
def hanging_module():
    """Two chrome rails (front / rear) on brackets from the back wall; HOOK.nn = SLOT.hanging.nn."""
    side = L.WEDGES["hanging"][0]
    for k, (r, z, a0, a1) in enumerate(L.rail_arcs()):
        pts = [L.pol(r, a0 + (a1 - a0) * i / 12, z) for i in range(13)]
        sweep("rail", pts, 0.011, "chrome", segments=16)
        for t in (a0, (a0 + a1) / 2, a1):
            p = L.pol(r, t, z)
            q = L.pol(L.BACK_IN - 0.004, t, z)
            sweep("bracket", [p, q], 0.006, "chrome", segments=10)
            cylinder("bracket_plate", 0.02, 0.004, (q[0], q[1], q[2]), rot=(math.radians(90), 0, math.radians(-t)), mat="chrome", segments=16)
    for s in L.hooks():
        n = s.name.split(".")[2]
        empty(f"HOOK.{n}", s.pos, rot=(0, 0, s.yaw))
        empty(s.name, s.pos, rot=(0, 0, s.yaw))
    empty("LIGHT.spot_hanging", L.pol(0.45, side * 94, 2.0), rot=(0, 0, 0), display="CONE", size=0.1)


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
