using System;
using System.Collections;
using MultiTravel.Core.Products;
using MultiTravel.Gameplay.Common;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Root component of every product prefab (ARCHITECTURE.md §2.8). Wraps the <see cref="XRGrabInteractable"/>,
    /// owns the item state machine (<see cref="ProductItemState"/>) and its physics configuration per state.
    /// <para>
    /// Transitions are guarded: Pooled → Free (<see cref="ActivateAtSpawn"/>), Free ↔ Held (XRI select events),
    /// Free → Placed / Placed → Free (suitcase only), Placed → Held (grabbing a placed item), any → Pooled
    /// (<see cref="ReturnToPool"/>). Calls that do not match the current state are rejected and return false.
    /// </para>
    /// <para>
    /// Physics: Free = dynamic Rigidbody with gravity; Held = driven by XRI (Kinematic movement type);
    /// Placed and Pooled = kinematic.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class ProductItem : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Product data. Assigned by the content generator on the prefab and re-asserted by ItemPool.Setup at runtime.")]
        private ProductDefinition definition;

        [SerializeField]
        [Tooltip("Point used for the inside-suitcase test (usually the visual centre). Defaults to this transform.")]
        private Transform anchorPoint;

        [SerializeField]
        [Tooltip("Total duration of the shrink / grow animation used when the item is returned to its spawn slot.")]
        [Min(0f)]
        private float returnFadeSeconds = 0.2f;

        [SerializeField]
        [Tooltip("Apply ProductDefinition.Interaction (mass, grab scale) to the Rigidbody and the held scale.")]
        private bool applyDefinitionPhysics = true;

        private XRGrabInteractable grab;
        private Rigidbody body;
        private bool initialized;
        private bool listenersAdded;
        private Vector3 baseLocalScale = Vector3.one;
        private Pose spawnPose = Pose.identity;
        private bool hasSpawnPose;
        private Transform homeParent;
        private Coroutine returnRoutine;
        private Bounds localBounds;
        private bool hasLocalBounds;
        private bool interactionEnabled = true;

        /// <summary>Raised after the item entered <see cref="ProductItemState.Held"/>. Args: item, interactor transform (may be null), previous state.</summary>
        public event Action<ProductItem, Transform, ProductItemState> Grabbed;

        /// <summary>Raised after the item left <see cref="ProductItemState.Held"/>. Args: item, interactor transform (may be null), canceled (forced release).</summary>
        public event Action<ProductItem, Transform, bool> Released;

        /// <summary>Raised on every state transition with (item, previous, next).</summary>
        public event Action<ProductItem, ProductItemState, ProductItemState> StateChanged;

        /// <summary>Current state; a freshly created instance starts <see cref="ProductItemState.Pooled"/>.</summary>
        public ProductItemState State { get; private set; } = ProductItemState.Pooled;

        /// <summary>The product this instance represents.</summary>
        public ProductDefinition Definition => definition;

        /// <summary>Product id or an empty string when no definition is assigned.</summary>
        public string ProductId => definition != null && definition.Id != null ? definition.Id : string.Empty;

        /// <summary>Transform used for the inside-volume test.</summary>
        public Transform AnchorPoint => anchorPoint != null ? anchorPoint : transform;

        /// <summary>Spawn pose assigned by <see cref="SpawnSlotLayout"/> (world space).</summary>
        public Pose SpawnPose => spawnPose;

        /// <summary>True once a spawn pose has been assigned.</summary>
        public bool HasSpawnPose => hasSpawnPose;

        /// <summary>Index inside the owning <see cref="ItemPool"/> (-1 when not pooled).</summary>
        public int PoolIndex { get; internal set; } = -1;

        /// <summary>True while the return-to-spawn animation runs.</summary>
        public bool IsReturning => returnRoutine != null;

        /// <summary>True when grabbing is currently allowed (see <see cref="InteractionLock"/>).</summary>
        public bool InteractionEnabled => interactionEnabled;

        /// <summary>True while any interactor selects the item.</summary>
        public bool IsHeld => State == ProductItemState.Held;

        /// <summary>Interactor that last grabbed or released the item (null for hands without haptics or forced releases).</summary>
        public Transform LastInteractor { get; private set; }

        /// <summary><see cref="Time.unscaledTime"/> of the last release (negative when never released).</summary>
        public float LastReleaseTime { get; private set; } = -1000f;

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
        /// Axis-aligned bounds of the item's non-trigger colliders in the item's local space (root scale excluded).
        /// Computed once from collider geometry, so it is valid while the item is inactive.
        /// </summary>
        public Bounds LocalBounds
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

        /// <summary>Local scale of the item when not held.</summary>
        public Vector3 BaseLocalScale => baseLocalScale;

        /// <summary>
        /// Applies the XRI configuration required by ARCHITECTURE.md §2.8. Public so editor tooling can apply the same
        /// values when it builds prefabs.
        /// </summary>
        public static void ConfigureGrabInteractable(XRGrabInteractable target)
        {
            if (target == null)
            {
                return;
            }

            target.movementType = XRBaseInteractable.MovementType.Kinematic;
            target.throwOnDetach = false;
            target.useDynamicAttach = true;
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

        /// <summary>Assigns the definition and applies its interaction settings. Safe to call while inactive.</summary>
        public void Setup(ProductDefinition productDefinition)
        {
            definition = productDefinition;
            EnsureInitialized();
            if (definition != null && applyDefinitionPhysics && definition.Interaction != null)
            {
                body.mass = Mathf.Max(0.01f, definition.Interaction.Mass);
            }
        }

        /// <summary>Generator API: point used for the inside-suitcase test (null = item root).</summary>
        public void SetAnchorPoint(Transform anchor)
        {
            anchorPoint = anchor;
        }

        /// <summary>Parent the item returns to when it goes back to its spawn slot or the pool.</summary>
        public void SetHomeParent(Transform parent)
        {
            homeParent = parent;
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

        /// <summary>Pooled → Free: enables the GameObject at its spawn pose (current pose when none was assigned).</summary>
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
            ApplyFreePhysics();
            SetState(ProductItemState.Free);
            return true;
        }

        /// <summary>
        /// Moves a Free or Held item back to its spawn pose. Held items are force-released first (the release is reported
        /// as canceled, so the suitcase never places it). Placed and Pooled items are rejected.
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
                ApplyFreePhysics();
                return true;
            }

            returnRoutine = StartCoroutine(ReturnRoutine());
            return true;
        }

        /// <summary>Any state → Pooled: releases, resets physics and scale, and disables the GameObject.</summary>
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
            transform.localScale = baseLocalScale;
            if (hasSpawnPose)
            {
                ApplySpawnTransform();
            }
            else if (homeParent != null && transform.parent != homeParent)
            {
                transform.SetParent(homeParent, true);
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
            return wasHeld;
        }

        /// <summary>World position of <see cref="AnchorPoint"/>.</summary>
        public Vector3 AnchorPosition => AnchorPoint.position;

        /// <summary>True when the Rigidbody is sleeping or moving slower than the thresholds.</summary>
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

            ApplyFreePhysics();
            SetState(ProductItemState.Free);
            return true;
        }

        // ----- Unity -----

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDisable()
        {
            // Coroutines stop with the component; make sure the item is not left shrunk or frozen.
            if (returnRoutine != null)
            {
                returnRoutine = null;
                transform.localScale = baseLocalScale;
            }
        }

        private void OnDestroy()
        {
            if (listenersAdded && grab != null)
            {
                grab.selectEntered.RemoveListener(OnSelectEntered);
                grab.selectExited.RemoveListener(OnSelectExited);
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

            if (!listenersAdded)
            {
                grab.selectEntered.AddListener(OnSelectEntered);
                grab.selectExited.AddListener(OnSelectExited);
                listenersAdded = true;
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (State == ProductItemState.Pooled || State == ProductItemState.Held)
            {
                return;
            }

            StopReturnRoutine();
            var previous = State;
            LastInteractor = args != null && args.interactorObject != null ? args.interactorObject.transform : null;

            float grabScale = GrabScale();
            if (!Mathf.Approximately(grabScale, 1f))
            {
                transform.localScale = baseLocalScale * grabScale;
            }

            SetState(ProductItemState.Held);
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

            transform.localScale = baseLocalScale;
            if (homeParent != null && transform.parent == null && gameObject.scene == homeParent.gameObject.scene)
            {
                // XRI unparents on grab (retainTransformParent = false); keep the hierarchy tidy.
                transform.SetParent(homeParent, true);
            }

            ApplyFreePhysics();
            SetState(ProductItemState.Free);
            Released?.Invoke(this, LastInteractor, canceled);
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
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = baseLocalScale * Mathf.Lerp(1f, 0.01f, Mathf.Clamp01(t / half));
                yield return null;
            }

            ApplySpawnTransform();
            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = baseLocalScale * Mathf.Lerp(0.01f, 1f, Mathf.Clamp01(t / half));
                yield return null;
            }

            transform.localScale = baseLocalScale;
            returnRoutine = null;
            if (State == ProductItemState.Free)
            {
                ApplyFreePhysics();
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

            transform.localScale = baseLocalScale;
            transform.SetPositionAndRotation(spawnPose.position, spawnPose.rotation);
            if (body != null && gameObject.activeInHierarchy)
            {
                ZeroVelocities();
                body.position = spawnPose.position;
                body.rotation = spawnPose.rotation;
            }
        }

        private void ApplyFreePhysics()
        {
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
                if (collider == null || collider.isTrigger)
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
