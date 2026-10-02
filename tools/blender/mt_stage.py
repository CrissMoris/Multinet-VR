"""Stage pieces of the v2 event stand: backdrop, round floor, floor mat, stopwatch, scoreboard, luggage rack,
suitcase v2. Everything (except the suitcase, see below) is authored at its final world position around the
participant origin (0,0,0) facing +Y (Unity +Z); export origin 'none'.

suitcase-open: authored with its own origin at the centre of the base shell's underside (the point that rests on the
luggage rack). Place it in Unity at (0, RACK_TOP, SUITCASE_CENTRE_Y) = (0, 0.52, 0.40) -> rim at 0.78.
"""
import math

import bpy
from mathutils import Matrix, Vector

import mt_layout as L
from mtlib import (add_bevel, arc_points, arc_slab, assign_node, box, bake, cylinder, empty, extrude_outline, from_bmesh,
                   rotate, rounded_rect, sector_outline, sphere, sweep, torus, translate, world_bounds, scene_meshes)
import bmesh

BUILDERS = {}


def builder(name):
    def wrap(fn):
        BUILDERS[name] = fn
        return fn
    return wrap


# --------------------------------------------------------------------------------------------- backdrop / floor
@builder("stage-backdrop")
def stage_backdrop():
    r, arc, h = L.BACKDROP_R, L.BACKDROP_ARC, L.BACKDROP_H
    t0, t1 = -arc / 2, arc / 2
    bands = 16
    for i in range(bands):
        a0 = t0 + (t1 - t0) * i / bands
        a1 = t0 + (t1 - t0) * (i + 1) / bands
        arc_slab("band", r, r + 0.06, a0, a1 + 0.01, 0.78, h, f"stage_grad_{i}", steps=4)
    arc_slab("lower_band", r, r + 0.06, t0, t1, 0.16, 0.76, "stage_lower", steps=40)
    arc_slab("EMISSIVE.led_mid", r - 0.004, r + 0.0, t0 + 1, t1 - 1, 0.76, 0.78, "led_teal", steps=40)
    # cove: dark plinth with a recessed LED strip, and a top cornice with a second strip
    arc_slab("cove", r - 0.05, r + 0.08, t0, t1, 0.0, 0.14, "stage_cove", steps=40)
    arc_slab("cove_lip", r - 0.06, r - 0.04, t0, t1, 0.12, 0.16, "stage_trim", steps=40)
    led = arc_slab("EMISSIVE.led_cove", r - 0.045, r - 0.005, t0 + 1, t1 - 1, 0.14, 0.155, "led_cove", steps=40)
    arc_slab("cornice", r - 0.08, r + 0.08, t0, t1, h, h + 0.08, "stage_cove", steps=40)
    arc_slab("EMISSIVE.led_top", r - 0.075, r - 0.02, t0 + 1, t1 - 1, h - 0.012, h, "led_cove", steps=40)
    # side returns (end caps) so the panel reads as a solid stand wall
    for t in (t0, t1):
        p = L.pol(r + 0.01, t, 0)
        box("return", (0.06, 0.20, h + 0.08), (p[0], p[1], (h + 0.08) / 2), (0, 0, math.radians(-t)), mat="stage_trim")
    # logo zone: a slightly proud white panel; UI.logo quad for the (not supplied) official logo
    lz = 2.22
    # recessed logo panel: navy inset with a slim trim frame (two horizontal + two vertical trim strips)
    arc_slab("logo_panel", r - 0.004, r + 0.0, -17, 17, lz - 0.17, lz + 0.17, "stage_logo", steps=12)
    arc_slab("logo_trim", r - 0.014, r - 0.004, -17.5, 17.5, lz - 0.18, lz - 0.17, "stage_trim", steps=12)
    arc_slab("logo_trim", r - 0.014, r - 0.004, -17.5, 17.5, lz + 0.17, lz + 0.18, "stage_trim", steps=12)
    for t in (-17.5, 17.5):
        arc_slab("logo_trim", r - 0.014, r - 0.004, t - 0.3, t + 0.3, lz - 0.18, lz + 0.18, "stage_trim", steps=2)
    arc_slab("EMISSIVE.led_logo", r - 0.012, r - 0.006, -17.2, 17.2, lz - 0.17, lz - 0.165, "led_cove", steps=12)
    empty("UI.logo", L.pol(r - 0.006, 0, lz), rot=(0, 0, 0), scale=(1.1, 1.0, 0.28))
    # light hints (positions; the scene builder adds the lights)
    empty("LIGHT.key", (0.0, 0.35, 2.75), rot=(60, 0, 0), display="CONE", size=0.15)
    empty("LIGHT.fill_left", (-1.3, 0.1, 2.4), rot=(55, 0, 40), display="CONE", size=0.15)
    empty("LIGHT.fill_right", (1.3, 0.1, 2.4), rot=(55, 0, -40), display="CONE", size=0.15)
    empty("LIGHT.backdrop_wash", (0.0, 1.2, 2.7), rot=(25, 0, 0), display="CONE", size=0.15)
    empty("LIGHT.suitcase_spot", (0.0, -0.3, 2.5), rot=(20, 0, 0), display="CONE", size=0.15)


