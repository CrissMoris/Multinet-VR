using System;
using System.Collections.Generic;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Products;
using MultiTravel.EditorTools.Art;
using UnityEditor;
using UnityEngine;

namespace MultiTravel.EditorTools.Data
{
    /// <summary>
    /// Creates the default product data (ARCHITECTURE §9, OVERHAUL_PLAN §3), the <see cref="ProductCatalog"/>, the
    /// <see cref="AppConfig"/> (§6) and the non-catalog practice item (<c>Data/Practice/practice-tag.asset</c>).
    /// Existing assets are never overwritten unless <c>force</c> is true; even then GUIDs are kept (updated in place) and
    /// hand-wired references (VisualPrefab, BrandSprite, LogoSprite) plus backend credentials are preserved.
    /// Without <c>force</c>, an existing definition only receives its <see cref="ProductPresentation"/> when that is still at
    /// its defaults (customer edits are kept).
    /// </summary>
    public static class ProductDataGenerator
    {
        public const string CatalogPath = GeneratedAssetUtil.DataFolder + "/ProductCatalog.asset";
        public const string AppConfigPath = GeneratedAssetUtil.ResourcesFolder + "/AppConfig.asset";
        public const string PracticeFolder = GeneratedAssetUtil.DataFolder + "/Practice";
        public const string PracticeId = "practice-tag";
        public const string PracticePath = PracticeFolder + "/" + PracticeId + ".asset";

        /// <summary>Display / packing / grip presentation of one default product (OVERHAUL_PLAN §3).</summary>
        public readonly struct PresentationSpec
        {
            public PresentationSpec(DisplayZone zone, PackedKind packed, GripPreset grip, bool hasHangingVariant)
            {
                Zone = zone;
                Packed = packed;
                Grip = grip;
                HasHangingVariant = hasHangingVariant;
            }

            public DisplayZone Zone { get; }

            public PackedKind Packed { get; }

            public GripPreset Grip { get; }

            public bool HasHangingVariant { get; }

            public ProductPresentation ToPresentation()
            {
                return new ProductPresentation
                {
                    Zone = Zone,
                    Packed = Packed,
                    Grip = Grip,
                    HasHangingVariant = HasHangingVariant
                };
            }
        }

        /// <summary>Default data row for one product.</summary>
        public sealed class ProductSpec
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly ProductCategory Category;
            public readonly GenderAvailability Availability;
            public readonly bool IsCorrect;
            public readonly float Mass;
            public readonly bool TwoHanded;
            public readonly PresentationSpec Presentation;

            /// <summary>Explicit IsRequiredForCompletion (null = follows IsCorrect).</summary>
            public readonly bool? RequiredOverride;

            public ProductSpec(string id, string displayName, ProductCategory category, GenderAvailability availability, bool isCorrect, float mass,
                PresentationSpec presentation, bool twoHanded = false, bool? requiredOverride = null)
            {
                Id = id;
                DisplayName = displayName;
                Category = category;
                Availability = availability;
                IsCorrect = isCorrect;
                Mass = mass;
                Presentation = presentation;
                TwoHanded = twoHanded;
                RequiredOverride = requiredOverride;
            }
        }

        private const GenderAvailability Both = GenderAvailability.Both;
        private const GenderAvailability Female = GenderAvailability.Female;
        private const GenderAvailability Male = GenderAvailability.Male;

        private static PresentationSpec Pres(DisplayZone zone, PackedKind packed, GripPreset grip, bool hasHangingVariant = false)
        {
            return new PresentationSpec(zone, packed, grip, hasHangingVariant);
        }

        // OVERHAUL_PLAN §3 presentation presets.
        private static PresentationSpec HangingGarment() => Pres(DisplayZone.Hanging, PackedKind.Flat, GripPreset.Hanger, true);

        private static PresentationSpec FoldedGarment() => Pres(DisplayZone.Folded, PackedKind.Flat, GripPreset.FoldedGarment);

        private static PresentationSpec Shoe() => Pres(DisplayZone.Shoes, PackedKind.ShoeCorner, GripPreset.Shoe);

