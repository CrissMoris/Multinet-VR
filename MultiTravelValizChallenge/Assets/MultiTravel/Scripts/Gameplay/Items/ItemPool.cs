using System;
using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Owns exactly one <see cref="ProductItem"/> instance per <see cref="ProductDefinition"/> of the catalog
    /// (ARCHITECTURE.md §2.8). Instances are created once (<see cref="EnsureCreated"/>, called from <c>Start</c>)
    /// under an "Items" root and kept disabled; sessions only enable, re-pose and disable them.
    /// Nothing is instantiated or destroyed while a session runs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemPool : MonoBehaviour
    {
        /// <summary>Name of the root created when <see cref="itemsRoot"/> is not assigned.</summary>
        public const string ItemsRootName = "Items";

        [SerializeField]
        [Tooltip("Catalog whose products are instantiated (Assets/MultiTravel/Data/ProductCatalog.asset).")]
        private ProductCatalog catalog;

        [SerializeField]
        [Tooltip("Parent of all item instances. When empty, a child named 'Items' is created.")]
        private Transform itemsRoot;

        private readonly List<ProductItem> items = new List<ProductItem>();
        private readonly List<ProductItem> activeItems = new List<ProductItem>();
        private readonly Dictionary<ProductDefinition, ProductItem> byDefinition = new Dictionary<ProductDefinition, ProductItem>();
        private readonly Dictionary<string, ProductItem> byId = new Dictionary<string, ProductItem>(StringComparer.Ordinal);
        private bool created;

        /// <summary>Raised once after the catalog instances were created.</summary>
        public event Action<ItemPool> ItemsCreated;

        /// <summary>Raised for every instance added to the pool (catalog instances and <see cref="Register"/> calls).</summary>
        public event Action<ProductItem> ItemRegistered;

        /// <summary>Every registered instance (pooled or active), in catalog order.</summary>
        public IReadOnlyList<ProductItem> AllItems => items;

        /// <summary>Instances activated by the last <see cref="Activate"/> / <see cref="ActivateItem"/> calls.</summary>
        public IReadOnlyList<ProductItem> ActiveItems => activeItems;

        /// <summary>True after <see cref="EnsureCreated"/> ran.</summary>
        public bool IsCreated => created;

        /// <summary>The catalog used to create instances.</summary>
        public ProductCatalog Catalog => catalog;

        /// <summary>Parent of all instances.</summary>
        public Transform ItemsRoot
        {
            get
            {
                EnsureRoot();
                return itemsRoot;
            }
        }

        /// <summary>Generator / test API: assigns the catalog. Must be called before <see cref="EnsureCreated"/>.</summary>
        public void SetCatalog(ProductCatalog productCatalog)
        {
            if (created && productCatalog != catalog)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "ItemPool: catalog changed after the instances were created; the change is ignored.", this);
                return;
            }

            catalog = productCatalog;
        }

        /// <summary>Generator API: assigns the parent of the instances.</summary>
        public void SetItemsRoot(Transform root)
        {
            itemsRoot = root;
        }

        /// <summary>
        /// Creates one disabled instance per catalog product (idempotent). Products without a prefab are skipped with an error.
        /// </summary>
        public void EnsureCreated()
        {
            if (created)
            {
                return;
            }

            created = true;
            if (catalog == null)
            {
                ServiceResolver.ResolveCatalog(ref catalog, this, nameof(ItemPool));
            }

            EnsureRoot();
            if (catalog != null && catalog.Products != null)
            {
                // Instantiate under a temporarily inactive root so no Awake / OnEnable (XRI registration) runs yet.
                bool rootWasActive = itemsRoot.gameObject.activeSelf;
                itemsRoot.gameObject.SetActive(false);
                try
                {
                    for (int i = 0; i < catalog.Products.Count; i++)
                    {
                        CreateInstance(catalog.Products[i], i);
                    }
                }
                finally
                {
                    itemsRoot.gameObject.SetActive(rootWasActive);
                }
            }

            ItemsCreated?.Invoke(this);
        }

        /// <summary>
        /// Registers an externally created instance (hand-placed scene items, tests). The item is returned to the pool.
        /// Returns false for null, duplicate instances or duplicate product ids.
        /// </summary>
        public bool Register(ProductItem item)
        {
            if (item == null || items.Contains(item))
            {
                return false;
            }

            var definition = item.Definition;
            if (definition != null)
            {
                if (byDefinition.ContainsKey(definition) || (!string.IsNullOrEmpty(definition.Id) && byId.ContainsKey(definition.Id)))
                {
                    Debug.LogError(ServiceResolver.LogPrefix + "ItemPool: duplicate instance for product '" + definition.Id + "' ignored.", item);
                    return false;
                }

                byDefinition.Add(definition, item);
                if (!string.IsNullOrEmpty(definition.Id))
                {
                    byId.Add(definition.Id, item);
                }
            }

            item.PoolIndex = items.Count;
            item.SetHomeParent(ItemsRoot);
            items.Add(item);
            item.ReturnToPool();
            ItemRegistered?.Invoke(item);
            return true;
        }

        /// <summary>Instance for a definition, or null.</summary>
        public ProductItem Find(ProductDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            if (byDefinition.TryGetValue(definition, out var item))
            {
                return item;
            }

            return Find(definition.Id);
        }

        /// <summary>Instance for a product id, or null.</summary>
        public ProductItem Find(string productId)
        {
            return !string.IsNullOrEmpty(productId) && byId.TryGetValue(productId, out var item) ? item : null;
        }

        /// <summary>
        /// Deactivates everything, then activates the instance of every product in <paramref name="set"/> (Pooled → Free at
        /// its current spawn pose). Products without an instance are reported once. Returns <see cref="ActiveItems"/>.
        /// </summary>
        public IReadOnlyList<ProductItem> Activate(ProductSet set)
        {
            EnsureCreated();
            DeactivateAll();
            if (set == null)
            {
                return activeItems;
            }

            for (int i = 0; i < set.Items.Count; i++)
            {
                var definition = set.Items[i];
                var item = Find(definition);
                if (item == null)
                {
                    Debug.LogError(
                        ServiceResolver.LogPrefix + "ItemPool: product '" + (definition != null ? definition.Id : "null") +
                        "' has no instance (missing VisualPrefab?). It is skipped for this session.",
                        this);
                    continue;
                }

                ActivateItem(item);
            }

            return activeItems;
        }

        /// <summary>Activates one registered instance (Pooled → Free). Returns false when it is not registered or not pooled.</summary>
        public bool ActivateItem(ProductItem item)
        {
            if (item == null || !items.Contains(item))
            {
                return false;
            }

            if (!item.ActivateAtSpawn())
            {
                return false;
            }

            if (!activeItems.Contains(item))
            {
                activeItems.Add(item);
            }

            return true;
        }

        /// <summary>Returns every instance to the pool (released, kinematic, disabled).</summary>
        public void DeactivateAll()
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item != null && item.State != ProductItemState.Pooled)
                {
                    item.ReturnToPool();
                }
            }

            activeItems.Clear();
        }

        private void Start()
        {
            EnsureCreated();
        }

        private void EnsureRoot()
        {
            if (itemsRoot != null)
            {
                return;
            }

            var existing = transform.Find(ItemsRootName);
            if (existing != null)
            {
                itemsRoot = existing;
                return;
            }

            var root = new GameObject(ItemsRootName);
            root.transform.SetParent(transform, false);
            itemsRoot = root.transform;
        }

        private void CreateInstance(ProductDefinition definition, int catalogIndex)
        {
            if (definition == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "ItemPool: catalog entry " + catalogIndex + " is null; skipped.", this);
                return;
            }

            if (definition.VisualPrefab == null)
            {
                Debug.LogError(
                    ServiceResolver.LogPrefix + "ItemPool: product '" + definition.Id + "' has no VisualPrefab; it will not appear in the room. " +
                    "Run MultiTravel/Generate/Content.",
                    definition);
                return;
            }

            if (byDefinition.ContainsKey(definition) || (!string.IsNullOrEmpty(definition.Id) && byId.ContainsKey(definition.Id)))
            {
                Debug.LogError(ServiceResolver.LogPrefix + "ItemPool: duplicate product id '" + definition.Id + "' in the catalog; second entry skipped.", definition);
                return;
            }

            var instance = Instantiate(definition.VisualPrefab, itemsRoot, false);
            instance.name = "Item_" + definition.Id;
            var item = instance.GetComponent<ProductItem>();
            if (item == null)
            {
                Debug.LogWarning(
                    ServiceResolver.LogPrefix + "ItemPool: prefab of '" + definition.Id + "' has no ProductItem on its root; one was added at runtime. " +
                    "Regenerate the item prefabs.",
                    definition.VisualPrefab);
                item = instance.AddComponent<ProductItem>();
            }

            instance.SetActive(false);
            item.Setup(definition);
            Register(item);
        }
    }
}
