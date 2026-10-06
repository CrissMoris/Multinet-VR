# Asset manifest

Every third-party file in the project is listed here with its source and licence. Last verified: 2026-10-02.

- **Poly Haven** licence checked 2026-10-02: https://polyhaven.com/license — all assets are **CC0 1.0** (public domain
  dedication; commercial use, modification and redistribution allowed, no attribution required — authors are credited
  here anyway). Download script: `tools/assets/fetch-polyhaven.mjs` (list: `tools/assets/polyhaven-assets.json`).
- **Original project models** are authored from code in this repository (`tools/blender/*.py`, Blender 5.1 headless) and
  carry no third-party licence.
- **Official MultiTravel branding: NOT SUPPLIED.** No logo, brand typeface or brand colours were provided. The apps show
  the product title as plain text ("MultiTravel: Packing Challenge"); no logo was drawn or imitated. Dependency: the event
  owner must supply the official logo (PNG/SVG) → `AppConfig.Branding.LogoSprite` (VR/operator) and the web leaderboard
  header. The suitcase model is deliberately unbranded.

## 1. Poly Haven models (CC0)

Import path: `MultiTravelValizChallenge/Assets/MultiTravel/ThirdParty/PolyHaven/<id>/` (FBX + 1K textures).

| Asset | Used as | Source URL | Author | Modifications |
|---|---|---|---|---|
| Round Spectacles | product `glasses` (Gözlük) and `sunglasses` (Güneş Gözlüğü) | https://polyhaven.com/a/round_spectacles | Sean Buckley | URP materials; glass lens made transparent (glasses) or dark tinted (`sunglass_lens`, sunglasses). |
| Rubber Duck Toy | product `rubber-duck` (Oyuncak Ördek, wrong item) | https://polyhaven.com/a/rubber_duck_toy | Plat251 | Scaled to 16 cm. |
| Garden Gnome | product `garden-gnome` (Bahçe Cücesi, wrong item) | https://polyhaven.com/a/garden_gnome | Bhargav Kubal | Scaled to 32 cm. |
| Ukulele 01 | product `ukulele` (wrong item) | https://polyhaven.com/a/Ukulele_01 | Joseph Burgan | Laid on its back for shelving. |
| Football | product `football` (Futbol Topu, wrong item) | https://polyhaven.com/a/football | Amal Kumar | The asset contains an intact and a deflated ball; only the intact ball is shown; scaled to 22 cm. |
| Fishermans Hat | product `beach-hat` (Balıkçı Şapkası, wrong item) | https://polyhaven.com/a/fishermans_hat | PierreB3D | none |
| Binoculars | product `binoculars` (Dürbün, wrong item) | https://polyhaven.com/a/binoculars | Derek Wight | Scaled to 18 cm. |
| Mid Century Lounge Chair | room furniture | https://polyhaven.com/a/mid_century_lounge_chair | Kuutti Siitonen | URP material remap, scale/placement. |
| Modern Coffee Table 01 | room furniture | https://polyhaven.com/a/modern_coffee_table_01 | Amin | URP material remap, scale/placement. |
| Calathea Orbifolia 01 | room plant | https://polyhaven.com/a/calathea_orbifolia_01 | Rob Tuytel, Rico Cilliers | Scale/placement. |
| Drawer Cabinet | room furniture (drawers) | https://polyhaven.com/a/drawer_cabinet | Ulan Cabanilla | URP material remap, scale/placement. |
| Ceramic Vase 01 | decoration | https://polyhaven.com/a/ceramic_vase_01 | James Ray Cock | Scale/placement. |
| Hanging Picture Frame 01 | wall decoration | https://polyhaven.com/a/hanging_picture_frame_01 | James Ray Cock | Scale/placement. |
| Wall Clock | wall decoration | https://polyhaven.com/a/wall_clock | PierreB3D | Scale/placement. |

