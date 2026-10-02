using System;
using System.Collections;
using System.Collections.Generic;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Products;
using MultiTravel.Core.Scoring;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Feedback;
using MultiTravel.Gameplay.Items;
using UnityEngine;

namespace MultiTravel.Gameplay.Suitcase
{
    /// <summary>
    /// The suitcase (ARCHITECTURE.md §2.8, OVERHAUL_PLAN §5). Owns the placement volume and the slots, places / removes items
    /// and applies score and completion progress.
    /// <para>
    /// Placement is triggered by (a) a non-canceled release whose anchor is inside <see cref="PlacementVolume"/> (the funnel
    /// may extend up to 0.20 m above the rim) or (b) <see cref="SettleWatcher"/> for unheld items that come to rest inside it.
    /// No trigger callbacks are used: containment is a geometric test, and every path goes through <see cref="TryPlace"/>,
    /// which only accepts Free items that are not already placed. Together with <see cref="ScoreService.TryApplyPlacement"/>
    /// (once per id) physics jitter can never double-score.
    /// </para>
    /// <para>
    /// Typed packing: the item takes a free slot whose <see cref="SuitcaseSlot.Kind"/> equals its
    /// <see cref="ProductPresentation.Packed"/>, then a <see cref="PackedKind.Flat"/> slot, then a <see cref="PackedKind.Top"/>
    /// slot, then the nearest free slot. Packing columns stack bottom-up as before. The item settles with a 0.30 s
    /// ease-out-back tween, garments switch to their folded visual half-way, soft goods squash briefly when they land and
    /// <see cref="ItemLanded"/> reports the landing (foley).
    /// </para>
    /// <para>
    /// Practice items (<see cref="ProductItem.IsPractice"/>) are accepted while <see cref="AcceptPracticePlacements"/> is on;
    /// they settle visually but never touch <see cref="ScoreService"/> / <see cref="CompletionEvaluator"/> and raise
    /// <see cref="PracticeItemPlaced"/> / <see cref="PracticeItemRemoved"/> instead of <see cref="ItemPlaced"/> /
    /// <see cref="ItemRemoved"/>.
    /// </para>
    /// <para>
    /// Grabbing a placed item removes it: the slot is freed, the score reverted (policy
    /// <see cref="ScoreService.RevertScoreOnRemoval"/>) and <see cref="CompletionEvaluator.NotifyRemoved"/> called.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SuitcaseController : MonoBehaviour
    {
        /// <summary>Settle tween duration (seconds, unscaled).</summary>
        public const float DefaultSettleSeconds = 0.30f;

        /// <summary>Soft-goods squash: starting y-scale.</summary>
        public const float SquashScale = 0.92f;

        /// <summary>Soft-goods squash duration (seconds, unscaled).</summary>
        public const float SquashSeconds = 0.15f;

        [SerializeField]
        [Tooltip("Trigger BoxCollider covering the interior plus up to 0.20 m above the rim.")]
        private BoxCollider placementVolume;

        [SerializeField]
        [Tooltip("Slots inside the suitcase base, in fill order. When empty, SuitcaseSlot children are collected.")]
        private List<SuitcaseSlot> slots = new List<SuitcaseSlot>();

        [SerializeField]
        [Tooltip("Pool whose items are watched for grab / release.")]
        private ItemPool itemPool;

        [SerializeField]
        [Tooltip("Catalog used for ProductCatalog.ScoreFor. Falls back to an AppServices registration.")]
        private ProductCatalog catalog;

        [SerializeField]
        [Tooltip("Duration of the settle tween into the slot (unscaled seconds).")]
        [Min(0f)]
        private float tweenSeconds = DefaultSettleSeconds;

        private readonly List<ProductItem> placedItems = new List<ProductItem>();
        private readonly Dictionary<ProductItem, SuitcaseSlot> slotByItem = new Dictionary<ProductItem, SuitcaseSlot>();
        private readonly Dictionary<ProductItem, Coroutine> tweens = new Dictionary<ProductItem, Coroutine>();
        private readonly HashSet<ProductItem> subscribedItems = new HashSet<ProductItem>();
        private readonly List<ProductItem> scratch = new List<ProductItem>();

        private ScoreService scoreService;
        private CompletionEvaluator completionEvaluator;
        private bool servicesReady;
        private bool warnedNoSlot;
        private float stackHeight;

        /// <summary>Raised after a scoring item was placed. The change has Delta 0 when the product was already counted.</summary>
        public event Action<ProductItem, ScoreChange> ItemPlaced;

        /// <summary>Raised after a scoring item was removed. The change has Delta 0 when nothing was reverted.</summary>
        public event Action<ProductItem, ScoreChange> ItemRemoved;

        /// <summary>Raised after the practice item was placed (no score, no completion).</summary>
        public event Action<ProductItem> PracticeItemPlaced;

        /// <summary>Raised after the practice item was removed.</summary>
        public event Action<ProductItem> PracticeItemRemoved;

        /// <summary>
        /// Raised when a placed item finishes its settle tween and lands (item, foley kind, isCorrect). Practice items report
        /// isCorrect = true. Not raised for column compaction moves.
        /// </summary>
        public event Action<ProductItem, SoundKind, bool> ItemLanded;

        /// <summary>Raised when <see cref="StackHeight"/> changed (new height in metres).</summary>
        public event Action<float> StackHeightChanged;

        /// <summary>When false (outside Playing) releases and settle checks never place scoring items. Direct <see cref="TryPlace"/> calls are also rejected.</summary>
        public bool AcceptPlacements { get; set; } = true;

        /// <summary>When true, practice items are placed even while <see cref="AcceptPlacements"/> is false (tutorial).</summary>
        public bool AcceptPracticePlacements { get; set; }

        /// <summary>The placement trigger.</summary>
        public BoxCollider PlacementVolume => placementVolume;

        /// <summary>The slots.</summary>
        public IReadOnlyList<SuitcaseSlot> Slots => slots;

        /// <summary>Items currently placed (scoring and practice), in placement order.</summary>
        public IReadOnlyList<ProductItem> PlacedItems => placedItems;

        /// <summary>Number of placed items (scoring and practice).</summary>
        public int PlacedCount => placedItems.Count;

        /// <summary>The pool watched by the suitcase.</summary>
        public ItemPool Pool => itemPool;

        /// <summary>Settle tween duration in seconds.</summary>
        public float SettleSeconds => tweenSeconds;

        /// <summary>
        /// Height (metres) of the highest packed item top above the lowest slot surface, from the items' resting poses
        /// (0 when empty). Drives <see cref="StrapLift"/> and the lid press.
        /// </summary>
        public float StackHeight => stackHeight;

        // ----- setup -----

        /// <summary>
        /// Injects the services explicitly (tests, or callers that already hold them). Services not passed (null) are
        /// resolved from AppServices in <c>Start</c>.
        /// </summary>
        public void Bind(ScoreService score, CompletionEvaluator evaluator, ProductCatalog productCatalog)
        {
            if (score != null)
            {
                scoreService = score;
            }

            if (evaluator != null)
            {
                completionEvaluator = evaluator;
            }

            if (productCatalog != null)
            {
                catalog = productCatalog;
            }

            servicesReady = scoreService != null && completionEvaluator != null && catalog != null;
        }

        /// <summary>Generator / test API: placement volume and slots.</summary>
        public void Configure(BoxCollider volume, IList<SuitcaseSlot> slotList)
        {
            placementVolume = volume;
            if (placementVolume != null)
            {
                placementVolume.isTrigger = true;
            }

            slots.Clear();
            if (slotList != null)
            {
                for (int i = 0; i < slotList.Count; i++)
                {
                    if (slotList[i] != null)
                    {
                        slots.Add(slotList[i]);
                    }
                }
            }

            RecomputeStackHeight();
        }

        /// <summary>Test / tuning API: settle tween duration (0 = snap).</summary>
        public void SetSettleSeconds(float seconds)
        {
            tweenSeconds = Mathf.Max(0f, seconds);
        }

        /// <summary>Generator / test API: the pool whose items are watched. Subscribes to its items.</summary>
        public void SetPool(ItemPool pool)
        {
            if (itemPool == pool)
            {
                SubscribePool();
                return;
            }

            UnsubscribeAll();
            UnsubscribePool();
            itemPool = pool;
            SubscribePool();
        }

        /// <summary>Subscribes to grab / release / state events of an item (pool items are watched automatically).</summary>
        public void Watch(ProductItem item)
        {
            if (item == null || subscribedItems.Contains(item))
            {
                return;
            }

            subscribedItems.Add(item);
            item.Grabbed += OnItemGrabbed;
            item.Released += OnItemReleased;
            item.StateChanged += OnItemStateChanged;
        }

        // ----- queries -----

        /// <summary>True when the item's anchor is inside the placement volume.</summary>
        public bool IsInsideVolume(ProductItem item)
        {
            return item != null && IsInsideVolume(item.AnchorPosition);
        }

        /// <summary>True when the world position is inside the placement volume (oriented box test).</summary>
        public bool IsInsideVolume(Vector3 worldPosition)
        {
            if (placementVolume == null)
            {
                return false;
            }

            var local = placementVolume.transform.InverseTransformPoint(worldPosition) - placementVolume.center;
            var half = placementVolume.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>True when the item is placed in this suitcase.</summary>
        public bool Contains(ProductItem item)
        {
            return item != null && slotByItem.ContainsKey(item);
        }

        /// <summary>True when <paramref name="item"/> would currently be accepted by a release / settle (state and gates).</summary>
        public bool CanAccept(ProductItem item)
        {
            if (item == null || item.State != ProductItemState.Free || slotByItem.ContainsKey(item))
            {
                return false;
            }

            return item.IsPractice ? AcceptPracticePlacements || AcceptPlacements : AcceptPlacements;
        }

        /// <summary>Slot of a placed item (null when placed without a free slot).</summary>
        public bool TryGetSlot(ProductItem item, out SuitcaseSlot slot)
        {
            slot = null;
            return item != null && slotByItem.TryGetValue(item, out slot) && slot != null;
        }

        /// <summary>Number of free slots.</summary>
        public int FreeSlotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] != null && !slots[i].IsOccupied)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>True while a settle tween / squash runs for <paramref name="item"/>.</summary>
        public bool IsSettling(ProductItem item)
        {
            return item != null && tweens.ContainsKey(item);
        }

