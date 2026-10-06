# MultiTravel: Packing Challenge — Experience Overhaul (v2) Contract

Goal: turn the playable build into an event-grade VR experience. The participant stands on a floor mark inside a
branded dressing-room stage, in front of an open wardrobe whose zones match the item types, and packs a real-feeling
suitcase. Core rules, scoring, backend and session flow do NOT change (ARCHITECTURE.md stays binding for them).

Everything visual is produced by generators (Blender scripts → FBX, `ArtImporter` → prefabs/materials,
`MainSceneBuilder` → scenes). Nobody hand-edits `Main.unity`.

## 1. Ergonomics (hard constraints, enforced by an automated audit)

- Tracking origin Floor. Player origin (0,0,0) facing +Z on a floor mat ("Buraya bas" mark). No locomotion.
- Reference shoulders at (±0.19, 1.40, 0.0) for a 1.70 m adult; audit also at 1.50 m and 1.90 m eye heights
  (`ReachRoot` shifts the whole interactive set ±0.12 m with the measured head height at `Loading`).
- Every spawn slot: distance to the nearer shoulder ≤ 0.65 m (≥ 90 % of slots) and ≤ 0.72 m (all);
  slot height 0.80–1.55 m (best band 0.95–1.35); azimuth within ±115°; nothing behind the player.
- Suitcase rim at 0.78 m (luggage rack top ≈ 0.52 m), interior centre at z ≈ 0.40.
- Interactive items have a grab collider ≥ 5 cm in every axis (invisible enlarged collider for small items).

## 2. Layout (top view, metres; azimuth θ: 0 = +Z, + to the right)

```
                 ~~~ curved stage backdrop, r ≈ 1.9, 150° arc, deep blue → teal ~~~
                 [ STOPWATCH Ø0.42 centre y 1.70 ]  [ SCOREBOARD 1.0×0.45 right of it, y 1.70 ]
    [HANGING module θ−65°]                                   [FOLDED module θ+65°]
     rail y 1.55, garments 1.10–1.45                          shelves 0.95 / 1.18 / 1.40
          [BUSINESS table θ−40°]   ┌───────────┐   [LEISURE table θ+40°]
           top 0.92                 │ SUITCASE  │    top 0.92 + wicker basket
                                    │ lid opens │
                                    │ toward +Z │
  [LEFT DOOR θ−105°: SHOE rack       └──rack─────┘     [RIGHT DOOR θ+105°: ACCESSORY drawer 0.95,
   tiers 0.85/1.05/1.25, 12° tilt]       ▲ P(0,0)        hat peg 1.45, JEWELLERY valet tray 1.25 tilted 15°]
```

The wardrobe is one white-lacquer/oak carcass in a shallow U around the player; the two outer doors are swung open
(hinges and handles visible) and carry the shoe rack (left) and accessory drawer + jewellery tray (right).
Exact geometry is decided by the art + scene builder within the constraints of §1; a reach/overlap audit test is the
acceptance gate.

## 3. Zones and item mapping (data on `ProductDefinition.Presentation`)

| Zone | Items (ids) |
|---|---|
| Hanging | shirt, blouse, jacket, blazer, dress (+ decoys on clip hangers: swimsuit, bikini) |
| Folded | men-trousers, women-trousers, men-tshirt, women-tshirt, socks, swim-shorts, beach-towel |
| Shoes | men-shoes, women-shoes, flip-flops |
| Accessories | tie, glasses, sunglasses, beach-hat (on peg), toiletry-bag |
| Jewellery | wristwatch, cufflinks, pearl-earrings, minimal-necklace, shell-necklace, party-tiara |
| Business | laptop, laptop-bag, laptop-charger, phone-cable, phone, notebook, pen, id-card, passport, headphones |
| Leisure | snorkel-mask, rubber-duck, football, ukulele, garden-gnome, binoculars, kids-book, neck-pillow, travel-bag |

Items shuffle only within their own zone (seeded per session). Each zone has ≥ 20 % spare slots.

New jewellery products (category `Jewellery`, all `IsRequiredForCompletion = false` by default so the required count
does not change — the customer can flip it in data):

| id | DisplayName | Availability | Correct |
|---|---|---|---|
| wristwatch | Kol Saati | Both | yes |
| cufflinks | Kol Düğmesi | Male | yes |
| pearl-earrings | İnci Küpe | Female | yes |
| minimal-necklace | İnce Kolye | Female | yes |
| shell-necklace | Deniz Kabuğu Kolye | Both | no |
| party-tiara | Parti Tacı | Female | no |

Presentation per item (`Packed` / `Grip` / `HasHangingVariant`):
- Hanging garments: `Flat` / `Hanger` / true. Folded garments, towel: `Flat` / `FoldedGarment`.
- Shoes, flip-flops: `ShoeCorner` / `Shoe`.
- toiletry-bag, laptop-bag, travel-bag: `Upright` / `Handle`.
- laptop, notebook, kids-book: `Flat` / `FlatEdge`. passport, id-card, phone: `LidPocket` / `FlatEdge`.
- pen, phone-cable, laptop-charger, glasses, sunglasses, tie, all jewellery: `Organiser` / `Dynamic`.
- Everything odd (gnome, duck, football, ukulele, binoculars, snorkel, neck-pillow, headphones, beach-hat): `Top` / `Dynamic`.

## 4. Art contract (Blender → `Assets/MultiTravel/Art/Models/<folder>/<name>.fbx`, listed in `models.json`)

Node-name conventions inside FBX (empties import as Transforms):
- `SLOT.<zone>.<nn>` — spawn slot marker; +Z of the empty = item forward, +Y = up. `<zone>` is the lower-case
  `DisplayZone` name (`hanging`, `folded`, `shoes`, `accessories`, `jewellery`, `business`, `leisure`).
