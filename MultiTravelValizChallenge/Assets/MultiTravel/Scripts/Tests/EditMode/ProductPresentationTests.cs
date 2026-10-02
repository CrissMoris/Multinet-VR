using System.Collections.Generic;
using System.Linq;
using MultiTravel.Core.Products;
using MultiTravel.Core.Session;
using MultiTravel.EditorTools.Data;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Feedback;
using MultiTravel.Gameplay.Items;
using MultiTravel.Tests.EditMode.Fakes;
using NUnit.Framework;
using UnityEditor;

namespace MultiTravel.Tests.EditMode
{
    /// <summary>
    /// Zone mapping, presentation data, jewellery data and zone capacity (OVERHAUL_PLAN §3, §7).
    /// </summary>
    public sealed class ProductPresentationTests
    {
        /// <summary>
        /// Planned dressing-room slot counts per zone (OVERHAUL_PLAN §2/§3). The scene builder validates the real counts with
        /// <see cref="ZoneCapacityValidator"/>; this map documents the minimum layout the default data needs.
        /// </summary>
        public static readonly IReadOnlyDictionary<DisplayZone, int> PlannedSlotCounts = new Dictionary<DisplayZone, int>
        {
            { DisplayZone.Hanging, 8 },
            { DisplayZone.Folded, 9 },
            { DisplayZone.Shoes, 6 },
            { DisplayZone.Accessories, 8 },
            { DisplayZone.Jewellery, 8 },
            { DisplayZone.Business, 14 },
            { DisplayZone.Leisure, 12 },
        };

        private static readonly Dictionary<DisplayZone, string[]> ZoneTable = new Dictionary<DisplayZone, string[]>
        {
            { DisplayZone.Hanging, new[] { "shirt", "blouse", "jacket", "blazer", "dress", "swimsuit", "bikini" } },
            { DisplayZone.Folded, new[] { "men-trousers", "women-trousers", "men-tshirt", "women-tshirt", "socks", "swim-shorts", "beach-towel" } },
            { DisplayZone.Shoes, new[] { "men-shoes", "women-shoes", "flip-flops" } },
            { DisplayZone.Accessories, new[] { "tie", "glasses", "sunglasses", "beach-hat", "toiletry-bag" } },
            { DisplayZone.Jewellery, new[] { "wristwatch", "cufflinks", "pearl-earrings", "minimal-necklace", "shell-necklace", "party-tiara" } },
            { DisplayZone.Business, new[] { "laptop", "laptop-bag", "laptop-charger", "phone-cable", "phone", "notebook", "pen", "id-card", "passport", "headphones" } },
            { DisplayZone.Leisure, new[] { "snorkel-mask", "rubber-duck", "football", "ukulele", "garden-gnome", "binoculars", "kids-book", "neck-pillow", "travel-bag" } },
        };

        private ScriptableObjectFactory so;

        [SetUp]
        public void SetUp()
        {
            so = new ScriptableObjectFactory();
        }

        [TearDown]
        public void TearDown()
        {
            so.DestroyAll();
        }

        private static ProductDataGenerator.ProductSpec Spec(string id)
        {
            var spec = ProductDataGenerator.DefaultProducts.FirstOrDefault(p => p.Id == id);
            Assert.IsNotNull(spec, "no default product '" + id + "'");
            return spec;
        }

        private static IEnumerable<ZoneCapacityValidator.Entry> DefaultEntries()
        {
            return ProductDataGenerator.DefaultProducts.Select(p => new ZoneCapacityValidator.Entry(p.Id, p.Presentation.Zone, p.Availability));
        }

        private ProductDefinition Definition(ProductDataGenerator.ProductSpec spec)
        {
            var definition = so.Product(spec.Id, spec.IsCorrect, spec.Availability, required: spec.RequiredOverride, displayName: spec.DisplayName);
            definition.Category = spec.Category;
            definition.Presentation = spec.Presentation.ToPresentation();
            return definition;
        }

        // ----- presentation defaults -----

        [Test]
        public void Presentation_Defaults_AreAnyFlatDynamic()
        {
            var presentation = new ProductPresentation();
            Assert.AreEqual(DisplayZone.Any, presentation.Zone);
            Assert.AreEqual(PackedKind.Flat, presentation.Packed);
            Assert.AreEqual(GripPreset.Dynamic, presentation.Grip);
            Assert.IsFalse(presentation.HasHangingVariant);
            Assert.IsTrue(ProductDataGenerator.IsDefaultPresentation(presentation));
            Assert.IsTrue(ProductDataGenerator.IsDefaultPresentation(null));

            var definition = so.Product("x", true);
            Assert.IsNotNull(definition.Presentation, "a new definition carries a presentation object");
        }

