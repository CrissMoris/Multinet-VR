using System;
using System.Collections.Generic;
using System.IO;
using MultiTravel.Core.Config;
using MultiTravel.Core.Services;
using MultiTravel.Core.Xr;
using MultiTravel.Gameplay.Common;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;

namespace MultiTravel.Gameplay.Xr
{
    /// <summary>
    /// Adapts the template rig "Complete XR Origin Set Up Hands Variant" (STACK_NOTES §1) to the booth
    /// (ARCHITECTURE.md §2.8, OVERHAUL_PLAN §5): when <c>Gameplay.EnableLocomotion</c> is false every
    /// <see cref="LocomotionProvider"/> (move, snap / continuous turn, teleport, gravity, climb, jump, grab-move) is
    /// disabled, the <c>Locomotion</c> child is deactivated, the "Teleport Interactor" rays are turned off, the sample
    /// <c>ControllerInputActionManager</c> is disabled and the template's tunnelling vignette is removed. Hands,
    /// controllers, Near-Far (grab + UI ray) and poke interactors stay untouched.
    /// <para>
    /// Implements <see cref="IXrRigControl"/> for the operator screen (registered in <see cref="AppServices"/>):
    /// <see cref="Recenter"/> moves / rotates the XR Origin so the headset stands over the floor mark facing +Z, and
    /// <see cref="AdjustFloorOffset"/> raises / lowers the virtual floor in 0.02 m steps within ±0.12 m through the
    /// origin's camera-offset object. The offset is persisted per station in <c>persistentDataPath/rig.json</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrRigController : MonoBehaviour, IXrRigControl
    {
        /// <summary>File name of the persisted rig settings.</summary>
        public const string SettingsFileName = "rig.json";

        /// <summary>Floor offset step (metres).</summary>
        public const float FloorStep = 0.02f;

        /// <summary>Floor offset limit (metres, ±).</summary>
        public const float FloorLimit = 0.12f;

        private const string LocomotionChildName = "Locomotion";
        private const string TeleportInteractorName = "Teleport Interactor";
        private const string TutorialCalloutPrefix = "Affordance Callouts";
        private const string ControllerInputActionManagerTypeName = "ControllerInputActionManager";
        private const string SampleHandVisualizerName = "Hand Visualizer";

        [Serializable]
        private sealed class RigSettings
        {
            public float floorOffset;
        }

        [SerializeField]
        [Tooltip("Hide the VR template's tutorial tooltips (Grab / Move / UI Press callouts) on the controllers.")]
        private bool hideTutorialCallouts = true;

        [SerializeField]
        [Tooltip("Products must be reached and grabbed with the hand/controller (near casting only). Disables the template's " +
                 "far (ray) grabbing on every Near-Far interactor.")]
        private bool requirePhysicalReach = true;

        [SerializeField]
        [Tooltip("The XR Origin of the template rig. Found in the scene when empty.")]
        private XROrigin xrOrigin;

        [SerializeField]
        [Tooltip("Disable the rig-root XR Hands sample 'Hand Visualizer' (redundant with the Quest hand visuals).")]
        private bool disableSampleHandVisualizer = true;

        [SerializeField]
        [Tooltip("Disable the template's tunnelling vignette (comfort effect for locomotion; there is none).")]
        private bool disableTunnelingVignette = true;

        [SerializeField]
        [Tooltip("Floor mark the participant is recentred onto (position + forward). Origin facing +Z when empty.")]
        private Transform recenterTarget;

        [SerializeField]
        [Tooltip("Load / save the floor offset in persistentDataPath/rig.json.")]
        private bool persistSettings = true;

        private readonly List<LocomotionProvider> providers = new List<LocomotionProvider>();
        private readonly List<XRRayInteractor> rays = new List<XRRayInteractor>();
        private readonly List<MonoBehaviour> behaviours = new List<MonoBehaviour>();
        private readonly List<TunnelingVignetteController> vignettes = new List<TunnelingVignetteController>();
        private RuntimeConfig config;
        private float floorOffset;
        private bool settingsLoaded;

        /// <summary>Test seam: full path of the settings file (null = persistentDataPath/rig.json). Never set by the app.</summary>
        public static string SettingsPathOverride { get; set; }

        /// <summary>True once far (ray) grabbing has been disabled on the rig's Near-Far interactors.</summary>
        public bool FarGrabDisabled { get; private set; }

        /// <summary>True once the tunnelling vignette was found and disabled.</summary>
        public bool TunnelingVignetteDisabled { get; private set; }

        /// <summary>The rig in use (null when none was found).</summary>
        public XROrigin Origin => xrOrigin;

        /// <summary>Locomotion state applied last.</summary>
        public bool LocomotionEnabled { get; private set; } = true;

        /// <summary>Main camera of the rig (null when no rig).</summary>
        public Camera RigCamera => xrOrigin != null ? xrOrigin.Camera : null;

        /// <summary>Number of recenters performed (diagnostics).</summary>
        public int RecenterCount { get; private set; }

        /// <inheritdoc />
        public float FloorOffset => floorOffset;

        /// <summary>Raised after <see cref="AdjustFloorOffset"/> changed the offset.</summary>
        public event Action<float> FloorOffsetChanged;

        /// <summary>Raised after <see cref="Recenter"/>.</summary>
        public event Action Recentered;

        /// <summary>Generator API.</summary>
        public void SetOrigin(XROrigin origin)
        {
            xrOrigin = origin;
        }

        /// <summary>Generator API: floor mark used by <see cref="Recenter"/>.</summary>
        public void SetRecenterTarget(Transform target)
        {
            recenterTarget = target;
        }

        /// <inheritdoc />
        public void Recenter()
        {
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "XrRigController.Recenter ignored: no XR Origin / camera.", this);
                return;
            }