@builder("stage-floor")
def stage_floor():
    cylinder("platform", 2.5, 0.05, (0, 0, 0.025), mat="parquet_dark", segments=96)
    torus("edge_trim", 2.5, 0.025, (0, 0, 0.025), mat="stage_trim", steps=96, segments=10)
    # a slim teal inlay ring marking the play area
    arc_slab("inlay", 1.08, 1.12, -180, 180, 0.05, 0.052, "mat_teal", steps=96)


@builder("floor-mat")
def floor_mat():
    w, d = 0.9, 0.7
    extrude_outline("mat", rounded_rect(w, d, 0.05, 6), 0.012, "mat_navy", bevel=0.004)
    extrude_outline("border", rounded_rect(w - 0.04, d - 0.04, 0.04, 6), 0.002, "mat_teal", loc=(0, 0, 0.012))
    extrude_outline("inner", rounded_rect(w - 0.07, d - 0.07, 0.035, 6), 0.0021, "mat_navy", loc=(0, 0, 0.012))
    # footprints ("stand here"), toes forward
    foot = [(0.0, 0.13), (0.035, 0.12), (0.05, 0.07), (0.045, 0.0), (0.035, -0.06), (0.03, -0.12), (0.0, -0.135),
            (-0.03, -0.12), (-0.04, -0.06), (-0.035, 0.0), (-0.045, 0.07), (-0.03, 0.12)]
    for s in (-1, 1):
        pts = [(s * x + s * 0.09, y) for x, y in foot]
        extrude_outline("footprint", pts, 0.0015, "mat_teal", loc=(0, 0, 0.0141), rot=(0, 0, math.radians(-s * 8)))
    empty("UI.floor_text", (0, -0.24, 0.016), rot=(0, 0, 0), scale=(0.5, 1, 0.08))


