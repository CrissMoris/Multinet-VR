using System;
using System.Collections;
using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.Gameplay.Common;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Root component of every product prefab (ARCHITECTURE.md §2.8, OVERHAUL_PLAN §5). Wraps the
    /// <see cref="XRGrabInteractable"/>, owns the item state machine (<see cref="ProductItemState"/>) and its physics per state.
    /// <para>
    /// Transitions are guarded: Pooled → Free (<see cref="ActivateAtSpawn"/>), Free ↔ Held (XRI select events),
    /// Free → Placed / Placed → Free (suitcase only), Placed → Held (grabbing a placed item), any → Pooled
    /// (<see cref="ReturnToPool"/>). Calls that do not match the current state are rejected and return false.
    /// </para>
    /// <para>
    /// Physics: a Free item resting at its home slot is <b>docked</b> (kinematic, so garments can hang on the rail and shelf
    /// items never topple); a Free item that was released anywhere else is a dynamic Rigidbody with gravity; Held = driven by
    /// XRI (Kinematic movement type); Placed and Pooled = kinematic.
    /// </para>
    /// <para>
    /// v2 mechanics: grip presets build an <c>Attach</c> child (<see cref="GripPreset"/>), the release velocity is clamped to
    /// <see cref="MaxReleaseSpeed"/>, a release within <see cref="HomeSnapRadius"/> of the home slot tweens the item back
    /// home, an inverted-hull <c>Outline</c> child is shown while hovered (plus a short haptic tick), and garments with an
    /// <see cref="ItemVisualVariant"/> switch between hanging and folded visuals.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class ProductItem : MonoBehaviour
    {
        /// <summary>Name of the grip attach child created per preset.</summary>
        public const string AttachChildName = "Attach";

        /// <summary>Name of the anchor child used for the inside-suitcase test.</summary>
        public const string AnchorChildName = "Anchor";

        /// <summary>Name of inverted-hull outline renderers (children of the visual meshes).</summary>
        public const string OutlineChildName = "Outline";

        /// <summary>Name of the enlarged invisible grab collider of small items (excluded from placement bounds).</summary>
        public const string GrabColliderName = "GrabCollider";

        /// <summary>Maximum linear speed an item keeps when released (m/s).</summary>
        public const float MaxReleaseSpeed = 1.5f;

        /// <summary>Maximum angular speed an item keeps when released (rad/s).</summary>
        public const float MaxReleaseAngularSpeed = 6f;

        /// <summary>A release closer than this to the home slot (metres) tweens the item back home.</summary>
        public const float HomeSnapRadius = 0.6f;

        /// <summary>Duration of the home-snap tween (seconds, unscaled).</summary>
        public const float HomeSnapSeconds = 0.25f;

        [SerializeField]
        [Tooltip("Product data. Assigned by the content generator on the prefab and re-asserted by ItemPool.Setup at runtime.")]
        private ProductDefinition definition;

        [SerializeField]
        [Tooltip("Point used for the inside-suitcase test (usually the visual centre). Defaults to this transform.")]
        private Transform anchorPoint;

        [SerializeField]
        [Tooltip("Grip attach transform. Built from the grip preset (child named 'Attach') when empty.")]
        private Transform gripAttach;

        [SerializeField]
        [Tooltip("Total duration of the shrink / grow animation used when the item is returned to its spawn slot.")]
        [Min(0f)]
        private float returnFadeSeconds = 0.2f;

        [SerializeField]
        [Tooltip("Apply ProductDefinition.Interaction (mass, grab scale) to the Rigidbody and the held scale.")]
        private bool applyDefinitionPhysics = true;

        [SerializeField]
        [Tooltip("Scale while the item rests in its display slot (1 = real size). The item grows to real size when grabbed.")]
        [Range(0.4f, 1f)]
        private float displayScale = 1f;

        [SerializeField]
        [Tooltip("Width (metres, along the shelf) of the item at display scale; picks the slot size class.")]
        [Min(0f)]
        private float displayFootprint = 0.1f;

        [SerializeField]
        [Tooltip("Height (metres) of the item at display scale; tall items need a top-row slot.")]
        [Min(0f)]
        private float displayHeight = 0.1f;

        [SerializeField]
        [Tooltip("Keep Free items kinematic while they rest at their home slot (hangers, shelves).")]
        private bool dockAtHome = true;

        [SerializeField]
        [Tooltip("Tween the item back home when it is released within HomeSnapRadius of its home slot.")]
        private bool homeSnap = true;

        private XRGrabInteractable grab;
        private Rigidbody body;
        private ItemVisualVariant variant;
        private bool initialized;
        private bool listenersAdded;
        private Vector3 baseLocalScale = Vector3.one;
        private Pose spawnPose = Pose.identity;
        private bool hasSpawnPose;
        private Transform homeParent;
        private Coroutine returnRoutine;
        private Coroutine scaleRoutine;
        private Bounds localBounds;
        private bool hasLocalBounds;
        private bool interactionEnabled = true;
        private bool docked;
        private GripPreset appliedGrip = (GripPreset)(-1);
        private readonly List<Renderer> outlineRenderers = new List<Renderer>();
        private readonly HashSet<Transform> hoveringInteractors = new HashSet<Transform>();
        private bool outlineForced;
        private bool outlineVisible;
        private Vector3 heldLastPosition;
        private Quaternion heldLastRotation = Quaternion.identity;
        private Vector3 heldVelocity;
        private Vector3 heldAngularVelocity;
        private bool heldSampled;

        /// <summary>Raised after the item entered <see cref="ProductItemState.Held"/>. Args: item, interactor transform (may be null), previous state.</summary>
        public event Action<ProductItem, Transform, ProductItemState> Grabbed;

        /// <summary>Raised after the item left <see cref="ProductItemState.Held"/>. Args: item, interactor transform (may be null), canceled (forced release).</summary>
        public event Action<ProductItem, Transform, bool> Released;

        /// <summary>Raised on every state transition with (item, previous, next).</summary>
        public event Action<ProductItem, ProductItemState, ProductItemState> StateChanged;

        /// <summary>Raised when the first interactor starts hovering the item (item, interactor transform).</summary>
        public event Action<ProductItem, Transform> HoverStarted;

        /// <summary>Raised when the last hovering interactor left the item (item, interactor transform that left last).</summary>
        public event Action<ProductItem, Transform> HoverEnded;

        /// <summary>Raised when a release near the home slot starts the home-snap tween.</summary>
        public event Action<ProductItem> HomeSnapStarted;

        /// <summary>Raised when the item is docked at its home slot again (activation, home-snap, recovery, return).</summary>
        public event Action<ProductItem> ReturnedHome;

        /// <summary>Current state; a freshly created instance starts <see cref="ProductItemState.Pooled"/>.</summary>
        public ProductItemState State { get; private set; } = ProductItemState.Pooled;

        /// <summary>The product this instance represents.</summary>
        public ProductDefinition Definition => definition;

        /// <summary>Product id or an empty string when no definition is assigned.</summary>
        public string ProductId => definition != null && definition.Id != null ? definition.Id : string.Empty;

        /// <summary>Transform used for the inside-volume test.</summary>
        public Transform AnchorPoint => anchorPoint != null ? anchorPoint : transform;

        /// <summary>Spawn (home) pose assigned by <see cref="SpawnSlotLayout"/> (world space).</summary>
        public Pose SpawnPose => spawnPose;

        /// <summary>True once a spawn pose has been assigned.</summary>
        public bool HasSpawnPose => hasSpawnPose;

        /// <summary>The spawn slot this item was assigned to for the current session (null for overflow / practice items).</summary>
        public SpawnSlot HomeSlot { get; private set; }

        /// <summary>True for the tutorial practice item: it settles in the suitcase but never scores or counts for completion.</summary>
        public bool IsPractice { get; private set; }

        /// <summary>Index inside the owning <see cref="ItemPool"/> (-1 when not pooled).</summary>
        public int PoolIndex { get; internal set; } = -1;

        /// <summary>True while the return-to-spawn or home-snap animation runs.</summary>
        public bool IsReturning => returnRoutine != null;

        /// <summary>True while a Free item rests kinematically at its home slot.</summary>
        public bool IsDocked => docked && State == ProductItemState.Free;

        /// <summary>True when grabbing is currently allowed (see <see cref="InteractionLock"/>).</summary>
        public bool InteractionEnabled => interactionEnabled;

        /// <summary>True while any interactor selects the item.</summary>
        public bool IsHeld => State == ProductItemState.Held;

        /// <summary>True while at least one interactor hovers the item.</summary>
        public bool IsHovered => hoveringInteractors.Count > 0;

        /// <summary>True while the outline is visible.</summary>
        public bool IsOutlineVisible => outlineVisible;

        /// <summary>Interactor that last grabbed or released the item (null for hands without haptics or forced releases).</summary>
        public Transform LastInteractor { get; private set; }

        /// <summary><see cref="Time.unscaledTime"/> of the last release (negative when never released).</summary>
        public float LastReleaseTime { get; private set; } = -1000f;

        /// <summary>Grip preset applied to the grab interactable.</summary>
        public GripPreset Grip => PresentationRules.EffectiveGrip(definition);

        /// <summary>The grip attach transform (child 'Attach'), created on demand.</summary>
        public Transform GripAttach
        {
            get
            {
                EnsureInitialized();
                return gripAttach;
            }
        }

        /// <summary>Visual variant switcher (null for items without a hanging variant).</summary>
        public ItemVisualVariant Variant
        {
            get
            {
                EnsureInitialized();
                return variant;
            }
        }

        /// <summary>The wrapped grab interactable.</summary>
        public XRGrabInteractable Grab
        {
            get
            {
                EnsureInitialized();
                return grab;
            }
        }

        /// <summary>The item's Rigidbody.</summary>
        public Rigidbody Body
        {
            get
            {
                EnsureInitialized();
                return body;
            }
        }

        /// <summary>
        /// Axis-aligned bounds of the item's body colliders in the item's local space (root scale excluded) for the active
        /// visual variant. The enlarged <see cref="GrabColliderName"/> collider and trigger colliders are ignored. Valid while
        /// the item is inactive.
        /// </summary>
        public Bounds LocalBounds
        {
            get
            {
                EnsureInitialized();
                if (variant != null)
                {
                    return variant.BoundsFor(variant.Current);
                }

                return ComputedBounds;
            }
        }

        /// <summary>Bounds used at the home slot (the hanging visual for garments).</summary>
        public Bounds DisplayBounds
        {
            get
            {
                EnsureInitialized();
                return variant != null ? variant.BoundsFor(ItemVariant.Hanging) : ComputedBounds;
            }
        }

        /// <summary>Bounds used inside the suitcase (the folded visual for garments).</summary>
        public Bounds PackedBounds
        {
            get
            {
                EnsureInitialized();
                return variant != null ? variant.BoundsFor(ItemVariant.Folded) : ComputedBounds;
            }
        }

        /// <summary>
        /// Item-local point that hangs from a hook (hanging display slots): the grip attach for <see cref="GripPreset.Hanger"/>
        /// items, otherwise the top centre of <see cref="DisplayBounds"/>.
        /// </summary>
        public Vector3 HangPointLocal
        {
            get
            {
                EnsureInitialized();
                if (Grip == GripPreset.Hanger && gripAttach != null)
                {
                    return transform.InverseTransformPoint(gripAttach.position);
                }

                var b = DisplayBounds;
                return new Vector3(b.center.x, b.max.y, b.center.z);
            }
        }

        /// <summary>Local scale of the item when not held.</summary>
        public Vector3 BaseLocalScale => baseLocalScale;

        private Bounds ComputedBounds
        {
            get
            {
                if (!hasLocalBounds)
                {
                    localBounds = ComputeLocalBounds();
                    hasLocalBounds = true;
                }

                return localBounds;
            }
        }

        // ----- static configuration (shared with editor tooling) -----

        /// <summary>
        /// Applies the XRI base configuration required by ARCHITECTURE.md §2.8. Public so editor tooling can apply the same
        /// values when it builds prefabs. Grip specific values are applied by <see cref="ConfigureGrip"/>.
        /// </summary>
        public static void ConfigureGrabInteractable(XRGrabInteractable target)
        {
            if (target == null)
            {
                return;
            }

            target.movementType = XRBaseInteractable.MovementType.Kinematic;
            // Throwing is done by ProductItem with a clamped velocity (MaxReleaseSpeed) so placements never warn about
            // throwing a kinematic body.
            target.throwOnDetach = false;
            target.useDynamicAttach = true;
            target.matchAttachPosition = true;
            target.matchAttachRotation = true;
            target.snapToColliderVolume = true;
            target.selectMode = InteractableSelectMode.Single;
            target.retainTransformParent = false;
            target.trackPosition = true;
            target.trackRotation = true;
            // Scale is driven by ProductItem (GrabScale) instead of the grab transformers.
            target.trackScale = false;
            target.smoothPosition = true;
            target.smoothRotation = true;
            target.attachEaseInTime = 0.15f;
            target.forceGravityOnDetach = false;
        }

        /// <summary>
        /// Applies a grip preset to <paramref name="target"/>:
        /// <list type="bullet">
        /// <item><see cref="GripPreset.Dynamic"/>: dynamic attach at the touched point (snapped onto the collider).</item>
        /// <item><see cref="GripPreset.FoldedGarment"/>, <see cref="GripPreset.Shoe"/>, <see cref="GripPreset.Handle"/>:
        /// static attach at <paramref name="attach"/>.</item>
        /// <item><see cref="GripPreset.Hanger"/>: static attach at the hook, rotation not tracked so the garment keeps hanging
        /// straight down.</item>
        /// <item><see cref="GripPreset.FlatEdge"/>: dynamic attach constrained to the collider surface (nearest edge), the item
        /// keeps its orientation.</item>
        /// </list>
        /// </summary>
        public static void ConfigureGrip(XRGrabInteractable target, GripPreset preset, Transform attach)
        {
            if (target == null)
            {
                return;
            }

            ConfigureGrabInteractable(target);
            target.attachTransform = attach;
            switch (preset)
            {
                case GripPreset.FoldedGarment:
                case GripPreset.Shoe:
                case GripPreset.Handle:
                    target.useDynamicAttach = false;
                    break;
                case GripPreset.Hanger:
                    target.useDynamicAttach = false;
                    target.matchAttachRotation = false;
                    target.trackRotation = false;
                    break;
                case GripPreset.FlatEdge:
                    target.useDynamicAttach = true;
                    target.matchAttachPosition = true;
                    target.snapToColliderVolume = true;
                    target.matchAttachRotation = false;
                    break;
                default:
                    target.useDynamicAttach = true;
                    break;
            }
        }

        /// <summary>
        /// Local pose of the grip attach point for <paramref name="preset"/> given the item's visual bounds (item space; +Z is
        /// the item's front, +Y up). <paramref name="hookAtPivot"/> = the prefab pivot is the hanger hook top.
        /// </summary>
        public static Vector3 GripAttachPosition(GripPreset preset, Bounds bounds, bool hookAtPivot)
        {
            var c = bounds.center;
            switch (preset)
            {
                case GripPreset.FoldedGarment:
                    return new Vector3(c.x, bounds.max.y, bounds.max.z);
                case GripPreset.Hanger:
                    return hookAtPivot ? Vector3.zero : new Vector3(c.x, bounds.max.y, c.z);
                case GripPreset.Shoe:
                    return new Vector3(c.x, Mathf.Lerp(bounds.min.y, bounds.max.y, 0.55f), bounds.min.z + bounds.size.z * 0.12f);
                case GripPreset.FlatEdge:
                    return new Vector3(c.x, c.y, bounds.max.z);
                case GripPreset.Handle:
                    return new Vector3(c.x, bounds.max.y, c.z);
                default:
                    return c;
            }
        }

        /// <summary>
        /// Creates (or re-poses) the <see cref="AttachChildName"/> child of <paramref name="root"/> for <paramref name="preset"/>
        /// from the visual bounds. Used by the art importer and at runtime when a prefab has no attach child.
        /// </summary>
        public static Transform BuildGripAttach(Transform root, GripPreset preset, bool hookAtPivot)
        {
            if (root == null)
            {
                return null;
            }

            var attach = root.Find(AttachChildName);
            if (attach == null)
            {
                attach = new GameObject(AttachChildName).transform;
                attach.SetParent(root, false);
            }

            var bounds = ItemVisualVariant.ComputeVisualBounds(root, root);
            var variantComponent = root.GetComponent<ItemVisualVariant>();
            if (variantComponent != null)
            {
                // Grip on the display visual (what the participant reaches for).
                var hanging = variantComponent.HangingVisual;
                if (hanging != null)
                {
                    bounds = ItemVisualVariant.ComputeVisualBounds(root, hanging);
                }
            }

            attach.localPosition = GripAttachPosition(preset, bounds, hookAtPivot);
            attach.localRotation = Quaternion.identity;
            attach.localScale = Vector3.one;
            return attach;
        }

        // ----- setup -----

        /// <summary>Assigns the definition and applies its interaction settings and grip. Safe to call while inactive.</summary>
        public void Setup(ProductDefinition productDefinition)
        {
            definition = productDefinition;
            EnsureInitialized();
            if (definition != null && applyDefinitionPhysics && definition.Interaction != null)
            {
                body.mass = Mathf.Max(0.01f, definition.Interaction.Mass);
            }

            ApplyGrip(false);
        }

        /// <summary>Generator API: point used for the inside-suitcase test (null = item root).</summary>
        /// <summary>Scale while resting in the display slot (1 = real size).</summary>
        public float DisplayScale => displayScale;

        /// <summary>Width in metres along the shelf at display scale.</summary>
        public float DisplayFootprint => displayFootprint;

        /// <summary>Height in metres at display scale.</summary>
        public float DisplayHeight => displayHeight;

        /// <summary>Slot size class this item needs (<see cref="SlotSize"/>).</summary>
        public SlotSize RequiredSlotSize => SpawnSlot.ClassFor(displayFootprint);

        /// <summary>True when the item needs a tall (open above) slot.</summary>
        public bool NeedsTallSlot => SpawnSlot.IsTall(displayHeight);

        /// <summary>Generator / test API: display scale, width and height (metres, already at display scale).</summary>
        public void SetDisplayMetrics(float scale, float width, float height)
        {
            displayScale = Mathf.Clamp(scale, 0.4f, 1f);
            displayFootprint = Mathf.Max(0f, width);
            displayHeight = Mathf.Max(0f, height);
        }

        private Vector3 HomeScale => baseLocalScale * displayScale;

        private void StopScaleRoutine()
        {
            if (scaleRoutine != null)
            {
                StopCoroutine(scaleRoutine);
                scaleRoutine = null;
            }
        }

        private IEnumerator GrowTo(Vector3 target, float seconds)
        {
            var start = transform.localScale;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }

            transform.localScale = target;
            scaleRoutine = null;
        }

        public void SetAnchorPoint(Transform anchor)
        {
            anchorPoint = anchor;
        }

        /// <summary>Generator API: grip attach transform (null = build from the preset).</summary>
        public void SetGripAttach(Transform attach)
        {
            gripAttach = attach;
            appliedGrip = (GripPreset)(-1);
            if (initialized)
            {
                ApplyGrip(false);
            }
        }

        /// <summary>Marks the item as the tutorial practice item (never scores, see <see cref="IsPractice"/>).</summary>
        public void SetPractice(bool practice)
        {
            IsPractice = practice;
        }

        /// <summary>Parent the item returns to when it goes back to its spawn slot or the pool.</summary>
        public void SetHomeParent(Transform parent)
        {
            homeParent = parent;
        }

        /// <summary>Records the spawn slot assigned for the session (informational; the pose is set by <see cref="SetSpawnPose(Pose)"/>).</summary>
        public void SetHomeSlot(SpawnSlot slot)
        {
            HomeSlot = slot;
        }

        /// <summary>Sets the world-space spawn pose used by <see cref="ActivateAtSpawn"/> and <see cref="ReturnToSpawn"/>.</summary>
        public void SetSpawnPose(Vector3 position, Quaternion rotation)
        {
            spawnPose = new Pose(position, rotation);
            hasSpawnPose = true;
        }

        /// <summary>Sets the world-space spawn pose.</summary>
        public void SetSpawnPose(Pose pose)
        {
            SetSpawnPose(pose.position, pose.rotation);
        }

        /// <summary>Switches the visual variant (no-op for items without <see cref="ItemVisualVariant"/>).</summary>
        public void SetVariant(ItemVariant value)
        {
            EnsureInitialized();
            if (variant != null)
            {
                variant.SetVariant(value);
            }
        }

        /// <summary>Shows the outline regardless of hover (tutorial glow). False returns to hover-driven behaviour.</summary>
        public void SetOutlineForced(bool forced)
        {
            outlineForced = forced;
            RefreshOutline();
        }

        /// <summary>Distance (metres) from the item root to its home pose; infinity without a spawn pose.</summary>
        public float DistanceFromHome()
        {
            return hasSpawnPose ? Vector3.Distance(transform.position, spawnPose.position) : float.PositiveInfinity;
        }

        // ----- commands -----

        /// <summary>Pooled → Free: enables the GameObject docked at its spawn pose (current pose when none was assigned).</summary>
        public bool ActivateAtSpawn()
        {
            EnsureInitialized();
            if (State != ProductItemState.Pooled)
            {
                return false;
            }

            if (!hasSpawnPose)
            {
                SetSpawnPose(transform.position, transform.rotation);
            }

            ApplySpawnTransform();
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            grab.enabled = interactionEnabled;
            SetState(ProductItemState.Free);
            ApplyHomePhysics();
            ReturnedHome?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Moves a Free or Held item back to its spawn pose (shrink / grow unless <paramref name="instant"/>). Held items are
        /// force-released first (the release is reported as canceled, so the suitcase never places it). Placed and Pooled
        /// items are rejected.
        /// </summary>
        public bool ReturnToSpawn(bool instant)
        {
            EnsureInitialized();
            if (State == ProductItemState.Pooled || State == ProductItemState.Placed)
            {
                return false;
            }

            if (State == ProductItemState.Held)
            {
                ForceRelease();
                if (State != ProductItemState.Free)
                {
                    return false;
                }
            }

            StopReturnRoutine();
            if (instant || returnFadeSeconds <= 0f || !isActiveAndEnabled)
            {
                ApplySpawnTransform();
                ApplyHomePhysics();
                ReturnedHome?.Invoke(this);
                return true;
            }

            returnRoutine = StartCoroutine(ReturnRoutine());
            return true;
        }

        /// <summary>Any state → Pooled: releases, resets physics, scale and variant, and disables the GameObject.</summary>
        public void ReturnToPool()
        {
            EnsureInitialized();
            StopReturnRoutine();
            if (grab.isSelected)
            {
                ForceRelease();
            }

            ZeroVelocities();
            body.isKinematic = true;
            docked = false;
            transform.localScale = baseLocalScale;
            ClearHover();
            if (hasSpawnPose)
            {
                ApplySpawnTransform();
            }
            else
            {
                if (variant != null)
                {
                    variant.SetVariant(ItemVariant.Hanging);
                }

                if (homeParent != null && transform.parent != homeParent)
                {
                    transform.SetParent(homeParent, true);
                }
            }

            SetState(ProductItemState.Pooled);
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>Cancels every selection on the item. Returns true when the item is no longer selected.</summary>
        public bool ForceRelease()
        {
            EnsureInitialized();
            if (!grab.isSelected)
            {
                return true;
            }

            var manager = grab.interactionManager;
            if (manager != null)
            {
                manager.CancelInteractableSelection((IXRSelectInteractable)grab);
            }

            if (grab.isSelected && grab.enabled)
            {
                // Unregistering through OnDisable also cancels the selection.
                grab.enabled = false;
                grab.enabled = interactionEnabled;
            }

            return !grab.isSelected;
        }

        /// <summary>
        /// Enables or disables grabbing. Disabling cancels an active selection (reported as a canceled release).
        /// Returns true when the item was held before the call.
        /// </summary>
        public bool SetInteractionEnabled(bool enabled)
        {
            EnsureInitialized();
            bool wasHeld = State == ProductItemState.Held;
            interactionEnabled = enabled;
            if (!enabled && grab.isSelected)
            {
                ForceRelease();
            }

            grab.enabled = enabled;
            if (!enabled)
            {
                ClearHover();
            }

            return wasHeld;
        }

        /// <summary>World position of <see cref="AnchorPoint"/>.</summary>
        public Vector3 AnchorPosition => AnchorPoint.position;

        /// <summary>True when the Rigidbody is kinematic, sleeping or moving slower than the thresholds.</summary>
        public bool IsAtRest(float maxLinearSpeed, float maxAngularSpeed)
        {
            EnsureInitialized();
            if (body.isKinematic)
            {
                return true;
            }

            if (body.IsSleeping())
            {
                return true;
            }

            return body.linearVelocity.sqrMagnitude <= maxLinearSpeed * maxLinearSpeed &&
                   body.angularVelocity.sqrMagnitude <= maxAngularSpeed * maxAngularSpeed;
        }

        // ----- suitcase-only transitions -----

        /// <summary>Free → Placed (suitcase only). Freezes the body.</summary>
        internal bool EnterPlaced()
        {
            EnsureInitialized();
            if (State != ProductItemState.Free)
            {
                return false;
            }

            StopReturnRoutine();
            transform.localScale = baseLocalScale;
            ZeroVelocities();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            docked = false;
            SetState(ProductItemState.Placed);
            return true;
        }

        /// <summary>Placed → Free (suitcase only). Restores dynamic physics.</summary>
        internal bool ExitPlacedToFree()
        {
            EnsureInitialized();
            if (State != ProductItemState.Placed)
            {
                return false;
            }

            transform.localScale = baseLocalScale;
            ApplyFreePhysics();
            SetState(ProductItemState.Free);
            return true;
        }

        /// <summary>Restores the resting scale (used by the suitcase when a squash animation is interrupted).</summary>
        internal void ResetScale()
        {
            transform.localScale = baseLocalScale;
        }

        // ----- Unity -----

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDisable()
        {
            // Coroutines stop with the component; make sure the item is not left shrunk or frozen half-way.
            if (returnRoutine != null)
            {
                returnRoutine = null;
                transform.localScale = baseLocalScale;
                if (State == ProductItemState.Free && hasSpawnPose)
                {
                    ApplySpawnTransform();
                    docked = dockAtHome;
                }
            }

            ClearHover();
        }

        private void OnDestroy()
        {
            if (listenersAdded && grab != null)
            {
                grab.selectEntered.RemoveListener(OnSelectEntered);
                grab.selectExited.RemoveListener(OnSelectExited);
                grab.hoverEntered.RemoveListener(OnHoverEntered);
                grab.hoverExited.RemoveListener(OnHoverExited);
            }

            listenersAdded = false;
        }

        private void OnValidate()
        {
            if (returnFadeSeconds < 0f)
            {
                returnFadeSeconds = 0f;
            }
        }

        private void Update()
        {
            if (State != ProductItemState.Held)
            {
                heldSampled = false;
                return;
            }

            // Release velocity estimate (XRI throwing is off; see ConfigureGrabInteractable).
            float dt = Time.deltaTime;
            var position = transform.position;
            var rotation = transform.rotation;
            if (heldSampled && dt > 1e-5f)
            {
                var velocity = (position - heldLastPosition) / dt;
                var delta = rotation * Quaternion.Inverse(heldLastRotation);
                delta.ToAngleAxis(out float angle, out var axis);
                if (angle > 180f)
                {
                    angle -= 360f;
                }

                var angular = float.IsNaN(axis.x) || float.IsInfinity(axis.x) ? Vector3.zero : axis * (angle * Mathf.Deg2Rad / dt);
                heldVelocity = Vector3.Lerp(heldVelocity, velocity, 0.5f);
                heldAngularVelocity = Vector3.Lerp(heldAngularVelocity, angular, 0.5f);
            }

            heldLastPosition = position;
            heldLastRotation = rotation;
            heldSampled = true;
        }

        // ----- internals -----

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            grab = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            variant = GetComponent<ItemVisualVariant>();
            if (grab == null)
            {
                grab = gameObject.AddComponent<XRGrabInteractable>();
            }

            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            baseLocalScale = transform.localScale;
            ConfigureGrabInteractable(grab);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            CollectOutlines();
            ApplyGrip(false);

            if (!listenersAdded)
            {
                grab.selectEntered.AddListener(OnSelectEntered);
                grab.selectExited.AddListener(OnSelectExited);
                grab.hoverEntered.AddListener(OnHoverEntered);
                grab.hoverExited.AddListener(OnHoverExited);
                listenersAdded = true;
            }
        }

        private void ApplyGrip(bool force)
        {
            var preset = Grip;
            if (!force && preset == appliedGrip && (gripAttach != null || preset == GripPreset.Dynamic))
            {
                return;
            }

            if (gripAttach == null)
            {
                gripAttach = transform.Find(AttachChildName);
            }

            if (gripAttach == null && preset != GripPreset.Dynamic)
            {
                gripAttach = BuildGripAttach(transform, preset, variant != null);
            }

            appliedGrip = preset;
            ConfigureGrip(grab, preset, gripAttach);
        }

        private void CollectOutlines()
        {
            outlineRenderers.Clear();
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].gameObject.name == OutlineChildName)
                {
                    outlineRenderers.Add(renderers[i]);
                    renderers[i].enabled = false;
                }
            }

            outlineVisible = false;
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            var interactor = args != null && args.interactorObject != null ? args.interactorObject.transform : null;
            if (interactor == null || !hoveringInteractors.Add(interactor))
            {
                return;
            }

            if (State != ProductItemState.Held)
            {
                Haptics.Send(interactor, Haptics.Hover);
            }

            if (hoveringInteractors.Count == 1)
            {
                RefreshOutline();
                HoverStarted?.Invoke(this, interactor);
            }
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            var interactor = args != null && args.interactorObject != null ? args.interactorObject.transform : null;
            if (interactor == null || !hoveringInteractors.Remove(interactor))
            {
                return;
            }

            if (hoveringInteractors.Count == 0)
            {
                RefreshOutline();
                HoverEnded?.Invoke(this, interactor);
            }
        }

        private void ClearHover()
        {
            if (hoveringInteractors.Count == 0)
            {
                RefreshOutline();
                return;
            }

            hoveringInteractors.Clear();
            RefreshOutline();
            HoverEnded?.Invoke(this, null);
        }

        private void RefreshOutline()
        {
            bool visible = outlineForced ||
                           (hoveringInteractors.Count > 0 && State != ProductItemState.Held && State != ProductItemState.Pooled);
            if (visible == outlineVisible)
            {
                return;
            }

            outlineVisible = visible;
            for (int i = 0; i < outlineRenderers.Count; i++)
            {
                if (outlineRenderers[i] != null)
                {
                    outlineRenderers[i].enabled = visible;
                }
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (State == ProductItemState.Pooled || State == ProductItemState.Held)
            {
                return;
            }

            StopReturnRoutine();
            docked = false;
            var previous = State;
            LastInteractor = args != null && args.interactorObject != null ? args.interactorObject.transform : null;

            float grabScale = GrabScale();
            var heldScale = Mathf.Approximately(grabScale, 1f) ? baseLocalScale : baseLocalScale * grabScale;
            StopScaleRoutine();
            if (displayScale < 0.999f && isActiveAndEnabled && (transform.localScale - heldScale).sqrMagnitude > 1e-6f)
            {
                scaleRoutine = StartCoroutine(GrowTo(heldScale, 0.12f));
            }
            else
            {
                transform.localScale = heldScale;
            }

            heldSampled = false;
            heldVelocity = Vector3.zero;
            heldAngularVelocity = Vector3.zero;
            SetState(ProductItemState.Held);
            RefreshOutline();
            Haptics.Send(LastInteractor, Haptics.Grab);
            Grabbed?.Invoke(this, LastInteractor, previous);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (State != ProductItemState.Held || grab.isSelected)
            {
                return;
            }

            LastInteractor = args != null && args.interactorObject != null ? args.interactorObject.transform : null;
            LastReleaseTime = Time.unscaledTime;
            bool canceled = args != null && args.isCanceled;

            StopScaleRoutine();
            transform.localScale = baseLocalScale;
            if (homeParent != null && transform.parent == null && gameObject.scene == homeParent.gameObject.scene)
            {
                // XRI unparents on grab (retainTransformParent = false); keep the hierarchy tidy.
                transform.SetParent(homeParent, true);
            }

            ApplyFreePhysics();
            if (!canceled && heldSampled && gameObject.activeInHierarchy)
            {
                body.linearVelocity = Vector3.ClampMagnitude(heldVelocity, MaxReleaseSpeed);
                body.angularVelocity = Vector3.ClampMagnitude(heldAngularVelocity, MaxReleaseAngularSpeed);
            }

            heldSampled = false;
            SetState(ProductItemState.Free);
            RefreshOutline();
            Released?.Invoke(this, LastInteractor, canceled);

            // Listeners (the suitcase) may have placed the item; only a still-free item can snap home.
            if (!canceled && homeSnap && State == ProductItemState.Free && !IsReturning && hasSpawnPose &&
                DistanceFromHome() <= HomeSnapRadius && isActiveAndEnabled)
            {
                StartHomeSnap();
            }
        }

        /// <summary>Starts the short tween back into the home slot (used after a release near home).</summary>
        public bool StartHomeSnap()
        {
            EnsureInitialized();
            if (State != ProductItemState.Free || !hasSpawnPose || !isActiveAndEnabled)
            {
                return false;
            }

            StopReturnRoutine();
            returnRoutine = StartCoroutine(HomeSnapRoutine());
            HomeSnapStarted?.Invoke(this);
            return true;
        }

        private float GrabScale()
        {
            if (!applyDefinitionPhysics || definition == null || definition.Interaction == null)
            {
                return 1f;
            }

            return Mathf.Max(0.1f, definition.Interaction.GrabScale);
        }

        private IEnumerator ReturnRoutine()
        {
            ZeroVelocities();
            body.isKinematic = true;
            float half = returnFadeSeconds * 0.5f;

            float t = 0f;
            var from = transform.localScale;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = from * Mathf.Lerp(1f, 0.01f, Mathf.Clamp01(t / half));
                yield return null;
            }

            ApplySpawnTransform();
            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = HomeScale * Mathf.Lerp(0.01f, 1f, Mathf.Clamp01(t / half));
                yield return null;
            }

            transform.localScale = HomeScale;
            returnRoutine = null;
            if (State == ProductItemState.Free)
            {
                ApplyHomePhysics();
                ReturnedHome?.Invoke(this);
            }
        }

        private IEnumerator HomeSnapRoutine()
        {
            ZeroVelocities();
            body.isKinematic = true;
            var startPosition = transform.position;
            var startRotation = transform.rotation;
            var startScale = transform.localScale;
            bool swapped = false;
            float t = 0f;
            while (t < HomeSnapSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / HomeSnapSeconds);
                float eased = 1f - (1f - k) * (1f - k);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, spawnPose.position, eased),
                    Quaternion.Slerp(startRotation, spawnPose.rotation, eased));
                transform.localScale = Vector3.Lerp(startScale, HomeScale, eased);
                if (!swapped && k >= 0.5f)
                {
                    swapped = true;
                    SetVariant(ItemVariant.Hanging);
                }

                yield return null;
            }

            ApplySpawnTransform();
            returnRoutine = null;
            if (State == ProductItemState.Free)
            {
                ApplyHomePhysics();
                ReturnedHome?.Invoke(this);
            }
        }

        private void StopReturnRoutine()
        {
            if (returnRoutine == null)
            {
                return;
            }

            StopCoroutine(returnRoutine);
            returnRoutine = null;
            transform.localScale = baseLocalScale;
        }

        private void ApplySpawnTransform()
        {
            if (homeParent != null && transform.parent != homeParent)
            {
                transform.SetParent(homeParent, true);
            }

            if (variant != null)
            {
                variant.SetVariant(ItemVariant.Hanging);
            }

            transform.localScale = HomeScale;
            transform.SetPositionAndRotation(spawnPose.position, spawnPose.rotation);
            if (body != null && gameObject.activeInHierarchy)
            {
                ZeroVelocities();
                body.position = spawnPose.position;
                body.rotation = spawnPose.rotation;
            }
        }

        /// <summary>At the home pose: docked (kinematic) or dynamic, depending on <see cref="dockAtHome"/>.</summary>
        private void ApplyHomePhysics()
        {
            if (!dockAtHome)
            {
                docked = false;
                ApplyFreePhysics();
                return;
            }

            ZeroVelocities();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            docked = true;
        }

        private void ApplyFreePhysics()
        {
            docked = false;
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            ZeroVelocities();
            if (gameObject.activeInHierarchy)
            {
                body.WakeUp();
            }
        }

        private void ZeroVelocities()
        {
            if (body == null || body.isKinematic || !gameObject.activeInHierarchy)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private void SetState(ProductItemState next)
        {
            if (State == next)
            {
                return;
            }

            var previous = State;
            State = next;
            StateChanged?.Invoke(this, previous, next);
        }

        private Bounds ComputeLocalBounds()
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            bool any = false;
            var result = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider.isTrigger || collider.gameObject.name == GrabColliderName)
                {
                    continue;
                }

                if (!TryGetColliderLocalBox(collider, out var box))
                {
                    continue;
                }

                var colliderTransform = collider.transform;
                var min = box.min;
                var max = box.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    var itemLocal = transform.InverseTransformPoint(colliderTransform.TransformPoint(local));
                    if (!any)
                    {
                        result = new Bounds(itemLocal, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        result.Encapsulate(itemLocal);
                    }
                }
            }

            if (!any)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "ProductItem '" + name + "' has no supported collider; using a 10 cm placement box.", this);
                result = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            }

            return result;
        }

        private static bool TryGetColliderLocalBox(Collider collider, out Bounds box)
        {
            switch (collider)
            {
                case BoxCollider boxCollider:
                    box = new Bounds(boxCollider.center, boxCollider.size);
                    return true;
                case SphereCollider sphere:
                    box = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                    return true;
                case CapsuleCollider capsule:
                {
                    float diameter = capsule.radius * 2f;
                    var size = new Vector3(diameter, diameter, diameter);
                    float height = Mathf.Max(capsule.height, diameter);
                    if (capsule.direction == 0)
                    {
                        size.x = height;
                    }
                    else if (capsule.direction == 1)
                    {
                        size.y = height;
                    }
                    else
                    {
                        size.z = height;
                    }

                    box = new Bounds(capsule.center, size);
                    return true;
                }

                case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                    box = meshCollider.sharedMesh.bounds;
                    return true;
                default:
                    box = default;
                    return false;
            }
        }
    }
}
