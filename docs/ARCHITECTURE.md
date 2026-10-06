# MultiTravel: Packing Challenge — Architecture Contract

This document is the binding contract for every module in the repository. All contributors
must follow the names, folders, data shapes and behaviours below. If something here is
impossible, change the document in the same change set and say why.

Product language: **Turkish** for every user-facing string. Code, identifiers, comments, docs: English.

## 1. Repository layout

```
vr-oyun/                                  (git root)
  docs/                                   architecture, event-ops runbook, test reports
  MultiTravelValizChallenge/              Unity 6000.4.10f1 project (created from com.unity.template.vr 9.2.3, URP)
    Assets/
      MultiTravel/                        ALL project-owned content lives here
        Scripts/
          Core/                           asmdef MultiTravel.Core  (no XR / no UI dependencies)
          Gameplay/                       asmdef MultiTravel.Gameplay (XRI, XR Hands, TMP, InputSystem, Core)
          Operator/                       asmdef MultiTravel.Operator (uGUI operator screen; Core, Gameplay)
          Editor/                         asmdef MultiTravel.Editor (content + scene generators, build script, CLI commands)
          Tests/EditMode/                 asmdef MultiTravel.Tests.EditMode
          Tests/PlayMode/                 asmdef MultiTravel.Tests.PlayMode
        Data/                             ScriptableObject assets (AppConfig, ProductCatalog, ProductDefinitions, Branding)
        Art/                              production art: materials.json (shared palette), Models/{Products,Environment}/*.fbx
                                          (built by tools/blender), Materials/ + Textures/ (built by ArtImporter)
        ThirdParty/PolyHaven/             CC0 models + textures (tools/assets/fetch-polyhaven.mjs), see ASSET_MANIFEST.md
        Generated/                        palette materials, base meshes and the product prefabs (Generated/Prefabs/<id>.prefab)
        Prefabs/                          hand-maintained prefabs (if any)
        Scenes/                           Bootstrap.unity, Main.unity
        Fonts/                            TMP font asset with Turkish glyph coverage (dynamic atlas)
        Resources/                        AppConfig.asset lives here (loaded by name "AppConfig")
    Assets/Samples/, Assets/XR/, Assets/Settings/   template-owned (XR rig prefabs, URP settings, OpenXR settings) — keep
    Packages/manifest.json
    ProjectSettings/
  backend/
    supabase/
      migrations/                         SQL migrations (schema, RPCs, grants) — deployable with `supabase db push`
      seed.sql                            creates the default event row for local/dev
      tests/                              Node integration tests that call the RPCs over REST
      README.md                           deploy + configure instructions
  tools/
    blender/                              mt_build_assets.py (+ mtlib, mt_products, mt_environment): original 3D models, headless Blender 5.x
    assets/                               fetch-polyhaven.mjs + polyhaven-assets.json (CC0 downloads)
    Configure-Local.ps1                   writes git-ignored local-dev configs from `supabase status`
  Deployment/                             git-ignored event configs: multitravel.config.json (copied into the player by the build),
                                          leaderboard-config.local.js (production leaderboard)
  leaderboard-web/
    index.html                            static branded leaderboard, auto-refresh, reads RPC get_leaderboard
    config.js                             SUPABASE_URL / SUPABASE_ANON_KEY / EVENT_SLUG (client-safe only)
    serve.ps1 / serve.sh                  one-liner LAN hosting helper
  README.md
```

Rules:
- Never put the Supabase `service_role` key or DB password in any file under this repo. Only the anon/publishable key.
- Never hard-code event values (slug, access code, station id, URLs) in C#. They live in `AppConfig` + runtime JSON override.
- Template content (Samples, XR, Settings) is preserved; the template `SampleScene` is **not** shipped (removed from build list, may be deleted).

## 2. Unity runtime architecture

### 2.1 Assemblies and namespaces