        private static PresentationSpec Bag(DisplayZone zone) => Pres(zone, PackedKind.Upright, GripPreset.Handle);

        private static PresentationSpec FlatRigid(DisplayZone zone) => Pres(zone, PackedKind.Flat, GripPreset.FlatEdge);

        private static PresentationSpec Pocket(DisplayZone zone) => Pres(zone, PackedKind.LidPocket, GripPreset.FlatEdge);

        private static PresentationSpec Small(DisplayZone zone) => Pres(zone, PackedKind.Organiser, GripPreset.Dynamic);

        private static PresentationSpec Odd(DisplayZone zone) => Pres(zone, PackedKind.Top, GripPreset.Dynamic);

        /// <summary>The initial product list from ARCHITECTURE.md §9, in catalog order.</summary>
        public static readonly IReadOnlyList<ProductSpec> DefaultProducts = new[]
        {
            // Common, correct (+10, required).
            new ProductSpec("laptop", "Dizüstü Bilgisayar", ProductCategory.Electronics, Both, true, 1.6f, FlatRigid(DisplayZone.Business)),
            new ProductSpec("laptop-charger", "Şarj Adaptörü", ProductCategory.Electronics, Both, true, 0.35f, Small(DisplayZone.Business)),
            new ProductSpec("phone-cable", "Telefon Şarj Kablosu", ProductCategory.Electronics, Both, true, 0.08f, Small(DisplayZone.Business)),
            new ProductSpec("notebook", "Not Defteri", ProductCategory.Business, Both, true, 0.3f, FlatRigid(DisplayZone.Business)),
            new ProductSpec("pen", "Kalem", ProductCategory.Business, Both, true, 0.02f, Small(DisplayZone.Business)),
            new ProductSpec("toiletry-bag", "Kozmetik/Tıraş Çantası", ProductCategory.Toiletry, Both, true, 0.6f, Bag(DisplayZone.Accessories)),
            new ProductSpec("id-card", "Kimlik Kartı", ProductCategory.Document, Both, true, 0.01f, Pocket(DisplayZone.Business)),
            // Female, correct.
            new ProductSpec("blouse", "Bluz", ProductCategory.Clothing, Female, true, 0.2f, HangingGarment()),
            new ProductSpec("women-trousers", "Kumaş Pantolon", ProductCategory.Clothing, Female, true, 0.45f, FoldedGarment()),
            new ProductSpec("blazer", "Blazer Ceket", ProductCategory.Clothing, Female, true, 0.7f, HangingGarment()),
            new ProductSpec("women-shoes", "Klasik Ayakkabı", ProductCategory.Shoes, Female, true, 0.6f, Shoe()),
            // Male, correct.
            new ProductSpec("shirt", "Gömlek", ProductCategory.Clothing, Male, true, 0.25f, HangingGarment()),
            new ProductSpec("men-trousers", "Kumaş Pantolon", ProductCategory.Clothing, Male, true, 0.5f, FoldedGarment()),
            new ProductSpec("jacket", "Ceket", ProductCategory.Clothing, Male, true, 0.9f, HangingGarment()),
            new ProductSpec("men-shoes", "Deri Ayakkabı", ProductCategory.Shoes, Male, true, 0.9f, Shoe()),
            new ProductSpec("tie", "Kravat", ProductCategory.Accessory, Male, true, 0.06f, Small(DisplayZone.Accessories)),
            // Common, incorrect (-5).
            new ProductSpec("beach-towel", "Plaj Havlusu", ProductCategory.Leisure, Both, false, 0.6f, FoldedGarment()),
            new ProductSpec("snorkel-mask", "Şnorkel Maskesi", ProductCategory.Leisure, Both, false, 0.4f, Odd(DisplayZone.Leisure)),
            new ProductSpec("beach-hat", "Balıkçı Şapkası", ProductCategory.Accessory, Both, false, 0.15f, Odd(DisplayZone.Accessories)),
            new ProductSpec("neck-pillow", "Boyun Yastığı", ProductCategory.Leisure, Both, false, 0.3f, Odd(DisplayZone.Leisure)),
            new ProductSpec("kids-book", "Boyama Kitabı", ProductCategory.Leisure, Both, false, 0.25f, FlatRigid(DisplayZone.Leisure)),
            new ProductSpec("passport", "Pasaport", ProductCategory.Document, Both, false, 0.04f, Pocket(DisplayZone.Business)),
            new ProductSpec("rubber-duck", "Oyuncak Ördek", ProductCategory.Leisure, Both, false, 0.1f, Odd(DisplayZone.Leisure)),
            new ProductSpec("football", "Futbol Topu", ProductCategory.Leisure, Both, false, 0.43f, Odd(DisplayZone.Leisure)),
            new ProductSpec("ukulele", "Ukulele", ProductCategory.Leisure, Both, false, 0.5f, Odd(DisplayZone.Leisure)),
            new ProductSpec("garden-gnome", "Bahçe Cücesi", ProductCategory.Other, Both, false, 0.8f, Odd(DisplayZone.Leisure)),
            new ProductSpec("binoculars", "Dürbün", ProductCategory.Leisure, Both, false, 0.6f, Odd(DisplayZone.Leisure)),
            // Female, incorrect.
            new ProductSpec("bikini", "Bikini", ProductCategory.Clothing, Female, false, 0.1f, HangingGarment()),
            new ProductSpec("swimsuit", "Mayo", ProductCategory.Clothing, Female, false, 0.15f, HangingGarment()),
            // Male, incorrect.
            new ProductSpec("swim-shorts", "Deniz Şortu", ProductCategory.Clothing, Male, false, 0.2f, FoldedGarment()),
            new ProductSpec("flip-flops", "Terlik", ProductCategory.Shoes, Male, false, 0.3f, Shoe()),
            new ProductSpec("phone", "Telefon", ProductCategory.Electronics, Both, true, 0.18f, Pocket(DisplayZone.Business)),
            new ProductSpec("laptop-bag", "Bilgisayar Çantası", ProductCategory.Business, Both, true, 0.4f, Bag(DisplayZone.Business)),
            new ProductSpec("headphones", "Kulaklık", ProductCategory.Electronics, Both, false, 0.2f, Odd(DisplayZone.Business)),
            new ProductSpec("glasses", "Gözlük", ProductCategory.Accessory, Both, true, 0.05f, Small(DisplayZone.Accessories)),
            new ProductSpec("sunglasses", "Güneş Gözlüğü", ProductCategory.Accessory, Both, false, 0.05f, Small(DisplayZone.Accessories)),
            new ProductSpec("travel-bag", "Ek Seyahat Çantası", ProductCategory.Leisure, Both, false, 0.4f, Bag(DisplayZone.Leisure)),
            new ProductSpec("socks", "Çorap", ProductCategory.Clothing, Both, true, 0.08f, FoldedGarment()),
            new ProductSpec("men-tshirt", "Erkek Tişört", ProductCategory.Clothing, Male, true, 0.2f, FoldedGarment()),
            new ProductSpec("women-tshirt", "Kadın Tişört", ProductCategory.Clothing, Female, true, 0.2f, FoldedGarment()),
            new ProductSpec("dress", "Elbise", ProductCategory.Clothing, Female, true, 0.3f, HangingGarment()),
            // Jewellery (OVERHAUL_PLAN §3): not required by default so the required count does not change.
            new ProductSpec("wristwatch", "Kol Saati", ProductCategory.Jewellery, Both, true, 0.08f, Small(DisplayZone.Jewellery), requiredOverride: false),
            new ProductSpec("cufflinks", "Kol Düğmesi", ProductCategory.Jewellery, Male, true, 0.02f, Small(DisplayZone.Jewellery), requiredOverride: false),
            new ProductSpec("pearl-earrings", "İnci Küpe", ProductCategory.Jewellery, Female, true, 0.01f, Small(DisplayZone.Jewellery), requiredOverride: false),
            new ProductSpec("minimal-necklace", "İnce Kolye", ProductCategory.Jewellery, Female, true, 0.02f, Small(DisplayZone.Jewellery), requiredOverride: false),
            new ProductSpec("shell-necklace", "Deniz Kabuğu Kolye", ProductCategory.Jewellery, Both, false, 0.05f, Small(DisplayZone.Jewellery), requiredOverride: false),
            new ProductSpec("party-tiara", "Parti Tacı", ProductCategory.Jewellery, Female, false, 0.1f, Small(DisplayZone.Jewellery), requiredOverride: false),
        };

