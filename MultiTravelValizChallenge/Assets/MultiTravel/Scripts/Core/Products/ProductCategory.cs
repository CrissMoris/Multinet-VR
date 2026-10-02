namespace MultiTravel.Core.Products
{
    /// <summary>Product category used for grouping and environment placement (ARCHITECTURE.md §2.4).</summary>
    public enum ProductCategory
    {
        Clothing,
        Shoes,
        Accessory,
        Business,
        Electronics,
        Document,
        Toiletry,
        Leisure,
        Other,

        /// <summary>Watches, earrings, necklaces, cufflinks. Appended last to keep serialized values stable.</summary>
        Jewellery
    }
}
