using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.Core.Session;
using MultiTravel.Tests.EditMode.Fakes;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    public sealed class ProductCatalogTests
    {
        private ScriptableObjectFactory so;
        private ProductSetResolver resolver;

        [SetUp]
        public void SetUp()
        {
            so = new ScriptableObjectFactory();
            resolver = new ProductSetResolver();
        }

        [TearDown]
        public void TearDown()
        {
            so.DestroyAll();
        }

        [Test]
        public void ScoreFor_UsesOverride_OrDefaultBySign()
        {
            var catalog = so.Catalog();
            catalog.DefaultPositiveScore = 10;
            catalog.DefaultNegativeScore = -5;

            Assert.AreEqual(10, catalog.ScoreFor(so.Product("a", true)));
            Assert.AreEqual(-5, catalog.ScoreFor(so.Product("b", false)));
            Assert.AreEqual(25, catalog.ScoreFor(so.Product("c", true, scoreOverride: 25)));
            Assert.AreEqual(-1, catalog.ScoreFor(so.Product("d", false, scoreOverride: -1)));
        }

        [Test]
        public void ForGender_FiltersByAvailability()
        {
            var common = so.Product("laptop", true, GenderAvailability.Both);
            var female = so.Product("blouse", true, GenderAvailability.Female);
            var male = so.Product("tie", true, GenderAvailability.Male);
            var nobody = so.Product("ghost", true, GenderAvailability.None);
            var catalog = so.Catalog(common, female, male, nobody);
            catalog.Products.Add(null);

            var forFemale = catalog.ForGender(Gender.Female);
            var forMale = catalog.ForGender(Gender.Male);

            CollectionAssert.AreEquivalent(new[] { common, female }, forFemale);
            CollectionAssert.AreEquivalent(new[] { common, male }, forMale);
        }

        [Test]
        public void Resolver_BuildsItemsAndRequired()
        {
            var laptop = so.Product("laptop", true, GenderAvailability.Both);
            var blouse = so.Product("blouse", true, GenderAvailability.Female);
            var optional = so.Product("notebook", true, GenderAvailability.Both, required: false);
            var towel = so.Product("beach-towel", false, GenderAvailability.Both);
            var tie = so.Product("tie", true, GenderAvailability.Male);
            var catalog = so.Catalog(laptop, blouse, optional, towel, tie);

            var set = resolver.Resolve(catalog, Gender.Female);

            Assert.AreEqual(Gender.Female, set.Gender);
            CollectionAssert.AreEquivalent(new[] { laptop, blouse, optional, towel }, set.Items);
            CollectionAssert.AreEquivalent(new[] { laptop, blouse }, set.Required);
            CollectionAssert.AreEquivalent(new[] { "laptop", "blouse" }, set.RequiredIds);
            Assert.IsTrue(set.IsRequired("laptop"));
            Assert.IsFalse(set.IsRequired("notebook"));
            Assert.IsFalse(set.IsRequired("tie"));
            Assert.AreSame(towel, set.Find("beach-towel"));
            Assert.IsNull(set.Find("tie"));
        }

        [Test]
        public void Resolver_IncorrectProductsAreNeverRequired()
        {
            var towel = so.Product("beach-towel", false, required: true);
            var set = resolver.Resolve(so.Catalog(towel), Gender.Male);

            Assert.AreEqual(1, set.Items.Count);
            Assert.AreEqual(0, set.Required.Count);
        }

        [Test]
        public void RequiredDefault_FollowsIsCorrect_UntilExplicitlySet()
        {
            var product = so.Product("laptop", false);
            Assert.IsFalse(product.IsRequiredForCompletion);
            Assert.IsFalse(product.RequiredExplicitlySet);

            product.IsCorrect = true;
            product.SyncRequiredDefault();
            Assert.IsTrue(product.IsRequiredForCompletion, "required follows IsCorrect while not explicit");

            product.IsRequiredForCompletion = false;
            product.SyncRequiredDefault();
            Assert.IsTrue(product.RequiredExplicitlySet, "a manual change marks the flag explicit");
            Assert.IsFalse(product.IsRequiredForCompletion);

            product.IsCorrect = false;
            product.SyncRequiredDefault();
            product.IsCorrect = true;
            product.SyncRequiredDefault();
            Assert.IsFalse(product.IsRequiredForCompletion, "explicit value no longer follows IsCorrect");

            product.ClearRequiredOverride();
            Assert.IsFalse(product.RequiredExplicitlySet);
            Assert.IsTrue(product.IsRequiredForCompletion);
        }

        [Test]
        public void Validate_ReportsDuplicateIds_MissingPrefabs_AndMissingRequired()
        {
            var a = so.Product("laptop", true, GenderAvailability.Female, withPrefab: true);
            var duplicate = so.Product("laptop", false, GenderAvailability.Both, withPrefab: true);
            var noPrefab = so.Product("pen", true, GenderAvailability.Female);
            var badId = so.Product("Bad_Id", false, GenderAvailability.Both, withPrefab: true);
            var catalog = so.Catalog(a, duplicate, noPrefab, badId);

            var errors = new List<string>();
            Assert.IsFalse(catalog.Validate(errors));

            Assert.IsTrue(errors.Exists(e => e.Contains("duplicate Id")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("pen") && e.Contains("VisualPrefab")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("Bad_Id") && e.Contains("kebab-case")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("Male")), "no required product for Male: " + string.Join("\n", errors));
            Assert.IsFalse(errors.Exists(e => e.Contains("gender Female")), "Female has required products");
        }

        [Test]
        public void Validate_EmptyCatalog_Fails()
        {
            var errors = new List<string>();
            Assert.IsFalse(so.Catalog().Validate(errors));
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void Validate_NullEntryAndBadDefaults_AreReported()
        {
            var catalog = so.Catalog(so.Product("laptop", true, withPrefab: true));
            catalog.Products.Add(null);
            catalog.DefaultPositiveScore = 0;
            catalog.DefaultNegativeScore = 5;

            var errors = new List<string>();
            Assert.IsFalse(catalog.Validate(errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("is null")));
            Assert.IsTrue(errors.Exists(e => e.Contains("DefaultPositiveScore")));
            Assert.IsTrue(errors.Exists(e => e.Contains("DefaultNegativeScore")));
        }

        [Test]
        public void Validate_WellFormedCatalog_Passes()
        {
            var catalog = so.Catalog(
                so.Product("laptop", true, withPrefab: true, displayName: "Dizüstü Bilgisayar"),
                so.Product("blouse", true, GenderAvailability.Female, withPrefab: true, displayName: "Bluz"),
                so.Product("tie", true, GenderAvailability.Male, withPrefab: true, displayName: "Kravat"),
                so.Product("beach-towel", false, withPrefab: true, displayName: "Plaj Havlusu"));

            var errors = new List<string>();
            Assert.IsTrue(catalog.Validate(errors), string.Join("\n", errors));
            Assert.AreEqual(0, errors.Count);
            Assert.AreSame(catalog.Products[2], catalog.FindById("tie"));
            Assert.IsNull(catalog.FindById("nope"));
        }
    }
}
