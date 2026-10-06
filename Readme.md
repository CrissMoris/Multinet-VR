# MultiTravel: Packing Challenge

## Marka Hakkında

**MultiTravel**, Multinet Up tarafından sunulan; şirketlerin iş seyahati ve konaklama süreçlerini tek bir dijital merkez üzerinden yönetmesini sağlayan **ücretsiz bir kurumsal seyahat platformudur**.

## Challenge Hakkında

**MultiTravel: Packing Challenge**, katılımcıların arabayla gerçekleştirecekleri **1 gece konaklamalı bir toplantı seyahati** için ihtiyaç duyacakları ürünleri seçerek valize yerleştirdikleri interaktif bir oyundur.

### Oyun Akışı

Katılımcılardan:

- Seyahat için gerekli ürünleri seçmeleri,
- Seçtikleri ürünleri valize yerleştirmeleri,
- Valizi mümkün olan en kısa sürede tamamlamaları beklenir.

### Puanlama

- Seyahat için **doğru ürünlerin** seçilmesi puan kazandırır.
- Toplantı seyahatiyle ilgisi olmayan **yanlış ürünlerin** seçilmesi eksi puan olarak değerlendirilir.
- Katılımcının valizi tamamlama süresi kayıt altına alınır.

### Kazananın Belirlenmesi

Her gün;

1. En yüksek puana ulaşan,
2. Eşit puan durumunda valizi en kısa sürede tamamlayan

katılımcı **valiz ödülünün sahibi olur**.
---

## Teknik Bilgiler


Corporate-event VR game for Meta Quest (via Quest Link on a Windows PC). The participant packs a suitcase
for "Arabayla, 1 gece konaklamalı toplantı seyahati" using hand interaction; correct items score positive,
irrelevant items score negative; completion time is measured; results go to a central Supabase backend and
a public web leaderboard.

| Part | Path | Tech |
|---|---|---|
| VR application | `MultiTravelValizChallenge/` | Unity 6000.4.10f1, URP, OpenXR, XR Interaction Toolkit 3.4.1, XR Hands 1.7.3 |
| Backend | `backend/supabase/` | Supabase (Postgres + PostgREST RPC), SQL migrations, Node tests |
| Public leaderboard | `leaderboard-web/` | Static HTML/JS, no build step |
| Docs | `docs/` | Architecture contract, stack notes, event-day runbook, example config |

### Quick start (developer)

```bash
# Unity project (requires Unity 6000.4.10f1 with Windows Build Support)
unity open MultiTravelValizChallenge

# Backend tests (Postgres-in-WASM, no Docker needed)
cd backend/supabase/tests && npm install && npm test

# Deploy backend to a Supabase project (one time)
cd backend/supabase && supabase link --project-ref <ref> && supabase db push
```

Production art (only needed after changing a model or the palette):

```bash
# 1. CC0 textures/models from Poly Haven (already in the repo; re-run only to restore them)
node tools/assets/fetch-polyhaven.mjs
# 2. Original models (products, suitcase, wardrobe, room) -> Assets/MultiTravel/Art/Models
blender -b --factory-startup --python tools/blender/mt_build_assets.py
# 3. In the open editor: materials, prefabs, Main scene, validation
unity command mt_generate_all --rebuild_main true
```

Windows build (from the repo root):

```bash
unity build MultiTravelValizChallenge --target StandaloneWindows64 --execute-method MultiTravel.EditorTools.BuildScript.BuildWindows --output-path Build/Windows/MultiTravelValizChallenge.exe
# or, with the editor open:
unity command mt_build_windows --timeout 3600
```

The build copies `Deployment/multitravel.config.json` (git-ignored: event URL, anon key, access code) into the player's
StreamingAssets; the developer's localhost config never ships. Per-station `stationId`: see `docs/EVENT_OPERATIONS.md`.

### Configuration

Event-specific values are never in code. Defaults live in `Assets/MultiTravel/Resources/AppConfig.asset`;
per-station overrides go in `StreamingAssets/multitravel.config.json` or the persistent-data folder
(see `docs/config.example.json` and `docs/EVENT_OPERATIONS.md`). Product list, scores, gender availability
and required items are ScriptableObjects under `Assets/MultiTravel/Data/`.

### Documents

- `docs/ARCHITECTURE.md` — binding architecture contract (modules, data model, backend RPC contract, reset guarantees)
- `docs/STACK_NOTES.md` — verified API/stack reference for XRI 3.4.1, OpenXR, XR Hands, TMP, Pipeline CLI
- `docs/EVENT_OPERATIONS.md` — event-day runbook (Turkish), incl. the mandatory headset test checklist
- `ASSET_MANIFEST.md` — every third-party asset with source, licence and author; branding dependency
- `docs/qa/` — QA screenshots
- `backend/supabase/README.md` — backend deployment and security model
- `leaderboard-web/README.md` — leaderboard hosting