        [Test]
        public void Presentation_EffectiveZone_FallsBackToCategory()
        {
            var shoe = so.Product("shoe", true);
            shoe.Category = ProductCategory.Shoes;
            Assert.AreEqual(DisplayZone.Shoes, PresentationRules.EffectiveZone(shoe));

            shoe.Presentation.Zone = DisplayZone.Leisure;
            Assert.AreEqual(DisplayZone.Leisure, PresentationRules.EffectiveZone(shoe), "explicit zone wins");

            var other = so.Product("other", false);
            other.Category = ProductCategory.Other;
            Assert.AreEqual(DisplayZone.Any, PresentationRules.EffectiveZone(other));
            Assert.AreEqual(DisplayZone.Jewellery, ProductPresentation.DefaultZoneFor(ProductCategory.Jewellery));
            Assert.AreEqual(DisplayZone.Any, PresentationRules.EffectiveZone(null));
        }

        // ----- zone mapping -----

        [Test]
        public void DefaultProducts_EveryProductHasANamedZone()
        {
            var errors = new List<string>();
            Assert.IsTrue(ZoneCapacityValidator.ValidateZonesAssigned(DefaultEntries(), errors), string.Join("\n", errors));
        }

        [Test]
        public void DefaultProducts_ZonesMatchTheOverhaulTable()
        {
            var expected = new Dictionary<string, DisplayZone>();
            foreach (var pair in ZoneTable)
            {
                foreach (var id in pair.Value)
                {
                    expected.Add(id, pair.Key);
                }
            }

            CollectionAssert.AreEquivalent(expected.Keys, ProductDataGenerator.DefaultProducts.Select(p => p.Id), "every product is in exactly one zone");
            foreach (var spec in ProductDataGenerator.DefaultProducts)
            {
                Assert.AreEqual(expected[spec.Id], spec.Presentation.Zone, spec.Id);
            }
        }

        [Test]
        public void DefaultProducts_PackedGripAndVariantFollowTheTable()
        {
            foreach (var id in new[] { "shirt", "blouse", "jacket", "blazer", "dress", "swimsuit", "bikini" })
            {
                AssertPresentation(id, PackedKind.Flat, GripPreset.Hanger, true);
            }

            foreach (var id in new[] { "men-trousers", "women-trousers", "men-tshirt", "women-tshirt", "socks", "swim-shorts", "beach-towel" })
            {
                AssertPresentation(id, PackedKind.Flat, GripPreset.FoldedGarment, false);
            }

            foreach (var id in new[] { "men-shoes", "women-shoes", "flip-flops" })
            {
                AssertPresentation(id, PackedKind.ShoeCorner, GripPreset.Shoe, false);
            }

            foreach (var id in new[] { "toiletry-bag", "laptop-bag", "travel-bag" })
            {
                AssertPresentation(id, PackedKind.Upright, GripPreset.Handle, false);
            }

            foreach (var id in new[] { "laptop", "notebook", "kids-book" })
            {
                AssertPresentation(id, PackedKind.Flat, GripPreset.FlatEdge, false);
            }

            foreach (var id in new[] { "passport", "id-card", "phone" })
            {
                AssertPresentation(id, PackedKind.LidPocket, GripPreset.FlatEdge, false);
            }

            foreach (var id in new[] { "pen", "phone-cable", "laptop-charger", "glasses", "sunglasses", "tie" }.Concat(ZoneTable[DisplayZone.Jewellery]))
            {
                AssertPresentation(id, PackedKind.Organiser, GripPreset.Dynamic, false);
            }

            foreach (var id in new[] { "garden-gnome", "rubber-duck", "football", "ukulele", "binoculars", "snorkel-mask", "neck-pillow", "headphones", "beach-hat" })
            {
                AssertPresentation(id, PackedKind.Top, GripPreset.Dynamic, false);
            }
        }

        private static void AssertPresentation(string id, PackedKind packed, GripPreset grip, bool hanging)
        {
            var presentation = Spec(id).Presentation;
            Assert.AreEqual(packed, presentation.Packed, id + " packed");
            Assert.AreEqual(grip, presentation.Grip, id + " grip");
            Assert.AreEqual(hanging, presentation.HasHangingVariant, id + " hanging variant");
        }

        // ----- jewellery -----

