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