        /// <summary>
        /// Slot the item would take now (typed packing: own kind → Flat → Top → nearest), without occupying it.
        /// </summary>
        public SuitcaseSlot PreviewSlot(ProductItem item, Vector3 dropPosition)
        {
            var kind = item != null ? PresentationRules.EffectivePacked(item.Definition) : PackedKind.Flat;
            var slot = NearestAvailable(dropPosition, kind, true);
            if (slot == null && kind != PackedKind.Flat)
            {
                slot = NearestAvailable(dropPosition, PackedKind.Flat, true);
            }

            if (slot == null && kind != PackedKind.Top)
            {
                slot = NearestAvailable(dropPosition, PackedKind.Top, true);
            }

            return slot ?? NearestAvailable(dropPosition, PackedKind.Flat, false);
        }

        // ----- commands -----

        /// <summary>
        /// Places a Free item: takes a free slot (typed packing), freezes it, tweens it in, applies the score once and
        /// notifies the completion evaluator (scoring items only). Returns false (and changes nothing) for any other state,
        /// an item that is already placed, missing services, or while placements are closed for the item.
        /// </summary>
        public bool TryPlace(ProductItem item)
        {
            if (!CanAccept(item))
            {
                return false;
            }

            bool practice = item.IsPractice;
            if (!practice && !EnsureServices())
            {
                return false;
            }

            var definition = item.Definition;
            if (definition == null || string.IsNullOrEmpty(definition.Id))
            {
                Debug.LogError(ServiceResolver.LogPrefix + "SuitcaseController: item '" + item.name + "' has no ProductDefinition; not placed.", item);
                return false;
            }

            if (!item.EnterPlaced())
            {
                return false;
            }

            var slot = PreviewSlot(item, item.AnchorPosition);
            if (slot != null)
            {
                slot.Occupy(item);
            }
            else if (!warnedNoSlot)
            {
                warnedNoSlot = true;
                Debug.LogWarning(ServiceResolver.LogPrefix + "SuitcaseController: no free slot left; items are placed where they rest. Add more SuitcaseSlots.", this);
            }

            slotByItem.Add(item, slot);
            placedItems.Add(item);
            Watch(item);
            RecomputeStackHeight();

            if (practice)
            {
                StartSettle(item, slot);
                PracticeItemPlaced?.Invoke(item);
                return true;
            }

            int delta = catalog.ScoreFor(definition);
            ScoreChange change;
            if (scoreService.TryApplyPlacement(definition.Id, delta))
            {
                change = new ScoreChange(definition.Id, delta, scoreService.Score, ScoreChangeReason.Placed);
            }
            else
            {
                change = new ScoreChange(definition.Id, 0, scoreService.Score, ScoreChangeReason.Placed);
            }

            completionEvaluator.NotifyPlaced(definition.Id);
            StartSettle(item, slot);
            ItemPlaced?.Invoke(item, change);
            return true;
        }

