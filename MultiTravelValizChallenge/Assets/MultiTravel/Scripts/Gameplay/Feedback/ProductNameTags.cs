using System.Collections.Generic;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.UI;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace MultiTravel.Gameplay.Feedback
{
    /// <summary>
    /// Floating Turkish product name above every item a hand is hovering or holding, so participants can tell similar
    /// items apart (blouse / shirt, glasses / sunglasses) without reading anything on the item itself. Labels are pooled
    /// (no allocation during a session), face the HMD and disappear when the hand leaves the item.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductNameTags : MonoBehaviour
    {
        [SerializeField]
        private ItemPool pool;

        [SerializeField]
        [Tooltip("Gap between the top of the item and the label (metres).")]
        private float gap = 0.06f;

        [SerializeField]
        [Tooltip("Label font size in world units (TextMeshPro).")]
        private float fontSize = 0.45f;

        [SerializeField]
        [Min(1)]
        private int poolSize = 4;

        private readonly Dictionary<ProductItem, int> interest = new Dictionary<ProductItem, int>();
        private readonly Dictionary<ProductItem, TextMeshPro> shown = new Dictionary<ProductItem, TextMeshPro>();
        private readonly Stack<TextMeshPro> free = new Stack<TextMeshPro>();
        private readonly HashSet<ProductItem> subscribed = new HashSet<ProductItem>();
        private readonly List<ProductItem> scratch = new List<ProductItem>();
        private Camera viewCamera;
        private Material outlinedMaterial;

        /// <summary>Generator API.</summary>
        public void SetPool(ItemPool itemPool)
        {
            pool = itemPool;
        }

        /// <summary>Number of labels currently visible (tests / diagnostics).</summary>
        public int VisibleCount => shown.Count;

        private void Awake()
        {
            for (int i = 0; i < poolSize; i++)
            {
                free.Push(CreateLabel(i));
            }
        }

        private void OnDestroy()
        {
            if (outlinedMaterial != null)
            {
                Destroy(outlinedMaterial);
            }
        }

        private void OnEnable()
        {
            if (pool != null)
            {
                pool.ItemRegistered += Subscribe;
                foreach (var item in pool.AllItems)
                {
                    Subscribe(item);
                }
            }
        }

        private void OnDisable()
        {
            if (pool != null)
            {
                pool.ItemRegistered -= Subscribe;
            }

            foreach (var item in subscribed)
            {
                if (item != null && item.Grab != null)
                {
                    item.Grab.hoverEntered.RemoveListener(OnHoverEntered);
                    item.Grab.hoverExited.RemoveListener(OnHoverExited);
                    item.Grab.selectEntered.RemoveListener(OnSelectEntered);
                    item.Grab.selectExited.RemoveListener(OnSelectExited);
                }
            }

            subscribed.Clear();
            interest.Clear();
            HideAll();
        }

        private void Subscribe(ProductItem item)
        {
            if (item == null || item.Grab == null || !subscribed.Add(item))
            {
                return;
            }

            item.Grab.hoverEntered.AddListener(OnHoverEntered);
            item.Grab.hoverExited.AddListener(OnHoverExited);
            item.Grab.selectEntered.AddListener(OnSelectEntered);
            item.Grab.selectExited.AddListener(OnSelectExited);
        }

        private void OnHoverEntered(HoverEnterEventArgs args) => Change(args.interactableObject, +1);

        private void OnHoverExited(HoverExitEventArgs args) => Change(args.interactableObject, -1);

        private void OnSelectEntered(SelectEnterEventArgs args) => Change(args.interactableObject, +1);

        private void OnSelectExited(SelectExitEventArgs args) => Change(args.interactableObject, -1);

        private void Change(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable interactable, int delta)
        {
            if (interactable == null)
            {
                return;
            }

            var item = interactable.transform.GetComponent<ProductItem>();
            if (item == null)
            {
                return;
            }

            interest.TryGetValue(item, out int count);
            count = Mathf.Max(0, count + delta);
            if (count == 0)
            {
                interest.Remove(item);
                Hide(item);
            }
            else
            {
                interest[item] = count;
                Show(item);
            }
        }

        private void Show(ProductItem item)
        {
            if (shown.ContainsKey(item) || item.Definition == null || string.IsNullOrEmpty(item.Definition.DisplayName))
            {
                return;
            }

            if (free.Count == 0)
            {
                return; // more hands than labels: the extra item simply has no label
            }

            var label = free.Pop();
            label.text = item.Definition.DisplayName;
            label.gameObject.SetActive(true);
            shown[item] = label;
            Place(item, label);
        }

        private void Hide(ProductItem item)
        {
            if (shown.TryGetValue(item, out var label))
            {
                shown.Remove(item);
                label.gameObject.SetActive(false);
                free.Push(label);
            }
        }

        /// <summary>Hides every label (session reset / interaction lock).</summary>
        public void HideAll()
        {
            scratch.Clear();
            scratch.AddRange(shown.Keys);
            foreach (var item in scratch)
            {
                Hide(item);
            }

            scratch.Clear();
        }

        private void LateUpdate()
        {
            if (shown.Count == 0)
            {
                return;
            }

            scratch.Clear();
            foreach (var pair in shown)
            {
                if (pair.Key == null || !pair.Key.gameObject.activeInHierarchy || pair.Key.State == ProductItemState.Pooled)
                {
                    scratch.Add(pair.Key);
                }
            }

            foreach (var item in scratch)
            {
                interest.Remove(item);
                Hide(item);
            }

            scratch.Clear();
            foreach (var pair in shown)
            {
                Place(pair.Key, pair.Value);
            }
        }

        private void Place(ProductItem item, TextMeshPro label)
        {
            if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            {
                viewCamera = Camera.main;
            }

            var bounds = WorldBounds(item);
            var position = new Vector3(bounds.center.x, bounds.max.y + gap, bounds.center.z);
            label.transform.position = position;
            if (viewCamera != null)
            {
                var toLabel = position - viewCamera.transform.position;
                toLabel.y = 0f;
                if (toLabel.sqrMagnitude > 1e-6f)
                {
                    label.transform.rotation = Quaternion.LookRotation(toLabel.normalized, Vector3.up);
                }
            }
        }

        private static Bounds WorldBounds(ProductItem item)
        {
            var colliders = item.GetComponentsInChildren<Collider>();
            bool first = true;
            var b = new Bounds(item.transform.position, Vector3.zero);
            foreach (var c in colliders)
            {
                if (c.isTrigger || !c.enabled)
                {
                    continue;
                }

                if (first)
                {
                    b = c.bounds;
                    first = false;
                }
                else
                {
                    b.Encapsulate(c.bounds);
                }
            }

            return b;
        }

        private TextMeshPro CreateLabel(int index)
        {
            var go = new GameObject($"Product name tag {index + 1}");
            go.transform.SetParent(transform, false);
            var label = go.AddComponent<TextMeshPro>();
            label.font = VrUiStyle.Font;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontStyle = FontStyles.Bold;
            if (outlinedMaterial == null && label.font != null)
            {
                // One private material for all tags: the outline must not leak into the shared font material (VR panel).
                outlinedMaterial = new Material(label.font.material) { name = "Product name tag (outline)" };
                outlinedMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
                outlinedMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.25f);
                outlinedMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(12, 22, 48, 255));
            }

            if (outlinedMaterial != null)
            {
                label.fontSharedMaterial = outlinedMaterial;
            }

            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(0.6f, 0.08f);
            go.SetActive(false);
            return label;
        }
    }
}