| asmdef | Namespace root | May reference |
|---|---|---|
| MultiTravel.Core | `MultiTravel.Core.*` | UnityEngine, Newtonsoft.Json (`com.unity.nuget.newtonsoft-json`) |
| MultiTravel.Gameplay | `MultiTravel.Gameplay.*` | Core, Unity.XR.Interaction.Toolkit, Unity.XR.CoreUtils, Unity.XR.Hands, Unity.InputSystem, Unity.TextMeshPro, Unity.XR.Management |
| MultiTravel.Operator | `MultiTravel.Operator.*` | Core, Gameplay, Unity.TextMeshPro, Unity.InputSystem, UnityEngine.UI |
| MultiTravel.Editor | `MultiTravel.EditorTools.*` | everything (Editor only) |
| MultiTravel.Tests.EditMode | `MultiTravel.Tests.EditMode` | Core, Gameplay, Operator, NUnit, UnityEditor |
| MultiTravel.Tests.PlayMode | `MultiTravel.Tests.PlayMode` | Core, Gameplay, Operator, NUnit |

### 2.2 Service composition

`AppServices` (Core, static) is a tiny typed registry: `Register<T>(T instance)`, `Get<T>()`, `TryGet<T>(out T)`, `Clear()`.
`AppBootstrap` (MonoBehaviour, Bootstrap scene, `DontDestroyOnLoad`) builds and registers in this order:

1. `IClock` → `StopwatchClock` (System.Diagnostics.Stopwatch based; never frame counting)
2. `AppConfig` effective config → `RuntimeConfig` (see §6)
3. `ILocalStore` → `FileLocalStore` (persistentDataPath JSON files)
4. `IBackendClient` → `SupabaseBackendClient` (UnityWebRequest + Awaitable; never blocks main thread)
5. `SubmissionOutbox` (idempotent retry queue persisted via ILocalStore)
6. `ScoreService`, `GameTimer`, `CompletionEvaluator`, `ProductSetResolver`
7. `SessionController` (state machine, owns `ParticipantSession`)
8. `XrStatusService`
9. then loads `Main` scene (`SceneManager.LoadSceneAsync`, Single). Failure → `SessionController.ReportFatal(SceneLoadFailed)`; operator UI shows retry.

Gameplay MonoBehaviours in `Main` resolve services with `AppServices.Get<T>()` in `Start()` (never in `Awake`, bootstrap order).

### 2.3 Session state machine (Core/Session)

```
enum SessionState { Welcome, Registration, GenderSelection, Instructions, Loading, Countdown, Playing,
                    Completed, Submitting, SubmissionFailed, Finished, Fatal }
```
`SessionController`:
- `State`, `event Action<SessionState, SessionState> StateChanged`, `ParticipantSession Current`.
- Transitions (public methods, each validates the source state and throws `InvalidOperationException` otherwise):
  - `BeginRegistration()` Welcome→Registration
  - `SubmitRegistration(ParticipantInput input)` Registration→GenderSelection (validates with `ParticipantValidator`; returns `ValidationResult`; on failure stays)
  - `SelectGender(Gender g)` GenderSelection→Instructions
  - `StartGame()` Instructions→Loading (gameplay director prepares the item set) → `Countdown` (3 s, VR countdown) → `Playing` (timer starts on the exact frame Playing is entered)
  - `CompleteGame(CompletionReason reason)` Playing→Completed (timer stops first, result snapshot built) → auto `Submitting`
  - `OnSubmissionSucceeded(SubmissionReceipt)` Submitting→Finished
  - `OnSubmissionFailed(BackendError)` Submitting→SubmissionFailed; `RetrySubmission()` SubmissionFailed→Submitting
  - `AbandonSession()` any non-Welcome → Finished with `SessionOutcome.Abandoned` (no leaderboard submission; a local log line is written)
  - `ResetForNextParticipant()` Finished/SubmissionFailed/Fatal→Welcome. Must clear `Current`, reset score, timer, items, suitcase (through `SessionResetRequested` event that gameplay subscribes to) and leave zero per-participant state behind.
- `CompletionReason { RequiredItemsPlaced, ManualConfirm, TimeLimit, OperatorForced }`.

`ParticipantSession` (plain class, Core): `ClientSessionId` (Guid, created at Registration), `Input` (ParticipantInput), `Gender`, `ParticipantId` (server uuid, nullable until registered), `StartedAtUtc`, `Result` (`GameResult`, null until completed), `SubmissionId` (Guid, created when the result is finalised), `SubmissionAttempts`, `Outcome`.

