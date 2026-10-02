using System;
using System.Collections;
using System.Collections.Generic;
using MultiTravel.Core.Services;
using MultiTravel.Core.Xr;
using MultiTravel.Gameplay.Common;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace MultiTravel.Gameplay.Xr
{
    /// <summary>
    /// XR runtime status for the operator status bar (ARCHITECTURE.md §2.8, STACK_NOTES §5). Registers itself in
    /// <see cref="AppServices"/> as <see cref="IXrStatusService"/> (and as <see cref="XrStatusService"/>) in <c>Awake</c>.
    /// Values are sampled at <see cref="pollSeconds"/> so callers can read the properties every frame for free.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrStatusService : MonoBehaviour, IXrStatusService
    {
        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();

        [SerializeField]
        [Tooltip("Seconds between status samples.")]
        [Min(0.1f)]
        private float pollSeconds = 0.5f;

        [SerializeField]
        [Tooltip("Keep this object (and the registration) across scene loads. Enable when it lives in the Bootstrap scene.")]
        private bool dontDestroyOnLoad;

        private bool isXrRunning;
        private bool hmdPresent;
        private bool hmdConnected;
        private float nextPoll;
        private Coroutine retryRoutine;

        /// <summary>Raised when <see cref="IsXrRunning"/>, <see cref="HmdConnected"/> or <see cref="HmdPresent"/> changed.</summary>
        public event Action<XrStatusService> StatusChanged;

        /// <summary>True when an XR loader is initialised and the display subsystem is running.</summary>
        public bool IsXrRunning
        {
            get
            {
                PollIfDue();
                return isXrRunning;
            }
        }

        /// <summary>True when a headset is connected and the user wears it (user presence; tracked state as fallback).</summary>
        public bool HmdPresent
        {
            get
            {
                PollIfDue();
                return hmdPresent;
            }
        }

        /// <summary>True when a head device is connected (worn or not).</summary>
        public bool HmdConnected
        {
            get
            {
                PollIfDue();
                return hmdConnected;
            }
        }

        /// <summary>True while a <see cref="Retry"/> is running.</summary>
        public bool IsRetrying => retryRoutine != null;

        /// <summary>
        /// Generator API: keep the service across scene loads (use when it lives in the Bootstrap scene).
        /// Must be set before <c>Awake</c> runs to take effect (i.e. serialized by the generator).
        /// </summary>
        public void SetPersistAcrossScenes(bool persist)
        {
            dontDestroyOnLoad = persist;
        }

        /// <summary>Stops and re-initialises the XR loader (coroutine; one loader per frame).</summary>
        public void Retry()
        {
            if (retryRoutine != null)
            {
                return;
            }

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "XrStatusService.Retry ignored: component is disabled.", this);
                return;
            }

            retryRoutine = StartCoroutine(RetryRoutine());
        }

        /// <summary>Samples the status now.</summary>
        public void Poll()
        {
            nextPoll = Time.unscaledTime + pollSeconds;
            bool running = ReadXrRunning();
            bool connected = ReadHmdConnected(out bool present);
            bool changed = running != isXrRunning || connected != hmdConnected || present != hmdPresent;
            isXrRunning = running;
            hmdConnected = connected;
            hmdPresent = present;
            if (changed)
            {
                StatusChanged?.Invoke(this);
            }
        }

        private void Awake()
        {
            if (dontDestroyOnLoad)
            {
                if (transform.parent != null)
                {
                    transform.SetParent(null, true);
                }

                DontDestroyOnLoad(gameObject);
            }

            AppServices.Register<IXrStatusService>(this);
            AppServices.Register(this);
            Poll();
        }

        private void Update()
        {
            PollIfDue();
        }

        private void OnDestroy()
        {
            if (AppServices.TryGet(out IXrStatusService registered) && ReferenceEquals(registered, this))
            {
                AppServices.Unregister<IXrStatusService>();
            }

            if (AppServices.TryGet(out XrStatusService self) && ReferenceEquals(self, this))
            {
                AppServices.Unregister<XrStatusService>();
            }
        }

        private void PollIfDue()
        {
            if (Time.unscaledTime >= nextPoll)
            {
                Poll();
            }
        }

        private static XRManagerSettings Manager()
        {
            var settings = XRGeneralSettings.Instance;
            return settings != null ? settings.Manager : null;
        }

        private static bool ReadXrRunning()
        {
            var manager = Manager();
            if (manager == null || !manager.isInitializationComplete || manager.activeLoader == null)
            {
                return false;
            }

            SubsystemManager.GetSubsystems(Displays);
            for (int i = 0; i < Displays.Count; i++)
            {
                if (Displays[i] != null && Displays[i].running)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReadHmdConnected(out bool present)
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.isValid)
            {
                if (head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool userPresent))
                {
                    present = userPresent;
                }
                else
                {
                    present = !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || tracked;
                }

                return true;
            }

            var hmd = UnityEngine.InputSystem.InputSystem.GetDevice<XRHMD>();
            if (hmd == null)
            {
                present = false;
                return false;
            }

            var presence = hmd.TryGetChildControl<ButtonControl>("UserPresence");
            present = presence != null ? presence.isPressed : hmd.isTracked.isPressed;
            return true;
        }

        private IEnumerator RetryRoutine()
        {
            var manager = Manager();
            if (manager == null)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "XrStatusService: XR Plug-in Management is not configured; nothing to retry.", this);
                retryRoutine = null;
                yield break;
            }

            if (manager.isInitializationComplete)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
                yield return null;
            }

            yield return manager.InitializeLoader();
            if (manager.activeLoader != null)
            {
                manager.StartSubsystems();
            }
            else
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "XrStatusService: XR loader could not be initialised (is the headset connected and Quest Link running?).", this);
            }

            retryRoutine = null;
            Poll();
        }
    }
}
