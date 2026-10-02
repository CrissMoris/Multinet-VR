"""Builds the original 3D models of MultiTravel Valiz Challenge (products, suitcase, furniture).

Run (from the repo root):
  blender -b --factory-startup --python tools/blender/mt_build_assets.py -- [--only id1,id2]

Output: MultiTravelValizChallenge/Assets/MultiTravel/Art/Models/{Products,Environment}/<name>.fbx and
Art/Models/models.json (triangle counts, dimensions, material keys). All content is authored from code
in this repository; textures are referenced by material key (see Art/materials.json) and applied in Unity.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import mtlib  # noqa: E402
import mt_products  # noqa: E402
import mt_environment  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "MultiTravelValizChallenge", "Assets", "MultiTravel", "Art")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only = None
    if "--only" in argv:
        only = set(argv[argv.index("--only") + 1].split(","))
    mtlib.load_palette(os.path.join(ART, "materials.json"))
    manifest_path = os.path.join(ART, "Models", "models.json")
    manifest = {}
    if os.path.exists(manifest_path):
        with open(manifest_path, encoding="utf-8") as f:
            manifest = json.load(f)
    builders = [(n, f, "Products") for n, f in mt_products.BUILDERS.items()]
    builders += [(n, f, "Environment") for n, f in mt_environment.BUILDERS.items()]
    failures = []
    for name, fn, folder in builders:
        if only and name not in only:
            continue
        mtlib.reset()
        try:
            fn()
            # Environment pieces keep their authored origin (floor level, footprint centre / hinge-relative layout);
            # products are re-centred on the bottom of their bounds.
            origin = "none" if folder == "Environment" else "bottom"
            info = mtlib.export_asset(name, os.path.join(ART, "Models", folder), origin=origin)
            info["folder"] = folder
            manifest[name] = info
            print(f"MT_ASSET {name}: {info['tris']} tris, dims {info['dims_xyz']}")
        except Exception as ex:  # keep building the rest, report at the end
            import traceback
            traceback.print_exc()
            failures.append(f"{name}: {ex}")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(dict(sorted(manifest.items())), f, indent=2)
    if failures:
        print("MT_FAILURES " + " | ".join(failures))
        sys.exit(1)
    print("MT_DONE")


main()
