using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Tests.EditMode.Fakes;
using NUnit.Framework;
using UnityEngine;

namespace MultiTravel.Tests.EditMode
{
    public sealed class ConfigLoaderTests
    {
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

        private AppConfig AssetWithDefaults()
        {
            var asset = so.AppConfig();
            asset.Backend.SupabaseUrl = "https://asset.example.invalid";
            asset.Backend.SupabaseAnonKey = "asset-key";
            asset.Backend.EventSlug = "asset-slug";
            asset.Backend.EventAccessCode = "asset-code";
            asset.Backend.StationId = "asset-station";
            return asset;
        }

        [Test]
        public void NoOverrides_UsesAssetValues()
        {
            var config = ConfigLoader.Load(AssetWithDefaults(), null, null, "1.0.0");

            Assert.AreEqual("asset-slug", config.Backend.EventSlug);
            Assert.AreEqual("asset-station", config.Backend.StationId);
            Assert.AreEqual(10, config.Backend.RequestTimeoutSeconds);
            Assert.AreEqual(5, config.Backend.MaxAutoRetries);
            Assert.AreEqual("1.0.0", config.ClientVersion);
            Assert.AreEqual(0, config.AppliedSources.Count);
            Assert.AreEqual(0, config.Warnings.Count);
            Assert.IsTrue(config.Backend.IsConfigured);
        }

        [Test]
        public void NullAsset_UsesBuiltInDefaults()
        {
            var config = ConfigLoader.Load(null, null, null);

            Assert.AreEqual(CompletionMode.RequiredItemsPlaced, config.Gameplay.CompletionMode);
            Assert.AreEqual(0, config.Gameplay.TimeLimitSeconds);
            Assert.IsTrue(config.Gameplay.RevertScoreOnRemoval);
            Assert.AreEqual(3, config.Gameplay.CountdownSeconds);
            Assert.IsFalse(config.Gameplay.EnableLocomotion);
            Assert.IsTrue(config.Gameplay.ShuffleSpawnPositions);
            Assert.AreEqual("MultiTravel Valiz Challenge", config.Branding.ProductTitle);
            Assert.IsFalse(config.Privacy.IsConsentRequired);
            Assert.IsTrue(config.Debug.EnableDeviceSimulatorWhenNoHmd);
            Assert.IsFalse(config.Backend.IsConfigured);
            Assert.IsFalse(string.IsNullOrEmpty(config.Backend.StationId), "station id falls back to the machine name");
            Assert.AreEqual("0.0.0", config.ClientVersion);
        }

        [Test]
        public void StreamingOverride_IsApplied()
        {
            const string json = "{ \"backend\": { \"eventSlug\": \"stream-slug\", \"requestTimeoutSeconds\": 20 }," +
                                "  \"gameplay\": { \"completionMode\": \"ManualConfirm\", \"timeLimitSeconds\": 120, \"enableLocomotion\": true }," +
                                "  \"texts\": { \"welcomeTitle\": \"Merhaba\" }," +
                                "  \"privacy\": { \"consentText\": \"KVKK metni\", \"consentVersion\": \"2026-01\" }," +
                                "  \"debug\": { \"enableDeviceSimulatorWhenNoHmd\": false } }";

            var config = ConfigLoader.Load(AssetWithDefaults(), json, null);

            Assert.AreEqual("stream-slug", config.Backend.EventSlug);
            Assert.AreEqual(20, config.Backend.RequestTimeoutSeconds);
            Assert.AreEqual("asset-code", config.Backend.EventAccessCode, "untouched keys keep the asset value");
            Assert.AreEqual(CompletionMode.ManualConfirm, config.Gameplay.CompletionMode);
            Assert.AreEqual(120, config.Gameplay.TimeLimitSeconds);
            Assert.IsTrue(config.Gameplay.EnableLocomotion);
            Assert.AreEqual("Merhaba", config.Texts.WelcomeTitle);
            Assert.AreEqual("KVKK metni", config.Privacy.ConsentText);
            Assert.AreEqual("2026-01", config.Privacy.ConsentVersion);
            Assert.IsTrue(config.Privacy.IsConsentRequired);
            Assert.IsFalse(config.Debug.EnableDeviceSimulatorWhenNoHmd);
            CollectionAssert.AreEqual(new[] { ConfigLoader.StreamingSourceName }, config.AppliedSources);
            Assert.AreEqual(0, config.Warnings.Count);
        }

        [Test]
        public void PersistentOverride_WinsOverStreaming()
        {
            var config = ConfigLoader.Load(
                AssetWithDefaults(),
                "{ \"backend\": { \"eventSlug\": \"a\", \"stationId\": \"s1\" } }",
                "{ \"backend\": { \"eventSlug\": \"b\" } }");

            Assert.AreEqual("b", config.Backend.EventSlug);
            Assert.AreEqual("s1", config.Backend.StationId, "keys only in the earlier source survive");
            CollectionAssert.AreEqual(new[] { ConfigLoader.StreamingSourceName, ConfigLoader.PersistentSourceName }, config.AppliedSources);
        }

        [Test]
        public void InvalidJson_WarnsAndKeepsDefaults()
        {
            var config = ConfigLoader.Load(AssetWithDefaults(), "{ this is not json", null);

            Assert.AreEqual("asset-slug", config.Backend.EventSlug);
            Assert.AreEqual(0, config.AppliedSources.Count);
            Assert.AreEqual(1, config.Warnings.Count);
            StringAssert.Contains("invalid JSON", config.Warnings[0]);
        }

        [Test]
        public void NonObjectJson_WarnsAndIsIgnored()
        {
            var config = ConfigLoader.Load(AssetWithDefaults(), "[1, 2, 3]", null);

            Assert.AreEqual(0, config.AppliedSources.Count);
            Assert.AreEqual(1, config.Warnings.Count);
        }