        /// <summary>
        /// The tutorial practice item (MultiTravel luggage tag). Not part of the catalog: never scored, never in a session set.
        /// </summary>
        public static readonly ProductSpec PracticeSpec =
            new ProductSpec(PracticeId, "Deneme Etiketi", ProductCategory.Accessory, Both, true, 0.04f, Pres(DisplayZone.Any, PackedKind.Organiser, GripPreset.Dynamic),
                requiredOverride: false);

        /// <summary>Products that were part of earlier default data and are deleted (definition + prefab) by <see cref="Generate"/>.</summary>
        public static readonly IReadOnlyList<string> RetiredProductIds = new[] { "straw-hat", "crayons", "luggage-tag", "gamepad" };

        public static string ProductPath(string id)
        {
            return $"{GeneratedAssetUtil.ProductsFolder}/{id}.asset";
        }

        public static ProductDefinition LoadDefinition(string id)
        {
            return AssetDatabase.LoadAssetAtPath<ProductDefinition>(ProductPath(id));
        }

        /// <summary>The practice item definition (<see cref="PracticePath"/>), or null before generation.</summary>
        public static ProductDefinition LoadPracticeDefinition()
        {
            return AssetDatabase.LoadAssetAtPath<ProductDefinition>(PracticePath);
        }

