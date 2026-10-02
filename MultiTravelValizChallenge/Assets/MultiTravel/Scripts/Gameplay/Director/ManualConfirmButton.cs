using MultiTravel.Core.Completion;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.UI;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MultiTravel.Gameplay.Director
{
    /// <summary>
    /// Poke / select button "Valizi Tamamla" on the pedestal (ARCHITECTURE.md §2.8). Calls
    /// <see cref="CompletionEvaluator.NotifyManualConfirm"/> only while Playing and only when the configured
    /// <see cref="CompletionMode"/> allows manual confirmation; otherwise the button root is hidden.
    /// A missing <see cref="XRPokeFilter"/> is added in <c>Awake</c> (the rig's poke interactors require one).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class ManualConfirmButton : MonoBehaviour
    {
        /// <summary>Turkish button caption.</summary>
        public const string Caption = "Valizi Tamamla";

        [SerializeField]
        [Tooltip("Director asked for an immediate completion check after a confirm.")]
        private GameplayDirector director;

        [SerializeField]
        [Tooltip("Visual root shown only when manual confirmation is allowed (defaults to this GameObject's children).")]
        private GameObject visualRoot;

        [SerializeField]
        [Tooltip("Optional caption text; set to 'Valizi Tamamla' in Awake.")]
        private TMP_Text label;

        [SerializeField]
        [Tooltip("Optional renderer that flashes on press.")]
        private Renderer pressRenderer;

        [SerializeField]
        [Tooltip("Collider used by the poke filter (defaults to the first collider on this object).")]
        private Collider pokeCollider;

        [SerializeField]
        [Tooltip("Minimum seconds between two accepted presses.")]
        [Min(0f)]
        private float debounceSeconds = 0.5f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private XRSimpleInteractable interactable;
        private SessionController session;
        private CompletionEvaluator completionEvaluator;
        private bool servicesReady;
        private float lastPressTime = -1000f;
        private float pressFlashUntil;
        private MaterialPropertyBlock block;
        private bool visualShown = true;
        private bool listenerAdded;

        /// <summary>Raised when a press was accepted and forwarded to the evaluator.</summary>
        public event System.Action Confirmed;

        /// <summary>True when the configured mode accepts manual confirmation.</summary>
        public bool ModeAllowsManualConfirm => completionEvaluator != null && completionEvaluator.AllowsManualConfirm;

        /// <summary>Generator API: visual references (any may be null).</summary>
        public void Configure(GameplayDirector gameplayDirector, GameObject buttonVisualRoot, TMP_Text caption, Renderer flashRenderer, Collider pokeSurface)
        {
            director = gameplayDirector;
            visualRoot = buttonVisualRoot;
            label = caption;
            pressRenderer = flashRenderer;
            pokeCollider = pokeSurface;
        }

        /// <summary>Test / generator API.</summary>
        public void Bind(SessionController sessionController, CompletionEvaluator evaluator, GameplayDirector gameplayDirector)
        {
            session = sessionController ?? session;
            completionEvaluator = evaluator ?? completionEvaluator;
            director = gameplayDirector != null ? gameplayDirector : director;
            servicesReady = session != null && completionEvaluator != null;
        }

        /// <summary>
        /// Performs a press (also used by the operator / tests). Returns true when the confirmation was forwarded.
        /// </summary>
        public bool Press()
        {
            if (!EnsureServices())
            {
                return false;
            }

            if (session.State != SessionState.Playing || !completionEvaluator.AllowsManualConfirm)
            {
                return false;
            }

            if (Time.unscaledTime - lastPressTime < debounceSeconds)
            {
                return false;
            }

            lastPressTime = Time.unscaledTime;
            completionEvaluator.NotifyManualConfirm();
            if (director != null)
            {
                director.RequestCompletionCheck();
            }

            FlashPress();
            Confirmed?.Invoke();
            return true;
        }

        private void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            interactable.selectMode = InteractableSelectMode.Single;
            if (visualRoot == gameObject)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "ManualConfirmButton: visualRoot must be a child (hiding the button's own GameObject would stop it); ignored.", this);
                visualRoot = null;
            }

            if (pokeCollider == null)
            {
                pokeCollider = GetComponentInChildren<Collider>(true);
            }

            var filter = GetComponent<XRPokeFilter>();
            if (filter == null)
            {
                filter = gameObject.AddComponent<XRPokeFilter>();
            }

            filter.pokeInteractable = interactable;
            if (pokeCollider != null)
            {
                filter.pokeCollider = pokeCollider;
            }

            interactable.selectEntered.AddListener(OnSelectEntered);
            listenerAdded = true;

            if (label != null)
            {
                label.text = Caption;
                if (VrUiStyle.Font != null)
                {
                    label.font = VrUiStyle.Font;
                }
            }
        }

        private void Start()
        {
            EnsureServices();
            if (director == null)
            {
                director = FindAnyObjectByType<GameplayDirector>();
            }

            UpdateVisibility();
        }

        private void Update()
        {
            UpdateVisibility();
            if (pressRenderer != null && pressFlashUntil > 0f && Time.unscaledTime >= pressFlashUntil)
            {
                pressFlashUntil = 0f;
                pressRenderer.SetPropertyBlock(null);
            }
        }

        private void OnDestroy()
        {
            if (listenerAdded && interactable != null)
            {
                interactable.selectEntered.RemoveListener(OnSelectEntered);
            }

            listenerAdded = false;
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            Press();
        }

        private bool EnsureServices()
        {
            if (servicesReady)
            {
                return true;
            }

            bool ok = ServiceResolver.Resolve(ref session, this, nameof(ManualConfirmButton));
            ok &= ServiceResolver.Resolve(ref completionEvaluator, this, nameof(ManualConfirmButton));
            servicesReady = ok;
            return ok;
        }

        private void UpdateVisibility()
        {
            bool allowed = servicesReady && completionEvaluator.AllowsManualConfirm;
            bool interactive = allowed && session.State == SessionState.Playing;
            if (interactable != null && interactable.enabled != interactive)
            {
                interactable.enabled = interactive;
            }

            if (visualRoot != null && visualShown != allowed)
            {
                visualShown = allowed;
                visualRoot.SetActive(allowed);
            }
        }

        private void FlashPress()
        {
            if (pressRenderer == null)
            {
                return;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            pressRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, VrUiStyle.Teal);
            pressRenderer.SetPropertyBlock(block);
            pressFlashUntil = Time.unscaledTime + 0.3f;
        }
    }
}
