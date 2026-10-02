using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>Pose helpers shared by spawn slots and suitcase slots.</summary>
    public static class ItemPlacementMath
    {
        /// <summary>
        /// World pose that puts <paramref name="item"/> on the surface described by <paramref name="surface"/>:
        /// rotation = surface rotation, the item's collider bounds centred horizontally on the surface origin and its
        /// lowest point lifted <paramref name="lift"/> metres along the surface's up axis.
        /// </summary>
        public static Pose PoseOnSurface(Transform surface, ProductItem item, float lift)
        {
            return PoseOnSurface(surface.position, surface.rotation, item, lift);
        }

        /// <summary>Same as <see cref="PoseOnSurface(Transform, ProductItem, float)"/> for an explicit surface point and rotation.</summary>
        public static Pose PoseOnSurface(Vector3 surfacePosition, Quaternion surfaceRotation, ProductItem item, float lift)
        {
            var rotation = surfaceRotation;
            var offset = Vector3.zero;
            if (item != null)
            {
                var bounds = item.LocalBounds;
                var scale = item.BaseLocalScale;
                var parent = item.transform.parent;
                if (parent != null)
                {
                    scale = Vector3.Scale(scale, parent.lossyScale);
                }

                offset = rotation * Vector3.Scale(new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z), scale);
            }

            return new Pose(surfacePosition + rotation * Vector3.up * lift + offset, rotation);
        }

        /// <summary>Height of <paramref name="item"/> along its own up axis in world units (collider bounds x scale).</summary>
        public static float ItemHeight(ProductItem item)
        {
            if (item == null)
            {
                return 0f;
            }

            var scale = item.BaseLocalScale;
            var parent = item.transform.parent;
            if (parent != null)
            {
                scale = Vector3.Scale(scale, parent.lossyScale);
            }

            return item.LocalBounds.size.y * scale.y;
        }
    }
}
