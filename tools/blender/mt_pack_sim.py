"""Quick packer simulation for the shelf wall (pure Python). Run: python tools/blender/mt_pack_sim.py"""
import math

R = 0.66
SW = {"S": 0.12, "M": 0.24, "L": 0.30}
GAP = 0.02
NEEDS = {
    "business": "SSSSSMMMMM",
    "leisure": "SSSSMMLLL",
    "folded": "MMMMM",
    "shoes": "MMM",
    "accessories": "MMMMS",
    "jewellery": "SSSSS",
}


def start_angle(row_top, first):
    return 22.0 if row_top else (math.degrees(math.asin(0.45 / R)) if first else 0.0)


def pack(zone, width_deg, first, front, rows=4):
    """Returns True when the zone's slots fit in a wedge of the given width (deg); rows 0..3, L only on the top row."""
    need = sorted(NEEDS[zone], key=lambda c: -SW[c])
    used = [0.0] * rows
    avail = []
    for ri in range(rows):
        lo = (22.0 if (front and ri == 3) else (start_angle(False, first) if first else 0.0))
        w = math.radians(max(0.0, width_deg - lo)) * R - 0.06
        avail.append(w)
    for c in need:
        cand = [ri for ri in range(rows) if (c != "L" or ri == 3)]
        best = None
        for ri in cand:
            if used[ri] + SW[c] + GAP <= avail[ri] + 1e-9:
                if best is None or used[ri] < used[best]:
                    best = ri
        if best is None:
            return False
        used[best] += SW[c] + GAP
    return True


if __name__ == "__main__":
    tot = 0
    for z in NEEDS:
        first = z in ("business", "leisure")
        for w in range(8, 120):
            if pack(z, w, first, first):
                print(z, w)
                tot += w
                break
    print("sum", tot, "+ hanging 22")
