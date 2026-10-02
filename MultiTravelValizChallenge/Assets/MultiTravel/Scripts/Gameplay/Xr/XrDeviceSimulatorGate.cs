using System.Collections;
using MultiTravel.Core.Config;
using MultiTravel.Core.Xr;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Xr
{
    /// <summary>
    /// Development fallback (ARCHITECTURE.md §3): instantiates the XR Interaction / Device Simulator prefab only when
    /// running in the Editor or a Development build, the configuration allows it
    /// (<c>Debug.EnableDeviceSimulatorWhenNoHmd</c>) and no headset is connected after <see cref="detectionDelaySeconds"/>.
    /// Release builds compile the check out entirely. With no prefab assigned the component does nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrDeviceSimulatorGate : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("XR Interaction Simulator prefab (Assets/Samples/XR Interaction Toolkit/3.4.1/XR Interaction Simulator/). Leave empty to disable.")]
        private GameObject simulatorPrefab;

        [SerializeField]
        [Tooltip("Seconds to wait for the headset to appear before falling back to the simulator.")]
        [Min(0f)]
        private float detectionDelaySeconds = 2f;

        private static GameObject spawnedSimulator;

        /// <summary>The simulator instance created by any gate (null when none).</summary>
        public static GameObject SpawnedSimulator => spawnedSimulator;

        /// <summary>The simulator prefab (null = gate disabled).</summary>
        public GameObject SimulatorPrefab => simulatorPrefab;

        /// <summary>Seconds waited for a headset before the simulator is enabled.</summary>
        public float DetectionDelaySeconds => detectionDelaySeconds;

        /// <summary>Generator API.</summary>
        public void SetSimulatorPrefab(GameObject prefab)
        {
            simulatorPrefab = prefab;
        }

        private void Start()
        {
            if (simulatorPrefab == null || spawnedSimulator != null)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            StartCoroutine(GateRoutine());
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private IEnumerator GateRoutine()
        {
            RuntimeConfig config = null;
            if (!ServiceResolver.Resolve(ref config, this, nameof(XrDeviceSimulatorGate)))
            {
                yield break;
            }

            if (!config.Debug.EnableDeviceSimulatorWhenNoHmd)
            {
                yield break;
            }

            if (detectionDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(detectionDelaySeconds);
            }

            if (spawnedSimulator != null || simulatorPrefab == null)
            {
                yield break;
            }

            if (HeadsetConnected())
            {
                yield break;
            }

            spawnedSimulator = Instantiate(simulatorPrefab);
            spawnedSimulator.name = simulatorPrefab.name;
            DontDestroyOnLoad(spawnedSimulator);
            Debug.Log(ServiceResolver.LogPrefix + "No headset detected: XR simulator enabled (development only).", this);
        }

        private bool HeadsetConnected()
        {
            XrStatusService concrete = null;
            if (ServiceResolver.TryResolve(ref concrete))
            {
                return concrete.HmdConnected;
            }

            IXrStatusService status = null;
            if (ServiceResolver.TryResolve(ref status))
            {
                return status.IsXrRunning || status.HmdPresent;
            }

            var found = FindAnyObjectByType<XrStatusService>();
            return found != null && found.HmdConnected;
        }
#endif
    }
}
