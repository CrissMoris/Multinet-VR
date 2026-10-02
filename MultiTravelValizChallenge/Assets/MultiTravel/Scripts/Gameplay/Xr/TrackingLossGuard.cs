using System.Collections.Generic;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MultiTravel.Gameplay.Xr
{
    /// <summary>
    /// Keeps items from being flung or lost when hand tracking drops (OVERHAUL_PLAN §5). When the
    /// <see cref="XRHandSubsystem"/> reports a hand as lost while that hand's interactor holds an item, the item is
    /// released and frozen at its last pose for <see cref="freezeSeconds"/> with the caption "Elini göster". If the hand
    /// is tracked again within <see cref="returnAfterSeconds"/> the item is handed back to the same interactor (or simply
    /// unfrozen when it cannot select); otherwise it floats back to its spawn slot. Controller interactors are never
    /// touched: only interactors under the rig's tracked-hand objects count as hands.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrackingLossGuard : MonoBehaviour
    {
        public const string Caption = "Elini göster";

        [SerializeField]
        [Tooltip("Pool whose items are watched.")]
        private ItemPool itemPool;

        [SerializeField]
        [Tooltip("Rig modality manager (found in the scene when empty); its Left/Right Hand objects define which interactors are hands.")]
        private XRInputModalityManager modalityManager;

        [SerializeField]
        [Min(0.1f)]
        private float freezeSeconds = 0.6f;

        [SerializeField]
        [Min(0.2f)]
        private float returnAfterSeconds = 1.5f;

        [SerializeField]
        [Tooltip("A release this close to the tracking loss still counts as 'was holding' (seconds).")]
        [Min(0f)]
        private float recentReleaseWindow = 0.3f;

        [SerializeField] private float captionFontSize = 0.6f;

        private struct HandSlot
        {
            public ProductItem Held;
            public XRBaseInteractor Interactor;
            public ProductItem LastReleased;
            public float LastReleaseTime;
            public bool GuardActive;
            public ProductItem Guarded;
            public float GuardStart;
            public bool Reacquired;
        }

        private static readonly List<XRHandSubsystem> Subsystems = new List<XRHandSubsystem>();

        private readonly HandSlot[] hands = new HandSlot[2];
        private readonly HashSet<ProductItem> watched = new HashSet<ProductItem>();
        private XRHandSubsystem subsystem;
        private float nextSubsystemLookup;
        private FloatingCaption caption;
        private Camera viewCamera;
        private float nextCameraLookup;

        /// <summary>Number of hands whose item is currently guarded (tests / diagnostics).</summary>
        public int ActiveGuardCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < hands.Length; i++)
                {
                    if (hands[i].GuardActive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Generator / test API.</summary>
        public void Configure(ItemPool pool, XRInputModalityManager modality)
        {
            UnwatchAll();
            itemPool = pool;
            modalityManager = modality;
            if (isActiveAndEnabled)
            {
                WatchPool();
            }
        }

        /// <summary>Simulates a tracking loss of a hand (tests; the subsystem callback calls the same code).</summary>
        public void SimulateTrackingLost(Handedness handedness)
        {
            HandleTrackingLost(handedness);
        }

        /// <summary>Simulates a hand being tracked again (tests).</summary>
        public void SimulateTrackingAcquired(Handedness handedness)
        {
            HandleTrackingAcquired(handedness);
        }

        // ----- Unity -----

        private void Awake()
        {
            caption = new FloatingCaption("Tracking caption", transform, captionFontSize, PresentationStyle.Orange, new Vector2(0.8f, 0.2f), false);
        }

        private void OnEnable()
        {
            WatchPool();
        }

        private void Start()
        {
            if (modalityManager == null)
            {
                modalityManager = FindAnyObjectByType<XRInputModalityManager>();
            }

            WatchPool();
            TryBindSubsystem();
        }

        private void OnDisable()
        {
            UnwatchAll();
            UnbindSubsystem();
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].GuardActive)
                {
                    EndGuard(i, false);
                }
            }
        }

        private void OnDestroy()
        {
            caption?.Destroy();
        }

        private void Update()
        {
            if (subsystem == null || !subsystem.running)
            {
                if (Time.unscaledTime >= nextSubsystemLookup)
                {
                    nextSubsystemLookup = Time.unscaledTime + 1f;
                    TryBindSubsystem();
                }
            }

            float now = Time.unscaledTime;
            for (int i = 0; i < hands.Length; i++)
            {
                ref var hand = ref hands[i];
                if (!hand.GuardActive)
                {
                    continue;
                }

                var item = hand.Guarded;
                if (item == null || item.State == ProductItemState.Pooled || item.State == ProductItemState.Placed || item.State == ProductItemState.Held)
                {
                    EndGuard(i, false);
                    continue;
                }

                float elapsed = now - hand.GuardStart;
                if (hand.Reacquired && elapsed >= freezeSeconds)
                {
                    EndGuard(i, true);
                }
                else if (!hand.Reacquired && elapsed >= returnAfterSeconds)
                {
                    item.ReturnToSpawn(false);
                    EndGuard(i, false);
                }
                else
                {
                    caption.MoveTo(item.AnchorPosition + Vector3.up * 0.15f);
                }
            }
        }

        private void LateUpdate()
        {
            if (caption != null && caption.IsShown)
            {
                caption.Tick(ResolveCamera());
            }
        }

        // ----- tracking events -----

        private void TryBindSubsystem()
        {
            if (subsystem != null && subsystem.running)
            {
                return;
            }

            UnbindSubsystem();
            SubsystemManager.GetSubsystems(Subsystems);
            for (int i = 0; i < Subsystems.Count; i++)
            {
                if (Subsystems[i] != null && Subsystems[i].running)
                {
                    subsystem = Subsystems[i];
                    break;
                }
            }

            Subsystems.Clear();
            if (subsystem != null)
            {
                subsystem.trackingLost += OnSubsystemTrackingLost;
                subsystem.trackingAcquired += OnSubsystemTrackingAcquired;
            }
        }

        private void UnbindSubsystem()
        {
            if (subsystem != null)
            {
                subsystem.trackingLost -= OnSubsystemTrackingLost;
                subsystem.trackingAcquired -= OnSubsystemTrackingAcquired;
                subsystem = null;
            }
        }

        private void OnSubsystemTrackingLost(XRHand hand)
        {
            HandleTrackingLost(hand.handedness);
        }

        private void OnSubsystemTrackingAcquired(XRHand hand)
        {
            HandleTrackingAcquired(hand.handedness);
        }

        private void HandleTrackingLost(Handedness handedness)
        {
            int index = IndexOf(handedness);
            if (index < 0)
            {
                return;
            }

            ref var hand = ref hands[index];
            if (hand.GuardActive)
            {
                return;
            }

            var item = hand.Held;
            if (item == null && hand.LastReleased != null && Time.unscaledTime - hand.LastReleaseTime <= recentReleaseWindow)
            {
                item = hand.LastReleased;
            }

            if (item == null || item.State == ProductItemState.Pooled || item.State == ProductItemState.Placed)
            {
                return;
            }

            if (item.State == ProductItemState.Held)
            {
                item.ForceRelease(); // reported as canceled: the suitcase never places it
            }

            if (item.State != ProductItemState.Free)
            {
                return;
            }

            var body = item.Body;
            if (body != null)
            {
                if (!body.isKinematic && item.gameObject.activeInHierarchy)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            hand.GuardActive = true;
            hand.Guarded = item;
            hand.GuardStart = Time.unscaledTime;
            hand.Reacquired = false;
            hand.Held = null;
            caption.Show(Caption, item.AnchorPosition + Vector3.up * 0.15f);
        }

        private void HandleTrackingAcquired(Handedness handedness)
        {
            int index = IndexOf(handedness);
            if (index < 0 || !hands[index].GuardActive)
            {
                return;
            }

            hands[index].Reacquired = true;
        }

        private void EndGuard(int index, bool handBack)
        {
            ref var hand = ref hands[index];
            var item = hand.Guarded;
            hand.GuardActive = false;
            hand.Guarded = null;
            if (item != null && item.State == ProductItemState.Free)
            {
                bool regrabbed = false;
                if (handBack)
                {
                    regrabbed = TryHandBack(hand.Interactor, item);
                }

                if (!regrabbed)
                {
                    var body = item.Body;
                    if (body != null)
                    {
                        body.isKinematic = false;
                        body.useGravity = true;
                        if (item.gameObject.activeInHierarchy)
                        {
                            body.WakeUp();
                        }
                    }
                }
            }

            if (ActiveGuardCount == 0)
            {
                caption.Hide();
            }
        }

        private static bool TryHandBack(XRBaseInteractor interactor, ProductItem item)
        {
            if (interactor == null || !interactor.isActiveAndEnabled || interactor.hasSelection)
            {
                return false;
            }

            var grab = item.Grab;
            if (grab == null || !grab.enabled || !item.InteractionEnabled)
            {
                return false;
            }

            var manager = interactor.interactionManager;
            if (manager == null || !manager.IsRegistered((IXRInteractor)interactor) || !manager.IsRegistered((IXRInteractable)grab))
            {
                return false;
            }

            manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)grab);
            return item.State == ProductItemState.Held;
        }

        // ----- item bookkeeping -----

        private void WatchPool()
        {
            if (itemPool == null)
            {
                return;
            }

            itemPool.ItemRegistered -= Watch;
            itemPool.ItemRegistered += Watch;
            var items = itemPool.AllItems;
            for (int i = 0; i < items.Count; i++)
            {
                Watch(items[i]);
            }
        }

        private void Watch(ProductItem item)
        {
            if (item == null || !watched.Add(item))
            {
                return;
            }

            item.Grabbed += OnItemGrabbed;
            item.Released += OnItemReleased;
        }

        private void UnwatchAll()
        {
            if (itemPool != null)
            {
                itemPool.ItemRegistered -= Watch;
            }

            foreach (var item in watched)
            {
                if (item != null)
                {
                    item.Grabbed -= OnItemGrabbed;
                    item.Released -= OnItemReleased;
                }
            }

            watched.Clear();
            for (int i = 0; i < hands.Length; i++)
            {
                hands[i].Held = null;
                hands[i].Interactor = null;
                hands[i].LastReleased = null;
            }
        }

        private void OnItemGrabbed(ProductItem item, Transform interactorTransform, ProductItemState previous)
        {
            int index = HandIndexOf(interactorTransform, out var interactor);
            // Any grab (hand or controller) ends a running guard on that item.
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].GuardActive && hands[i].Guarded == item)
                {
                    EndGuard(i, false);
                }
            }

            if (index < 0)
            {
                return;
            }

            hands[index].Held = item;
            hands[index].Interactor = interactor;
        }

        private void OnItemReleased(ProductItem item, Transform interactorTransform, bool canceled)
        {
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].Held == item)
                {
                    hands[i].Held = null;
                    hands[i].LastReleased = item;
                    hands[i].LastReleaseTime = Time.unscaledTime;
                }
            }
        }

        private int HandIndexOf(Transform interactorTransform, out XRBaseInteractor interactor)
        {
            interactor = null;
            if (interactorTransform == null || modalityManager == null)
            {
                return -1;
            }

            interactor = interactorTransform.GetComponentInParent<XRBaseInteractor>(true);
            var left = modalityManager.leftHand;
            var right = modalityManager.rightHand;
            if (left != null && interactorTransform.IsChildOf(left.transform))
            {
                return 0;
            }

            if (right != null && interactorTransform.IsChildOf(right.transform))
            {
                return 1;
            }

            return -1;
        }

        private static int IndexOf(Handedness handedness)
        {
            switch (handedness)
            {
                case Handedness.Left: return 0;
                case Handedness.Right: return 1;
                default: return -1;
            }
        }

        private Camera ResolveCamera()
        {
            if (viewCamera != null && viewCamera.isActiveAndEnabled)
            {
                return viewCamera;
            }

            if (Time.unscaledTime >= nextCameraLookup)
            {
                nextCameraLookup = Time.unscaledTime + 1f;
                viewCamera = Camera.main;
            }

            return viewCamera;
        }
    }
}