        [Test]
        public void WrongValueTypes_WarnAndKeepDefaults()
        {
            const string json = "{ \"backend\": { \"requestTimeoutSeconds\": \"abc\", \"eventSlug\": { \"nested\": 1 } }," +
                                "  \"gameplay\": { \"revertScoreOnRemoval\": \"maybe\", \"completionMode\": \"Nope\" }," +
                                "  \"texts\": 42 }";

            var config = ConfigLoader.Load(AssetWithDefaults(), json, null);

            Assert.AreEqual(10, config.Backend.RequestTimeoutSeconds);
            Assert.AreEqual("asset-slug", config.Backend.EventSlug);
            Assert.IsTrue(config.Gameplay.RevertScoreOnRemoval);
            Assert.AreEqual(CompletionMode.RequiredItemsPlaced, config.Gameplay.CompletionMode);
            Assert.AreEqual(5, config.Warnings.Count, string.Join("\n", config.Warnings));
        }

        [Test]
        public void Keys_AreCaseInsensitive_AndNullsAreSkipped()
        {
            var config = ConfigLoader.Load(AssetWithDefaults(), "{ \"Backend\": { \"EventSlug\": \"x\", \"stationId\": null } }", null);

            Assert.AreEqual("x", config.Backend.EventSlug);
            Assert.AreEqual("asset-station", config.Backend.StationId);
            Assert.AreEqual(0, config.Warnings.Count);
        }

        [Test]
        public void Enum_AcceptsCaseInsensitiveName_AndInteger()
        {
            var byName = ConfigLoader.Load(null, "{ \"gameplay\": { \"completionMode\": \"requireditemsormanual\" } }", null);
            Assert.AreEqual(CompletionMode.RequiredItemsOrManual, byName.Gameplay.CompletionMode);

            var byNumber = ConfigLoader.Load(null, "{ \"gameplay\": { \"completionMode\": 1 } }", null);
            Assert.AreEqual(CompletionMode.ManualConfirm, byNumber.Gameplay.CompletionMode);

            var invalid = ConfigLoader.Load(null, "{ \"gameplay\": { \"completionMode\": 99 } }", null);
            Assert.AreEqual(CompletionMode.RequiredItemsPlaced, invalid.Gameplay.CompletionMode);
            Assert.AreEqual(1, invalid.Warnings.Count);
        }

        [Test]
        public void Colors_AreParsedFromHtml()
        {
            var config = ConfigLoader.Load(null, "{ \"branding\": { \"primaryColor\": \"#FF0000\", \"accentColor\": \"00FF00\", \"productTitle\": \"X\" } }", null);

            Assert.AreEqual(Color.red, config.Branding.PrimaryColor);
            Assert.AreEqual(Color.green, config.Branding.AccentColor);
            Assert.AreEqual("X", config.Branding.ProductTitle);

            var invalid = ConfigLoader.Load(null, "{ \"branding\": { \"primaryColor\": \"not-a-color\" } }", null);
            Assert.AreEqual(1, invalid.Warnings.Count);
        }

        [Test]
        public void NumbersAcceptStringsAndFloats()
        {
            var config = ConfigLoader.Load(null, "{ \"backend\": { \"requestTimeoutSeconds\": \"15\", \"maxAutoRetries\": 2.6 }, \"gameplay\": { \"revertScoreOnRemoval\": 0 } }", null);

            Assert.AreEqual(15, config.Backend.RequestTimeoutSeconds);
            Assert.AreEqual(3, config.Backend.MaxAutoRetries);
            Assert.IsFalse(config.Gameplay.RevertScoreOnRemoval);
            Assert.AreEqual(0, config.Warnings.Count);
        }

        [Test]
        public void OutOfRangeValues_AreSanitizedWithWarnings()
        {
            const string json = "{ \"backend\": { \"requestTimeoutSeconds\": 0, \"maxAutoRetries\": -1 }," +
                                "  \"gameplay\": { \"countdownSeconds\": -5, \"timeLimitSeconds\": -1 }," +
                                "  \"privacy\": { \"consentText\": \"metin\", \"consentVersion\": \"\" } }";

            var config = ConfigLoader.Load(null, json, null);

            Assert.AreEqual(10, config.Backend.RequestTimeoutSeconds);
            Assert.AreEqual(0, config.Backend.MaxAutoRetries);
            Assert.AreEqual(0, config.Gameplay.CountdownSeconds);
            Assert.AreEqual(0, config.Gameplay.TimeLimitSeconds);
            Assert.AreEqual("1.0", config.Privacy.ConsentVersion);
            Assert.AreEqual(5, config.Warnings.Count, string.Join("\n", config.Warnings));
        }

        [Test]
        public void SupabaseUrl_IsTrimmed_AndRpcUrlIsBuilt()
        {
            var config = ConfigLoader.Load(null, "{ \"backend\": { \"supabaseUrl\": \" https://x.example.invalid/ \", \"supabaseAnonKey\": \"k\", \"eventSlug\": \"e\" } }", null);

            Assert.AreEqual("https://x.example.invalid", config.Backend.SupabaseUrl);
            Assert.AreEqual("https://x.example.invalid/rest/v1/rpc/ping_event", config.Backend.RpcUrl("ping_event"));
            Assert.IsTrue(config.Backend.HasEndpoint);
            Assert.IsFalse(config.Backend.IsConfigured, "access code missing");
        }

        [Test]
        public void EmptyOrWhitespaceJson_IsTreatedAsAbsent()
        {
            var config = ConfigLoader.Load(AssetWithDefaults(), "   ", "");

            Assert.AreEqual(0, config.AppliedSources.Count);
            Assert.AreEqual(0, config.Warnings.Count);
        }
    }
}