Downloaded by the previous development phase and currently **not placed** in a scene (kept, CC0, not included in the
player build because nothing references them): Modern Wooden Cabinet (Patrik Pangerl), Steel Frame Shelves 03 (Ulan
Cabanilla), Wooden Display Shelves 01 (James Ray Cock), Modern Ceiling Lamp 01 (James Ray Cock), Standing Picture
Frame 01 (James Ray Cock), Throw Pillows 01 (Serhii Khromov), Stationery Supplies (Mateusz Sadek), Binder Notebook
(DaDrood), Wicker Basket 02 (Kuutti Siitonen) — URLs `https://polyhaven.com/a/<id>`. Credits file:
`ThirdParty/PolyHaven/CREDITS.md`.

## 2. Poly Haven textures (CC0)

Import path: `ThirdParty/PolyHaven/Textures/<id>/` (1K diffuse / normal (GL) / roughness / ARM / AO). Used through the
material palette `Assets/MultiTravel/Art/materials.json`. **Modification:** for tinted palette entries the importer
writes a desaturated, brightness-normalised copy to `Assets/MultiTravel/Art/Textures/<id>_neutral.png` so the palette
colour (not the scan's own colour) shows; `white_plaster_02` is used as normal map only.

| Texture | Used for | Source URL | Author |
|---|---|---|---|
| Cotton Jersey | t-shirts, swimwear | https://polyhaven.com/a/cotton_jersey | colormass, Rico Cilliers |
| Waffle Pique Cotton | shirts | https://polyhaven.com/a/waffle_pique_cotton | colormass, Rico Cilliers |
| Rough Linen | blouse, trousers, bedding, curtains, lamp shades | https://polyhaven.com/a/rough_linen | colormass, Rico Cilliers |
| Denim Fabric | toiletry bag, duffel bag | https://polyhaven.com/a/denim_fabric | Rob Tuytel |
| Poly Wool Herringbone | suit jacket/trousers, bed throw | https://polyhaven.com/a/poly_wool_herringbone | colormass, Rico Cilliers |
| Brown Leather | men's shoes | https://polyhaven.com/a/brown_leather | Rob Tuytel |
| Leather White | women's shoes, laptop bag | https://polyhaven.com/a/leather_white | Rob Tuytel |
| Fabric Leather 02 | notebook, passport, headphone cushions, trims | https://polyhaven.com/a/fabric_leather_02 | Rob Tuytel |
| Terry Cloth | beach towel | https://polyhaven.com/a/terry_cloth | colormass, Rico Cilliers |
| Knitted Fleece | socks | https://polyhaven.com/a/knitted_fleece | colormass, Rico Cilliers |
| Velour Velvet | dress, neck pillow, upholstery | https://polyhaven.com/a/velour_velvet | colormass, Rico Cilliers |
| Quatrefoil Jacquard Fabric | tie, suitcase lining | https://polyhaven.com/a/quatrefoil_jacquard_fabric | colormass, Rico Cilliers |
| Wool Boucle | blazer, bench, rug | https://polyhaven.com/a/wool_boucle | colormass, Rico Cilliers |
| Herringbone Parquet | floor | https://polyhaven.com/a/herringbone_parquet | Jenelle van Heerden, Sergej Majboroda |
| White Plaster 02 | walls (normal map) | https://polyhaven.com/a/white_plaster_02 | Rob Tuytel |
| White Oak Veneer | wardrobe, furniture, hangers | https://polyhaven.com/a/white_oak_veneer | Jenelle van Heerden |

## 3. Original project models (no third-party licence)

Source: `tools/blender/mt_products.py`, `mt_garments.py`, `mt_jewellery.py`, `mt_stage.py`, `mt_wardrobe.py`, `mt_layout.py`, `mt_environment.py`, `mtlib.py` (run with
`blender -b --factory-startup --python tools/blender/mt_build_assets.py`). Output:
`Assets/MultiTravel/Art/Models/{Products,Environment}/<name>.fbx`, triangle counts in `Art/Models/models.json`.
Author: created for this project. Products are turned into prefabs (`Generated/Prefabs/<id>.prefab`: model + box collider
+ Rigidbody + XRGrabInteractable + ProductItem) by `ArtImporter` (menu *MultiTravel/Generate/Import Production Art*).

| Model | Description |
|---|---|
| shirt, blouse, men-tshirt, women-tshirt, jacket, blazer | Retail-folded garments: voxel-remeshed soft body, collar / lapels / placket / pockets, cloth wrinkles, buttons. |
| men-trousers, women-trousers | Folded trousers with waistband, belt loops, fly, crease, button. |
| dress, swimsuit, bikini, swim-shorts, socks, tie | Flat-laid soft garments from garment outlines (remeshed, wrinkled), belts/cords/cuffs. |
| men-shoes, women-shoes, flip-flops | Lofted shoe lasts (oxford with laces; pump with stiletto heel), soles, insoles; foam flip-flops. Pairs. |
| laptop, phone, laptop-charger (EU plug), phone-cable (USB-C/USB-A), headphones | Hard-surface electronics, unbranded. |
| notebook, pen, id-card, passport, kids-book | Stationery / documents. ID card and passport use generic text only ("KİMLİK KARTI", "PASAPORT") — no official emblem, layout or security features. |
| toiletry-bag, laptop-bag, travel-bag, beach-towel, snorkel-mask, neck-pillow | Bags and leisure items. |
| suitcase-open | 70 × 46 cm hard-shell spinner, open (lid ~100°), jacquard lining, straps, zipper rim, wheels. Unbranded. |
| wardrobe-bay | 45 cm dressing-room wardrobe module: 3 shelves (0.68 / 1.08 / 1.48 m), drawers, LED strips. 12 used. |
| stage-floor, stage-backdrop, floor-mat | v2 stage: round parquet floor, curved navy-to-teal backdrop with LED bands, "Buraya bas" floor mat. |
| wardrobe-carcass, wardrobe-hanging-module, wardrobe-folded-module, wardrobe-door-left, wardrobe-door-right, hanger | v2 dressing-room wardrobe: carcass with LED strips, hanging rail module, folded shelf module, shoe-rack door, drawer / hat-peg / valet-tray door, reusable hanger. |
| console-table-business, console-table-leisure, luggage-rack | Accessory / jewellery consoles for the two scenario sets; luggage rack carries the suitcase at 0.52 m. |
| stopwatch, scoreboard | Diegetic displays (needle pivot, UI anchors for TextMeshPro). |
| shirt-hanging, blouse-hanging, jacket-hanging, blazer-hanging, dress-hanging, swimsuit-hanging, bikini-hanging | Hanger variants of the garments (shown on the rail, ≤ 8 k tris each). |
| wristwatch, cufflinks, pearl-earrings, minimal-necklace, shell-necklace, party-tiara, practice-tag | Jewellery set and the tutorial practice tag (never scored). |
| room-shell, bed, nightstand, table-lamp, floor-mirror, curtains, rug, luggage-bench, hanger-rail-decor | Hotel room: walls with window and door, queen bed, furniture, decorative clothes rack. |

## 4. Unity template / packages

XR rig, hand visuals and input actions come from the Unity VR template (`com.unity.template.vr`) and the XR Interaction
Toolkit / XR Hands package samples (`Assets/VRTemplateAssets`, `Assets/Samples`), used under the Unity Companion /
package licences shipped in the package cache. UI font: Inter (`Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf`,
SIL Open Font License, shipped with the template) baked into the dynamic TextMeshPro asset `Resources/MultiTravelFont`
with Turkish glyphs.

## 5. Superseded content

The earlier procedural "flat silhouette" product meshes (`Generated/Meshes/<product>_*.asset`) and the procedural
wardrobe ring were replaced on 2026-10-02 by the models above and removed from the project. Retired products:
`straw-hat`, `crayons`, `luggage-tag` (weak visuals), `gamepad` (Poly Haven model includes a 1 m cable).

## Remaining art review (hardware)

Item readability at arm's length, shelf heights for short/tall participants, hand occlusion and overall look must be
checked in a Meta Quest headset before the event; event-owner art acceptance is still pending.
