using System;

namespace MultiTravel.Core.Session
{
    /// <summary>
    /// Core-facing view of the in-headset tutorial (OVERHAUL_PLAN §5, <c>TutorialController</c> in the Gameplay assembly).
    /// Registered in <c>AppServices</c> so the operator screen can show "Katılımcı hazır" without referencing Gameplay.
    /// <see cref="Passed"/> is true only while the session is in <see cref="SessionState.Instructions"/> and the participant
    /// has grabbed the practice item and dropped it into the suitcase; it resets when the state changes.
    /// </summary>
    public interface ITutorialStatus
    {
        /// <summary>True once the participant completed the practice grab-and-place of the current session.</summary>
        bool Passed { get; }

        /// <summary>Raised whenever <see cref="Passed"/> changes.</summary>
        event Action Changed;
    }
}