        public static ProductCatalog LoadCatalog()
        {
            return AssetDatabase.LoadAssetAtPath<ProductCatalog>(CatalogPath);
        }

        public static AppConfig LoadAppConfig()
        {
            return AssetDatabase.LoadAssetAtPath<AppConfig>(AppConfigPath);
        }

        /// <summary>
        /// Generates product definitions, the catalog and AppConfig. With <paramref name="force"/> existing assets are reset to the
        /// defaults in place (prefab/sprite references and backend URL/key/access code are kept).
        /// </summary>
        public static GeneratorReport Generate(bool force = false)
        {
            var report = new GeneratorReport(force ? "Generate product data (force)" : "Generate product data");
            try
            {
                GeneratedAssetUtil.EnsureFolder(GeneratedAssetUtil.ProductsFolder);
                GeneratedAssetUtil.EnsureFolder(GeneratedAssetUtil.ResourcesFolder);

                var definitions = new List<ProductDefinition>(DefaultProducts.Count);
                var newlyCreated = new List<ProductDefinition>();
                foreach (var spec in DefaultProducts)
                {
                    var definition = EnsureDefinition(spec, force, report, out bool created);
                    if (definition != null)
                    {
                        definitions.Add(definition);
                        if (created)
                        {
                            newlyCreated.Add(definition);
                        }
                    }
                }

                RetireProducts(report);
                EnsureCatalog(definitions, newlyCreated, force, report);
                GeneratedAssetUtil.EnsureFolder(PracticeFolder);
                EnsureDefinition(PracticeSpec, PracticePath, force, report, out _);
                EnsureAppConfig(force, report);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                report.AddError(ex.ToString());
            }

            return report;
        }

        /// <summary>
        /// Assigns the visual prefab of product <paramref name="id"/> (used by the content generator). Returns false when the
        /// definition does not exist. The prefab should carry <c>ProductItem</c> on its root.
        /// </summary>
        public static bool AssignPrefab(string id, GameObject prefab)
        {
            var definition = LoadDefinition(id);
            if (definition == null)
            {
                Debug.LogError($"[MultiTravel] AssignPrefab: no ProductDefinition '{id}' at {ProductPath(id)}.");
                return false;
            }

            if (prefab != null && !EditorUtility.IsPersistent(prefab))
            {
                Debug.LogError($"[MultiTravel] AssignPrefab: '{prefab.name}' is not a prefab asset.");
                return false;
            }

            if (definition.VisualPrefab != prefab)
            {
                definition.VisualPrefab = prefab;
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
            }

            return true;
        }