        /// <summary>
        /// Removes a placed item: frees its slot, reverts the score (when the policy is on) and notifies the evaluator
        /// (scoring items only). A Placed item becomes Free (dynamic again); a Held item (removal through grab) stays Held.
        /// Returns false when the item is not in the suitcase.
        /// </summary>
        public bool Remove(ProductItem item)
        {
            if (item == null || !slotByItem.TryGetValue(item, out var slot))
            {
                return false;
            }

            StopTween(item);
            slotByItem.Remove(item);
            placedItems.Remove(item);
            if (slot != null)
            {
                slot.Release();
                CompactColumn(slot);
            }

            RecomputeStackHeight();
            if (item.IsPractice)
            {
                if (item.State == ProductItemState.Placed)
                {
                    item.ExitPlacedToFree();
                }

                PracticeItemRemoved?.Invoke(item);
                return true;
            }

            string id = item.ProductId;
            ScoreChange change = new ScoreChange(id, 0, scoreService != null ? scoreService.Score : 0, ScoreChangeReason.Removed);
            if (scoreService != null && !string.IsNullOrEmpty(id))
            {
                int counted = scoreService.CountedDeltaFor(id);
                if (scoreService.RevertScoreOnRemoval && scoreService.TryRevertPlacement(id))
                {
                    change = new ScoreChange(id, -counted, scoreService.Score, ScoreChangeReason.Removed);
                }
            }

            if (completionEvaluator != null && !string.IsNullOrEmpty(id))
            {
                completionEvaluator.NotifyRemoved(id);
            }

            if (item.State == ProductItemState.Placed)
            {
                item.ExitPlacedToFree();
            }

            ItemRemoved?.Invoke(item, change);
            return true;
        }

