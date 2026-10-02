using System;
using System.Collections.Generic;

namespace MultiTravel.Core.Services
{
    /// <summary>
    /// Tiny typed service registry used for composition at bootstrap time (see ARCHITECTURE.md §2.2).
    /// <para>
    /// <c>AppBootstrap</c> registers every service in a fixed order; gameplay and operator MonoBehaviours
    /// resolve them in <c>Start()</c> (never in <c>Awake()</c>) with <see cref="Get{T}"/>.
    /// Registering a type that is already registered replaces the previous instance, which keeps
    /// tests and editor tooling simple. All access is expected from the Unity main thread.
    /// </para>
    /// </summary>
    public static class AppServices
    {
        private static readonly Dictionary<Type, object> Instances = new Dictionary<Type, object>();

        /// <summary>Number of registered services.</summary>
        public static int Count => Instances.Count;

        /// <summary>Registers (or replaces) the instance exposed for <typeparamref name="T"/>.</summary>
        public static void Register<T>(T instance) where T : class
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            Instances[typeof(T)] = instance;
        }

        /// <summary>Returns the registered instance or throws when the service is missing.</summary>
        public static T Get<T>() where T : class
        {
            if (Instances.TryGetValue(typeof(T), out var value))
            {
                return (T)value;
            }

            throw new InvalidOperationException(
                $"Service '{typeof(T).FullName}' is not registered. Services are registered by AppBootstrap; " +
                "resolve them in Start(), not Awake().");
        }

        /// <summary>Returns true and the instance when registered, false otherwise.</summary>
        public static bool TryGet<T>(out T instance) where T : class
        {
            if (Instances.TryGetValue(typeof(T), out var value))
            {
                instance = (T)value;
                return true;
            }

            instance = null;
            return false;
        }

        /// <summary>True when a service of type <typeparamref name="T"/> is registered.</summary>
        public static bool IsRegistered<T>() where T : class
        {
            return Instances.ContainsKey(typeof(T));
        }

        /// <summary>Removes a single registration. Returns true when something was removed.</summary>
        public static bool Unregister<T>() where T : class
        {
            return Instances.Remove(typeof(T));
        }

        /// <summary>Removes every registration (tests, domain reload, application shutdown).</summary>
        public static void Clear()
        {
            Instances.Clear();
        }
    }
}