- `HOOK.<nn>` — hanger hook position on the rail (hanging slots coincide with hooks).
- `PACK.<kind>.<nn>` — suitcase packing anchors (`flat`, `shoecorner`, `upright`, `lidpocket`, `organiser`, `top`).
- `PIVOT.<name>` — animation pivots (`PIVOT.lid` on the suitcase hinge, `PIVOT.needle` on the stopwatch).
- `UI.<name>` — flat quads/empties where text is rendered (`UI.stopwatch_face`, `UI.scoreboard`, `UI.result`).
- `VOL.<name>` — box empties sized by scale for trigger volumes (`VOL.placement` inside the suitcase).
- `LIGHT.<name>` — light placement hints (spot/area positions); `EMISSIVE.<name>` meshes for LED strips.

Models (new or rebuilt):
`stage-backdrop`, `stage-floor`, `floor-mat`, `wardrobe-carcass`, `wardrobe-hanging-module`,
`wardrobe-folded-module`, `wardrobe-door-left` (shoe rack), `wardrobe-door-right` (drawer open + hat peg + valet tray),
`hanger` (reusable), `console-table-business`, `console-table-leisure`, `luggage-rack`, `suitcase-open` v2
(nodes `Base`, `Lid` under `PIVOT.lid`, `Straps`, `LidPocket`, `Organiser`, `VOL.placement`, `PACK.*`),
`stopwatch` (nodes `Shell`, `Face`, `Needle` under `PIVOT.needle`, `UI.stopwatch_face`), `scoreboard`,
`practice-tag` (MultiTravel-branded luggage tag, practice item), jewellery products (6 ids above),
hanging variants `<id>-hanging` for shirt, blouse, jacket, blazer, dress, swimsuit, bikini.
Budgets: environment total ≤ 150 k tris; each product ≤ 8 k tris; hanging variant ≤ 8 k.

## 5. Gameplay contract (component names)

Mechanics (Items / Suitcase / Director / data):
- `SpawnSlot.Zone` (`DisplayZone`); `SpawnSlotLayout.Assign` is zone-strict (pass 1 = own zone; falls back to `Any`
  slots only with a warning).
- `SuitcaseSlot.Kind` (`PackedKind`); `SuitcaseController` picks a free slot of the item's kind, then `Flat`, then `Top`.
  Settle tween 0.30 s ease-out-back + soft-goods squash; `StrapLift` raises the straps with the stack height.
- `SuitcaseLid` — closes on `Completed` (100° → 0° in 1.1 s, latch clicks), reopens on reset.
- `ItemVisualVariant` on garment prefabs (`Hanging` ↔ `Folded` child visuals, swap at 50 % of the settle tween).
- `ProductItem`: grip presets → attach transforms; release-velocity clamp 1.5 m/s; home-snap within 0.22 m of its own
  slot; recovery after 1.2 s resting anywhere that is not its slot or the suitcase.
- Hover affordance: inverted-hull outline child (`MT_Outline` material, teal) + 15 ms haptic tick.
- `InteractionLock.AllowPractice(ProductItem)` — whitelists the practice item during `Instructions`.
- `SuitcaseController.TryPlace` ignores `IsPractice` items for score and completion (still settles visually).

Presentation (`Gameplay/Presentation`, `Gameplay/Audio`, `Gameplay/Tutorial`):
- `StopwatchDisplay` (needle + digits; countdown 3-2-1 + "BAŞLA!"; mm:ss.f during play),
  `ScoreboardDisplay` (score roll-up, required pips n/N, first name), `ResultCard`, `ScreenFade`,
  `AttractMode` (Welcome: dimmed moods, top-5 leaderboard cycle on the scoreboard), `MoodLighting`
  (Idle / Tutorial / Countdown / Playing / Completed light groups).
- `TutorialController` (Instructions: practice tag glows on the suitcase handle → grab → place → "Hazırsın!").
- `AudioDirector` + AudioMixer (Master/Ambient/SFX/UI); 3D foley per `SoundKind` (Cloth, Leather, Hard, Metal, Paper);
  procedural fallback clips; ambient room tone.
- `TrackingLossGuard` (hand tracking lost while holding: freeze 0.6 s, return home after 1.5 s).
- `XrRigController` implements `IXrRigControl` (Recenter, floor offset ±0.12 m in 0.02 steps, persisted).
- The old flat `VrPanelUI` is retired from the scene (component kept for tests until replaced).

Operator (`Scripts/Operator`): dark premium theme, stepper, icons, spectator view (RenderTexture from a smoothed
third-person camera), registration UX, result + leaderboard, Recenter / Zemin ± buttons via `IXrRigControl`.

## 6. Lighting / rendering

OpenXR Standalone render mode SinglePassInstanced. Baked GI (Mixed, Shadowmask) with Adaptive Probe Volume or a Light
Probe Group over the play volume, baked box-projected reflection probe, 1 realtime shadowed key light + ≤ 2 shadowed
spots, LED strips as emissive meshes, Volume: Neutral tonemapping + subtle bloom, no vignette. MSAA 4×, HDR on.
Budget at 90 Hz on RTX 2060-class: ≤ 9 ms GPU, ≤ 300 draw calls, ≤ 800 k visible tris.

## 7. Acceptance

Existing EditMode/PlayMode suites stay green; new tests: zone mapping + capacity, reach audit, typed packing,
lid close/reopen, practice item never scores, home-snap, recovery, reset leaves zero state. QA screenshots from fixed
cameras under `docs/qa/<date>/`. Headset-only checks are listed in `docs/EVENT_OPERATIONS.md`.