        private static ProductDefinition EnsureDefinition(ProductSpec spec, bool force, GeneratorReport report, out bool created)
        {
            return EnsureDefinition(spec, ProductPath(spec.Id), force, report, out created);
        }

        private static ProductDefinition EnsureDefinition(ProductSpec spec, string path, bool force, GeneratorReport report, out bool created)
        {
            var definition = GeneratedAssetUtil.LoadOrCreateAsset(path, ScriptableObject.CreateInstance<ProductDefinition>, out created);
            if (created)
            {
                Apply(definition, spec);
                EditorUtility.SetDirty(definition);
                report.Created.Add(path);
            }
            else if (force)
            {
                Apply(definition, spec);
                EditorUtility.SetDirty(definition);
                report.Updated.Add(path);
            }
            else if (IsDefaultPresentation(definition.Presentation))
            {
                // Data added after the definition was first generated: fill it once, never over a customer edit.
                definition.Presentation = spec.Presentation.ToPresentation();
                EditorUtility.SetDirty(definition);
                report.Updated.Add($"{path} (presentation)");
            }
            else
            {
                report.Skipped.Add(path);
                if (definition.Id != spec.Id)
                {
                    report.Warnings.Add($"{path}: Id is '{definition.Id}', expected '{spec.Id}' (left unchanged; use force to reset).");
                }
            }

            return definition;
        }

        /// <summary>True when the presentation is missing or still at the <see cref="ProductPresentation"/> field defaults.</summary>
        public static bool IsDefaultPresentation(ProductPresentation presentation)
        {
            return presentation == null ||
                   (presentation.Zone == DisplayZone.Any && presentation.Packed == PackedKind.Flat &&
                    presentation.Grip == GripPreset.Dynamic && !presentation.HasHangingVariant);
        }

        private static void Apply(ProductDefinition d, ProductSpec spec)
        {
            d.Id = spec.Id;
            d.DisplayName = spec.DisplayName;
            d.Category = spec.Category;
            d.Availability = spec.Availability;
            d.IsCorrect = spec.IsCorrect;
            d.ScoreOverride = 0;
            d.ClearRequiredOverride(); // IsRequiredForCompletion follows IsCorrect
            if (spec.RequiredOverride.HasValue)
            {
                d.SetRequiredForCompletion(spec.RequiredOverride.Value);
            }

            d.Presentation = spec.Presentation.ToPresentation();
            d.Interaction ??= new ProductInteractionSettings();
            d.Interaction.Mass = spec.Mass;
            d.Interaction.GrabScale = 1f;
            d.Interaction.TwoHanded = spec.TwoHanded;
            d.Interaction.HoldOffset = Vector3.zero;
            // VisualPrefab / BrandSprite intentionally untouched (wired by the content generator / customer).
        }

        private static void RetireProducts(GeneratorReport report)
        {
            var catalog = LoadCatalog();
            foreach (var id in RetiredProductIds)
            {
                var definition = LoadDefinition(id);
                if (definition == null)
                {
                    continue;
                }

                if (catalog != null && catalog.Products != null && catalog.Products.Remove(definition))
                {
                    EditorUtility.SetDirty(catalog);
                }

                var prefabPath = $"{GeneratedAssetUtil.PrefabsFolder}/{id}.prefab";
                if (AssetDatabase.LoadMainAssetAtPath(prefabPath) != null)
                {
                    AssetDatabase.DeleteAsset(prefabPath);
                }

                AssetDatabase.DeleteAsset(ProductPath(id));
                report.Updated.Add($"{ProductPath(id)} (retired)");
            }
        }

