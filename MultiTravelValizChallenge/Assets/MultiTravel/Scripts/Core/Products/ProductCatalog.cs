using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MultiTravel.Core.Session;
using UnityEngine;

namespace MultiTravel.Core.Products
{
    /// <summary>
    /// The product catalog asset (<c>Assets/MultiTravel/Data/ProductCatalog.asset</c>, ARCHITECTURE.md §2.4).
    /// Holds scenario text, the product list, default scores and spawn-shuffle policy.
    /// Fields are public so the editor data generator can populate them directly.
    /// </summary>
    [CreateAssetMenu(fileName = "ProductCatalog", menuName = "MultiTravel/Product Catalog")]
    public sealed class ProductCatalog : ScriptableObject
    {
        private static readonly Regex KebabCase = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

        [Tooltip("Scenario title shown in the UI (Turkish).")]
        public string ScenarioTitle = "Arabayla, 1 gece konaklamalı toplantı seyahati";

        public List<ProductDefinition> Products = new List<ProductDefinition>();

        [Tooltip("Score for a correct product without an override. Must be positive.")]
        public int DefaultPositiveScore = 10;

        [Tooltip("Score for an incorrect product without an override. Must be negative.")]
        public int DefaultNegativeScore = -5;

        [Tooltip("Shuffle the spawn slots of the items every session.")]
        public bool ShuffleSpawnPositions = true;

        public ShuffleSeedMode ShuffleSeedMode = ShuffleSeedMode.PerSession;

        [Tooltip("Seed used when ShuffleSeedMode is Fixed.")]
        public int FixedShuffleSeed = 1;

        /// <summary>Score applied when the product is placed: its override, or the catalog default by <c>IsCorrect</c>.</summary>
        public int ScoreFor(ProductDefinition product)
        {
            if (product == null)
            {
                throw new ArgumentNullException(nameof(product));
            }

            if (product.ScoreOverride != 0)
            {
                return product.ScoreOverride;
            }

            return product.IsCorrect ? DefaultPositiveScore : DefaultNegativeScore;
        }

        /// <summary>Products available for the gender (allocates; not for per-frame use).</summary>
        public IReadOnlyList<ProductDefinition> ForGender(Gender gender)
        {
            var result = new List<ProductDefinition>();
            if (Products == null)
            {
                return result;
            }

            for (int i = 0; i < Products.Count; i++)
            {
                var product = Products[i];
                if (product != null && product.Availability.Includes(gender))
                {
                    result.Add(product);
                }
            }

            return result;
        }

        /// <summary>Finds a product by id (ordinal), or null.</summary>
        public ProductDefinition FindById(string id)
        {
            if (string.IsNullOrEmpty(id) || Products == null)
            {
                return null;
            }

            for (int i = 0; i < Products.Count; i++)
            {
                var product = Products[i];
                if (product != null && string.Equals(product.Id, id, StringComparison.Ordinal))
                {
                    return product;
                }
            }

            return null;
        }

        /// <summary>
        /// Validates the catalog and appends developer-facing messages to <paramref name="errors"/>.
        /// Checks: empty list, null entries, empty / non-kebab-case / duplicate ids, missing display names,
        /// missing prefabs, unavailable products, default score signs, and that every gender has at least one required product.
        /// Returns true when no error was added.
        /// </summary>
        public bool Validate(List<string> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            int before = errors.Count;

            if (Products == null || Products.Count == 0)
            {
                errors.Add("Catalog has no products.");
                return false;
            }

            if (DefaultPositiveScore <= 0)
            {
                errors.Add($"DefaultPositiveScore must be positive (is {DefaultPositiveScore}).");
            }

            if (DefaultNegativeScore >= 0)
            {
                errors.Add($"DefaultNegativeScore must be negative (is {DefaultNegativeScore}).");
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            int requiredFemale = 0;
            int requiredMale = 0;

            for (int i = 0; i < Products.Count; i++)
            {
                var product = Products[i];
                if (product == null)
                {
                    errors.Add($"Products[{i}] is null.");
                    continue;
                }

                var label = string.IsNullOrEmpty(product.Id) ? $"Products[{i}] ({product.name})" : product.Id;

                if (string.IsNullOrWhiteSpace(product.Id))
                {
                    errors.Add($"{label}: Id is empty.");
                }
                else
                {
                    if (!KebabCase.IsMatch(product.Id))
                    {
                        errors.Add($"{label}: Id must be kebab-case (lowercase letters, digits, single dashes).");
                    }

                    if (!seenIds.Add(product.Id))
                    {
                        errors.Add($"{label}: duplicate Id.");
                    }
                }

                if (string.IsNullOrWhiteSpace(product.DisplayName))
                {
                    errors.Add($"{label}: DisplayName is empty.");
                }

                if (product.VisualPrefab == null)
                {
                    errors.Add($"{label}: VisualPrefab is missing.");
                }

                if (product.Availability == GenderAvailability.None)
                {
                    errors.Add($"{label}: Availability is None; the product never appears.");
                }

                if (product.IsEffectivelyRequired)
                {
                    if (product.Availability.Includes(Gender.Female))
                    {
                        requiredFemale++;
                    }

                    if (product.Availability.Includes(Gender.Male))
                    {
                        requiredMale++;
                    }
                }
            }

            if (requiredFemale == 0)
            {
                errors.Add("No required product is available for gender Female; the game could never auto-complete.");
            }

            if (requiredMale == 0)
            {
                errors.Add("No required product is available for gender Male; the game could never auto-complete.");
            }

            return errors.Count == before;
        }
    }
}