`ParticipantInput`: FirstName, LastName, Phone, Email, ConsentAccepted (bool?), ConsentVersion (string, null if consent not collected).
`ParticipantValidator` (Core, pure): trims; first/last 1..60 chars; phone normalised to digits (optional leading +), 10..15 digits; email RFC-lite regex; returns Turkish error messages keyed by field.

`GameResult`: Score, CompletionMs, CorrectCount, IncorrectCount, RequiredTotal, PlacedProductIds (string[]), CompletedAtUtc, Reason.

### 2.4 Product data (Core/Products)

- `enum Gender { Female, Male }`; `[Flags] enum GenderAvailability { None=0, Female=1, Male=2, Both=3 }`
- `enum ProductCategory { Clothing, Shoes, Accessory, Business, Electronics, Document, Toiletry, Leisure, Other }`
- `ProductDefinition : ScriptableObject` (asset per product under `Assets/MultiTravel/Data/Products/`):
  `Id` (string, unique, kebab-case), `DisplayName` (TR), `Category`, `Availability`, `IsCorrect`, `ScoreOverride` (int, 0 = use catalog default), `IsRequiredForCompletion` (default = IsCorrect; only meaningful when IsCorrect), `VisualPrefab` (GameObject, must contain `ProductItem`), `BrandSprite` (optional), `Interaction` (`ProductInteractionSettings`: Mass, GrabScale, TwoHanded(bool), HoldOffset).
- `ProductCatalog : ScriptableObject` (`Assets/MultiTravel/Data/ProductCatalog.asset`): `ScenarioTitle` ("Arabayla, 1 gece konaklamalı toplantı seyahati"), `Products` (List<ProductDefinition>), `DefaultPositiveScore` (+10), `DefaultNegativeScore` (-5), `ShuffleSpawnPositions` (true), `ShuffleSeedMode` (PerSession).
  Methods: `int ScoreFor(ProductDefinition p)` → override or default by IsCorrect; `IReadOnlyList<ProductDefinition> ForGender(Gender g)`; `Validate(List<string> errors)` (duplicate ids, missing prefabs, no required items for a gender).
- `ProductSetResolver` (Core): `ProductSet Resolve(ProductCatalog, Gender)` → `ProductSet { Gender, Items (all available), Required (IsCorrect && IsRequiredForCompletion), RequiredIds }`.

### 2.5 Scoring (Core/Scoring)

`ScoreService`:
- `int Score`, `event Action<ScoreChange> Changed`, `Reset()`.
- `ScoreChange { ProductId, Delta, NewTotal, IsPositive, Reason (Placed|Removed|Reset) }`.
- `bool TryApplyPlacement(string productId, int delta)` — applies once per product id; returns false (no change) if already counted. 
- `bool TryRevertPlacement(string productId)` — only if `RevertScoreOnRemoval` policy is on and the product is counted; subtracts the same delta.
- Internally tracks `Dictionary<string,int> countedDeltas`. Duplicate protection lives HERE, in addition to the item state machine, so physics jitter can never double-count.
- `IReadOnlyCollection<string> CountedProductIds`.

### 2.6 Timer (Core/Timing)

`IClock { double NowSeconds { get; } }`, `StopwatchClock`, `ManualClock` (tests).
`GameTimer(IClock)`: `Start()`, `Stop()`, `Reset()`, `bool IsRunning`, `long ElapsedMs` (exact: stopped value is frozen), `event Action Started/Stopped`. Not a MonoBehaviour; `GameplayHud` polls `ElapsedMs` for display.

### 2.7 Completion (Core/Completion)

`enum CompletionMode { RequiredItemsPlaced, ManualConfirm, RequiredItemsOrManual }`
`CompletionEvaluator(ProductSet set, CompletionMode mode, int timeLimitSeconds)`:
- `void NotifyPlaced(productId)`, `NotifyRemoved(productId)`, `NotifyManualConfirm()`, `NotifyElapsed(ms)`
- `bool TryGetCompletion(out CompletionReason reason)`; deterministic: RequiredItemsPlaced only when **every** `RequiredIds` is currently placed. Time limit: `timeLimitSeconds > 0 && elapsed >= limit*1000`.
- `int RequiredPlacedCount`, `int RequiredTotal`.

