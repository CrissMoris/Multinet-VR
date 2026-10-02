"""Stage layout + ergonomics audit for the v2 dressing-room stand (pure Python, no bpy; importable from plain
python for quick checks and from the Blender builders).

Frame: Blender authoring frame — X right, Y forward (away from the participant), Z up; metres.
Azimuth theta: 0 = +Y (forward), positive = to the participant's right (+X).  pos = (r sin t, r cos t, z).
Unity frame after export: X right, Z forward, Y up (same numbers, Y<->Z swapped).

Every SLOT / HOOK produced here is checked against OVERHAUL_PLAN §1:
  distance to the nearer shoulder (±0.19, 0, 1.40) <= 0.72 (all) and <= 0.65 (>= 90 %),
  height 0.80..1.55, |azimuth| <= 115 deg.
"""
import math

SHOULDERS = ((-0.19, 0.0, 1.40), (0.19, 0.0, 1.40))
REACH_ALL, REACH_90, Z_MIN, Z_MAX, AZ_MAX = 0.72, 0.65, 0.80, 1.55, 115.0

# ---------------------------------------------------------------------------------------------- key dimensions
SUITCASE_CENTRE_Y = 0.40          # interior centre (Unity z)
RACK_TOP = 0.52
SUITCASE_W, SUITCASE_D = 0.68, 0.44
RIM_Z = 0.78
# volume the tables must not enter: base + open lid (+ handle / wheels), generous
SUITCASE_VOLUME = ((-0.40, 0.14, 0.40), (0.40, 0.78, 1.30))   # (min xyz, max xyz)
X_CLEAR = 0.405                   # furniture below z 1.3 beside the suitcase stays at |x| >= X_CLEAR when y > 0.14

SIDE_FROM, SIDE_TO = 38.0, 78.0   # wing sector per side (deg); doors beyond
CARCASS_BACK_R = 0.98
CONSOLE_TOP = 0.92
CONSOLE_R = (0.42, 0.58)            # console top footprint (radial)
RISER_R = (0.62, 0.80)
TOP_SHELF_R = (0.44, 0.90)
RISER_TOP = 1.02
TOP_SHELF = 1.55                  # hutch / top display shelf (slot height = 1.55 = allowed max)
RAIL_Z, RAIL_R = 1.485, 0.76      # rail centre; hooks/slots sit on its top surface (RAIL_Z + 0.011)
HOOK_Z = RAIL_Z + 0.011
TOP_SHELF_T = 0.018               # top shelf board thickness (hanger hooks need the room underneath)
GARMENT_LEN = 0.415               # hanging variants: hook top -> hem (hems at ~1.11)
FOLDED_SHELVES = (0.98, 1.18, 1.40)   # bottom shelf 3 cm above the plan's 0.95: reach (it sits behind the console)
DOOR_HINGE_R, DOOR_HINGE_T, DOOR_OPEN_DEG, DOOR_W = 0.85, 80.0, 125.0, 0.50
BACKDROP_R, BACKDROP_ARC, BACKDROP_H = 1.90, 150.0, 2.60
STOPWATCH_Z, SCOREBOARD_T = 1.70, 22.0


def pol(r, t, z):
    a = math.radians(t)
    return (r * math.sin(a), r * math.cos(a), z)


def yaw_towards_player(t):
    """rotation_euler.z (deg) so that the empty's +Y points at the origin."""
    return 180.0 - t


class Slot:
    def __init__(self, name, pos, yaw=0.0, tilt=0.0):
        self.name, self.pos, self.yaw, self.tilt = name, tuple(pos), yaw, tilt   # tilt = rotation about local X

    def __repr__(self):
        return f"{self.name} {tuple(round(v, 3) for v in self.pos)} yaw {self.yaw:.0f}"


def arc_row(zone, start, r, z, t0, t1, n, side):
    """n slots evenly spread over the arc t0..t1 (deg, positive numbers; mirrored for side=-1)."""
    out = []
    for i in range(n):
        t = t0 + (t1 - t0) * (i + 0.5) / n
        ts = side * t
        out.append(Slot(f"SLOT.{zone}.{start + i:02d}", pol(r, ts, z), yaw_towards_player(ts)))
    return out


