using System;
using System.Collections.Generic;
using MultiTravel.Core.Session;

namespace MultiTravel.Core.Products
{
    /// <summary>
    /// The items available for one gender variant (ARCHITECTURE.md §2.4), produced by <see cref="ProductSetResolver"/>.
    /// Immutable; <see cref="RequiredIds"/> is a set for O(1) membership checks in <c>CompletionEvaluator</c>.
    /// </summary>
    public sealed class ProductSet
    {
        private readonly HashSet<string> requiredIds;

        public ProductSet(Gender gender, IReadOnlyList<ProductDefinition> items, IReadOnlyList<ProductDefinition> required)
        {
            Gender = gender;
            Items = items ?? Array.Empty<ProductDefinition>();
            Required = required ?? Array.Empty<ProductDefinition>();

            requiredIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Required.Count; i++)
            {
                var definition = Required[i];
                if (definition != null && !string.IsNullOrEmpty(definition.Id))
                {
                    requiredIds.Add(definition.Id);
                }
            }
        }

        /// <summary>An empty set (used before a gender is selected).</summary>
        public static ProductSet Empty(Gender gender)
        {
            return new ProductSet(gender, Array.Empty<ProductDefinition>(), Array.Empty<ProductDefinition>());
        }

        public Gender Gender { get; }

        /// <summary>Every product available for the gender (correct and incorrect ones).</summary>
        public IReadOnlyList<ProductDefinition> Items { get; }

        /// <summary>Products with <c>IsCorrect &amp;&amp; IsRequiredForCompletion</c>.</summary>
        public IReadOnlyList<ProductDefinition> Required { get; }

        /// <summary>Ids of the required products.</summary>
        public IReadOnlyCollection<string> RequiredIds => requiredIds;

        /// <summary>True when the id belongs to a required product.</summary>
        public bool IsRequired(string productId)
        {
            return productId != null && requiredIds.Contains(productId);
        }

        /// <summary>Finds an item by id, or null.</summary>
        public ProductDefinition Find(string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return null;
            }

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (item != null && string.Equals(item.Id, productId, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            return null;
        }
    }
}
