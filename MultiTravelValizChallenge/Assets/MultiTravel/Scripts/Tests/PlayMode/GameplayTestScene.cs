using System;
using System.Collections;
using System.Collections.Generic;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Products;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Object = UnityEngine.Object;

namespace MultiTravel.Tests.PlayMode
{
    /// <summary>
    /// Builds a minimal gameplay scene in code (no HMD, no scene assets): an XRInteractionManager, a floor, a table,
    /// a suitcase with a trigger volume and three slots, an <see cref="ItemPool"/> and Core services.
    /// Every created object is destroyed by <see cref="Dispose"/>.
    /// </summary>
    public sealed class GameplayTestScene : IDisposable
    {
        public const string RequiredA = "test-laptop";
        public const string RequiredB = "test-pen";
        public const string Wrong = "test-beach-towel";

        public static readonly Vector3 SuitcasePosition = new Vector3(1f, 0f, 0f);
        public static readonly Vector3 TableTop = new Vector3(0f, 0.45f, 2f);

        private readonly List<Object> created = new List<Object>();

        public GameplayTestScene(int countdownSeconds = 0, CompletionMode mode = CompletionMode.RequiredItemsPlaced)
        {
            Root = Track(new GameObject("TestSceneRoot"));

            var managerGo = Track(new GameObject("XR Interaction Manager"));
            Manager = managerGo.AddComponent<XRInteractionManager>();

            var floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            floor.name = "Floor";
            floor.transform.SetParent(Root.transform, false);
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(10f, 0.1f, 10f);

            var table = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            table.name = "Table";
            table.transform.SetParent(Root.transform, false);
            table.transform.position = TableTop - new Vector3(0f, 0.05f, 0f);
            table.transform.localScale = new Vector3(2f, 0.1f, 1f);

            Catalog = Track(ScriptableObject.CreateInstance<ProductCatalog>());
            DefinitionA = Product(RequiredA, true);
            DefinitionB = Product(RequiredB, true);
            DefinitionWrong = Product(Wrong, false);
            Catalog.Products = new List<ProductDefinition> { DefinitionA, DefinitionB, DefinitionWrong };
            Set = new ProductSetResolver().Resolve(Catalog, Gender.Female);

            Config = new RuntimeConfig(
                new BackendConfig("https://test.example.invalid", "test-anon", "test-event", "test-code", "station-test", 10, 0),
                new GameplayConfig(mode, 0, true, countdownSeconds, false, true),
                new TextsConfig("Başlık", "Alt başlık", "Talimat", "Senaryo"),
                new PrivacyConfig(string.Empty, "1.0"),
                new BrandingConfig("MultiTravel: Packing Challenge", null, Color.blue, Color.red),
                new DebugConfig(false),
                "0.0.0-test",
                Array.Empty<string>(),
                Array.Empty<string>());

            Clock = new ManualClock();
            Timer = new GameTimer(Clock);
            Score = new ScoreService(true);
            Evaluator = new CompletionEvaluator(Set, mode, 0);
            Session = new SessionController(Config, Score, Timer, Evaluator);

            var poolGo = Track(new GameObject("ItemPool"));
            poolGo.transform.SetParent(Root.transform, false);
            Pool = poolGo.AddComponent<ItemPool>();
            EmptyCatalog = Track(ScriptableObject.CreateInstance<ProductCatalog>());
            EmptyCatalog.Products = new List<ProductDefinition>();
            Pool.SetCatalog(EmptyCatalog);

            BuildSuitcase();
        }

        public GameObject Root { get; }
        public XRInteractionManager Manager { get; }
        public ProductCatalog Catalog { get; }
        public ProductCatalog EmptyCatalog { get; }
        public ProductDefinition DefinitionA { get; }
        public ProductDefinition DefinitionB { get; }
        public ProductDefinition DefinitionWrong { get; }
        public ProductSet Set { get; }
        public RuntimeConfig Config { get; }
        public ManualClock Clock { get; }
        public GameTimer Timer { get; }
        public ScoreService Score { get; }
        public CompletionEvaluator Evaluator { get; }
        public SessionController Session { get; }
        public ItemPool Pool { get; private set; }
        public SuitcaseController Suitcase { get; private set; }
        public BoxCollider Volume { get; private set; }
        public List<SuitcaseSlot> Slots { get; } = new List<SuitcaseSlot>();

