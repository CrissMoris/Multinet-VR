using MultiTravel.Core.Products;
using MultiTravel.Core.Services;
using UnityEngine;

namespace MultiTravel.Gameplay.Common
{
    /// <summary>
    /// Helpers used by gameplay components to resolve Core services from <see cref="AppServices"/> in <c>Start()</c>
    /// (ARCHITECTURE.md §2.2). Values that were injected explicitly (tests, generator wiring) are kept.
    /// </summary>
    public static class ServiceResolver
    {
        /// <summary>Log prefix shared by the gameplay assembly.</summary>
        public const string LogPrefix = "[MultiTravel] ";

        /// <summary>
        /// Keeps <paramref name="field"/> when already set, otherwise resolves it from <see cref="AppServices"/>.
        /// Logs a clear error (once per call) and returns false when the service is missing.
        /// </summary>
        public static bool Resolve<T>(ref T field, Object context, string owner) where T : class
        {
            if (field != null)
            {
                return true;
            }

            if (AppServices.TryGet(out T instance))
            {
                field = instance;
                return true;
            }

            Debug.LogError(
                LogPrefix + owner + ": service '" + typeof(T).Name + "' is not registered in AppServices. " +
                "Start the application from the Bootstrap scene (AppBootstrap registers all services before Main loads).",
                context);
            return false;
        }

        /// <summary>Same as <see cref="Resolve{T}"/> but silent when missing (optional services).</summary>
        public static bool TryResolve<T>(ref T field) where T : class
        {
            if (field != null)
            {
                return true;
            }

            if (AppServices.TryGet(out T instance))
            {
                field = instance;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves the product catalog: the serialized reference wins, then an optional <see cref="AppServices"/>
        /// registration. Logs an error when neither exists.
        /// </summary>
        public static bool ResolveCatalog(ref ProductCatalog catalog, Object context, string owner)
        {
            if (catalog != null)
            {
                return true;
            }

            if (AppServices.TryGet(out ProductCatalog registered))
            {
                catalog = registered;
                return true;
            }

            Debug.LogError(
                LogPrefix + owner + ": no ProductCatalog assigned. Assign Assets/MultiTravel/Data/ProductCatalog.asset " +
                "(MultiTravel/Generate/Scenes wires it) or register one in AppServices.",
                context);
            return false;
        }
    }
}