        /// <summary>
        /// Empties the suitcase without touching the score (the session reset clears the score). Stops all tweens,
        /// frees all slots, notifies the evaluator (scoring items) and makes still-placed items Free again.
        /// </summary>
        public void Clear()
        {
            scratch.Clear();
            scratch.AddRange(placedItems);
            for (int i = 0; i < scratch.Count; i++)
            {
                var item = scratch[i];
                StopTween(item);
                if (completionEvaluator != null && item != null && !item.IsPractice && !string.IsNullOrEmpty(item.ProductId))
                {
                    completionEvaluator.NotifyRemoved(item.ProductId);
                }
            }

            placedItems.Clear();
            slotByItem.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].Release();
                    slots[i].ResetVisual();
                }
            }

            for (int i = 0; i < scratch.Count; i++)
            {
                var item = scratch[i];
                if (item != null && item.State == ProductItemState.Placed)
                {
                    item.ExitPlacedToFree();
                }
            }

            scratch.Clear();
            warnedNoSlot = false;
            RecomputeStackHeight();
        }

        // ----- Unity -----

        private void Awake()
        {
            if (slots.Count == 0)
            {
                GetComponentsInChildren(true, slots);
            }

            if (placementVolume != null)
            {
                placementVolume.isTrigger = true;
            }
        }

        private void Start()
        {
            EnsureServices();
            if (placementVolume == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "SuitcaseController: no PlacementVolume (BoxCollider) assigned.", this);
            }

            if (itemPool != null)
            {
                SubscribePool();
                itemPool.EnsureCreated();
                SubscribeToPoolItems();
            }
        }

        private void OnDisable()
        {
            // Coroutines die with the component; snap running tweens to their target so no item is left half-way.
            scratch.Clear();
            foreach (var pair in tweens)
            {
                scratch.Add(pair.Key);
            }

            for (int i = 0; i < scratch.Count; i++)
            {
                var item = scratch[i];
                if (item == null)
                {
                    continue;
                }

                item.ResetScale();
                if (slotByItem.TryGetValue(item, out var slot) && slot != null && item.State == ProductItemState.Placed)
                {
                    item.SetVariant(ItemVariant.Folded);
                    var pose = slot.PoseFor(item);
                    item.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }
            }

            tweens.Clear();
            scratch.Clear();
        }

        private void OnDestroy()
        {
            UnsubscribePool();
            UnsubscribeAll();
        }

        // ----- internals -----

        private bool EnsureServices()
        {
            if (servicesReady)
            {
                return true;
            }

            bool ok = ServiceResolver.Resolve(ref scoreService, this, nameof(SuitcaseController));
            ok &= ServiceResolver.Resolve(ref completionEvaluator, this, nameof(SuitcaseController));
            ok &= ServiceResolver.ResolveCatalog(ref catalog, this, nameof(SuitcaseController));
            servicesReady = ok;
            return ok;
        }

        private void SubscribePool()
        {
            if (itemPool == null)
            {
                return;
            }

            itemPool.ItemsCreated -= OnItemsCreated;
            itemPool.ItemRegistered -= Watch;
            itemPool.ItemsCreated += OnItemsCreated;
            itemPool.ItemRegistered += Watch;
            SubscribeToPoolItems();
        }

        private void UnsubscribePool()
        {
            if (itemPool == null)
            {
                return;
            }

            itemPool.ItemsCreated -= OnItemsCreated;
            itemPool.ItemRegistered -= Watch;
        }

        private void OnItemsCreated(ItemPool pool)
        {
            SubscribeToPoolItems();
        }

        private void SubscribeToPoolItems()
        {
            if (itemPool == null)
            {
                return;
            }

            var items = itemPool.AllItems;
            for (int i = 0; i < items.Count; i++)
            {
                Watch(items[i]);
            }
        }

        private void UnsubscribeAll()
        {
            foreach (var item in subscribedItems)
            {
                if (item == null)
                {
                    continue;
                }

                item.Grabbed -= OnItemGrabbed;
                item.Released -= OnItemReleased;
                item.StateChanged -= OnItemStateChanged;
            }

            subscribedItems.Clear();
        }

        private void OnItemGrabbed(ProductItem item, Transform interactor, ProductItemState previous)
        {
            if (previous == ProductItemState.Placed || slotByItem.ContainsKey(item))
            {
                Remove(item);
            }
        }

        private void OnItemReleased(ProductItem item, Transform interactor, bool canceled)
        {
            if (canceled || !CanAccept(item))
            {
                return;
            }

            if (IsInsideVolume(item))
            {
                TryPlace(item);
            }
        }

        private void OnItemStateChanged(ProductItem item, ProductItemState previous, ProductItemState next)
        {
            // An item pulled back into the pool while placed (reset paths) must not keep a slot.
            if (next == ProductItemState.Pooled && slotByItem.TryGetValue(item, out var slot))
            {
                StopTween(item);
                slotByItem.Remove(item);
                placedItems.Remove(item);
                if (slot != null)
                {
                    slot.Release();
                    CompactColumn(slot);
                }

                RecomputeStackHeight();
            }
        }

        /// <summary>
        /// After a slot in a packing column is emptied, every item above it drops one slot down (re-tweened), so a column
        /// never has a gap. Standalone slots (no column links) are unaffected.
        /// </summary>
        private void CompactColumn(SuitcaseSlot emptied)
        {
            var target = emptied;
            var source = emptied.Above;
            int guard = 0;
            while (source != null && source.IsOccupied && guard++ < 256)
            {
                var moving = source.Occupant;
                source.Release();
                target.Occupy(moving);
                slotByItem[moving] = target;
                StartTween(moving, target, false);
                target = source;
                source = source.Above;
            }
        }

        private SuitcaseSlot NearestAvailable(Vector3 position, PackedKind kind, bool matchKind)
        {
            SuitcaseSlot best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.IsAvailable || (matchKind && slot.Kind != kind))
                {
                    continue;
                }

                // Columns are chosen by horizontal distance to the drop point; a taller stack costs a little extra so
                // items spread over the suitcase floor before piling up.
                float distance;
                if (slot.Below != null || slot.Above != null)
                {
                    var bottom = slot.ColumnBottom.transform.position;
                    var flat = new Vector2(bottom.x - position.x, bottom.z - position.z);
                    float stack = slot.SurfacePoint.y - bottom.y;
                    distance = flat.sqrMagnitude + stack * stack * 4f;
                }
                else
                {
                    distance = (slot.transform.position - position).sqrMagnitude;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = slot;
                }
            }

            return best;
        }

        private void StartSettle(ProductItem item, SuitcaseSlot slot)
        {
            if (slot != null)
            {
                StartTween(item, slot, true);
                return;
            }

            // No slot: the item stays where it rests (folded) and lands immediately.
            item.SetVariant(ItemVariant.Folded);
            RaiseLanded(item);
        }

        private void StartTween(ProductItem item, SuitcaseSlot slot, bool landing)
        {
            StopTween(item);
            if (tweenSeconds <= 0f || !isActiveAndEnabled)
            {
                item.SetVariant(ItemVariant.Folded);
                var target = slot.PoseFor(item);
                item.transform.SetPositionAndRotation(target.position, target.rotation);
                if (landing)
                {
                    RaiseLanded(item);
                }

                return;
            }

            tweens[item] = StartCoroutine(TweenRoutine(item, slot, landing));
        }

        private void StopTween(ProductItem item)
        {
            if (item != null && tweens.TryGetValue(item, out var routine))
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }

                tweens.Remove(item);
                item.ResetScale();
            }
        }

        private void RaiseLanded(ProductItem item)
        {
            bool correct = item.IsPractice || (item.Definition != null && item.Definition.IsCorrect);
            ItemLanded?.Invoke(item, ProductSoundKinds.For(item.Definition), correct);
        }

        /// <summary>Ease-out-back (overshoots slightly, then settles at 1).</summary>
        public static float EaseOutBack(float k)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = k - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        private IEnumerator TweenRoutine(ProductItem item, SuitcaseSlot slot, bool landing)
        {
            var itemTransform = item.transform;
            var startPosition = itemTransform.position;
            var startRotation = itemTransform.rotation;
            // The packed (folded) bounds define the resting pose, so the target does not jump when the visual swaps.
            var target = slot.PoseFor(item);
            bool swapped = false;
            float t = 0f;
            while (t < tweenSeconds)
            {
                if (item == null || item.State != ProductItemState.Placed)
                {
                    tweens.Remove(item);
                    yield break;
                }

                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / tweenSeconds);
                float eased = EaseOutBack(k);
                itemTransform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, target.position, eased),
                    Quaternion.SlerpUnclamped(startRotation, target.rotation, eased));
                if (!swapped && k >= 0.5f)
                {
                    swapped = true;
                    item.SetVariant(ItemVariant.Folded);
                }

                yield return null;
            }

            if (item == null || item.State != ProductItemState.Placed)
            {
                tweens.Remove(item);
                yield break;
            }

            item.SetVariant(ItemVariant.Folded);
            itemTransform.SetPositionAndRotation(target.position, target.rotation);
            if (landing)
            {
                RaiseLanded(item);
                if (ProductSoundKinds.IsSoft(item.Definition))
                {
                    yield return SquashRoutine(item, target);
                }
            }

            tweens.Remove(item);
        }

        private IEnumerator SquashRoutine(ProductItem item, Pose target)
        {
            var itemTransform = item.transform;
            var baseScale = item.BaseLocalScale;
            var parent = itemTransform.parent;
            float worldScaleY = baseScale.y * (parent != null ? parent.lossyScale.y : 1f);
            float bottom = item.PackedBounds.min.y * worldScaleY;
            var up = target.rotation * Vector3.up;
            float t = 0f;
            while (t < SquashSeconds)
            {
                if (item == null || item.State != ProductItemState.Placed)
                {
                    if (item != null)
                    {
                        item.ResetScale();
                    }

                    yield break;
                }

                float k = Mathf.Clamp01(t / SquashSeconds);
                float s = Mathf.Lerp(SquashScale, 1f, 1f - (1f - k) * (1f - k));
                itemTransform.localScale = new Vector3(baseScale.x, baseScale.y * s, baseScale.z);
                // Keep the bottom on the slot surface while the height changes.
                itemTransform.position = target.position + up * (bottom * (1f - s));
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            item.ResetScale();
            itemTransform.SetPositionAndRotation(target.position, target.rotation);
        }

        private void RecomputeStackHeight()
        {
            float floor = float.MaxValue;
            float top = float.MinValue;
            bool any = false;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                if (slot.Below == null)
                {
                    floor = Mathf.Min(floor, slot.transform.position.y);
                }

                if (slot.IsOccupied)
                {
                    any = true;
                    top = Mathf.Max(top, slot.TopWorldY);
                }
            }

            float height = any && floor < float.MaxValue ? Mathf.Max(0f, top - floor) : 0f;
            if (Mathf.Abs(height - stackHeight) < 1e-4f)
            {
                return;
            }

            stackHeight = height;
            StackHeightChanged?.Invoke(stackHeight);
        }
    }
}
