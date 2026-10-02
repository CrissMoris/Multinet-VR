using System.Collections.Generic;
using MultiTravel.Core.Config;
using MultiTravel.Gameplay.Common;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace MultiTravel.Gameplay.Xr
{
    /// <summary>
    /// Adapts the template rig "Complete XR Origin Set Up Hands Variant" (STACK_NOTES §1) to the booth
    /// (ARCHITECTURE.md §2.8): when <c>Gameplay.EnableLocomotion</c> is false every <see cref="LocomotionProvider"/>
    /// (move, snap / continuous turn, teleport, gravity, climb, jump, grab-move) is disabled, the <c>Locomotion</c> child
    /// is deactivated, the "Teleport Interactor" rays are turned off and the sample <c>ControllerInputActionManager</c>
    /// (which would re-activate the teleport ray and hide the Near-Far interactor on thumbstick input) is disabled.
    /// Hands, controllers, Near-Far (grab + UI ray) and poke interactors stay untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrRigController : MonoBehaviour
    {
        private const string LocomotionChildName = "Locomotion";
        private const string TeleportInteractorName = "Teleport Interactor";
        private const string TutorialCalloutPrefix = "Affordance Callouts";

        [SerializeField]
        [Tooltip("Hide the VR template's tutorial tooltips (Grab / Move / UI Press callouts) on the controllers.")]
        private bool hideTutorialCallouts = true;

        [SerializeField]
        [Tooltip("Products must be reached and grabbed with the hand/controller (near casting only). Disables the template's " +
                 "far (ray) grabbing on every Near-Far interactor.")]
        private bool requirePhysicalReach = true;

        /// <summary>True once far (ray) grabbing has been disabled on the rig's Near-Far interactors.</summary>
        public bool FarGrabDisabled { get; private set; }
        private const string ControllerInputActionManagerTypeName = "ControllerInputActionManager";
        private const string SampleHandVisualizerName = "Hand Visualizer";

        [SerializeField]
        [Tooltip("The XR Origin of the template rig. Found in the scene when empty.")]
        private XROrigin xrOrigin;

        [SerializeField]
        [Tooltip("Disable the rig-root XR Hands sample 'Hand Visualizer' (redundant with the Quest hand visuals).")]
        private bool disableSampleHandVisualizer = true;

        private readonly List<LocomotionProvider> providers = new List<LocomotionProvider>();
        private readonly List<XRRayInteractor> rays = new List<XRRayInteractor>();
        private readonly List<MonoBehaviour> behaviours = new List<MonoBehaviour>();
        private RuntimeConfig config;

        /// <summary>The rig in use (null when none was found).</summary>
        public XROrigin Origin => xrOrigin;

        /// <summary>Locomotion state applied last.</summary>
        public bool LocomotionEnabled { get; private set; } = true;

        /// <summary>Main camera of the rig (null when no rig).</summary>
        public Camera RigCamera => xrOrigin != null ? xrOrigin.Camera : null;

        /// <summary>Generator API.</summary>
        public void SetOrigin(XROrigin origin)
        {
            xrOrigin = origin;
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
                foreach (var nearFar in xrOrigin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(true))
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
                    if (t.name.StartsWith(TutorialCalloutPrefix, System.StringComparison.Ordinal))
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

            bool enableLocomotion = false;
            if (ServiceResolver.Resolve(ref config, this, nameof(XrRigController)))
            {
                enableLocomotion = config.Gameplay.EnableLocomotion;
            }

            ApplyLocomotion(enableLocomotion);
        }
    }
}
