using System;
using System.Collections.Generic;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Products;
using MultiTravel.Core.Session;
using UnityEngine;

namespace MultiTravel.Tests.EditMode.Fakes
{
    /// <summary>Builders for deterministic test inputs. Values are fixtures only; no real event data.</summary>
    public static class TestData
    {
        public const string Url = "https://test.example.invalid";
        public const string AnonKey = "test-anon-key";
        public const string EventSlug = "test-event";
        public const string AccessCode = "test-code";
        public const string StationId = "station-test";
        public const string ClientVersion = "9.9.9";

        public static RuntimeConfig Config(
            int maxAutoRetries = 5,
            int countdownSeconds = 3,
            CompletionMode completionMode = CompletionMode.RequiredItemsPlaced,
            int timeLimitSeconds = 0,
            bool revertScoreOnRemoval = true,
            string consentText = "",
            string consentVersion = "1.0",
            string url = Url,
            string anonKey = AnonKey,
            string eventSlug = EventSlug,
            string accessCode = AccessCode,
            string stationId = StationId,
            int requestTimeoutSeconds = 10)
        {
            return new RuntimeConfig(
                new BackendConfig(url, anonKey, eventSlug, accessCode, stationId, requestTimeoutSeconds, maxAutoRetries),
                new GameplayConfig(completionMode, timeLimitSeconds, revertScoreOnRemoval, countdownSeconds, false, true),
                new TextsConfig("Title", "Subtitle", "Instructions", "Scenario"),
                new PrivacyConfig(consentText, consentVersion),
                new BrandingConfig("Product", null, Color.blue, Color.red),
                new DebugConfig(true),
                ClientVersion,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        public static ParticipantInput ValidInput()
        {
            return new ParticipantInput
            {
                FirstName = "  Ayşe ",
                LastName = " Yılmaz",
                Title = "Müdür", Company = "Test A.Ş.", Location = "İstanbul / Şişli", Phone = "+90 (532) 123-45-67",
                Email = " ayse@example.com ",
                ConsentAccepted = true
            };
        }

        /// <summary>Normalised participant data with distinctive PII values (used to prove PII never reaches logs).</summary>
        public const string PiiFirstName = "Ayşe";
        public const string PiiLastName = "Yılmaz";
        public const string PiiPhone = "+905321234567";
        public const string PiiEmail = "ayse@example.com";

        /// <summary>A completed session (gender selected, result + submission id set) built without a controller.</summary>
        public static ParticipantSession CompletedSession(
            int score = 15,
            long completionMs = 12345,
            Gender gender = Gender.Female,
            CompletionReason reason = CompletionReason.RequiredItemsPlaced)
        {
            var input = new ParticipantInput
            {
                FirstName = PiiFirstName,
                LastName = PiiLastName,
                Phone = PiiPhone,
                Email = PiiEmail,
                ConsentAccepted = true,
                ConsentVersion = "1.0"
            };

            var session = new ParticipantSession(Guid.NewGuid(), input, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
            session.Gender = gender;
            session.GenderSelected = true;
            session.Result = new GameResult(
                score,
                completionMs,
                2,
                1,
                2,
                new[] { "laptop", "beach-towel", "pen" },
                new DateTime(2026, 10, 1, 9, 5, 30, 250, DateTimeKind.Utc),
                reason);
            session.SubmissionId = Guid.NewGuid();
            session.Outcome = SessionOutcome.Completed;
            return session;
        }

        /// <summary>An outbox payload for a fresh completed session.</summary>
        public static MultiTravel.Core.Backend.SubmissionPayload Payload(int score = 15, long completionMs = 12345)
        {
            return MultiTravel.Core.Backend.SubmissionPayload.FromSession(CompletedSession(score, completionMs), StationId, ClientVersion);
        }
    }

    /// <summary>Creates ScriptableObjects / GameObjects for a fixture and destroys them in TearDown.</summary>
    public sealed class ScriptableObjectFactory
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        public ProductDefinition Product(
            string id,
            bool isCorrect,
            GenderAvailability availability = GenderAvailability.Both,
            int scoreOverride = 0,
            bool? required = null,
            bool withPrefab = false,
            string displayName = null)
        {
            var product = ScriptableObject.CreateInstance<ProductDefinition>();
            product.name = id;
            product.Id = id;
            product.DisplayName = displayName ?? id;
            product.IsCorrect = isCorrect;
            product.Availability = availability;
            product.ScoreOverride = scoreOverride;
            if (required.HasValue)
            {
                product.SetRequiredForCompletion(required.Value);
            }
            else
            {
                product.ClearRequiredOverride();
            }

            if (withPrefab)
            {
                product.VisualPrefab = GameObject(id);
            }

            created.Add(product);
            return product;
        }

        public GameObject GameObject(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            created.Add(go);
            return go;
        }

        public ProductCatalog Catalog(params ProductDefinition[] products)
        {
            var catalog = ScriptableObject.CreateInstance<ProductCatalog>();
            catalog.Products = new List<ProductDefinition>(products);
            created.Add(catalog);
            return catalog;
        }

        public AppConfig AppConfig()
        {
            var config = ScriptableObject.CreateInstance<AppConfig>();
            created.Add(config);
            return config;
        }

        public ProductSet Set(Gender gender, params ProductDefinition[] products)
        {
            return new ProductSetResolver().Resolve(Catalog(products), gender);
        }

        public void DestroyAll()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(created[i]);
                }
            }

            created.Clear();
        }
    }
}
