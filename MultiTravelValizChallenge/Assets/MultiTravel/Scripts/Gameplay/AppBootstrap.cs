using System;
using System.Collections;
using System.Threading;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Outbox;
using MultiTravel.Core.Products;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Gameplay.Xr;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiTravel.Gameplay
{
    /// <summary>Owns the event services and persistent delivery loop for the lifetime of the player.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private ProductCatalog catalog;
        private static AppBootstrap instance;
        private CancellationTokenSource lifetime;
        private SubmissionOutbox outbox;
        private string startupError;
        public void Configure(ProductCatalog value) => catalog = value;

        /// <summary>
        /// Test seam: when set before the Bootstrap scene loads, outbox / result-log files go to this folder instead of
        /// <see cref="Application.persistentDataPath"/>. Never set by the application itself.
        /// </summary>
        public static string DataRootOverride { get; set; }

        /// <summary>
        /// Test seam: when set before the Bootstrap scene loads, replaces the Supabase client (so automated runs can never
        /// write to the configured event backend). Never set by the application itself.
        /// </summary>
        public static Func<RuntimeConfig, IBackendClient> BackendFactoryOverride { get; set; }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            AppServices.Clear();
            lifetime = new CancellationTokenSource();
            try
            {
                if (catalog == null) throw new InvalidOperationException("Ürün kataloğu bulunamadı.");
                var config = ConfigLoader.LoadFromDisk(Resources.Load<AppConfig>("AppConfig"));
                var clock = new StopwatchClock();
                var store = string.IsNullOrEmpty(DataRootOverride) ? FileLocalStore.CreateDefault() : new FileLocalStore(DataRootOverride);
                IBackendClient backend = BackendFactoryOverride != null ? BackendFactoryOverride(config) : new SupabaseBackendClient(config);
                outbox = new SubmissionOutbox(store, backend, config);
                var score = new ScoreService(config.Gameplay.RevertScoreOnRemoval);
                var timer = new GameTimer(clock);
                var completion = new CompletionEvaluator(null, config.Gameplay.CompletionMode, config.Gameplay.TimeLimitSeconds);
                var session = new SessionController(config, score, timer, completion, outbox, store, backend);
                AppServices.Register<IClock>(clock);
                AppServices.Register(config);
                AppServices.Register<ILocalStore>(store);
                AppServices.Register<IBackendClient>(backend);
                AppServices.Register(outbox);
                AppServices.Register(score);
                AppServices.Register(timer);
                AppServices.Register(completion);
                AppServices.Register(new ProductSetResolver());
                AppServices.Register(catalog);
                AppServices.Register(session);
                gameObject.AddComponent<XrStatusService>();
            }
            catch (Exception ex)
            {
                startupError = "Başlatma başarısız. Yapılandırmayı kontrol edip uygulamayı yeniden başlatın.\n" + ex.Message;
                Debug.LogError("[MultiTravel] " + startupError);
            }
        }

        private IEnumerator Start()
        {
            if (instance != this || startupError != null) yield break;
            if (!Application.CanStreamedLevelBeLoaded("Main"))
            {
                startupError = "Main oyun sahnesi bulunamadı. Kurulumu kontrol edin.";
                yield break;
            }
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            while (!lifetime.IsCancellationRequested)
            {
                var retry = outbox.RetryPendingAsync(lifetime.Token);
                while (!retry.IsCompleted) yield return null;
                if (retry.IsFaulted) Debug.LogWarning("[MultiTravel] Bekleyen sonuçlar gönderilemedi; tekrar denenecek.");
                yield return new WaitForSecondsRealtime(30f);
            }
        }

        private void OnGUI()
        {
            if (startupError == null) return;
            GUI.Box(new Rect(20, 20, Screen.width - 40, 160), startupError);
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            lifetime?.Cancel();
            lifetime?.Dispose();
            AppServices.Clear();
            instance = null;
        }
    }
}