        private static void EnsureCatalog(List<ProductDefinition> definitions, List<ProductDefinition> newlyCreated, bool force, GeneratorReport report)
        {
            var catalog = GeneratedAssetUtil.LoadOrCreateAsset(CatalogPath, ScriptableObject.CreateInstance<ProductCatalog>, out bool created);
            if (created || force)
            {
                var extras = new List<ProductDefinition>();
                if (!created && catalog.Products != null)
                {
                    foreach (var p in catalog.Products)
                    {
                        if (p != null && !definitions.Contains(p) && !extras.Contains(p))
                        {
                            extras.Add(p); // customer-added products survive a forced reset
                        }
                    }
                }

                catalog.ScenarioTitle = "Arabayla, 1 gece konaklamalı toplantı seyahati";
                catalog.DefaultPositiveScore = 10;
                catalog.DefaultNegativeScore = -5;
                catalog.ShuffleSpawnPositions = true;
                catalog.ShuffleSeedMode = ShuffleSeedMode.PerSession;
                catalog.Products = new List<ProductDefinition>(definitions);
                catalog.Products.AddRange(extras);
                EditorUtility.SetDirty(catalog);
                (created ? report.Created : report.Updated).Add(CatalogPath);
                return;
            }

            // Existing catalog, no force: only append products created in this run (a definition that already existed but is not
            // listed was removed on purpose and stays out).
            catalog.Products ??= new List<ProductDefinition>();
            int added = 0;
            foreach (var d in newlyCreated)
            {
                if (!catalog.Products.Contains(d))
                {
                    catalog.Products.Add(d);
                    added++;
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(catalog);
                report.Updated.Add($"{CatalogPath} (+{added} new product(s))");
            }
            else
            {
                report.Skipped.Add(CatalogPath);
            }
        }

        private static void EnsureAppConfig(bool force, GeneratorReport report)
        {
            var config = GeneratedAssetUtil.LoadOrCreateAsset(AppConfigPath, ScriptableObject.CreateInstance<AppConfig>, out bool created);
            if (!created && !force)
            {
                report.Skipped.Add(AppConfigPath);
                return;
            }

            // Preserve deployment secrets / hand-wired references across a forced reset.
            string url = created ? string.Empty : config.Backend?.SupabaseUrl ?? string.Empty;
            string key = created ? string.Empty : config.Backend?.SupabaseAnonKey ?? string.Empty;
            string accessCode = created ? string.Empty : config.Backend?.EventAccessCode ?? string.Empty;
            Sprite logo = created ? null : config.Branding?.LogoSprite;

            config.Backend = new AppConfig.BackendSection
            {
                SupabaseUrl = url,
                SupabaseAnonKey = key,
                EventSlug = "multitravel-2026",
                EventAccessCode = accessCode,
                StationId = "VR-01",
                RequestTimeoutSeconds = 10,
                MaxAutoRetries = 5
            };

            config.Gameplay = new AppConfig.GameplaySection
            {
                CompletionMode = CompletionMode.RequiredItemsPlaced,
                TimeLimitSeconds = 0,
                RevertScoreOnRemoval = true,
                CountdownSeconds = 3,
                EnableLocomotion = true,
                ShuffleSpawnPositions = true
            };

            config.Texts = new AppConfig.TextsSection
            {
                WelcomeTitle = "MultiTravel Valiz Challenge",
                WelcomeSubtitle = "Toplantı seyahatin için valizini hazırla!",
                InstructionsText =
                    "Bu seyahat için gerekli ürünleri bul ve valize koy.\n" +
                    "Doğru ürün: +10 puan. Yanlış ürün: -5 puan.\n" +
                    "Gerekli tüm ürünler valize girince oyun biter. Hızlı ol!",
                ScenarioText = "Arabayla, 1 gece konaklamalı toplantı seyahati"
            };

            config.Privacy = new AppConfig.PrivacySection
            {
                ConsentText = string.Empty,
                ConsentVersion = "1.0"
            };

            config.Branding = new AppConfig.BrandingSection
            {
                ProductTitle = "MultiTravel Valiz Challenge",
                LogoSprite = logo != null ? logo : BrandAssets.LoadSprite(BrandAssets.HorizontalLight),
                PrimaryColor = MaterialLibrary.Colors.BrandDeepBlue,
                AccentColor = MaterialLibrary.Colors.Orange
            };

            config.Debug = new AppConfig.DebugSection
            {
                EnableDeviceSimulatorWhenNoHmd = true
            };

            EditorUtility.SetDirty(config);
            (created ? report.Created : report.Updated).Add(AppConfigPath);
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
            {
                report.Warnings.Add("AppConfig backend URL / anon key are empty: fill them in (or use the runtime JSON override) before the event.");
            }
        }
    }
}
