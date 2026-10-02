using System.Collections;
using MultiTravel.Gameplay.Items;
using UnityEngine;

namespace MultiTravel.Gameplay.Suitcase
{
    /// <summary>
    /// One placement position inside the suitcase. Tracks its occupant and can pulse an optional marker renderer
    /// through a <see cref="MaterialPropertyBlock"/> (no material instances are created).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SuitcaseSlot : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField]
        [Tooltip("Optional marker renderer that pulses when an item lands in this slot.")]
        private Renderer highlightRenderer;

        [SerializeField]
        [Tooltip("Gap between the slot surface and the item's lowest point (metres).")]
        [Min(0f)]
        private float heightOffset = 0.005f;

        [SerializeField]
        [Tooltip("Duration of the highlight pulse in seconds (unscaled).")]
        [Min(0.05f)]
        private float pulseSeconds = 0.6f;

        [SerializeField]
        [Tooltip("Emission intensity at the peak of the pulse (used when the material has emission enabled).")]
        [Min(0f)]
        private float emissionIntensity = 2f;

        [SerializeField]
        [Tooltip("Slot directly beneath this one in the same packing column (null = bottom of the column / standalone slot).")]
        private SuitcaseSlot below;

        [SerializeField]
        [Tooltip("Maximum height an item below adds to this slot's surface (soft garments compress when packed).")]
        [Min(0.001f)]
        private float maxStackStep = 0.05f;

        private MaterialPropertyBlock block;
        private Coroutine pulseRoutine;
        private Color restColor = Color.white;
        private int colorPropertyId = -1;
        private bool hasEmission;

        /// <summary>True while an item occupies the slot.</summary>
        public bool IsOccupied => Occupant != null;

        /// <summary>Slot beneath this one in its packing column (null for the bottom slot).</summary>
        public SuitcaseSlot Below => below;

        /// <summary>Slot above this one in its packing column (null for the top slot). Set by <see cref="SetBelow"/>.</summary>
        public SuitcaseSlot Above { get; private set; }

        /// <summary>Free and resting on something: the bottom slot of a column, or the slot right above the column's top item.</summary>
        public bool IsAvailable => !IsOccupied && (below == null || below.IsOccupied);

        /// <summary>Lowest slot of this slot's column.</summary>
        public SuitcaseSlot ColumnBottom
        {
            get
            {
                var s = this;
                int guard = 0;
                while (s.below != null && guard++ < 256)
                {
                    s = s.below;
                }

                return s;
            }
        }

        /// <summary>Generator API: links this slot on top of <paramref name="slot"/> in a packing column.</summary>
        public void SetBelow(SuitcaseSlot slot)
        {
            below = slot;
            if (slot != null)
            {
                slot.Above = this;
            }
        }

        /// <summary>Generator API: cap for the surface rise contributed by the item beneath.</summary>
        public void SetMaxStackStep(float step)
        {
            maxStackStep = Mathf.Max(0.001f, step);
        }

        /// <summary>
        /// World point the slot's item rests on: the slot origin for the bottom slot, otherwise the top of the item beneath
        /// (its height capped at maxStackStep so soft items read as compressed).
        /// </summary>
        public Vector3 SurfacePoint
        {
            get
            {
                if (below == null)
                {
                    return transform.position;
                }

                var basePoint = below.SurfacePoint;
                float rise = below.heightOffset + Mathf.Min(maxStackStep, ItemPlacementMath.ItemHeight(below.Occupant));
                return new Vector3(transform.position.x, basePoint.y, transform.position.z) + transform.up * rise;
            }
        }

        private void Awake()
        {
            if (below != null)
            {
                below.Above = this;
            }
        }

        /// <summary>The item in the slot, or null.</summary>
        public ProductItem Occupant { get; private set; }

        /// <summary>Generator API: assigns the marker renderer.</summary>
        public void SetHighlightRenderer(Renderer target)
        {
            highlightRenderer = target;
            colorPropertyId = -1;
        }

        /// <summary>World pose of <paramref name="item"/> resting in this slot.</summary>
        public Pose PoseFor(ProductItem item)
        {
            if (below == null)
            {
                return ItemPlacementMath.PoseOnSurface(transform, item, heightOffset);
            }

            return ItemPlacementMath.PoseOnSurface(SurfacePoint, transform.rotation, item, heightOffset);
        }

        /// <summary>Marks the slot occupied by <paramref name="item"/>.</summary>
        public void Occupy(ProductItem item)
        {
            Occupant = item;
        }

        /// <summary>Frees the slot.</summary>
        public void Release()
        {
            Occupant = null;
        }

        /// <summary>Pulses the marker towards <paramref name="color"/> and back.</summary>
        public void Pulse(Color color)
        {
            if (highlightRenderer == null || !isActiveAndEnabled)
            {
                return;
            }

            CacheMaterialInfo();
            if (colorPropertyId < 0 && !hasEmission)
            {
                return;
            }

            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
            }

            pulseRoutine = StartCoroutine(PulseRoutine(color));
        }

        /// <summary>Stops the pulse and restores the marker.</summary>
        public void ResetVisual()
        {
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }

            if (highlightRenderer != null && block != null)
            {
                highlightRenderer.SetPropertyBlock(null);
            }
        }

        private IEnumerator PulseRoutine(Color color)
        {
            float t = 0f;
            while (t < pulseSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(t / pulseSeconds) * Mathf.PI);
                Apply(Color.Lerp(restColor, color, k), color * (k * emissionIntensity));
                yield return null;
            }

            highlightRenderer.SetPropertyBlock(null);
            pulseRoutine = null;
        }

        private void Apply(Color baseColor, Color emission)
        {
            highlightRenderer.GetPropertyBlock(block);
            if (colorPropertyId >= 0)
            {
                block.SetColor(colorPropertyId, baseColor);
            }

            if (hasEmission)
            {
                block.SetColor(EmissionColorId, emission);
            }

            highlightRenderer.SetPropertyBlock(block);
        }

        private void CacheMaterialInfo()
        {
            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            if (colorPropertyId >= 0 || hasEmission)
            {
                return;
            }

            var material = highlightRenderer.sharedMaterial;
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(BaseColorId))
            {
                colorPropertyId = BaseColorId;
            }
            else if (material.HasProperty(ColorId))
            {
                colorPropertyId = ColorId;
            }

            if (colorPropertyId >= 0)
            {
                restColor = material.GetColor(colorPropertyId);
            }

            hasEmission = material.HasProperty(EmissionColorId);
        }

        private void OnDisable()
        {
            pulseRoutine = null;
            if (highlightRenderer != null)
            {
                highlightRenderer.SetPropertyBlock(null);
            }
        }
    }
}
