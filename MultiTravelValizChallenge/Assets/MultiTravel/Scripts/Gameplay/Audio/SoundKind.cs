using MultiTravel.Core.Products;

namespace MultiTravel.Gameplay.Audio
{
    /// <summary>Foley family of a product when it lands in the suitcase (OVERHAUL_PLAN §5).</summary>
    public enum SoundKind
    {
        /// <summary>Soft garments, towels, socks: muffled fabric thump.</summary>
        Cloth = 0,

        /// <summary>Shoes, bags, wallets: low leather thump.</summary>
        Leather = 1,

        /// <summary>Electronics, plastics, toys: click plus thump.</summary>
        Hard = 2,

        /// <summary>Jewellery, cufflinks, keys: short bright ping.</summary>
        Metal = 3,

        /// <summary>Documents, books, notebooks: dry paper flutter.</summary>
        Paper = 4
    }

    /// <summary>Category → foley mapping used when no explicit <see cref="SoundKind"/> is supplied.</summary>
    public static class SoundKinds
    {
        /// <summary>Number of values in <see cref="SoundKind"/>.</summary>
        public const int Count = 5;

        /// <summary>Default foley family for a product category.</summary>
        public static SoundKind For(ProductCategory category)
        {
            switch (category)
            {
                case ProductCategory.Clothing:
                    return SoundKind.Cloth;
                case ProductCategory.Shoes:
                case ProductCategory.Toiletry:
                    return SoundKind.Leather;
                case ProductCategory.Jewellery:
                    return SoundKind.Metal;
                case ProductCategory.Document:
                    return SoundKind.Paper;
                case ProductCategory.Business:
                case ProductCategory.Electronics:
                case ProductCategory.Accessory:
                case ProductCategory.Leisure:
                case ProductCategory.Other:
                default:
                    return SoundKind.Hard;
            }
        }

        /// <summary>Default foley family for a product (Hard when null).</summary>
        public static SoundKind For(ProductDefinition definition)
        {
            return definition != null ? For(definition.Category) : SoundKind.Hard;
        }
    }
}
