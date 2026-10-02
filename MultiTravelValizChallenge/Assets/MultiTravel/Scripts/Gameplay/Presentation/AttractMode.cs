using System;
using System.Text;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Config;
using MultiTravel.Core.Leaderboard;
using MultiTravel.Core.Session;
using MultiTravel.Core.Utility;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>
    /// Welcome-state attract loop (OVERHAUL_PLAN §5): dims the stage through <see cref="MoodLighting"/> (Idle preset
    /// plus 4 s LED breathing) and cycles the top-5 leaderboard on the <see cref="ScoreboardDisplay"/>, fetched through
    /// <see cref="IBackendClient.GetLeaderboardAsync"/> and cached for <see cref="cacheSeconds"/>.
    /// <para>
    /// Every request captures the current <see cref="Epoch"/>; the epoch advances whenever Welcome is left or a session
    /// reset happens, so a response that arrives late (after a reset or during a new participant's session) is
    /// discarded. With no backend endpoint configured the scoreboard shows the welcome texts only.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttractMode : MonoBehaviour
    {
        /// <summary>Headline shown above the cycling entries.</summary>
        public const string LeaderboardHeadline = "Liderlik Tablosu";

        [SerializeField] private MoodLighting moodLighting;
        [SerializeField] private ScoreboardDisplay scoreboard;

        [SerializeField]
        [Min(5f)]
        private float cacheSeconds = 60f;

        [SerializeField]
        [Min(0.5f)]
        private float cycleSeconds = 3.5f;

        [SerializeField]
        [Range(1, 10)]
        private int topCount = 5;

        private readonly StringBuilder builder = new StringBuilder(128);
        private SessionController session;
        private RuntimeConfig config;
        private IBackendClient backend;
        private bool subscribed;
        private bool attracting;
        private LeaderboardEntry[] entries;
        private float fetchedAt = float.NegativeInfinity;
        private int cycleIndex = -1;
        private float nextCycle;
        private int pendingRequests;
        private bool fetchFailed;

        /// <summary>Participant-cycle counter; responses from an earlier epoch are ignored.</summary>
        public int Epoch { get; private set; }

        /// <summary>True while the attract loop runs (Welcome).</summary>
        public bool IsAttracting => attracting;

        /// <summary>Entries currently cycled (null when none / not fetched).</summary>
        public LeaderboardEntry[] CurrentEntries => entries;

        /// <summary>Number of leaderboard requests in flight.</summary>
        public int PendingRequests => pendingRequests;

        /// <summary>Index of the entry currently shown (-1 when none).</summary>
        public int CycleIndex => cycleIndex;

        /// <summary>True when the last leaderboard request failed (the welcome texts stay on the board).</summary>
        public bool LastFetchFailed => fetchFailed;

        /// <summary>Generator API.</summary>
        public void Configure(MoodLighting lighting, ScoreboardDisplay board)
        {
            moodLighting = lighting;
            scoreboard = board;
        }

        /// <summary>Test / generator API: explicit services (null = resolve from AppServices in Start).</summary>
        public void Bind(SessionController sessionController, RuntimeConfig runtimeConfig, IBackendClient backendClient)
        {
            if (sessionController != null && sessionController != session)
            {
                Unsubscribe();
                session = sessionController;
                Subscribe();
            }

            config = runtimeConfig ?? config;
            backend = backendClient ?? backend;
            if (session != null)
            {
                Sync(session.State);
            }
        }

        /// <summary>Drops the cache so the next Welcome fetches again.</summary>
        public void InvalidateCache()
        {
            fetchedAt = float.NegativeInfinity;
            entries = null;
        }

        // ----- Unity -----

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            bool ok = true;
            if (session == null)
            {
                ok &= ServiceResolver.Resolve(ref session, this, nameof(AttractMode));
                Subscribe();
            }

            if (config == null)
            {
                ok &= ServiceResolver.Resolve(ref config, this, nameof(AttractMode));
            }

            if (backend == null)
            {
                ServiceResolver.TryResolve(ref backend);
            }

            if (ok)
            {
                Sync(session.State);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (attracting)
            {
                LeaveWelcome();
            }
        }

        private void Update()
        {
            if (!attracting || scoreboard == null)
            {
                return;
            }

            if (entries == null || entries.Length == 0)
            {
                // The scoreboard may have entered Welcome after us (subscription order) and cleared the board.
                if (scoreboard.Headline != null && scoreboard.Headline.text.Length == 0)
                {
                    ShowWelcomeTexts();
                }

                return;
            }

            if (Time.unscaledTime < nextCycle && (scoreboard.Headline == null || scoreboard.Headline.text.Length > 0))
            {
                return;
            }

            nextCycle = Time.unscaledTime + cycleSeconds;
            cycleIndex = (cycleIndex + 1) % entries.Length;
            ShowEntry(entries[cycleIndex]);
        }

        // ----- internals -----

        private void Sync(SessionState state)
        {
            if (state == SessionState.Welcome)
            {
                if (!attracting)
                {
                    EnterWelcome();
                }
            }
            else if (attracting)
            {
                LeaveWelcome();
            }
        }

        private void EnterWelcome()
        {
            attracting = true;
            Epoch++;
            cycleIndex = -1;
            nextCycle = 0f;
            if (moodLighting != null)
            {
                moodLighting.ApplyPreset(MoodPreset.Idle);
                moodLighting.SetLedBreathing(true);
            }

            ShowWelcomeTexts();
            bool configured = config != null && config.Backend.HasEndpoint && backend != null;
            if (!configured)
            {
                entries = null;
                return;
            }

            bool cacheFresh = entries != null && Time.unscaledTime - fetchedAt < cacheSeconds;
            if (!cacheFresh)
            {
                FetchAsync(Epoch);
            }
        }

        private void LeaveWelcome()
        {
            attracting = false;
            Epoch++;
            if (moodLighting != null)
            {
                moodLighting.SetLedBreathing(false);
            }
        }

        private async void FetchAsync(int epoch)
        {
            pendingRequests++;
            BackendResult<LeaderboardEntry[]> result;
            try
            {
                result = await backend.GetLeaderboardAsync(topCount);
            }
            catch (Exception ex)
            {
                result = BackendResult<LeaderboardEntry[]>.Failure(BackendError.Transport(ex.GetType().Name + ": " + ex.Message));
            }

            pendingRequests--;
            if (this == null || epoch != Epoch || !attracting)
            {
                return; // stale: the participant cycle moved on (reset / new session)
            }

            if (!result.Ok)
            {
                fetchFailed = true;
                return;
            }

            fetchFailed = false;
            fetchedAt = Time.unscaledTime;
            entries = result.Value ?? Array.Empty<LeaderboardEntry>();
            cycleIndex = -1;
            nextCycle = 0f;
            if (entries.Length == 0)
            {
                ShowWelcomeTexts();
            }
        }

        private void ShowWelcomeTexts()
        {
            if (scoreboard == null)
            {
                return;
            }

            string title = config != null ? config.Texts.WelcomeTitle : string.Empty;
            string subtitle = config != null ? config.Texts.WelcomeSubtitle : string.Empty;
            scoreboard.ShowAttract(title, subtitle);
        }

        private void ShowEntry(LeaderboardEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            builder.Length = 0;
            builder.Append(entry.Rank).Append(". ").Append(entry.DisplayName ?? string.Empty)
                .Append("  ·  ").Append(entry.Score).Append(" puan  ·  ").Append(TimeFormat.FormatTenths(entry.CompletionMs));
            scoreboard.ShowAttract(LeaderboardHeadline, builder.ToString());
        }

        private void Subscribe()
        {
            if (session == null || subscribed)
            {
                return;
            }

            session.StateChanged += OnStateChanged;
            session.SessionResetRequested += OnSessionResetRequested;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (session != null && subscribed)
            {
                session.StateChanged -= OnStateChanged;
                session.SessionResetRequested -= OnSessionResetRequested;
            }

            subscribed = false;
        }

        private void OnSessionResetRequested()
        {
            // Anything requested before this point belongs to the previous participant cycle.
            Epoch++;
        }

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (isActiveAndEnabled)
            {
                Sync(next);
            }
        }
    }
}
