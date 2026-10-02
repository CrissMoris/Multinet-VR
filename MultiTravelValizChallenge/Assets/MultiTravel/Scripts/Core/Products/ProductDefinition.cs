using UnityEngine;

namespace MultiTravel.Core.Products
{
    /// <summary>
    /// One product asset under <c>Assets/MultiTravel/Data/Products/</c> (ARCHITECTURE.md §2.4).
    /// Fields are public so the editor data generator and inspectors can set them directly.
    /// <para>
    /// <see cref="IsRequiredForCompletion"/> follows <see cref="IsCorrect"/> until it is explicitly set
    /// (by hand in the inspector, or through <see cref="SetRequiredForCompletion"/>). The serialised
    /// <c>requiredExplicitlySet</c> flag records that an explicit choice was made.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "Product", menuName = "MultiTravel/Product Definition")]
    public sealed class ProductDefinition : ScriptableObject
    {
        [Tooltip("Unique kebab-case identifier (e.g. 'laptop'). Sent to the backend in placed_product_ids.")]
        public string Id;

        [Tooltip("Turkish display name shown in the UI.")]
        public string DisplayName;

        public ProductCategory Category = ProductCategory.Other;

        public GenderAvailability Availability = GenderAvailability.Both;

        [Tooltip("True when the product belongs in the suitcase for the scenario.")]
        public bool IsCorrect;

        [Tooltip("Score applied when placed. 0 = use the catalog default for correct / incorrect products.")]
        public int ScoreOverride;

        [Tooltip("Only meaningful when IsCorrect: the game cannot auto-complete until this product is placed. Defaults to IsCorrect.")]
        public bool IsRequiredForCompletion;

        [SerializeField, HideInInspector]
        private bool requiredExplicitlySet;

        [SerializeField, HideInInspector]
        private bool lastAutoRequired;

        [Tooltip("Prefab with a ProductItem component on its root.")]
        public GameObject VisualPrefab;

        [Tooltip("Optional brand sprite shown on the item or the HUD.")]
        public Sprite BrandSprite;

        public ProductInteractionSettings Interaction = new ProductInteractionSettings();

        /// <summary>True when <see cref="IsRequiredForCompletion"/> was set explicitly and no longer follows <see cref="IsCorrect"/>.</summary>
        public bool RequiredExplicitlySet => requiredExplicitlySet;

        /// <summary><c>IsCorrect &amp;&amp; IsRequiredForCompletion</c> — the value used by <see cref="ProductSetResolver"/>.</summary>
        public bool IsEffectivelyRequired => IsCorrect && IsRequiredForCompletion;

        /// <summary>Sets the required flag explicitly so it stops following <see cref="IsCorrect"/>.</summary>
        public void SetRequiredForCompletion(bool required)
        {
            IsRequiredForCompletion = required;
            requiredExplicitlySet = true;
            lastAutoRequired = required;
        }

        /// <summary>Returns to the default behaviour (required follows <see cref="IsCorrect"/>).</summary>
        public void ClearRequiredOverride()
        {
            requiredExplicitlySet = false;
            IsRequiredForCompletion = IsCorrect;
            lastAutoRequired = IsCorrect;
        }

        /// <summary>
        /// Keeps the required default in sync. Called by Unity in the editor (OnValidate) and usable by tooling.
        /// A manual change of <see cref="IsRequiredForCompletion"/> that diverges from the last automatic value marks it explicit.
        /// </summary>
        public void SyncRequiredDefault()
        {
            if (requiredExplicitlySet)
            {
                return;
            }

            if (IsRequiredForCompletion != lastAutoRequired)
            {
                requiredExplicitlySet = true;
                lastAutoRequired = IsRequiredForCompletion;
                return;
            }

            IsRequiredForCompletion = IsCorrect;
            lastAutoRequired = IsCorrect;
        }

        private void OnValidate()
        {
            SyncRequiredDefault();
            if (Interaction == null)
            {
                Interaction = new ProductInteractionSettings();
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Id) ? base.ToString() : Id;
        }
    }
}