### 2.8 Gameplay (Gameplay assembly)

- `GameplayDirector` (Main scene): subscribes to `SessionController`. On `Loading`: resolves `ProductSet` for gender, activates item instances (`ItemPool`), assigns spawn slots (`SpawnSlotLayout` with optional seeded shuffle), resets suitcase; signals `SessionController.LoadingFinished()`. On `Playing`: enables interaction. On `Completed`: locks interaction (`InteractionLock`). On `SessionResetRequested`: full reset (items back to pool, suitcase cleared, HUD reset).
- `ItemPool`: one instance per ProductDefinition prefab is created **once** at scene start (no per-session instantiation); instances are enabled/disabled and re-posed. No runtime `Instantiate` during a session.
- `ProductItem` (on each item prefab root): `Definition`, `State { Pooled, Free, Held, Placed }`, `AnchorPoint` (Transform used for inside-volume test), events `Grabbed`, `Released`, `StateChanged`. Wraps `XRGrabInteractable` (movementType Kinematic, throwOnDetach false, smoothing on, useDynamicAttach true, selectMode Single, retainTransformParent false). Collider(s) are convex, Rigidbody non-kinematic while Free, kinematic while Placed.
- `SuitcaseController` (Main scene): `PlacementVolume` (BoxCollider trigger, covers interior + 15 cm above rim), `SlotGrid` (list of slot transforms inside the base), `TryPlace(ProductItem)`, `Remove(ProductItem)`, `Clear()`, events `ItemPlaced(ProductItem, ScoreChange)`, `ItemRemoved(ProductItem, ScoreChange)`. Placement = item released with anchor inside volume **or** an unheld Free item comes to rest inside the volume (checked by `SettleWatcher` every 0.2 s via Rigidbody.IsSleeping/velocity threshold) → tween to free slot (0.25 s, unscaled), kinematic, State=Placed, `ScoreService.TryApplyPlacement`, `CompletionEvaluator.NotifyPlaced`. Grabbing a Placed item → `Remove`: slot freed, State=Held, revert score (policy), `NotifyRemoved`. All transitions are guarded by state, so trigger spam can never double-fire.
- `ItemRecoveryService`: every 0.5 s checks all Free items: if below `floorY - 0.2`, outside `PlayBounds`, or resting on the floor layer for ≥ 3 s → returns to its spawn slot (fade-out/in 0.2 s). Nothing is ever destroyed or lost.
- `InteractionLock`: disables all `XRGrabInteractable`s (and releases held items back to spawn) outside `Playing`.
- `PlacementFeedback`: audio (procedural clips generated with `AudioClip.Create` at startup: positive chime, negative buzz, placement tick), haptics via XRI haptic impulse on the interactor that released, slot emissive pulse, pooled floating `+10 / -5` TMP label.
- `VrPanelUI` (world-space canvas on the backdrop): title, scenario, score, timer `mm:ss.f`, progress `n/N gerekli ürün`, state panels (welcome, instructions, countdown, completed/submitting/failed, locked). Readable at 2 m: min 36 pt equivalent.
- `XrRigController`: references the template XR Origin; disables teleport/snap-turn/move providers when `EnableLocomotion` is false; keeps hands + controllers + UI ray/poke.
- `XrStatusService` (Core-facing interface, Gameplay impl): `IsXrRunning`, `HmdPresent`, `Retry()`; polls `XRGeneralSettings.Instance.Manager` + `XRDisplaySubsystem`.
- `ManualConfirmButton` (poke button on the pedestal "Valizi Tamamla"): calls `CompletionEvaluator.NotifyManualConfirm()` only when mode allows.

Additions (2026-10-02):
- Suitcase packing columns: `SuitcaseSlot.SetBelow` links slots into columns (Main: 2 x 2 columns x 9 layers). A slot is
  available only when the slot below is occupied; an item rests on top of the item below (rise capped at 5 cm, soft goods
  compress). `SuitcaseController` picks the column nearest the drop point (horizontal distance + stack-height penalty) and,
  when an item is taken out, drops every item above it one slot (`CompactColumn`). Unlinked slots behave as before.