# ---------------------------------------------------------------------------------------------- door leaf frame
def door_frame(side):
    """Returns (hinge xyz, leaf direction (unit, from hinge to free edge), inner-face normal) for side ±1."""
    t = side * DOOR_HINGE_T
    h = pol(DOOR_HINGE_R, t, 0.0)
    a = math.radians(t)
    # closed leaf runs from the hinge towards the sector centre (−theta direction); it swings open towards the
    # participant and beyond (DOOR_OPEN_DEG). Inner face = the face that looked into the cabinet when closed.
    closed_dir = (-math.cos(a) * side, math.sin(a) * side)
    inner_n = (math.sin(a), math.cos(a))               # +r when closed
    rot = math.radians(DOOR_OPEN_DEG) * side
    c, s = math.cos(rot), math.sin(rot)
    d = (closed_dir[0] * c - closed_dir[1] * s, closed_dir[0] * s + closed_dir[1] * c)
    n = (inner_n[0] * c - inner_n[1] * s, inner_n[0] * s + inner_n[1] * c)
    return h, d, n


def door_point(side, s, depth, z):
    h, d, n = door_frame(side)
    return (h[0] + d[0] * s + n[0] * depth, h[1] + d[1] * s + n[1] * depth, z)


def door_yaw(side):
    """Yaw so that +Y of the empty points along the leaf's inner normal (out of the leaf face)."""
    _, _, n = door_frame(side)
    return math.degrees(math.atan2(-n[0], n[1]))


# ---------------------------------------------------------------------------------------------- the slot plan
def hooks():
    """8 hooks on a straight rail (chord) at RAIL_Z, left wing. HOOK.nn and SLOT.hanging.nn coincide."""
    t0, t1 = -44.0, -75.0
    p0, p1 = pol(RAIL_R, t0, HOOK_Z), pol(RAIL_R, t1, HOOK_Z)
    out = []
    for i in range(8):
        f = (i + 0.5) / 8
        p = tuple(p0[k] + (p1[k] - p0[k]) * f for k in range(3))
        t = math.degrees(math.atan2(p[0], p[1]))
        out.append(Slot(f"SLOT.hanging.{i + 1:02d}", p, yaw_towards_player(t)))
    return out


def rail_ends():
    return pol(RAIL_R, -40.0, RAIL_Z), pol(RAIL_R, -78.0, RAIL_Z)


def business_slots():
    s = []
    s += arc_row("business", 1, 0.50, CONSOLE_TOP, 54, 70, 1, -1)       # console front row
    s += arc_row("business", 2, 0.56, CONSOLE_TOP, 40, 80, 2, -1)       # console back row
    s += arc_row("business", 4, 0.68, RISER_TOP, 47, 77, 4, -1)         # riser under the hanging garments
    s += arc_row("business", 8, 0.52, TOP_SHELF, 52, 76, 2, -1)         # top shelf front
    s += arc_row("business", 10, 0.68, TOP_SHELF, 40, 76, 5, -1)         # top shelf back
    return s


def leisure_slots():
    s = []
    s += [Slot("SLOT.leisure.01", pol(0.51, 79, CONSOLE_TOP + 0.01), yaw_towards_player(79))]   # inside the wicker basket
    s += arc_row("leisure", 2, 0.56, CONSOLE_TOP, 40, 80, 2, 1)
    s += arc_row("leisure", 4, 0.52, TOP_SHELF, 52, 76, 2, 1)
    s += arc_row("leisure", 6, 0.66, TOP_SHELF, 40, 76, 4, 1)
    s += arc_row("leisure", 10, 0.79, TOP_SHELF, 54, 78, 3, 1)
    return s


def folded_slots():
    s = []
    s += arc_row("folded", 1, 0.67, FOLDED_SHELVES[0], 58, 80, 2, 1)
    s += arc_row("folded", 3, 0.60, FOLDED_SHELVES[1], 42, 76, 2, 1)
    s += arc_row("folded", 5, 0.74, FOLDED_SHELVES[1], 50, 76, 2, 1)
    s += arc_row("folded", 7, 0.60, FOLDED_SHELVES[2], 42, 76, 2, 1)
    s += arc_row("folded", 9, 0.76, FOLDED_SHELVES[2], 46, 76, 2, 1)
    return s


SHOE_TIERS = (0.88, 1.08, 1.28)      # rack tier surfaces (tilted 12 deg), slot at the tier centre
SHOE_DEPTH = 0.12                    # from the leaf face to the slot


def shoe_slots():
    side = -1
    yaw = door_yaw(side)
    pts = [(0.33, SHOE_TIERS[0]), (0.14, SHOE_TIERS[1]), (0.34, SHOE_TIERS[1]), (0.14, SHOE_TIERS[2]), (0.34, SHOE_TIERS[2]),
           (0.24, 1.44)]   # a 4th high rail for the 6th pair
    return [Slot(f"SLOT.shoes.{i + 1:02d}", door_point(side, s, SHOE_DEPTH, z), yaw, tilt=12.0) for i, (s, z) in enumerate(pts)]


