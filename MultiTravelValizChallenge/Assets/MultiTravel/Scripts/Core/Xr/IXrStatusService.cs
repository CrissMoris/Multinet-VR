namespace MultiTravel.Core.Xr
{
    /// <summary>
    /// Core-facing view of the XR runtime state (ARCHITECTURE.md §2.8). Implemented by
    /// <c>XrStatusService</c> in the Gameplay assembly and registered in <c>AppServices</c>
    /// so the operator status bar can show the VR indicator without referencing XR packages.
    /// </summary>
    public interface IXrStatusService
    {
        /// <summary>True when an XR loader is initialised and the display subsystem is running.</summary>
        bool IsXrRunning { get; }

        /// <summary>True when a head-mounted display is connected and tracked.</summary>
        bool HmdPresent { get; }

        /// <summary>Attempts to (re)initialise the XR loader.</summary>
        void Retry();
    }
}
