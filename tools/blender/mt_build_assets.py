"""Builds the original 3D models of MultiTravel Valiz Challenge (products, suitcase, stage, wardrobe, garments).

Run (from the repo root):
  blender -b --factory-startup --python tools/blender/mt_build_assets.py -- [--only id1,id2] [--skip id1,id2]

Output: MultiTravelValizChallenge/Assets/MultiTravel/Art/Models/{Products,Environment}/<name>.fbx and
Art/Models/models.json (triangle counts, dimensions, material keys, nodes, empties). All content is authored from
code in this repository; textures are referenced by material key (see Art/materials.json) and applied in Unity.

The build fails when any SLOT/HOOK empty violates OVERHAUL_PLAN §1 (mt_layout.audit) or when the console tables
intersect the suitcase volume.
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy  # noqa: E402
import mtlib  # noqa: E402
import mt_layout  # noqa: E402
import mt_products  # noqa: E402
import mt_environment  # noqa: E402
import mt_stage  # noqa: E402
import mt_wardrobe  # noqa: E402
import mt_garments  # noqa: E402
import mt_jewellery  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "MultiTravelValizChallenge", "Assets", "MultiTravel", "Art")

# models whose SLOT empties are audited for reach, and whose geometry must stay out of the suitcase volume
TABLES = {"console-table-business", "console-table-leisure"}


def collect_slots():
    """SLOT.* empties of the current scene as mt_layout.Slot (authoring-frame world positions)."""
    bpy.context.view_layer.update()
    out = []
    for e in mtlib.scene_empties():
        if e.name.startswith("SLOT."):
            p = e.matrix_world.to_translation()
            out.append(mt_layout.Slot(e.name, (p.x, p.y, p.z)))
    return out


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None
    skip = set(argv[argv.index("--skip") + 1].split(",")) if "--skip" in argv else set()
    mtlib.load_palette(os.path.join(ART, "materials.json"))
    manifest_path = os.path.join(ART, "Models", "models.json")
    manifest = {}
    if os.path.exists(manifest_path):
        with open(manifest_path, encoding="utf-8") as f:
            manifest = json.load(f)
    builders = [(n, f, "Products") for n, f in mt_products.BUILDERS.items()]
    builders += [(n, f, "Products") for n, f in mt_garments.BUILDERS.items()]
    builders += [(n, f, "Products") for n, f in mt_jewellery.BUILDERS.items()]
    builders += [(n, f, "Environment") for n, f in mt_environment.BUILDERS.items()]
    builders += [(n, f, "Environment") for n, f in mt_stage.BUILDERS.items()]
    builders += [(n, f, "Environment") for n, f in mt_wardrobe.BUILDERS.items()]
    failures = []
    slots = []
    audited_models = []
    for name, fn, folder in builders:
        if (only and name not in only) or name in skip:
            continue
        mtlib.reset()
        try:
            fn()
            found = collect_slots()
            if found:
                ok, lines = mt_layout.audit(found, strict=False)
                print("\n".join(lines))
                if not ok:
                    raise RuntimeError(f"{name}: reach audit failed")
                slots += found
                audited_models.append(name)
            if name in TABLES:
                for o in mtlib.scene_meshes():
                    lo, hi = mtlib.world_bounds([o])
                    box = ((lo.x, lo.y, lo.z), (hi.x, hi.y, hi.z))
                    if mt_layout.boxes_overlap(box, mt_layout.SUITCASE_VOLUME):
                        raise RuntimeError(f"{name}/{o.name} bounds {box} intersect the suitcase volume {mt_layout.SUITCASE_VOLUME}")
            # Environment pieces keep their authored origin (world position / hinge-relative layout);
            # products are re-centred on the bottom of their bounds unless the builder returns an origin.
            origin = "none" if folder == "Environment" else "bottom"
            if getattr(fn, "origin", None) is not None:
                origin = fn.origin
            info = mtlib.export_asset(name, os.path.join(ART, "Models", folder), origin=origin)
            info["folder"] = folder
            manifest[name] = info
            print(f"MT_ASSET {name}: {info['tris']} tris, dims {info['dims_xyz']}")
        except Exception as ex:  # keep building the rest, report at the end
            import traceback
            traceback.print_exc()
            failures.append(f"{name}: {ex}")
    if slots:
        ok, lines = mt_layout.audit(slots, strict=True)
        print("MT_REACH_AUDIT over", audited_models)
        print("\n".join(lines[-2:]))
        if not ok:
            failures.append("reach audit (90 % rule) failed over " + ",".join(audited_models))
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(dict(sorted(manifest.items())), f, indent=2)
    if failures:
        print("MT_FAILURES " + " | ".join(failures))
        sys.exit(1)
    print("MT_DONE")


main()