DRAWER_Z = (0.95, 1.12)          # drawer floors (slots) - organiser cabinet on the right door
LEDGE_Z = (1.26, 1.38)
PEG_Z = 1.48
TRAY_Z = (1.05, 1.15, 1.25, 1.35)
TRAY_S = (0.09, 0.21)
DRAWER_S = (0.33, 0.45)


def accessory_slots():
    side = 1
    yaw = door_yaw(side)
    pts = [(DRAWER_S[0], DRAWER_Z[0], 0.16), (DRAWER_S[1], DRAWER_Z[0], 0.16),   # lower drawer (pulled out)
           (DRAWER_S[0], DRAWER_Z[1], 0.16), (DRAWER_S[1], DRAWER_Z[1], 0.16),   # upper drawer
           (0.39, LEDGE_Z[0], 0.08), (0.39, LEDGE_Z[1], 0.08),                   # small ledges
           (0.12, PEG_Z, 0.07), (0.36, PEG_Z, 0.07)]                             # two hat pegs
    return [Slot(f"SLOT.accessories.{i + 1:02d}", door_point(side, s, d, z), yaw) for i, (s, z, d) in enumerate(pts)]


def jewellery_slots():
    side = 1
    yaw = door_yaw(side)
    out = []
    i = 1
    for z in TRAY_Z:
        for s in TRAY_S:
            out.append(Slot(f"SLOT.jewellery.{i:02d}", door_point(side, s, 0.06 + (z - 1.0) * math.tan(math.radians(15)), z), yaw, tilt=15.0))
            i += 1
    return out


def all_slots():
    return hooks() + business_slots() + leisure_slots() + folded_slots() + shoe_slots() + accessory_slots() + jewellery_slots()


# ---------------------------------------------------------------------------------------------- audit
def audit(slots, strict=True):
    """Returns (ok, report lines). strict: also enforce the 90 % rule over the given set."""
    lines, bad, near = [], [], 0
    for s in slots:
        x, y, z = s.pos
        d = min(math.dist(s.pos, sh) for sh in SHOULDERS)
        az = math.degrees(math.atan2(x, y))
        errs = []
        if d > REACH_ALL:
            errs.append(f"reach {d:.3f}>{REACH_ALL}")
        if not (Z_MIN - 1e-6 <= z <= Z_MAX + 1e-6):
            errs.append(f"height {z:.3f}")
        if abs(az) > AZ_MAX:
            errs.append(f"azimuth {az:.0f}")
        if d <= REACH_90:
            near += 1
        flag = "OK " if not errs else "BAD"
        lines.append(f"{flag} {s.name:24s} r={math.hypot(x, y):.2f} az={az:6.1f} z={z:.2f} d={d:.3f}" + (" " + ",".join(errs) if errs else ""))
        if errs:
            bad.append(s.name)
    share = near / max(1, len(slots))
    lines.append(f"{len(slots)} slots, {near} within {REACH_90} m ({share:.0%}), {len(slots) - near} in the 0.65-0.72 band")
    ok = not bad and (share >= 0.9 or not strict)
    if strict and share < 0.9:
        lines.append("BAD: fewer than 90 % of slots within 0.65 m")
    return ok, lines


def clipped_sector(r_in, r_out, t_lo, t_hi, side, steps=12):
    """Ring sector (positive angles t_lo..t_hi on the given side) clipped to |x| >= X_CLEAR (suitcase clearance).
    Returns a CCW 2D outline."""
    ta = math.degrees(math.asin(min(1.0, X_CLEAR / r_out)))
    tb = math.degrees(math.asin(min(1.0, X_CLEAR / r_in)))
    t_out0, t_in0 = max(t_lo, ta), max(t_lo, tb)
    pts = []
    for i in range(steps + 1):
        t = t_out0 + (t_hi - t_out0) * i / steps
        pts.append((side * r_out * math.sin(math.radians(t)), r_out * math.cos(math.radians(t))))
    for i in range(steps, -1, -1):
        t = t_in0 + (t_hi - t_in0) * i / steps
        pts.append((side * r_in * math.sin(math.radians(t)), r_in * math.cos(math.radians(t))))
    area = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))
    return pts if area > 0 else list(reversed(pts))


def boxes_overlap(a, b, margin=0.0):
    return all(a[0][i] - margin < b[1][i] and b[0][i] - margin < a[1][i] for i in range(3))


if __name__ == "__main__":
    slots = all_slots()
    ok, lines = audit(slots)
    print("\n".join(lines))
    from collections import Counter
    print(Counter(s.name.split(".")[1] for s in slots))
    h, d, n = door_frame(1)
    print("door R hinge", h, "dir", d, "normal", n, "tip", door_point(1, DOOR_W, 0, 0))
    print("AUDIT", "PASS" if ok else "FAIL")