# --------------------------------------------------------------------------------------------- stopwatch / scoreboard
@builder("stopwatch")
def stopwatch():
    """Big red stopwatch on the backdrop at theta 0, centre z 1.70, face towards the participant (-Y)."""
    cx, cy, cz = 0.0, L.BACKDROP_R - 0.07, L.STOPWATCH_Z
    R = 0.21
    rot = (math.radians(90), 0, 0)   # cylinder axis -> Y
    shell = cylinder("Shell", R, 0.09, (cx, cy, cz), rot, mat="stopwatch_red", segments=72, bevel=0.02, bevel_segments=4)
    torus("bezel", R - 0.028, 0.009, (cx, cy - 0.048, cz), (math.radians(90), 0, 0), mat="chrome", steps=72, segments=12)
    cylinder("Face", R - 0.03, 0.004, (cx, cy - 0.047, cz), rot, mat="stopwatch_face", segments=72)
    cylinder("glass_rim", R - 0.02, 0.004, (cx, cy - 0.04, cz), rot, mat="stopwatch_red", segments=72)
    # minute ticks (60) + 5-minute markers
    for i in range(60):
        a = math.radians(i * 6)
        big = i % 5 == 0
        ln, wd = (0.022, 0.005) if big else (0.011, 0.0025)
        rr = R - 0.045 - ln / 2
        box("tick", (wd, 0.002, ln), (cx + math.sin(a) * rr, cy - 0.050, cz + math.cos(a) * rr), (0, math.radians(-i * 6), 0), mat="stopwatch_ink")
    # crown (start/stop) on top, side pushers, hanging ring
    cylinder("crown_stem", 0.014, 0.05, (cx, cy, cz + R + 0.02), mat="chrome", segments=24)
    cylinder("crown", 0.026, 0.03, (cx, cy, cz + R + 0.055), mat="stopwatch_red", segments=32, bevel=0.006)
    for s in (-1, 1):
        cylinder("pusher", 0.012, 0.035, (cx + s * (R - 0.04), cy, cz + R - 0.03), (0, math.radians(s * 35), 0), mat="chrome", segments=20)
    # needle: pivot at the face centre; needle points up (12 o'clock) at rest
    empty("PIVOT.needle", (cx, cy - 0.052, cz), rot=(0, 0, 0))
    needle = [box("needle_body", (0.016, 0.005, R - 0.07), (cx, cy - 0.055, cz + (R - 0.07) / 2 - 0.02), mat="needle_red", bevel=0.002),
              box("needle_tail", (0.016, 0.004, 0.05), (cx, cy - 0.055, cz - 0.035), mat="needle_red", bevel=0.002),
              cylinder("needle_hub", 0.014, 0.006, (cx, cy - 0.056, cz), rot, mat="chrome", segments=24)]
    assign_node(needle, "Needle", parent="PIVOT.needle")
    # digits quad below the centre
    empty("UI.stopwatch_face", (cx, cy - 0.053, cz - 0.095), rot=(0, 0, 0), scale=(0.2, 1, 0.055))
    # bracket to the backdrop
    box("bracket", (0.12, 0.06, 0.12), (cx, cy + 0.07, cz), mat="chrome_dark", bevel=0.01)


@builder("scoreboard")
def scoreboard():
    """1.0 x 0.45 dark glass panel with a teal edge light, right of the stopwatch, facing the participant."""
    w, h, t = 1.0, 0.45, 0.03
    th = L.SCOREBOARD_T
    objs = [box("Panel", (w, t, h), (0, 0, 0), mat="glass_navy", bevel=0.006),
            box("frame_top", (w + 0.02, 0.02, 0.012), (0, 0.006, h / 2 + 0.006), mat="chrome_dark"),
            box("frame_bottom", (w + 0.02, 0.02, 0.012), (0, 0.006, -h / 2 - 0.006), mat="chrome_dark"),
            box("EMISSIVE.edge_light", (w + 0.03, 0.008, h + 0.03), (0, 0.012, 0), mat="led_teal", bevel=0.002),
            box("standoff", (0.03, 0.06, 0.03), (-w / 2 + 0.06, 0.045, 0), mat="chrome", bevel=0.005),
            box("standoff", (0.03, 0.06, 0.03), (w / 2 - 0.06, 0.045, 0), mat="chrome", bevel=0.005)]
    ui = empty("UI.scoreboard", (0, -t / 2 - 0.002, 0), rot=(0, 0, 0), scale=(0.92, 1, 0.38))
    objs.append(ui)
    rotate(objs, -th, "Z")
    p = L.pol(L.BACKDROP_R - 0.075, th, L.STOPWATCH_Z)
    translate(objs, p)


