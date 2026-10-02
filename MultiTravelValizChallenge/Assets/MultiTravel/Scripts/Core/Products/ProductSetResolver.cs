using System;
using System.Collections.Generic;
using MultiTravel.Core.Session;

namespace MultiTravel.Core.Products
{
    /// <summary>Builds the <see cref="ProductSet"/> for a gender from a <see cref="ProductCatalog"/> (ARCHITECTURE.md §2.4).
    /// Stateless; registered in <c>AppServices</c> and called once per session by the gameplay director.</summary>
    public sealed class ProductSetResolver
    {
        public ProductSet Resolve(ProductCatalog catalog, Gender gender)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var items = new List<ProductDefinition>();
            var required = new List<ProductDefinition>();

            var products = catalog.Products;
            if (products != null)
            {
                for (int i = 0; i < products.Count; i++)
                {
                    var definition = products[i];
                    if (definition == null || !definition.Availability.Includes(gender))
                    {
                        continue;
                    }

                    items.Add(definition);
                    if (definition.IsEffectivelyRequired)
                    {
                        required.Add(definition);
                    }
                }
            }

            return new ProductSet(gender, items, required);
        }
    }
}
