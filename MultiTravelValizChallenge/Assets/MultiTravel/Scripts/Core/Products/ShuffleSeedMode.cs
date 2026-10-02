namespace MultiTravel.Core.Products
{
    /// <summary>How the spawn-slot shuffle seed is chosen when <c>ProductCatalog.ShuffleSpawnPositions</c> is on.</summary>
    public enum ShuffleSeedMode
    {
        /// <summary>A fresh random seed for every session.</summary>
        PerSession,

        /// <summary>Always use <c>ProductCatalog.FixedShuffleSeed</c> (reproducible layouts for testing).</summary>
        Fixed
    }
}
