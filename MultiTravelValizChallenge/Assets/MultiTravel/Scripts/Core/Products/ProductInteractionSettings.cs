using System;
using UnityEngine;

namespace MultiTravel.Core.Products
{
    /// <summary>Per-product physics / grab tuning consumed by the gameplay assembly (ARCHITECTURE.md §2.4).</summary>
    [Serializable]
    public sealed class ProductInteractionSettings
    {
        [Tooltip("Rigidbody mass in kilograms while the item is free.")]
        [Min(0.01f)]
        public float Mass = 0.5f;

        [Tooltip("Uniform scale multiplier applied while the item is held.")]
        [Min(0.1f)]
        public float GrabScale = 1f;

        [Tooltip("True when the item should support two-handed grabbing.")]
        public bool TwoHanded;

        [Tooltip("Local offset applied to the attach point while held.")]
        public Vector3 HoldOffset = Vector3.zero;
    }
}