- `ProductNameTags` (Feedback): pooled world-space TMP labels showing `ProductDefinition.DisplayName` above any item a hand
  hovers or holds (XRI hover/select events), facing the HMD; own outlined material (never the shared font material).
- `XrRigController`: `requirePhysicalReach` (default on) sets `NearFarInteractor.enableFarCasting = false` on the rig, so
  items can only be grabbed by reaching (no ray grabbing); `hideTutorialCallouts` hides the template's "Grab / Move / UI
  Press" controller tooltips.
- `AppBootstrap.DataRootOverride` / `BackendFactoryOverride`: static test seams (never set by the app) so automated runs
  never write to the real outbox or the configured event backend.

### 2.9 Operator UI (Operator assembly)

Screen-space overlay canvas on the PC monitor (not rendered in the HMD). Mouse + keyboard. One `OperatorScreen` root with child panels switched by `SessionState`:
Welcome(Başla) → Registration form (Ad, Soyad, Telefon, E-posta, consent checkbox shown only when `ConsentText` non-empty, Devam, inline TR validation) → Gender (Kadın / Erkek) → Instructions (text from config + "Oyunu Başlat") → Playing (live score, timer, progress, "Zorla Bitir", "Oturumu İptal Et") → Completed/Submitting/Failed (score, time, rank if returned, status, "Tekrar Gönder", "Yeni Katılımcı") → Fatal (message + "Yeniden Dene").
Status bar always visible: VR durumu (●), Backend durumu (● + last check), İstasyon, Etkinlik, Bekleyen gönderim sayısı, "Liderlik Tablosu" toggle (top 10 fetched from backend, refresh button), app version.
The VR headset mirrors a read-only version of the current state (VrPanelUI).

## 3. Interaction stack

- OpenXR (com.unity.xr.openxr) with: Meta Quest Touch Pro/Plus/Touch controller profiles, Hand Interaction Profile, Hand Tracking Subsystem (XR Hands), Meta Hand Tracking Aim. Windows standalone target via Meta Quest Link / Air Link.
- XR Interaction Toolkit 3.x (template version) rig from the template (`XR Origin (XR Rig)` with hands + controllers, near-far interactors, poke, UI ray).
- No second VR SDK. No Oculus Integration. No gaze input.
- Desktop fallback for development only: XR Device Simulator enabled when `EnableDeviceSimulatorWhenNoHmd` is true **and** no HMD is present **and** running in Editor/Development build. Never in a release build.

## 4. Backend contract (Supabase, PostgREST RPC)

Base URL `{SupabaseUrl}/rest/v1/rpc/{fn}`, headers: `apikey: {AnonKey}`, `Authorization: Bearer {AnonKey}`, `Content-Type: application/json`. All functions are `SECURITY DEFINER`, owned by postgres, `GRANT EXECUTE ... TO anon`; **anon has no direct table privileges** (REVOKE ALL on all tables/views from anon). Every RPC validates `p_event_slug` + `p_access_code` against `events` (active) and raises `EVENT_ACCESS_DENIED` otherwise.

Tables (`public`):
- `events(id uuid pk, slug text unique, name text, access_code text, is_active bool, name_display_mode text check in ('full','first_last_initial') default 'full', created_at)`
- `participants(id uuid pk, event_id fk, client_session_id uuid unique, station_id text, first_name text, last_name text, phone text, email text, gender text check in ('female','male'), consent_accepted bool null, consent_version text null, created_at, updated_at)`
- `results(id uuid pk, event_id fk, participant_id fk, submission_id uuid unique, station_id, score int, completion_ms int check > 0, correct_count int, incorrect_count int, required_total int, placed_product_ids text[], gender text, status text check in ('completed','abandoned'), completion_reason text, completed_at timestamptz, client_version text, created_at)`
- Indexes: results(event_id, status, score desc, completion_ms asc, completed_at asc).

