using System;
using System.Collections;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Suitcase
{
    /// <summary>State of the suitcase lid animation.</summary>
    public enum LidState
    {
        Open,
        Closing,
        Closed,
        Opening
    }

    /// <summary>
    /// Closes the suitcase lid when the session reaches <see cref="SessionState.Completed"/> and reopens it on
    /// <see cref="SessionController.SessionResetRequested"/> (OVERHAUL_PLAN §5). The lid pivot (art node <c>PIVOT.lid</c>)
    /// rotates from its authored open pose by <see cref="closeAngle"/> (−100°) about its local X axis in 1.1 s
    /// (ease-in-out); when the packed stack is high the lid presses a few degrees further and springs back; then two latch
    /// clicks are reported (<see cref="LatchClicked"/>, for audio). Reopening takes 0.6 s. Purely visual.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SuitcaseLid : MonoBehaviour
    {
        /// <summary>Art node name of the lid hinge pivot.</summary>
        public const string PivotNodeName = "PIVOT.lid";

        [SerializeField]
        [Tooltip("Lid hinge pivot (art node 'PIVOT.lid'). Found by name below this object when empty.")]
        private Transform lidPivot;

        [SerializeField]
        [Tooltip("Suitcase whose StackHeight decides the press. Defaults to a SuitcaseController in the parents.")]
        private SuitcaseController suitcase;

        [SerializeField]
        [Tooltip("Rotation from the open pose to the closed pose about the pivot's local X axis (degrees).")]
        private float closeAngle = -100f;

        [SerializeField]
        [Min(0.05f)]
        private float closeSeconds = 1.1f;

        [SerializeField]
        [Min(0.05f)]
        private float openSeconds = 0.6f;

        [SerializeField]
        [Tooltip("Stack height (metres) above which the lid presses the pile down before latching.")]
        [Min(0f)]
        private float pressStackHeight = 0.16f;

        [SerializeField]
        [Tooltip("Extra rotation of the press (degrees, same sign as closeAngle).")]
        private float pressAngle = -4f;

        [SerializeField]
        [Min(0.02f)]
        private float pressSeconds = 0.12f;

        [SerializeField]
        [Tooltip("Gap between the two latch clicks (seconds).")]
        [Min(0f)]
        private float latchGapSeconds = 0.14f;

        private SessionController session;
        private bool subscribed;
        private Quaternion openRotation = Quaternion.identity;
        private bool hasOpenRotation;
        private float currentAngle;
        private Coroutine routine;
        private float nextBindAttempt;

        /// <summary>Raised when the lid starts closing.</summary>
        public event Action Closing;

        /// <summary>Raised after both latches clicked (lid fully closed).</summary>
        public event Action Closed;

        /// <summary>Raised when the lid starts opening again (reset).</summary>
        public event Action Opening;

        /// <summary>Raised when the lid is fully open again.</summary>
        public event Action Opened;

        /// <summary>Raised for each latch click (index 0, then 1).</summary>
        public event Action<int> LatchClicked;

        /// <summary>Raised when the lid presses a high stack before latching.</summary>
        public event Action Pressed;

        /// <summary>Current animation state.</summary>
        public LidState State { get; private set; } = LidState.Open;

        /// <summary>Current rotation from the open pose (degrees; 0 = open, <see cref="CloseAngle"/> = closed).</summary>
        public float CurrentAngle => currentAngle;

        /// <summary>Configured closing rotation (degrees).</summary>
        public float CloseAngle => closeAngle;

        /// <summary>The lid pivot.</summary>
        public Transform Pivot => lidPivot;

        /// <summary>Test / custom-host injection of the session controller (otherwise resolved from AppServices).</summary>
        public void Bind(SessionController sessionController)
        {
            if (sessionController == session && subscribed)
            {
                return;
            }

            Unsubscribe();
            session = sessionController;
            Subscribe();
        }

        /// <summary>Generator / test API.</summary>
        public void Configure(Transform pivot, SuitcaseController target)
        {
            lidPivot = pivot;
            suitcase = target;
            hasOpenRotation = false;
            CaptureOpenRotation();
        }

        /// <summary>Test / tuning API: animation durations (seconds).</summary>
        public void SetDurations(float close, float open, float latchGap)
        {
            closeSeconds = Mathf.Max(0.05f, close);
            openSeconds = Mathf.Max(0.05f, open);
            latchGapSeconds = Mathf.Max(0f, latchGap);
        }

        /// <summary>Starts closing (no-op when already closing / closed).</summary>
        public void Close()
        {
            if (State == LidState.Closing || State == LidState.Closed)
            {
                return;
            }

            StartRoutine(CloseRoutine());
        }

        /// <summary>Starts opening (no-op when already open / opening).</summary>
        public void Open()
        {
            if (State == LidState.Open || State == LidState.Opening)
            {
                return;
            }

            StartRoutine(OpenRoutine());
        }

        /// <summary>Snaps the lid open without events (scene setup, tests).</summary>
        public void SnapOpen()
        {
            StopRoutine();
            SetAngle(0f);
            State = LidState.Open;
        }

        private void Awake()
        {
            if (suitcase == null)
            {
                suitcase = GetComponentInParent<SuitcaseController>();
            }

            if (lidPivot == null)
            {
                lidPivot = StrapLift.FindDeep(transform, PivotNodeName);
            }

            CaptureOpenRotation();
        }

        private void Start()
        {
            if (lidPivot == null)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "SuitcaseLid: no lid pivot ('" + PivotNodeName + "'); the lid will not animate.", this);
            }

            TryResolveSession();
        }

        private void Update()
        {
            if (!subscribed && Time.unscaledTime >= nextBindAttempt)
            {
                TryResolveSession();
            }
        }

        private void OnDisable()
        {
            // Coroutines stop with the component: finish the current move instantly.
            if (routine != null)
            {
                routine = null;
                if (State == LidState.Closing)
                {
                    SetAngle(closeAngle);
                    State = LidState.Closed;
                }
                else if (State == LidState.Opening)
                {
                    SetAngle(0f);
                    State = LidState.Open;
                }
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void TryResolveSession()
        {
            nextBindAttempt = Time.unscaledTime + 1f;
            if (session == null)
            {
                ServiceResolver.TryResolve(ref session);
            }

            Subscribe();
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

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (next == SessionState.Completed)
            {
                Close();
            }
        }

        private void OnSessionResetRequested()
        {
            Open();
        }

        private void CaptureOpenRotation()
        {
            if (hasOpenRotation || lidPivot == null)
            {
                return;
            }

            openRotation = lidPivot.localRotation;
            hasOpenRotation = true;
            currentAngle = 0f;
        }

        private void SetAngle(float angle)
        {
            currentAngle = angle;
            if (lidPivot != null && hasOpenRotation)
            {
                lidPivot.localRotation = openRotation * Quaternion.Euler(angle, 0f, 0f);
            }
        }

        private void StartRoutine(IEnumerator next)
        {
            StopRoutine();
            if (!isActiveAndEnabled)
            {
                // Inactive host: run the sequence to its end synchronously (events still fire in order).
                RunToEnd(next);
                return;
            }

            routine = StartCoroutine(next);
        }

        private static void RunToEnd(IEnumerator enumerator)
        {
            while (enumerator.MoveNext())
            {
                if (enumerator.Current is IEnumerator nested)
                {
                    RunToEnd(nested);
                }
            }
        }

        private void StopRoutine()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
        }

        private static float EaseInOut(float k)
        {
            return k * k * (3f - 2f * k);
        }

        private IEnumerator Animate(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < seconds && isActiveAndEnabled)
            {
                t += Time.unscaledDeltaTime;
                SetAngle(Mathf.LerpUnclamped(from, to, EaseInOut(Mathf.Clamp01(t / seconds))));
                yield return null;
            }

            SetAngle(to);
        }

        private IEnumerator CloseRoutine()
        {
            State = LidState.Closing;
            Closing?.Invoke();
            yield return Animate(currentAngle, closeAngle, closeSeconds);

            if (suitcase != null && suitcase.StackHeight > pressStackHeight)
            {
                Pressed?.Invoke();
                yield return Animate(closeAngle, closeAngle + pressAngle, pressSeconds);
                yield return Animate(closeAngle + pressAngle, closeAngle, pressSeconds);
            }

            LatchClicked?.Invoke(0);
            if (latchGapSeconds > 0f && isActiveAndEnabled)
            {
                yield return new WaitForSecondsRealtime(latchGapSeconds);
            }

            LatchClicked?.Invoke(1);
            State = LidState.Closed;
            routine = null;
            Closed?.Invoke();
        }

        private IEnumerator OpenRoutine()
        {
            State = LidState.Opening;
            Opening?.Invoke();
            yield return Animate(currentAngle, 0f, openSeconds);
            State = LidState.Open;
            routine = null;
            Opened?.Invoke();
        }
    }
}
