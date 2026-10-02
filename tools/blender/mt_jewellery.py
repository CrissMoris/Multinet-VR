"""Jewellery products (presented in open boxes / on stands so each is >= 5 cm in two axes) and the practice tag.
Products: origin at the bottom centre (export origin 'bottom'). Axes: X right, Y forward, Z up."""
import math

import bpy

from mtlib import (box, cylinder, extrude_outline, rounded_rect, sphere, sweep, torus, arc_points, text)

BUILDERS = {}


def builder(name):
    def wrap(fn):
        BUILDERS[name] = fn
        return fn
    return wrap


def open_box(w, d, h, lid_mat="velvet_navy", inner_mat="velvet_wine", wall=0.004, lid_angle=105):
    """Hinged presentation box, lid open at the back (+Y). Interior floor at z = wall."""
    base = extrude_outline("box_base", rounded_rect(w, d, 0.008, 4), h, lid_mat, bevel=0.002)
    cut = extrude_outline("box_cut", rounded_rect(w - 2 * wall, d - 2 * wall, 0.006, 4), h, inner_mat, loc=(0, 0, wall))
    mod = base.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cut
    from mtlib import bake
    bake(base)
    bpy.data.objects.remove(cut)
    box("box_floor", (w - 2 * wall - 0.001, d - 2 * wall - 0.001, 0.004), (0, 0, wall + 0.002), mat=inner_mat)
    lid = extrude_outline("box_lid", rounded_rect(w, d, 0.008, 4), h * 0.55, lid_mat, bevel=0.002)
    inner = box("lid_inner", (w - 0.012, d - 0.012, 0.002), (0, 0, 0.001), mat=inner_mat)
    from mtlib import rotate, translate
    translate([lid, inner], (0, 0, 0))
    rotate([lid, inner], 180 - lid_angle, "X", (0, d / 2, 0))   # lid standing open at the back
    translate([lid, inner], (0, 0, h))
    return base


@builder("wristwatch")
def wristwatch():
    """Dress watch wrapped around a leather roll (display pillow) in an open box."""
    open_box(0.085, 0.085, 0.045)
    # pillow
    cyl = cylinder("pillow", 0.022, 0.07, (0, 0, 0.028), (0, math.radians(90), 0), mat="leather_tan", segments=24)
    # strap around the pillow (torus), case on top
    torus("strap", 0.0245, 0.009, (0, 0, 0.028), (0, math.radians(90), 0), mat="leather_watch", steps=40, segments=8, scale=(0.9, 0.35))
    cylinder("case", 0.018, 0.007, (0, 0, 0.054), mat="gold", segments=36, bevel=0.0015)
    cylinder("dial", 0.0155, 0.001, (0, 0, 0.0578), mat="watch_dial", segments=36)
    cylinder("crystal", 0.0165, 0.0015, (0, 0, 0.059), mat="crystal", segments=36)
    for i in range(12):
        a = math.radians(i * 30)
        box("index", (0.0012, 0.003, 0.0006), (math.sin(a) * 0.0125, math.cos(a) * 0.0125, 0.0585), (0, 0, -a), mat="gold")
    box("hand_h", (0.0012, 0.008, 0.0005), (0, 0.003, 0.0586), mat="stopwatch_ink")
    box("hand_m", (0.001, 0.012, 0.0005), (0.003, 0.004, 0.0587), (0, 0, math.radians(-40)), mat="stopwatch_ink")
    cylinder("crown", 0.0025, 0.003, (0.0195, 0, 0.054), (0, math.radians(90), 0), mat="gold", segments=10)
    for s in (-1, 1):
        box("lug", (0.004, 0.012, 0.004), (s * 0.016, 0, 0.051), mat="gold", bevel=0.001)


@builder("cufflinks")
def cufflinks():
    """Pair of gold-and-navy enamel cufflinks on a velvet pad in an open box (6 x 5 cm)."""
    open_box(0.065, 0.05, 0.03)
    box("pad", (0.05, 0.034, 0.012), (0, 0, 0.012), mat="velvet_wine", bevel=0.004)
    for s in (-1, 1):
        x = s * 0.014
        cylinder("link_face", 0.0075, 0.0025, (x, 0.0, 0.0195), mat="gold", segments=24, bevel=0.0005)
        cylinder("enamel", 0.0055, 0.001, (x, 0.0, 0.0212), mat="plastic_navy", segments=24)
        cylinder("post", 0.0015, 0.012, (x, 0.0, 0.013), mat="gold", segments=8)
        box("toggle", (0.004, 0.012, 0.002), (x, 0, 0.007), (0, 0, math.radians(20)), mat="gold", bevel=0.0005)


@builder("pearl-earrings")
def pearl_earrings():
    """Pearl drop earrings on a presentation card standing in an open box."""
    open_box(0.07, 0.055, 0.035)
    card = box("card", (0.05, 0.002, 0.04), (0, 0.0, 0.025), (math.radians(-12), 0, 0), mat="box_card", bevel=0.001)
    for s in (-1, 1):
        x = s * 0.013
        cylinder("post", 0.0008, 0.008, (x, -0.001, 0.036), (math.radians(90), 0, 0), mat="gold", segments=6)
        sphere("stud", 0.0032, (x, -0.0045, 0.036), mat="gold")
        sweep("hook", [(x, -0.0045, 0.035), (x, -0.005, 0.03), (x, -0.0045, 0.026)], 0.0007, "gold", segments=6)
        sphere("pearl", 0.0055, (x, -0.005, 0.0205), (1, 1, 1.15), mat="pearl")


