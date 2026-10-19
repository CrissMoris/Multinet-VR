using MultiTravel.Core.Services;
using MultiTravel.Gameplay.Common;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.UI;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MultiTravel.Gameplay.Desktop
{
    /// <summary>
    /// Headset-free demo mode (separate "Masaüstü" build, scripting define <c>MT_DESKTOP</c>). The operator screen stays the
    /// entry point (registration, product set, start) and is hidden while the countdown / game runs, so the monitor shows
    /// the participant's view: WASD walks, the mouse looks around, a left click picks up the product under the crosshair
    /// and a second left click drops it (over the suitcase = packed). Grabbing goes through the real XRI path (a runtime
    /// <see cref="XRDirectInteractor"/> held in front of the camera), so scoring, placement and recovery are unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DesktopPlayMode : MonoBehaviour
    {
        /// <summary>True when the player was built as the desktop demo.</summary>
        public static bool IsDesktopBuild
        {
            get
            {
#if MT_DESKTOP
                return true;
#else
                return false;
#endif
            }
        }

        private const string OperatorCanvasName = "OperatorCanvas";
        private const string HintIdle = "<b>WASD</b> yürü   ·   <b>Fare</b> etrafına bak   ·   ürüne bakıp <b>Sol tık</b> ile al";
        private const string HintAim = "<b>Sol tık</b>: {0} ürününü al";
        private const string HintHeld = "Valizin üstüne getir ve <b>Sol tık</b> ile bırak   ·   <b>Sağ tık</b>: geri bırak";
        private const string HintUnlocked = "Oyuna dönmek için ekrana <b>tıkla</b>";

        [SerializeField] private float eyeHeight = 1.6f;
        [SerializeField] private float walkSpeed = 1.6f;
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float reach = 2.6f;
        [SerializeField] private float holdDistance = 0.55f;
        [SerializeField] private float holdDrop = 0.12f;
        [SerializeField] private float bodyRadius = 0.25f;

        private SessionController session;
        private XROrigin origin;
        private Camera cam;
        private XRInteractionManager manager;
        private XRDirectInteractor hand;
        private ProductItem held;
        private ProductItem aimed;
        private Canvas operatorCanvas;
        private GameObject hud;
        private TextMeshProUGUI hintText;
        private Graphic crosshair;
        private float yaw;
        private float pitch;
        private bool playing;
        private bool rigPrepared;

#if MT_DESKTOP
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += (_, _) => Attach();
            Attach();
        }
#endif

        /// <summary>Adds the mode to the loaded game scene once (the scene that contains the XR Origin).</summary>
        public static void Attach()
        {
            if (FindAnyObjectByType<DesktopPlayMode>() != null || FindAnyObjectByType<XROrigin>() == null)
            {
                return;
            }

            new GameObject("Desktop Play Mode").AddComponent<DesktopPlayMode>();
        }

        // ----- Unity -----

        private void Start()
        {
            origin = FindAnyObjectByType<XROrigin>();
            manager = FindAnyObjectByType<XRInteractionManager>();
            cam = origin != null ? origin.Camera : Camera.main;
            BuildHud();
            SetPlaying(false);
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.StateChanged -= OnStateChanged;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (session == null && ServiceResolver.TryResolve(ref session))
            {
                session.StateChanged += OnStateChanged;
                SetPlaying(IsGameState(session.State));
            }

            if (operatorCanvas == null)
            {
                var go = GameObject.Find(OperatorCanvasName);
                operatorCanvas = go != null ? go.GetComponent<Canvas>() : null;
                if (operatorCanvas != null)
                {
                    operatorCanvas.enabled = !playing;
                }
            }

            if (!rigPrepared && origin != null && cam != null)
            {
                PrepareRig();
            }

            if (!playing || cam == null)
            {
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                LockCursor(false);
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (!locked)
            {
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    LockCursor(true);
                }

                SetHint(HintUnlocked);
                return;
            }

            Look(mouse);
            Walk(keyboard);
            UpdateAim();

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                if (held != null)
                {
                    Drop();
                }
                else if (aimed != null && session.State == SessionState.Playing)
                {
                    Pick(aimed);
                }
            }
            else if (mouse != null && mouse.rightButton.wasPressedThisFrame && held != null)
            {
                // Drop where it is; the item falls / snaps home like a released VR item.
                Drop();
            }

            if (held != null && held.State != ProductItemState.Held)
            {
                ForgetHeld();
            }

            SetHint(held != null ? HintHeld : aimed != null ? string.Format(HintAim, aimed.Definition != null ? aimed.Definition.DisplayName : "") : HintIdle);
        }

        private void LateUpdate()
        {
            if (!playing || cam == null || origin == null)
            {
                return;
            }

            cam.transform.SetPositionAndRotation(
                origin.transform.position + Vector3.up * eyeHeight,
                Quaternion.Euler(pitch, yaw, 0f));
            if (hand != null)
            {
                hand.transform.SetPositionAndRotation(
                    cam.transform.position + cam.transform.forward * holdDistance - cam.transform.up * holdDrop,
                    Quaternion.Euler(0f, yaw, 0f));
            }
        }

        // ----- state -----

        private static bool IsGameState(SessionState state)
        {
            return state == SessionState.Countdown || state == SessionState.Playing;
        }

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (next == SessionState.Countdown && origin != null)
            {
                yaw = origin.transform.eulerAngles.y;
                pitch = 8f;
            }

            if (next != SessionState.Playing && held != null)
            {
                Drop();
            }

            SetPlaying(IsGameState(next));
        }

        private void SetPlaying(bool on)
        {
            playing = on;
            if (operatorCanvas != null)
            {
                operatorCanvas.enabled = !on;
            }

            if (hud != null)
            {
                hud.SetActive(on);
            }

            LockCursor(on);
        }

        private static void LockCursor(bool on)
        {
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        // ----- rig -----

        private void PrepareRig()
        {
            rigPrepared = true;

            // No headset: the camera is driven here, not by the tracked pose.
            foreach (var behaviour in cam.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().Name == "TrackedPoseDriver")
                {
                    behaviour.enabled = false;
                }
            }

            // Untracked controllers / hands would float at the floor: hide them.
            var offset = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform;
            for (int i = 0; i < offset.childCount; i++)
            {
                var child = offset.GetChild(i);
                if (child != cam.transform && !cam.transform.IsChildOf(child))
                {
                    child.gameObject.SetActive(false);
                }
            }

            yaw = origin.transform.eulerAngles.y;
            pitch = 8f;

            var handGo = new GameObject("Desktop hand");
            handGo.SetActive(false);
            var trigger = handGo.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.01f;
            hand = handGo.AddComponent<XRDirectInteractor>();
            hand.interactionManager = manager;
            handGo.SetActive(true);
        }

        private void Look(Mouse mouse)
        {
            if (mouse == null)
            {
                return;
            }

            var delta = mouse.delta.ReadValue() * lookSensitivity;
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -80f, 85f);
        }

        private void Walk(Keyboard keyboard)
        {
            if (keyboard == null)
            {
                return;
            }

            var input = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
            if (input == Vector2.zero)
            {
                return;
            }

            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var move = rotation * new Vector3(input.x, 0f, input.y).normalized * (walkSpeed * Time.deltaTime);
            var position = origin.transform.position;
            var chest = position + Vector3.up * 1.0f;

            // Simple capsule sweep against static geometry (walls, furniture); products and triggers are ignored.
            float distance = move.magnitude;
            var direction = move / distance;
            if (Physics.SphereCast(chest, bodyRadius, direction, out var hit, distance + 0.02f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<ProductItem>() == null)
            {
                // Slide along the obstacle.
                move = Vector3.ProjectOnPlane(move, hit.normal);
                move.y = 0f;
                if (move.sqrMagnitude < 1e-8f ||
                    Physics.SphereCast(chest, bodyRadius, move.normalized, out var hit2, move.magnitude + 0.02f, ~0, QueryTriggerInteraction.Ignore) &&
                    hit2.collider.GetComponentInParent<ProductItem>() == null)
                {
                    return;
                }
            }

            origin.transform.position = position + move;
        }

        // ----- grabbing -----

        private void UpdateAim()
        {
            aimed = null;
            if (held != null || session == null || session.State != SessionState.Playing)
            {
                SetCrosshair(false);
                return;
            }

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Physics.SphereCast(ray, 0.04f, out var hit, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                var item = hit.collider.GetComponentInParent<ProductItem>();
                if (item != null && item.isActiveAndEnabled && (item.State == ProductItemState.Free || item.State == ProductItemState.Placed) && !item.IsReturning)
                {
                    aimed = item;
                }
            }

            SetCrosshair(aimed != null);
        }

        private void Pick(ProductItem item)
        {
            if (manager == null || hand == null || item.Grab == null)
            {
                return;
            }

            LateUpdate(); // hand at its pose before the attach
            manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)item.Grab);
            if (item.State != ProductItemState.Held)
            {
                return;
            }

            held = item;
        }

        private void Drop()
        {
            var item = held;
            ForgetHeld();
            if (item != null && manager != null && hand != null && item.Grab != null && item.Grab.isSelected)
            {
                manager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)item.Grab);
            }
        }

        private void ForgetHeld()
        {
            held = null;
        }

        // ----- HUD -----

        private void BuildHud()
        {
            hud = new GameObject("Desktop HUD");
            var canvas = hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = hud.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var dot = new GameObject("Crosshair", typeof(RectTransform)).AddComponent<Image>();
            dot.transform.SetParent(hud.transform, false);
            dot.rectTransform.sizeDelta = new Vector2(10f, 10f);
            dot.raycastTarget = false;
            crosshair = dot;

            var hintBack = new GameObject("Hint", typeof(RectTransform)).AddComponent<Image>();
            hintBack.transform.SetParent(hud.transform, false);
            hintBack.color = new Color(0.03f, 0.16f, 0.39f, 0.82f);
            hintBack.raycastTarget = false;
            var backRect = hintBack.rectTransform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = new Vector2(0f, 36f);
            backRect.sizeDelta = new Vector2(1180f, 64f);

            hintText = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            hintText.transform.SetParent(backRect, false);
            if (VrUiStyle.Font != null)
            {
                hintText.font = VrUiStyle.Font;
            }

            hintText.fontSize = 28f;
            hintText.color = Color.white;
            hintText.alignment = TextAlignmentOptions.Center;
            hintText.raycastTarget = false;
            hintText.richText = true;
            var textRect = hintText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            SetCrosshair(false);
        }

        private void SetCrosshair(bool onItem)
        {
            if (crosshair == null)
            {
                return;
            }

            crosshair.color = onItem ? VrUiStyle.Orange : new Color(1f, 1f, 1f, 0.8f);
            crosshair.rectTransform.sizeDelta = onItem ? new Vector2(18f, 18f) : new Vector2(8f, 8f);
        }

        private void SetHint(string text)
        {
            if (hintText != null && hintText.text != text)
            {
                hintText.text = text;
            }
        }
    }
}