RPCs (all return `jsonb`):
1. `register_participant(p_event_slug, p_access_code, p_station_id, p_client_session_id uuid, p_first_name, p_last_name, p_phone, p_email, p_gender, p_consent_accepted bool, p_consent_version text)` → `{ "participant_id": uuid, "created": bool }`. Upsert on `client_session_id` (idempotent). Validates lengths/format/gender; raises `VALIDATION_FAILED:<field>`.
2. `submit_result(p_event_slug, p_access_code, p_station_id, p_submission_id uuid, p_client_session_id uuid, p_participant jsonb, p_score int, p_completion_ms int, p_correct_count int, p_incorrect_count int, p_required_total int, p_placed_product_ids text[], p_gender, p_status, p_completion_reason, p_completed_at timestamptz, p_client_version)` → `{ "result_id": uuid, "participant_id": uuid, "created": bool, "rank": int|null }`. Upserts the participant from `p_participant` (same fields as register) when missing, then inserts the result; if `submission_id` exists returns the existing row with `created=false` (duplicate protection). `rank` computed for `status='completed'` only.
3. `get_leaderboard(p_event_slug, p_limit int default 100)` → `[{ "rank", "display_name", "score", "completion_ms", "gender", "completed_at" }]` ordered by score desc, completion_ms asc, completed_at asc; only `completed`; **never** phone/email. `display_name` respects `events.name_display_mode`. This function requires **no access code** (public display) but only serves active events.
4. `ping_event(p_event_slug, p_access_code)` → `{ "ok": true, "event_name": text, "server_time": timestamptz }` for the operator status indicator.

Errors: raised with `RAISE EXCEPTION USING MESSAGE = '<CODE>', DETAIL = '<human detail>'`; PostgREST returns HTTP 400 with `{"message": "<CODE>", ...}`. Client maps codes to Turkish messages: `EVENT_ACCESS_DENIED`, `EVENT_INACTIVE`, `VALIDATION_FAILED:<field>`, `PARTICIPANT_NOT_FOUND`. Any other/transport error → "Sunucuya ulaşılamadı".

Client (`SupabaseBackendClient : IBackendClient`): `Task<BackendResult<RegisterReceipt>> RegisterAsync(...)`, `Task<BackendResult<SubmissionReceipt>> SubmitResultAsync(...)`, `Task<BackendResult<LeaderboardEntry[]>> GetLeaderboardAsync(limit)`, `Task<BackendResult<PingReceipt>> PingAsync()`. Timeout from config (default 10 s). All via `UnityWebRequest` + `Awaitable`/`Task` on the main thread (async, non-blocking). `BackendResult<T> { Ok, Value, Error: BackendError { Code, Message (TR), HttpStatus, IsTransient } }`.

`SubmissionOutbox`: on `Completed`, the session's full payload (participant + result) is written to `persistentDataPath/outbox/{submissionId}.json` **before** the first network attempt. Retries with backoff (2, 4, 8, 16, 30 s cap) while in `Submitting`; the operator can trigger a retry; pending files are retried on app start and every 60 s in the background; file deleted only after `created` or duplicate-ack. `PendingCount` exposed to the status bar. A PII-free line is appended to `persistentDataPath/results-log.csv` (session id, score, time, gender, status, submitted?) for the event team.

## 5. Ranking

Score DESC, completion_ms ASC, completed_at ASC. Implemented once in SQL (`get_leaderboard`, `rank`) and mirrored in `LeaderboardRanking.Compare` (Core) for local display/tests.

## 6. Configuration

`AppConfig : ScriptableObject` (Resources/AppConfig.asset) with sections:
- Backend: `SupabaseUrl`, `SupabaseAnonKey`, `EventSlug`, `EventAccessCode`, `StationId`, `RequestTimeoutSeconds=10`, `MaxAutoRetries=5`
- Gameplay: `CompletionMode=RequiredItemsPlaced`, `TimeLimitSeconds=0`, `RevertScoreOnRemoval=true`, `CountdownSeconds=3`, `EnableLocomotion=false`, `ShuffleSpawnPositions=true`
- Texts: `WelcomeTitle`, `WelcomeSubtitle`, `InstructionsText` (TR), `ScenarioText`
- Privacy: `ConsentText` (empty by default — the customer supplies KVKK wording), `ConsentVersion`
- Branding: `ProductTitle="MultiTravel: Packing Challenge"`, `LogoSprite` (optional; if null the UI shows the text wordmark and the generator logs an asset dependency), `PrimaryColor`, `AccentColor`
- Debug: `EnableDeviceSimulatorWhenNoHmd=true` (editor/dev builds only)