# --------------------------------------------------------------------------------------------- luggage rack
@builder("luggage-rack")
def luggage_rack():
    """Folding X-frame luggage rack, chrome tube + leather straps, top at RACK_TOP, centred under the suitcase."""
    w, d, top = 0.64, 0.46, L.RACK_TOP
    r = 0.011
    for s in (-1, 1):
        x = s * (w / 2 - 0.02)
        # X legs in the YZ plane
        sweep("leg_a", [(x, -d / 2 + 0.02, 0.02), (x, d / 2 - 0.02, top - 0.02)], r, "chrome", segments=14)
        sweep("leg_b", [(x, d / 2 - 0.02, 0.02), (x, -d / 2 + 0.02, top - 0.02)], r, "chrome", segments=14)
        cylinder("pivot", 0.016, 0.03, (x, 0, top / 2), (0, math.radians(90), 0), mat="chrome_dark", segments=16)
        for yy in (-d / 2 + 0.02, d / 2 - 0.02):
            sphere("foot", 0.016, (x, yy, 0.016), mat="plastic_black")
            cylinder("top_bar", r, w - 0.04, (0, yy, top - 0.02), (0, math.radians(90), 0), mat="chrome", segments=14)
    for i in range(4):
        y = -d / 2 + 0.05 + i * (d - 0.1) / 3
        box("strap", (w - 0.02, 0.045, 0.004), (0, y, top - 0.0), mat="leather_tan", bevel=0.001)
    # back-rest bar keeping the suitcase from sliding (far side)
    sweep("rest", [(-w / 2 + 0.02, d / 2 - 0.02, top - 0.02), (-w / 2 + 0.02, d / 2 + 0.02, top + 0.09), (w / 2 - 0.02, d / 2 + 0.02, top + 0.09), (w / 2 - 0.02, d / 2 - 0.02, top - 0.02)], r, "chrome", segments=14)
    objs = list(scene_meshes())
    translate(objs, (0, L.SUITCASE_CENTRE_Y, 0))


# --------------------------------------------------------------------------------------------- suitcase v2
def hollow(name, w, d, h, wall, corner, mat, inner_mat, steps=6):
    outer = extrude_outline(name, rounded_rect(w, d, corner, steps), h, mat, bevel=0.014, segments=3)
    inner = extrude_outline(name + "_cut", rounded_rect(w - 2 * wall, d - 2 * wall, max(0.005, corner - wall), steps), h + 0.02, inner_mat, loc=(0, 0, wall))
    mod = outer.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = inner
    mod.solver = "EXACT"
    bake(outer)
    bpy.data.objects.remove(inner)
    return outer


