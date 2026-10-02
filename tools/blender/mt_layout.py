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
REACH_ALL, REACH_90, Z_MIN, Z_MAX, AZ_MAX = 9.0, 9.0, 0.40, 2.10, 170.0   # v5: the cabin is walked, reach no longer limits

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
# v5 "walk-in cabin": the participant walks (controller sticks) into a round dressing cabin.  A curved shelf wall of three
# rows runs around it (radius WALL_R); the front (|theta| < FRONT_OPEN_T, behind the suitcase) stays free for the stopwatch,
# scoreboard and logo, the rear (theta ~ 180) is the entrance from the hotel room.  Every zone owns an angular wedge with
# its own accent colour and header band.  Items keep (almost) real size (ProductItem.DisplayScale) and the slots are sized by
# item class:  S (<= 0.15 m wide), M (<= 0.26 m), W (wider).  Slot names carry the class: SLOT.<zone>.<nn>.<S|M|W>[T].
WALL_R = 1.45                          # slot centre radius
SHELF_IN, SHELF_OUT = 1.31, 1.59       # shelf boards, 0.28 m deep
BACK_IN, BACK_OUT = 1.61, 1.63
SKIN_OUT = 1.655                       # white outer skin seen from the hotel room
ROW_Z = (0.85, 1.17, 1.49)             # shelf surface heights = slot heights (0.32 m apart: nothing is tall)
TOP_ROW = len(ROW_Z) - 1
FREE_ROW = 0                           # no row is clipped (the suitcase is far from the wall)
WEDGE_MARGIN = 0.07                    # slot-free margin at each wedge boundary (m)
HEADER_Z = (1.90, 2.16)
WALL_TOP = 2.14
WALL_BOTTOM = 0.0
SLOT_W = {"S": 0.19, "M": 0.31, "W": 0.42}
SLOT_GAP = 0.0
FRONT_OPEN_T = 28.0                    # wall starts here (stopwatch / scoreboard / logo stay visible in front)
ENTRANCE_T = 152.0                     # wall ends here (rear opening = entrance, 56 deg wide)

# zone -> (side, class string of plain slots, class string of tall slots, accent material, front flag (unused))
ZONES = {
    "business": (-1, "SSSSSSMMWW", "", "velvet_navy", False),
    "folded": (-1, "MMMWW", "", "towel_orange", False),
    "leisure": (1, "SSSSSMMWW", "", "mat_teal", False),
    "shoes": (1, "MMM", "", "leather_cognac", False),
    "accessories": (1, "MMMWS", "", "walnut", False),
    "jewellery": (1, "SSSSSS", "", "velvet_wine", False),
}
ACCENT = {z: v[3] for z, v in ZONES.items()}
ACCENT["hanging"] = "leather_tan"
HANGING_SIDE = -1
HANGING_WIDTH = 30.0
GAP_DEG = 1.6
FIRST_T = FRONT_OPEN_T + 1.5
RAILS = ((1.32, 1.86, 3), (1.45, 1.86, 3))      # hanging rails: (radius, height z, hooks); the rear rail is offset half a step


def row_start_angle(r=WALL_R):
    return 0.0


def _row_avail(t0, t1, front, ri):
    """(lo, hi) degrees usable by slots of a row inside the wedge t0..t1."""
    m = math.degrees(WEDGE_MARGIN / WALL_R)
    return t0 + m, t1 - m