        [Test]
        public void Jewellery_SixProducts_WithTurkishNamesAvailabilityAndCorrectness()
        {
            var expected = new[]
            {
                ("wristwatch", "Kol Saati", GenderAvailability.Both, true),
                ("cufflinks", "Kol Düğmesi", GenderAvailability.Male, true),
                ("pearl-earrings", "İnci Küpe", GenderAvailability.Female, true),
                ("minimal-necklace", "İnce Kolye", GenderAvailability.Female, true),
                ("shell-necklace", "Deniz Kabuğu Kolye", GenderAvailability.Both, false),
                ("party-tiara", "Parti Tacı", GenderAvailability.Female, false),
            };

            var jewellery = ProductDataGenerator.DefaultProducts.Where(p => p.Category == ProductCategory.Jewellery).ToList();
            Assert.AreEqual(expected.Length, jewellery.Count);
            foreach (var (id, name, availability, correct) in expected)
            {
                var spec = Spec(id);
                Assert.AreEqual(ProductCategory.Jewellery, spec.Category, id);
                Assert.AreEqual(name, spec.DisplayName, id);
                Assert.AreEqual(availability, spec.Availability, id);
                Assert.AreEqual(correct, spec.IsCorrect, id);
                Assert.AreEqual(false, spec.RequiredOverride, id + " must not be required by default");
                Assert.AreEqual(DisplayZone.Jewellery, spec.Presentation.Zone, id);

                var definition = Definition(spec);
                Assert.IsFalse(definition.IsEffectivelyRequired, id);
                Assert.IsTrue(definition.RequiredExplicitlySet, id + " uses SetRequiredForCompletion(false)");
            }
        }

        [Test]
        public void Jewellery_DoesNotChangeTheRequiredCountPerGender()
        {
            var definitions = ProductDataGenerator.DefaultProducts.Select(Definition).ToArray();
            var catalog = so.Catalog(definitions);
            var resolver = new ProductSetResolver();
            Assert.AreEqual(17, resolver.Resolve(catalog, Gender.Female).Required.Count);
            Assert.AreEqual(17, resolver.Resolve(catalog, Gender.Male).Required.Count);
            Assert.IsTrue(resolver.Resolve(catalog, Gender.Female).Items.Any(p => p.Id == "pearl-earrings"));
            Assert.IsFalse(resolver.Resolve(catalog, Gender.Male).Items.Any(p => p.Id == "pearl-earrings"));
        }

        [Test]
        public void PracticeItem_IsNotACatalogProduct()
        {
            var practice = ProductDataGenerator.PracticeSpec;
            Assert.AreEqual("practice-tag", practice.Id);
            Assert.AreEqual("Deneme Etiketi", practice.DisplayName);
            Assert.IsFalse(ProductDataGenerator.DefaultProducts.Any(p => p.Id == practice.Id));
            StringAssert.StartsWith("Assets/MultiTravel/Data/Practice/", ProductDataGenerator.PracticePath);
        }

        // ----- capacity -----

        [Test]
        public void ZoneCapacity_DefaultDataFitsThePlannedLayout_PerGender()
        {
            var errors = new List<string>();
            Assert.IsTrue(ZoneCapacityValidator.Validate(DefaultEntries(), PlannedSlotCounts, errors), string.Join("\n", errors));

            var minimum = ZoneCapacityValidator.MinimumSlotCounts(DefaultEntries());
            foreach (var pair in minimum)
            {
                Assert.LessOrEqual(pair.Value, PlannedSlotCounts[pair.Key], pair.Key.ToString());
            }

            // Hand-checked worst case per zone (OVERHAUL_PLAN §3): female hanging 5, male folded 5, business 10, leisure 9.
            Assert.AreEqual(7, minimum[DisplayZone.Hanging]);
            Assert.AreEqual(7, minimum[DisplayZone.Folded]);
            Assert.AreEqual(13, minimum[DisplayZone.Business]);
            Assert.AreEqual(12, minimum[DisplayZone.Leisure]);
        }