@builder("minimal-necklace")
def minimal_necklace():
    """Fine gold chain with a small pendant on a velvet display bust (8 x 6 cm)."""
    base = cylinder("bust_base", 0.034, 0.006, (0, 0, 0.003), mat="walnut", segments=32, bevel=0.0015)
    sphere("bust", 0.03, (0, 0, 0.05), (1.1, 0.75, 1.5), mat="velvet_navy")
    chain = []
    for i in range(41):
        a = math.radians(-100 + 200 * i / 40)
        r = 0.031
        chain.append((math.sin(a) * r * 1.06, math.cos(a) * r * 0.78 - 0.002, 0.062 - 0.03 * (1 - abs(a) / math.radians(100)) ** 1.6 * 1.4))
    sweep("chain", chain, 0.0009, "gold", segments=6)
    sphere("pendant", 0.0045, (0, 0.023, 0.03), (1, 0.6, 1.3), mat="gold")
    sphere("stone", 0.0025, (0, 0.0255, 0.031), mat="crystal")


@builder("shell-necklace")
def shell_necklace():
    """Beach cord necklace with cowrie shells and wooden beads, laid on a kraft card (wrong item)."""
    box("card", (0.1, 0.09, 0.0025), (0, 0, 0.00125), mat="box_card", bevel=0.001)
    pts = []
    for i in range(50):
        a = math.radians(-140 + 280 * i / 49)
        r = 0.036 + 0.004 * math.sin(i * 1.3)
        pts.append((math.sin(a) * r, math.cos(a) * r, 0.0045))
    sweep("cord", pts, 0.0016, "cord_natural", segments=5)
    for i in range(5, 45, 6):
        p = pts[i]
        sphere("shell", 0.0065, (p[0], p[1], 0.0075), (1.1, 0.75, 0.6), mat="shell_cream", u=12, v=6)
        box("shell_slit", (0.002, 0.009, 0.001), (p[0], p[1], 0.0113), mat="leather_tan")
    for i in range(2, 48, 6):
        p = pts[i]
        sphere("bead", 0.004, (p[0], p[1], 0.006), mat="walnut", u=10, v=5)
    # a small loop clasp
    torus("clasp", 0.004, 0.001, (pts[0][0], pts[0][1], 0.0045), mat="silver", steps=16, segments=6)


@builder("party-tiara")
def party_tiara():
    """Silver party tiara with crystal points on a velvet pad (wrong item)."""
    box("pad", (0.13, 0.09, 0.012), (0, 0, 0.006), mat="velvet_wine", bevel=0.004, segments=2)
    band = arc_points((0, 0.0, 0.016), 0.06, 20, 160, 30, "xy")
    sweep("band", band, 0.0022, "silver", segments=8)
    for i in range(1, 30, 4):
        x, y, z = band[i]
        h = 0.045 if i == 13 or i == 17 else 0.028
        sweep("spike", [(x, y, z), (x * 1.05, y * 1.05, z + h)], 0.0015, "silver", segments=6, radius_fn=lambda t: 1 - 0.6 * t)
        sphere("gem", 0.0045, (x * 1.05, y * 1.05, z + h + 0.002), mat="crystal", u=12, v=6)
        sphere("gem_base", 0.003, (x * 1.02, y * 1.02, z + h * 0.45), mat="silver", u=10, v=5)
    for i in range(3, 28, 4):
        x, y, z = band[i]
        sweep("arc", [(x * 0.97, y * 0.97, z), (x, y, z + 0.012), (x * 1.03, y * 1.03, z)], 0.0012, "silver", segments=6)


@builder("practice-tag")
def practice_tag():
    """MultiTravel-coloured luggage tag (blue body, teal window strip, buckle strap), ~10 cm, no artwork."""
    outline = rounded_rect(0.058, 0.098, 0.012, 6)
    extrude_outline("tag", outline, 0.004, "tag_blue", bevel=0.0015)
    extrude_outline("window", rounded_rect(0.046, 0.05, 0.006, 4), 0.0012, "card_white", loc=(0, -0.008, 0.004))
    box("stripe", (0.058, 0.014, 0.0012), (0, 0.035, 0.004), mat="tag_teal")
    cylinder("eyelet", 0.0045, 0.0045, (0, 0.04, 0.002), mat="chrome", segments=16)
    cylinder("eyelet_hole", 0.0025, 0.006, (0, 0.04, 0.002), mat="tag_blue", segments=12)
    sweep("strap", [(0, 0.04, 0.005), (0.004, 0.07, 0.006), (0.012, 0.1, 0.004), (0.022, 0.11, 0.003), (0.034, 0.1, 0.002)], 0.0025, "tag_teal", segments=8, scale=(1.6, 0.5))
    box("buckle", (0.012, 0.01, 0.004), (0.01, 0.097, 0.004), mat="chrome", bevel=0.001)
    from mtlib import empty as mk_empty
    mk_empty("UI.tag_text", (0, -0.008, 0.0055), rot=(0, 0, 0), scale=(0.042, 1, 0.046))