@builder("suitcase-open")
def suitcase_open():
    w, d = L.SUITCASE_W, L.SUITCASE_D
    base_h, lid_h, wall = 0.215, 0.10, 0.006
    stand = 0.045                 # back rails / handle housing the shell rests on
    z0 = stand
    rim = z0 + base_h             # = 0.26 -> 0.78 on the rack
    base = [hollow("shell", w, d, base_h, wall, 0.07, "shell_navy", "shell_inner")]
    translate(base, (0, 0, z0))
    # moulded ribs / texture lines on the long sides
    for i in range(4):
        z = z0 + base_h * (0.2 + 0.2 * i)
        for s in (-1, 1):
            base.append(box("rib", (w * 0.84, 0.006, 0.012), (0, s * (d / 2 + 0.001), z), mat="shell_navy", bevel=0.003))
    # underside: two rails + telescoping handle housing (the suitcase lies on these)
    for x in (-0.2, 0.2):
        base.append(box("rail", (0.03, d - 0.08, stand), (x, 0, stand / 2), mat="plastic_black", bevel=0.006))
    base.append(box("handle_housing", (0.22, 0.05, stand), (0.0, d / 2 - 0.05, stand / 2), mat="plastic_black", bevel=0.006))
    # lining, floor pad
    base.append(box("lining_floor", (w - 0.03, d - 0.03, 0.008), (0, 0, z0 + wall + 0.004), mat="lining_jacquard", bevel=0.004))
    for s in (-1, 1):
        base.append(box("lining_wall", (w - 0.03, 0.004, base_h - 0.03), (0, s * (d / 2 - wall - 0.004), z0 + base_h / 2 + 0.004), mat="lining_jacquard"))
        base.append(box("lining_wall", (0.004, d - 0.03, base_h - 0.03), (s * (w / 2 - wall - 0.004), 0, z0 + base_h / 2 + 0.004), mat="lining_jacquard"))
    # quilting lines on the floor pad
    for i in range(-3, 4):
        base.append(box("quilt", (0.003, d - 0.05, 0.002), (i * 0.085, 0, z0 + wall + 0.009), mat="lining_satin"))
    for j in range(-2, 3):
        base.append(box("quilt", (w - 0.05, 0.003, 0.002), (0, j * 0.085, z0 + wall + 0.009), mat="lining_satin"))
    # zipper rim
    rimo = extrude_outline("rim", rounded_rect(w + 0.006, d + 0.006, 0.07, 6), 0.014, "zipper_tape", loc=(0, 0, rim - 0.007))
    cut = extrude_outline("rim_cut", rounded_rect(w - 0.014, d - 0.014, 0.064, 6), 0.03, "zipper_tape", loc=(0, 0, rim - 0.012))
    mod = rimo.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cut
    bake(rimo)
    bpy.data.objects.remove(cut)
    base.append(rimo)
    base.append(box("zip_pull", (0.012, 0.03, 0.005), (0.25, -d / 2 - 0.004, rim - 0.005), mat="zipper_metal", bevel=0.001))
    # latches on the front long side, carry handle on the +X end, wheels on the -X end
    for x in (-0.22, 0.22):
        base.append(box("latch", (0.06, 0.016, 0.03), (x, -d / 2 - 0.006, rim - 0.03), mat="chrome", bevel=0.004))
    base.append(box("handle_base", (0.02, 0.16, 0.02), (w / 2 + 0.008, 0, z0 + base_h * 0.5), mat="plastic_black", bevel=0.005))
    base.append(sweep("handle_grip", [(w / 2 + 0.012, -0.06, z0 + base_h * 0.5), (w / 2 + 0.04, -0.05, z0 + base_h * 0.5), (w / 2 + 0.04, 0.05, z0 + base_h * 0.5), (w / 2 + 0.012, 0.06, z0 + base_h * 0.5)], 0.009, "plastic_black", scale=(1, 1.5)))
    # telescoping handle (collapsed) on the +X end: two tubes + grip
    for y in (-0.08, 0.08):
        base.append(cylinder("tele_tube", 0.008, 0.05, (w / 2 + 0.02, y, z0 + base_h * 0.22), (0, math.radians(90), 0), mat="chrome", segments=14))
    base.append(box("tele_grip", (0.025, 0.2, 0.025), (w / 2 + 0.052, 0, z0 + base_h * 0.22), mat="plastic_black", bevel=0.008))
    for y in (-d / 2 + 0.06, d / 2 - 0.06):
        for zz in (z0 + 0.04, z0 + base_h - 0.04):
            base.append(box("wheel_housing", (0.03, 0.05, 0.05), (-w / 2 - 0.012, y, zz), mat="plastic_black", bevel=0.008))
            base.append(cylinder("wheel", 0.024, 0.012, (-w / 2 - 0.038, y, zz), (math.radians(90), 0, 0), mat="wheel_rubber", segments=20, bevel=0.003))
            base.append(cylinder("hub", 0.009, 0.013, (-w / 2 - 0.038, y, zz), (math.radians(90), 0, 0), mat="chrome", segments=12))
    # divider: low mesh partition between the front band (shoes/upright) and the flat area
    base.append(box("divider", (w - 0.04, 0.004, 0.09), (0, -0.015, z0 + wall + 0.055), mat="mesh_pocket"))
    base.append(box("divider_hem", (w - 0.04, 0.008, 0.008), (0, -0.015, z0 + wall + 0.1), mat="zipper_tape"))
    assign_node(base, "Base")
    # organiser tray (front strip), 6 compartments
    org = [box("tray", (w - 0.05, 0.085, 0.004), (0, -d / 2 + 0.062, z0 + wall + 0.01), mat="lining_satin", bevel=0.002)]
    for i in range(7):
        x = -(w - 0.05) / 2 + i * (w - 0.05) / 6
        org.append(box("tray_wall", (0.004, 0.085, 0.045), (x, -d / 2 + 0.062, z0 + wall + 0.03), mat="lining_satin"))
    org.append(box("tray_wall", (w - 0.05, 0.004, 0.045), (0, -d / 2 + 0.105, z0 + wall + 0.03), mat="lining_satin"))
    assign_node(org, "Organiser", parent="Base")
    # elastic cross straps + buckle (lying over the flat area, raised later by StrapLift)
    sz = z0 + wall + 0.012
    straps = [box("strap", (0.04, d - 0.06, 0.003), (-0.18, 0.0, sz), (0, 0, math.radians(32)), mat="strap_elastic"),
              box("strap", (0.04, d - 0.06, 0.003), (0.18, 0.0, sz), (0, 0, math.radians(-32)), mat="strap_elastic"),
              box("buckle", (0.055, 0.035, 0.01), (0, 0.0, sz + 0.004), mat="plastic_black", bevel=0.003),
              box("buckle_bar", (0.02, 0.03, 0.012), (0, 0.0, sz + 0.004), mat="chrome", bevel=0.002)]
    for s in (-1, 1):
        straps.append(box("strap_anchor", (0.05, 0.03, 0.004), (s * 0.3, (d / 2 - 0.05), sz), mat="plastic_black", bevel=0.001))
        straps.append(box("strap_anchor", (0.05, 0.03, 0.004), (s * 0.3, -(d / 2 - 0.05), sz), mat="plastic_black", bevel=0.001))
    assign_node(straps, "Straps", parent="Base")
    # ---- lid (built closed, opening downward over the rim; then hinged open 100 deg about the far long edge)
    lid = [hollow("lid_shell", w, d, lid_h, wall, 0.07, "shell_navy", "shell_inner")]
    for i in range(3):
        z = lid_h * (0.25 + 0.25 * i)
        for s in (-1, 1):
            lid.append(box("rib", (w * 0.84, 0.006, 0.012), (0, s * (d / 2 + 0.001), z), mat="shell_navy", bevel=0.003))
    lid.append(box("lid_lining", (w - 0.03, d - 0.03, 0.005), (0, 0, wall + 0.003), mat="lining_jacquard", bevel=0.003))
    lid.append(box("lid_badge", (0.09, 0.05, 0.003), (0.0, 0.0, -0.001), mat="chrome", bevel=0.004))   # unbranded plate
    lid.append(box("lid_feet", (0.05, 0.03, 0.01), (0.25, -0.17, -0.004), mat="plastic_black", bevel=0.003))
    lid.append(box("lid_feet", (0.05, 0.03, 0.01), (-0.25, -0.17, -0.004), mat="plastic_black", bevel=0.003))
    # the lid interior as built points up; flip it so the opening faces down, lift onto the rim
    rotate(lid, 180, "X")
    translate(lid, (0, 0, rim + lid_h))
    # mesh zip pocket panel on the lid interior (now facing down) -> LidPocket node
    pz = rim + lid_h - wall - 0.010
    pocket = [box("pocket_mesh", (w - 0.08, d - 0.10, 0.006), (0, 0.0, pz), mat="lid_mesh", bevel=0.003),
              box("pocket_zip", (w - 0.08, 0.012, 0.004), (0, -(d - 0.10) / 2 + 0.004, pz - 0.004), mat="zipper_tape"),
              box("pocket_zip_teeth", (w - 0.10, 0.004, 0.0045), (0, -(d - 0.10) / 2 + 0.004, pz - 0.0045), mat="zipper_metal"),
              box("pocket_pull", (0.012, 0.03, 0.005), (0.18, -(d - 0.10) / 2 - 0.006, pz - 0.006), mat="zipper_metal", bevel=0.001),
              box("pocket_binding", (w - 0.07, 0.012, 0.005), (0, (d - 0.10) / 2, pz - 0.003), mat="zipper_tape"),
              box("pocket_binding", (0.012, d - 0.10, 0.005), (-(w - 0.08) / 2, 0, pz - 0.003), mat="zipper_tape"),
              box("pocket_binding", (0.012, d - 0.10, 0.005), ((w - 0.08) / 2, 0, pz - 0.003), mat="zipper_tape")]
    hinge = (0, d / 2, rim)
    pivot = empty("PIVOT.lid", hinge, rot=(0, 0, 0))
    # lid-pocket packing anchors (closed pose first, then rotated with the lid)
    packs = [empty(f"PACK.lidpocket.{i + 1:02d}", (-0.2 + i * 0.2, 0.0, rim + lid_h - wall - 0.03), rot=(180, 0, 0), parent="PIVOT.lid") for i in range(3)]
    # hinge pins
    for x in (-0.25, 0.25):
        lid.append(cylinder("hinge_pin", 0.007, 0.06, (x, d / 2 + 0.004, rim), (0, math.radians(90), 0), mat="chrome_dark", segments=12))
    # closed-pose clipping test: the lid must not dip below the rim anywhere except the hinge pins
    lo, hi = world_bounds([o for o in lid if o.name.startswith("lid_shell")])
    assert lo.z > rim - 0.002, f"lid shell dips below the rim: {lo.z:.4f} < {rim:.4f}"
    assert hi.z - rim >= 0.06 + 0.02, "lid interior too shallow for 6 cm stacked items"
    # open it: -100 deg about X around the hinge (authoring frame) = +100 deg about X in Unity
    rotate(lid + pocket + packs, -100, "X", hinge)
    assign_node(lid, "Lid", parent="PIVOT.lid")
    assign_node(pocket, "LidPocket", parent="Lid")
    # ---- packing anchors in the base (item bottom-centre, +Y forward = toward the hinge side item front)
    floor = z0 + wall + 0.012
    for i, (x, y) in enumerate([(-0.16, 0.05), (0.16, 0.05), (-0.16, 0.14), (0.16, 0.14)]):
        empty(f"PACK.flat.{i + 1:02d}", (x, y, floor), rot=(0, 0, 0), parent="Base")
    empty("PACK.shoecorner.01", (-0.24, -0.08, floor), rot=(0, 0, 90), parent="Base")
    empty("PACK.shoecorner.02", (0.24, -0.08, floor), rot=(0, 0, -90), parent="Base")
    empty("PACK.upright.01", (-0.07, -0.08, floor), rot=(0, 0, 0), parent="Base")
    empty("PACK.upright.02", (0.07, -0.08, floor), rot=(0, 0, 0), parent="Base")
    for i in range(6):
        x = -(w - 0.05) / 2 + (i + 0.5) * (w - 0.05) / 6
        empty(f"PACK.organiser.{i + 1:02d}", (x, -d / 2 + 0.062, floor), rot=(0, 0, 0), parent="Base")
    for i, (x, y) in enumerate([(-0.17, 0.08), (0.17, 0.08), (-0.1, -0.04), (0.1, -0.04)]):
        empty(f"PACK.top.{i + 1:02d}", (x, y, rim + 0.02), rot=(0, 0, 0), parent="Base")
    empty("VOL.placement", (0, 0, (floor + rim + 0.20) / 2), scale=(w - 0.03, d - 0.03, rim + 0.20 - floor), parent="Base", display="CUBE")
    empty("LIGHT.suitcase_fill", (0, -0.5, 1.0), rot=(-35, 0, 0), display="CONE", size=0.1)
