using System.Collections.Generic;
using MultiTravel.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace MultiTravel.Gameplay.Xr
{
    /// <summary>
    /// Gives the tracked-hand visuals the event look (OVERHAUL_PLAN §5): a translucent navy body with a teal emissive
    /// glow and a glossy specular rim, built at runtime from <c>Universal Render Pipeline/Lit</c> (transparent +
    /// emission). Every <see cref="SkinnedMeshRenderer"/> under the rig's Left / Right Hand objects (and the renderer
    /// of each <see cref="XRHandMeshController"/>) receives one shared material; the XRI hand animation is untouched.
    /// A true fresnel needs a shader asset, so the art pipeline can supply one through <see cref="overrideMaterial"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandPresenceStyler : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Rig modality manager (found in the scene when empty).")]
        private XRInputModalityManager modalityManager;

        [SerializeField]
        [Tooltip("Optional authored material (e.g. a ShaderGraph fresnel). When empty a URP Lit material is built in code.")]
        private Material overrideMaterial;

        [SerializeField] private Color bodyColor = new Color(0.06f, 0.18f, 0.42f, 0.62f);
        [SerializeField] private Color glowColor = new Color(0.1f, 0.7f, 0.58f, 1f);

        [SerializeField]
        [Range(0f, 2f)]
        private float glowStrength = 0.35f;

        [SerializeField]
        [Range(0f, 1f)]
        private float smoothness = 0.88f;

        private readonly List<SkinnedMeshRenderer> skinned = new List<SkinnedMeshRenderer>();
        private readonly List<XRHandMeshController> meshControllers = new List<XRHandMeshController>();
        private Material material;
        private bool subscribed;

        /// <summary>The material applied to the hands.</summary>
        public Material HandMaterial => material;

        /// <summary>Number of renderers styled by the last apply.</summary>
        public int StyledRendererCount { get; private set; }

        /// <summary>Generator API.</summary>
        public void Configure(XRInputModalityManager modality, Material authoredMaterial)
        {
            modalityManager = modality;
            overrideMaterial = authoredMaterial;
        }

        /// <summary>Applies the material to every hand renderer found now (idempotent).</summary>
        public void Apply()
        {
            if (modalityManager == null)
            {
                modalityManager = FindAnyObjectByType<XRInputModalityManager>();
            }

            if (modalityManager == null)
            {
                return;
            }

            EnsureMaterial();
            StyledRendererCount = 0;
            Style(modalityManager.leftHand);
            Style(modalityManager.rightHand);
        }

        private void Start()
        {
            Apply();
            if (modalityManager != null && !subscribed)
            {
                modalityManager.trackedHandModeStarted.AddListener(Apply);
                subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (modalityManager != null && subscribed)
            {
                modalityManager.trackedHandModeStarted.RemoveListener(Apply);
            }

            subscribed = false;
            if (material != null && material != overrideMaterial)
            {
                Destroy(material);
            }
        }

        private void EnsureMaterial()
        {
            if (material != null)
            {
                return;
            }

            if (overrideMaterial != null)
            {
                material = overrideMaterial;
                return;
            }

            material = PresentationStyle.CreateMaterial("MT_HandPresence", bodyColor, glowColor * glowStrength, true, false, smoothness, 0.05f);
        }

        private void Style(GameObject handRoot)
        {
            if (handRoot == null)
            {
                return;
            }

            skinned.Clear();
            handRoot.GetComponentsInChildren(true, skinned);
            for (int i = 0; i < skinned.Count; i++)
            {
                Assign(skinned[i]);
            }

            meshControllers.Clear();
            handRoot.GetComponentsInChildren(true, meshControllers);
            for (int i = 0; i < meshControllers.Count; i++)
            {
                var renderer = meshControllers[i].handMeshRenderer;
                if (renderer != null && !(renderer is SkinnedMeshRenderer))
                {
                    Assign(renderer);
                }
            }

            skinned.Clear();
            meshControllers.Clear();
        }

        private void Assign(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != material)
                {
                    materials[i] = material;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }

            StyledRendererCount++;
        }
    }
}