Runtime override (JSON, partial, same keys, camelCase): `StreamingAssets/multitravel.config.json` then `persistentDataPath/multitravel.config.json` (later wins). `RuntimeConfig` is the merged, immutable result registered in `AppServices`. `ConfigLoader` (Core, pure, testable) performs the merge. Missing/invalid JSON → logged warning, defaults used, app keeps running.

## 7. Reset guarantees

`ResetForNextParticipant()` must leave: Score=0, timer reset & stopped, every item in `Pooled` state and disabled, suitcase slots empty, HUD cleared, `SessionController.Current == null`, no coroutine/tween alive, no outbox file for the finished session unless unsent. A PlayMode test asserts all of these after a simulated full session.

## 8. Editor tooling (Editor assembly)

- `BaseAssetGenerator` / `MaterialLibrary` / `FontAssetGenerator` / `ProductDataGenerator`: palette materials and base
  meshes (used by small UI parts such as the confirm button), the dynamic Turkish TMP font (Inter), ProductDefinitions +
  catalog + AppConfig (never overwrites existing definitions unless `force`; deletes `RetiredProductIds`).
- `tools/blender/mt_build_assets.py` (outside Unity): builds every original model as FBX into `Art/Models`, with metre-based
  box UVs and material slots named by `Art/materials.json` keys; writes `Art/Models/models.json`.
- `ArtImporter` (`MultiTravel/Generate/Import Production Art`, CLI `mt_import_art`): texture import settings, URP/Lit
  palette materials from `materials.json` (tinted entries use a desaturated copy of the Poly Haven scan), material remaps on
  every model, URP materials for Poly Haven models, and the product prefabs (model + box collider + anchor + Rigidbody +
  XRGrabInteractable + ProductItem) saved in place at `Generated/Prefabs/<id>.prefab` (stable GUIDs).
- `MainSceneBuilder` (`MultiTravel/Generate/Rebuild Main Scene`, CLI `mt_build_main_scene`): hotel room + 12-bay U-shaped
  dressing-room wardrobe (36 spawn slots on shelves at 0.68 / 1.08 / 1.48 m, radius 1.05 m, front gap for the suitcase),
  luggage bench + open suitcase (colliders, placement volume, packing columns), furniture, lighting, reflection probe, VR
  panel on the front wall, operator screen, XR rig; saved over Main.unity (GUID kept).
- `EventSceneGenerator` (`MultiTravel/Generate/Complete Event Content`): runs the whole chain; creates Bootstrap.unity and
  Main.unity when missing, registers [Bootstrap, Main] in Build Settings, validates.
- `ProjectValidator` (`MultiTravel/Validate`): missing scripts, null required refs, catalog validation, config sanity, build-list check.
- `BuildScript.BuildWindows()`: Windows x64 Mono, `-buildOutput`, `-development`, `-stationConfig`; fails on validator errors;
  copies the station config into the player (see §11).
- CLI commands (`[CliCommand]`, com.unity.pipeline): `mt_generate_data`, `mt_generate_font`, `mt_generate_art`,
  `mt_import_art`, `mt_build_main_scene`, `mt_configure_project`, `mt_validate`, `mt_build_windows`.

## 9. Initial product list (customer will replace values; this is the default data)