        /// <summary>A world position well inside the placement volume.</summary>
        public Vector3 InsideVolume => SuitcasePosition + new Vector3(0f, 0.3f, 0f);

        public T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        /// <summary>Creates a 10 cm cube item for the definition, registers and activates it at <paramref name="spawn"/>.</summary>
        public ProductItem CreateItem(ProductDefinition definition, Vector3 spawn)
        {
            var go = Track(new GameObject("Item_" + definition.Id));
            go.transform.SetParent(Root.transform, false);
            go.transform.position = spawn;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.1f;
            var item = go.AddComponent<ProductItem>();
            item.Setup(definition);
            item.SetSpawnPose(spawn, Quaternion.identity);
            if (!Pool.Register(item))
            {
                throw new InvalidOperationException("Register failed for " + definition.Id);
            }

            if (!Pool.ActivateItem(item))
            {
                throw new InvalidOperationException("Activation failed for " + definition.Id);
            }

            return item;
        }

        /// <summary>Spawn point on the table for item index i (items rest there under gravity).</summary>
        public Vector3 TableSpawn(int index)
        {
            return TableTop + new Vector3(-0.6f + 0.3f * index, 0.1f, 0f);
        }

        /// <summary>Moves an item (transform and body) to a world position and clears its velocity.</summary>
        public static void Teleport(ProductItem item, Vector3 position)
        {
            item.transform.position = position;
            var body = item.Body;
            body.position = position;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
        }

        /// <summary>Drives the session from Welcome to Playing (countdown 0 → LoadingFinished enters Playing directly).</summary>
        public void EnterPlaying()
        {
            Session.BeginRegistration();
            var validation = Session.SubmitRegistration(ValidInput());
            if (!validation.IsValid)
            {
                throw new InvalidOperationException("Test input invalid: " + validation.FirstError);
            }

            Session.SelectGender(Gender.Female);
            Session.StartGame();
            Evaluator.Configure(Set);
            Session.LoadingFinished();
            if (Session.State == SessionState.Countdown)
            {
                Session.CountdownFinished();
            }
        }

        public static ParticipantInput ValidInput()
        {
            return new ParticipantInput
            {
                FirstName = "Ayşe",
                LastName = "Yılmaz",
                Title = "Müdür", Company = "Test A.Ş.", Location = "İstanbul / Şişli", Phone = "+905321234567",
                Email = "ayse@example.com"
            };
        }

        /// <summary>Yields frames until <paramref name="condition"/> holds or <paramref name="timeoutSeconds"/> (real time) elapse.</summary>
        public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        public void Dispose()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    Object.Destroy(created[i]);
                }
            }

            created.Clear();
        }

        private ProductDefinition Product(string id, bool isCorrect)
        {
            var definition = Track(ScriptableObject.CreateInstance<ProductDefinition>());
            definition.name = id;
            definition.Id = id;
            definition.DisplayName = id;
            definition.IsCorrect = isCorrect;
            definition.Availability = GenderAvailability.Both;
            definition.ClearRequiredOverride();
            return definition;
        }

        private void BuildSuitcase()
        {
            var suitcaseGo = Track(new GameObject("Suitcase"));
            suitcaseGo.transform.SetParent(Root.transform, false);
            suitcaseGo.transform.position = SuitcasePosition;

            var volumeGo = new GameObject("PlacementVolume");
            volumeGo.transform.SetParent(suitcaseGo.transform, false);
            Volume = volumeGo.AddComponent<BoxCollider>();
            Volume.isTrigger = true;
            Volume.center = new Vector3(0f, 0.25f, 0f);
            Volume.size = new Vector3(0.6f, 0.5f, 0.4f);

            for (int i = 0; i < 3; i++)
            {
                var slotGo = new GameObject("Slot_" + i);
                slotGo.transform.SetParent(suitcaseGo.transform, false);
                slotGo.transform.localPosition = new Vector3(-0.2f + 0.2f * i, 0.05f, 0f);
                Slots.Add(slotGo.AddComponent<SuitcaseSlot>());
            }

            Suitcase = suitcaseGo.AddComponent<SuitcaseController>();
            Suitcase.Configure(Volume, Slots);
            Suitcase.Bind(Score, Evaluator, Catalog);
            Suitcase.SetPool(Pool);
        }
    }
}