        [Test]
        public void ZoneCapacity_ReportsFullZonesMissingZonesAndAnyItems()
        {
            var entries = new[]
            {
                new ZoneCapacityValidator.Entry("a", DisplayZone.Shoes, GenderAvailability.Both),
                new ZoneCapacityValidator.Entry("b", DisplayZone.Shoes, GenderAvailability.Female),
                new ZoneCapacityValidator.Entry("c", DisplayZone.Shoes, GenderAvailability.Female),
                new ZoneCapacityValidator.Entry("d", DisplayZone.Leisure, GenderAvailability.Male),
                new ZoneCapacityValidator.Entry("e", DisplayZone.Any, GenderAvailability.Both),
            };
            var slots = new Dictionary<DisplayZone, int> { { DisplayZone.Shoes, 3 }, { DisplayZone.Any, 2 } };

            var errors = new List<string>();
            Assert.IsFalse(ZoneCapacityValidator.Validate(entries, slots, errors));
            Assert.IsTrue(errors.Any(e => e.Contains("Female") && e.Contains("Shoes")), "3 female shoes on 3 slots exceed 80 %");
            Assert.IsFalse(errors.Any(e => e.Contains("Male") && e.Contains("Shoes")), "1 male shoe on 3 slots is fine");
            Assert.IsTrue(errors.Any(e => e.Contains("Male") && e.Contains("Leisure") && e.Contains("no spawn slot")));

            var zoneErrors = new List<string>();
            Assert.IsFalse(ZoneCapacityValidator.ValidateZonesAssigned(entries, zoneErrors));
            Assert.AreEqual(1, zoneErrors.Count);
            StringAssert.Contains("e:", zoneErrors[0]);

            Assert.AreEqual(4, ZoneCapacityValidator.RequiredSlots(3));
            Assert.AreEqual(5, ZoneCapacityValidator.RequiredSlots(4));
            Assert.AreEqual(0, ZoneCapacityValidator.RequiredSlots(0));
        }

        [Test]
        public void ZoneCapacity_ValidatesProductDefinitionsWithEffectiveZones()
        {
            var shoe = so.Product("shoe", true);
            shoe.Category = ProductCategory.Shoes;
            var errors = new List<string>();
            Assert.IsTrue(ZoneCapacityValidator.Validate(new[] { shoe }, new Dictionary<DisplayZone, int> { { DisplayZone.Shoes, 2 } }, errors),
                string.Join("\n", errors));
        }

        // ----- generated catalog asset -----

        [Test]
        public void CatalogAsset_EveryProductHasANamedZone_AndTheJewellery()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ProductCatalog>(ProductDataGenerator.CatalogPath);
            if (catalog == null)
            {
                Assert.Ignore("ProductCatalog asset not generated yet (MultiTravel/Generate/Product Data).");
            }

            foreach (var product in catalog.Products)
            {
                Assert.IsNotNull(product);
                Assert.AreNotEqual(DisplayZone.Any, product.Presentation.Zone, product.Id + ": run MultiTravel/Generate/Product Data");
            }

            foreach (var id in ZoneTable[DisplayZone.Jewellery])
            {
                var product = catalog.FindById(id);
                Assert.IsNotNull(product, id + " must be in the catalog");
                Assert.AreEqual(ProductCategory.Jewellery, product.Category);
            }

            Assert.IsNull(catalog.FindById(ProductDataGenerator.PracticeId), "the practice item is not a catalog product");
        }

        // ----- foley mapping -----

        [Test]
        public void SoundKinds_FollowMaterial()
        {
            Assert.AreEqual(SoundKind.Metal, ProductSoundKinds.For(Definition(Spec("wristwatch"))));
            Assert.AreEqual(SoundKind.Leather, ProductSoundKinds.For(Definition(Spec("men-shoes"))));
            Assert.AreEqual(SoundKind.Leather, ProductSoundKinds.For(Definition(Spec("laptop-bag"))));
            Assert.AreEqual(SoundKind.Paper, ProductSoundKinds.For(Definition(Spec("notebook"))));
            Assert.AreEqual(SoundKind.Paper, ProductSoundKinds.For(Definition(Spec("passport"))));
            Assert.AreEqual(SoundKind.Cloth, ProductSoundKinds.For(Definition(Spec("beach-towel"))));
            Assert.AreEqual(SoundKind.Cloth, ProductSoundKinds.For(Definition(Spec("shirt"))));
            Assert.AreEqual(SoundKind.Hard, ProductSoundKinds.For(Definition(Spec("laptop"))));
            Assert.AreEqual(SoundKind.Hard, ProductSoundKinds.For(Definition(Spec("football"))));
            Assert.AreEqual(SoundKind.Hard, ProductSoundKinds.For(null));
            Assert.IsTrue(ProductSoundKinds.IsSoft(Definition(Spec("socks"))));
            Assert.IsTrue(ProductSoundKinds.IsSoft(Definition(Spec("beach-towel"))));
            Assert.IsFalse(ProductSoundKinds.IsSoft(Definition(Spec("laptop"))));
        }
    }
}
