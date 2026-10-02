using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>Pose helpers shared by spawn slots and suitcase slots.</summary>
    public static class ItemPlacementMath
    {
        /// <summary>
        /// World pose that puts <paramref name="item"/> on the surface described by <paramref name="surface"/>:
        /// rotation = surface rotation, the item's current collider bounds centred horizontally on the surface origin and its
        /// lowest point lifted <paramref name="lift"/> metres along the surface's up axis.
        /// </summary>
        public static Pose PoseOnSurface(Transform surface, ProductItem item, float lift)
        {
            return PoseOnSurface(surface.position, surface.rotation, item, lift);
        }

        /// <summary>Same as <see cref="PoseOnSurface(Transform, ProductItem, float)"/> for an explicit surface point and rotation.</summary>
        public static Pose PoseOnSurface(Vector3 surfacePosition, Quaternion surfaceRotation, ProductItem item, float lift)
        {
            return PoseOnSurface(surfacePosition, surfaceRotation, item, item != null ? item.LocalBounds : default, lift);
        }

        /// <summary>Pose on a surface for explicit item-local <paramref name="bounds"/> (e.g. <see cref="ProductItem.PackedBounds"/>).</summary>
        public static Pose PoseOnSurface(Vector3 surfacePosition, Quaternion surfaceRotation, ProductItem item, Bounds bounds, float lift)
        {
            var rotation = surfaceRotation;
            var offset = Vector3.zero;
            if (item != null)
            {
                var scale = WorldScale(item);
                offset = rotation * Vector3.Scale(new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z), scale);
            }

            return new Pose(surfacePosition + rotation * Vector3.up * lift + offset, rotation);
        }

        /// <summary>
        /// World pose that hangs <paramref name="item"/> from a hook: the item's <see cref="ProductItem.HangPointLocal"/> sits on
        /// <paramref name="hookPosition"/>, rotation = <paramref name="hookRotation"/>.
        /// </summary>
        public static Pose PoseHanging(Vector3 hookPosition, Quaternion hookRotation, ProductItem item)
        {
            var offset = Vector3.zero;
            if (item != null)
            {
                offset = hookRotation * Vector3.Scale(item.HangPointLocal, WorldScale(item));
            }

            return new Pose(hookPosition - offset, hookRotation);
        }

        /// <summary>Height of <paramref name="item"/> along its own up axis in world units (current bounds x scale).</summary>
        public static float ItemHeight(ProductItem item)
        {
            return item == null ? 0f : ItemHeight(item, item.LocalBounds);
        }

        /// <summary>Height of <paramref name="item"/> for explicit item-local <paramref name="bounds"/>.</summary>
        public static float ItemHeight(ProductItem item, Bounds bounds)
        {
            if (item == null)
            {
                return 0f;
            }

            return bounds.size.y * WorldScale(item).y;
        }

        /// <summary>Resting world scale of the item (base local scale times the parent's lossy scale).</summary>
        public static Vector3 WorldScale(ProductItem item)
        {
            var scale = item.BaseLocalScale;
            var parent = item.transform.parent;
            if (parent != null)
            {
                scale = Vector3.Scale(scale, parent.lossyScale);
            }

            return scale;
        }
    }
}