Common, correct (+10, required): `laptop` Dizüstü Bilgisayar, `laptop-charger` Şarj Adaptörü, `phone-cable` Telefon Şarj Kablosu, `notebook` Not Defteri, `pen` Kalem, `toiletry-bag` Kozmetik/Tıraş Çantası, `id-card` Kimlik Kartı, `phone` Telefon, `laptop-bag` Bilgisayar Çantası, `glasses` Gözlük, `socks` Çorap.
Female, correct: `blouse` Bluz, `women-trousers` Kumaş Pantolon, `blazer` Blazer Ceket, `women-shoes` Klasik Ayakkabı, `women-tshirt` Kadın Tişört, `dress` Elbise.
Male, correct: `shirt` Gömlek, `men-trousers` Kumaş Pantolon, `jacket` Ceket, `men-shoes` Deri Ayakkabı, `tie` Kravat, `men-tshirt` Erkek Tişört.
Common, incorrect (-5): `beach-towel` Plaj Havlusu, `snorkel-mask` Şnorkel Maskesi, `beach-hat` Balıkçı Şapkası, `neck-pillow` Boyun Yastığı, `kids-book` Boyama Kitabı, `passport` Pasaport, `headphones` Kulaklık, `sunglasses` Güneş Gözlüğü, `travel-bag` Ek Seyahat Çantası, `rubber-duck` Oyuncak Ördek, `football` Futbol Topu, `ukulele` Ukulele, `garden-gnome` Bahçe Cücesi, `binoculars` Dürbün.
Female, incorrect: `bikini` Bikini, `swimsuit` Mayo. Male, incorrect: `swim-shorts` Deniz Şortu, `flip-flops` Terlik.
Per gender: 17 correct (all required) + 16 incorrect = 33 items on 36 shelf slots. Retired: `straw-hat`, `crayons`, `luggage-tag`, `gamepad`.
Customer decision pending: whether `passport`, `sunglasses`, `headphones` and `travel-bag` count as wrong for a domestic car trip (data-only change).

## 10. Testing strategy

EditMode (pure logic): ScoreService (positive, negative, duplicate, revert, reset), GameTimer (ManualClock), CompletionEvaluator (all modes, time limit), ProductSetResolver + catalog validation, ParticipantValidator (TR messages), ConfigLoader merge, LeaderboardRanking, SubmissionOutbox (fake backend: transient failure → retry → success; duplicate → no double file), SupabaseBackendClient payload/JSON mapping (serialization only), SessionController transitions (legal/illegal).
PlayMode (no XR hardware) — the assembled-game test sets `AppBootstrap.DataRootOverride` (temp folder) and an in-memory
backend, so it never touches the real outbox or event backend. `LiveBackendTests` (opt-in: `MT_LIVE_BACKEND=1` or
`Temp/mt_live_backend.flag`, refuses non-local URLs unless allowed) plays a session through the real `SupabaseBackendClient`
and reads it back from `get_leaderboard`. Suites: Main scene loads; ItemPool creates one instance per product; simulated place/remove through `SuitcaseController.TryPlace/Remove` updates score once; full session + reset leaves zero state; ItemRecoveryService returns an out-of-bounds item.
Backend: `backend/supabase/tests/run.mjs` against local Supabase (or a linked project when `SUPABASE_URL`/`SUPABASE_ANON_KEY` env set): register idempotency, submit idempotency, validation errors, leaderboard order, PII absence, access-code rejection.
Hardware-only (documented, not claimed): hand tracking grab quality over Quest Link, haptics, comfort, readability in HMD.

## 11. Build

`unity build MultiTravelValizChallenge --target StandaloneWindows64 --execute-method MultiTravel.EditorTools.BuildScript.BuildWindows --output-path Build/Windows/MultiTravelValizChallenge.exe`. Player settings: company "ECR Etkinlik Bilgisayar", product "MultiTravel Packing Challenge", version from `ProjectSettings` (1.0.0), fullscreen window, run in background ON, resizable window, no splash config changes beyond defaults allowed by license, Active Input Handling: Input System (Both only if the template requires it), scripting backend Mono (IL2CPP module not installed on the dev machine; documented), XR: OpenXR initialised on startup for Standalone.

Station config: the editor's `StreamingAssets/multitravel.config.json` is a developer file (local Supabase). After a
successful build, `BuildScript` copies `-stationConfig <file>` or, by default, `Deployment/multitravel.config.json` into
`<exe>_Data/StreamingAssets/`; when neither exists a localhost config is removed from the player (warning in the build
report). Per-station `stationId` goes into the LocalLow override (partial JSON, see EVENT_OPERATIONS.md).