            var forward = recenterTarget != null ? recenterTarget.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.forward;
            }

            var mark = recenterTarget != null ? recenterTarget.position : Vector3.zero;
            xrOrigin.MatchOriginUpCameraForward(Vector3.up, forward.normalized);
            var cameraPosition = xrOrigin.Camera.transform.position;
            xrOrigin.MoveCameraToWorldLocation(new Vector3(mark.x, cameraPosition.y, mark.z));
            RecenterCount++;
            Recentered?.Invoke();
        }

        /// <inheritdoc />
        public void AdjustFloorOffset(float deltaMetres)
        {
            SetFloorOffset(floorOffset + deltaMetres);
        }

        /// <summary>Sets the floor offset directly (quantised to <see cref="FloorStep"/>, clamped to ±<see cref="FloorLimit"/>).</summary>
        public void SetFloorOffset(float metres)
        {
            float quantised = Mathf.Round(metres / FloorStep) * FloorStep;
            float clamped = Mathf.Clamp(quantised, -FloorLimit, FloorLimit);
            if (Mathf.Abs(clamped) < 1e-5f)
            {
                clamped = 0f;
            }

            if (Mathf.Approximately(clamped, floorOffset))
            {
                return;
            }

            floorOffset = clamped;
            ApplyFloorOffset();
            Save();
            FloorOffsetChanged?.Invoke(floorOffset);
        }

        /// <summary>Enables or disables every locomotion feature of the rig.</summary>
        public void ApplyLocomotion(bool enableLocomotion)
        {
            LocomotionEnabled = enableLocomotion;
            if (xrOrigin == null)
            {
                return;
            }

            var root = xrOrigin.gameObject;

            root.GetComponentsInChildren(true, providers);
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i] != null)
                {
                    providers[i].enabled = enableLocomotion;
                }
            }

            var locomotionRoot = root.transform.Find(LocomotionChildName);
            if (locomotionRoot != null)
            {
                locomotionRoot.gameObject.SetActive(enableLocomotion);
            }

            root.GetComponentsInChildren(true, behaviours);
            for (int i = 0; i < behaviours.Count; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().Name == ControllerInputActionManagerTypeName)
                {
                    behaviour.enabled = enableLocomotion;
                }
            }

            root.GetComponentsInChildren(true, rays);
            for (int i = 0; i < rays.Count; i++)
            {
                var ray = rays[i];
                if (ray != null && ray.gameObject.name == TeleportInteractorName)
                {
                    ray.enabled = enableLocomotion;
                    ray.gameObject.SetActive(false);
                }
            }

            providers.Clear();
            behaviours.Clear();
            rays.Clear();
        }

        // ----- Unity -----

        private void Awake()
        {
            AppServices.Register<IXrRigControl>(this);
            Load();
        }

        private void Start()
        {
            if (xrOrigin == null)
            {
                xrOrigin = FindAnyObjectByType<XROrigin>();
            }

            if (xrOrigin == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "XrRigController: no XROrigin in the scene. Add the template rig (Complete XR Origin Set Up Hands Variant).", this);
                return;
            }

            if (requirePhysicalReach)
            {
                foreach (var nearFar in xrOrigin.GetComponentsInChildren<NearFarInteractor>(true))
                {
                    nearFar.enableFarCasting = false;
                }

                FarGrabDisabled = true;
            }

            if (hideTutorialCallouts)
            {
                // The VR template's "Grab / Move / UI Press" tooltips are onboarding content, not event content.
                foreach (var t in xrOrigin.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith(TutorialCalloutPrefix, StringComparison.Ordinal))
                    {
                        t.gameObject.SetActive(false);
                    }
                }
            }

            if (disableSampleHandVisualizer)
            {
                var visualizer = xrOrigin.transform.Find(SampleHandVisualizerName);
                if (visualizer != null)
                {
                    visualizer.gameObject.SetActive(false);
                }
            }

            if (disableTunnelingVignette)
            {
                xrOrigin.GetComponentsInChildren(true, vignettes);
                for (int i = 0; i < vignettes.Count; i++)
                {
                    var vignette = vignettes[i];
                    if (vignette == null)
                    {
                        continue;
                    }

                    vignette.enabled = false;
                    vignette.gameObject.SetActive(false);
                    TunnelingVignetteDisabled = true;
                }

                vignettes.Clear();
            }

            bool enableLocomotion = false;
            if (ServiceResolver.Resolve(ref config, this, nameof(XrRigController)))
            {
                enableLocomotion = config.Gameplay.EnableLocomotion;
            }

            ApplyLocomotion(enableLocomotion);
            ApplyFloorOffset();
        }

        private void Update()
        {
            // XROrigin rewrites the offset object's height when the tracking origin mode changes; keep our correction on top.
            if (floorOffset != 0f)
            {
                ApplyFloorOffset();
            }
        }

        private void OnDestroy()
        {
            if (AppServices.TryGet(out IXrRigControl registered) && ReferenceEquals(registered, this))
            {
                AppServices.Unregister<IXrRigControl>();
            }
        }

        // ----- internals -----

        private void ApplyFloorOffset()
        {
            if (xrOrigin == null || xrOrigin.CameraFloorOffsetObject == null)
            {
                return;
            }

            float baseHeight = xrOrigin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor ? 0f : xrOrigin.CameraYOffset;
            // A positive correction raises the virtual floor relative to the player, i.e. the camera offset goes down.
            float expected = baseHeight - floorOffset;
            var offsetTransform = xrOrigin.CameraFloorOffsetObject.transform;
            var local = offsetTransform.localPosition;
            if (Mathf.Abs(local.y - expected) > 1e-4f)
            {
                local.y = expected;
                offsetTransform.localPosition = local;
            }
        }

        private static string SettingsPath()
        {
            return string.IsNullOrEmpty(SettingsPathOverride)
                ? Path.Combine(Application.persistentDataPath, SettingsFileName)
                : SettingsPathOverride;
        }

        private void Load()
        {
            if (settingsLoaded)
            {
                return;
            }

            settingsLoaded = true;
            if (!persistSettings)
            {
                return;
            }

            try
            {
                var path = SettingsPath();
                if (!File.Exists(path))
                {
                    return;
                }

                var settings = JsonUtility.FromJson<RigSettings>(File.ReadAllText(path));
                if (settings != null)
                {
                    floorOffset = Mathf.Clamp(Mathf.Round(settings.floorOffset / FloorStep) * FloorStep, -FloorLimit, FloorLimit);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "XrRigController: could not read " + SettingsFileName + ": " + ex.Message, this);
            }
        }

        private void Save()
        {
            if (!persistSettings)
            {
                return;
            }

            try
            {
                var path = SettingsPath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, JsonUtility.ToJson(new RigSettings { floorOffset = floorOffset }, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "XrRigController: could not write " + SettingsFileName + ": " + ex.Message, this);
            }
        }
    }
}
