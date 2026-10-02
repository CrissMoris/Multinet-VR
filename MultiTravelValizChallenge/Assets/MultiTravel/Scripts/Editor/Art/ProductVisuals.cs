using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.EditorTools.Data;
using MultiTravel.Gameplay.Items;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MultiTravel.EditorTools.Art
{
    /// <summary>Small scene-building helpers (empty nodes, rounded-box parts, TMP labels) used by the editor generators.</summary>
    public static class ProductVisuals
    {
        public static GameObject Node(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go;
        }

        public static GameObject Part(string name, Transform parent, Vector3 position, Vector3 size, string material, bool solid = false)
        {
            var go = Node(name, parent, position);
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshLibrary.Load("MT_RoundedBox_1m_r05");
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get(material);
            go.transform.localScale = size;
            if (solid) go.AddComponent<BoxCollider>();
            return go;
        }

        public static GameObject MeshPart(string name, Transform parent, Mesh mesh, Vector3 pos, string material)
        {
            var go = Node(name, parent, pos);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get(material);
            return go;
        }

        /// <summary>World thickness of the inverted-hull hover outline (metres).</summary>
        public const float OutlineThickness = 0.0025f;

        /// <summary>Smallest grab size of an interactive item in every axis (OVERHAUL_PLAN §1).</summary>
        public const float MinGrabSize = 0.05f;

        /// <summary>
        /// Adds an inverted-hull outline (child <see cref="ProductItem.OutlineChildName"/>, renderer disabled) under every mesh of
        /// <paramref name="root"/>: a copy of the mesh with vertices pushed <see cref="OutlineThickness"/> outwards along the
        /// position-averaged normals, one sub-mesh, rendered with <paramref name="outlineMaterial"/> (unlit, Cull Front).
        /// Meshes are stored at <c>&lt;meshFolder&gt;/&lt;key&gt;_&lt;n&gt;.asset</c> (updated in place, GUIDs stable).
        /// Source meshes must be readable. Returns the number of hulls built.
        /// </summary>
        public static int AddOutlineHulls(GameObject root, string meshFolder, string key, Material outlineMaterial, List<string> warnings)
        {
            int index = 0;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in filters)
            {
                if (filter == null || filter.sharedMesh == null || filter.gameObject.name == ProductItem.OutlineChildName)
                {
                    continue;
                }

                var source = filter.sharedMesh;
                if (!source.isReadable)
                {
                    warnings?.Add($"{key}: mesh '{source.name}' is not readable; no hover outline for it.");
                    continue;
                }

                var lossy = filter.transform.lossyScale;
                var rootScale = root.transform.lossyScale;
                float scale = (Mathf.Abs(lossy.x / rootScale.x) + Mathf.Abs(lossy.y / rootScale.y) + Mathf.Abs(lossy.z / rootScale.z)) / 3f;
                var hull = BuildHullMesh(source, OutlineThickness / Mathf.Max(1e-5f, scale));
                var mesh = GeneratedAssetUtil.UpsertMesh(hull, $"{meshFolder}/{key}_{index}.asset");
                index++;

                var existing = filter.transform.Find(ProductItem.OutlineChildName);
                var go = existing != null ? existing.gameObject : Node(ProductItem.OutlineChildName, filter.transform, Vector3.zero);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                var meshFilter = go.GetComponent<MeshFilter>();
                if (meshFilter == null)
                {
                    meshFilter = go.AddComponent<MeshFilter>();
                }

                meshFilter.sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    renderer = go.AddComponent<MeshRenderer>();
                }

                renderer.sharedMaterial = outlineMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                renderer.enabled = false;
            }

            return index;
        }

        /// <summary>Copy of <paramref name="source"/> with every vertex moved <paramref name="offset"/> along its smoothed normal.</summary>
        public static Mesh BuildHullMesh(Mesh source, float offset)
        {
            var vertices = source.vertices;
            var normals = source.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                var copy = Object.Instantiate(source);
                copy.RecalculateNormals();
                normals = copy.normals;
                Object.DestroyImmediate(copy);
            }

            // Average normals of coincident vertices so hard edges do not crack open.
            var sums = new Dictionary<Vector3Int, Vector3>();
            var keys = new Vector3Int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                var k = new Vector3Int(Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f));
                keys[i] = k;
                sums.TryGetValue(k, out var sum);
                sums[k] = sum + normals[i];
            }

            var moved = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var n = sums[keys[i]];
                n = n.sqrMagnitude > 1e-10f ? n.normalized : normals[i];
                moved[i] = vertices[i] + n * offset;
            }

            var triangles = new List<int>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) == MeshTopology.Triangles)
                {
                    triangles.AddRange(source.GetTriangles(sub));
                }
            }

            var hull = new Mesh
            {
                indexFormat = vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            hull.vertices = moved;
            hull.normals = normals;
            hull.SetTriangles(triangles, 0);
            hull.RecalculateBounds();
            return hull;
        }

        /// <summary>
        /// Adds (or updates) the invisible <see cref="ProductItem.GrabColliderName"/> box of a small item so it is at least
        /// <see cref="MinGrabSize"/> in every axis. The box is bottom-aligned with <paramref name="bounds"/> (the item still rests
        /// on its real bottom) and centred horizontally. Removes it when the item is large enough. Returns true when present.
        /// </summary>
        public static bool EnsureGrabCollider(GameObject root, Bounds bounds)
        {
            var existing = root.transform.Find(ProductItem.GrabColliderName);
            bool small = bounds.size.x < MinGrabSize || bounds.size.y < MinGrabSize || bounds.size.z < MinGrabSize;
            if (!small)
            {
                if (existing != null)
                {
                    Object.DestroyImmediate(existing.gameObject);
                }

                return false;
            }

            var go = existing != null ? existing.gameObject : Node(ProductItem.GrabColliderName, root.transform, Vector3.zero);
            var box = go.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = go.AddComponent<BoxCollider>();
            }

            var size = Vector3.Max(bounds.size, Vector3.one * MinGrabSize);
            box.isTrigger = false;
            box.size = size;
            box.center = new Vector3(bounds.center.x, bounds.min.y + size.y * 0.5f, bounds.center.z);
            return true;
        }

        public static TextMeshPro Label(Transform parent, string text, Vector3 pos, float size, Color color)
        {
            var label = Node("Label", parent, pos).AddComponent<TextMeshPro>();
            label.font = Fonts.FontAssetGenerator.Load();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(1.2f, .2f);
            return label;
        }
    }
}