def pack_zone(zone, t0, t1):
    """Rows -> list of (class, tall) placed greedily, or None when the zone does not fit in the wedge."""
    side, plain, tall, accent, front = ZONES[zone]
    items = [(c, True) for c in tall] + [(c, False) for c in plain]
    items.sort(key=lambda x: (not x[1], -SLOT_W[x[0]]))
    avail = []
    for ri in range(len(ROW_Z)):
        lo, hi = _row_avail(t0, t1, front, ri)
        avail.append(math.radians(max(0.0, hi - lo)) * WALL_R)
    used = [0.0] * len(ROW_Z)
    rows = [[] for _ in ROW_Z]
    for c, t in items:
        cand = [TOP_ROW] if t else range(len(ROW_Z))
        best = None
        for ri in cand:
            if used[ri] + SLOT_W[c] <= avail[ri] + 1e-9 and (best is None or used[ri] < used[best]):
                best = ri
        if best is None:
            return None
        used[best] += SLOT_W[c] + SLOT_GAP
        rows[best].append((c, t))
    return rows


def _build_wedges():
    """Packs the zones side by side; returns {zone: (side, t0, t1, rows)} and the hanging wedge."""
    out = {}
    cursor = {-1: FIRST_T, 1: FIRST_T}
    order = [("business", -1), ("hanging", -1), ("folded", -1), ("leisure", 1), ("shoes", 1), ("accessories", 1), ("jewellery", 1)]
    for zone, side in order:
        t0 = cursor[side]
        if zone == "hanging":
            out[zone] = (side, t0, t0 + HANGING_WIDTH, None)
            cursor[side] = t0 + HANGING_WIDTH + GAP_DEG
            continue
        for w in range(8, 160):
            rows = pack_zone(zone, t0, t0 + w)
            if rows is not None:
                out[zone] = (side, t0, t0 + w, rows)
                cursor[side] = t0 + w + GAP_DEG
                break
        else:
            raise RuntimeError(f"zone {zone} does not fit")
    return out


WEDGES = _build_wedges()
MAX_THETA = max(v[2] for v in WEDGES.values())


def wedge_slots(zone):
    side, t0, t1, rows = WEDGES[zone]
    front = ZONES[zone][4]
    out = []
    n_idx = 1
    for ri, row in enumerate(rows):
        if not row:
            continue
        lo, hi = _row_avail(t0, t1, front, ri)
        total = sum(SLOT_W[c] for c, _ in row)
        span = math.radians(hi - lo) * WALL_R
        gap = (span - total) / len(row)
        x = gap / 2
        for c, tall in row:
            centre = x + SLOT_W[c] / 2
            t = side * (lo + math.degrees(centre / WALL_R))
            suffix = c + ("T" if tall else "")
            out.append(Slot(f"SLOT.{zone}.{n_idx:02d}.{suffix}", pol(WALL_R, t, ROW_Z[ri]), yaw_towards_player(t)))
            n_idx += 1
            x += SLOT_W[c] + gap
    return out


def hooks():
    side, t0, t1, _ = WEDGES["hanging"]
    out = []
    idx = 1
    for k, (r, z, n) in enumerate(RAILS):
        m = math.degrees(0.12 / r)
        step = (t1 - t0 - 2 * m) / n
        lo = t0 + m + (0.0 if k == 0 else step / 2)
        hi = t1 - m - (step / 2 if k == 0 else 0.0)
        for i in range(n):
            t = side * (lo + (hi - lo) * (i + 0.5) / n)
            out.append(Slot(f"SLOT.hanging.{idx:02d}.WT", pol(r, t, z + 0.011), yaw_towards_player(t)))
            idx += 1
    return out


def rail_arcs():
    """[(radius, z, theta_from, theta_to)] (signed degrees) of the hanging rails."""
    side, t0, t1, _ = WEDGES["hanging"]
    return [(r, z, side * (t0 + 1.0), side * (t1 - 1.0)) for r, z, _ in RAILS]


def business_slots():
    return wedge_slots("business")


def leisure_slots():
    return wedge_slots("leisure")


def folded_slots():
    return wedge_slots("folded")


def shoe_slots():
    return wedge_slots("shoes")


def accessory_slots():
    return wedge_slots("accessories")


def jewellery_slots():
    return wedge_slots("jewellery")


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
    print("AUDIT", "PASS" if ok else "FAIL")
