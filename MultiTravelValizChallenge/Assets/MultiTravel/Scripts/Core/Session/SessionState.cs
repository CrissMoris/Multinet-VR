namespace MultiTravel.Core.Session
{
    /// <summary>
    /// States of the per-participant session state machine (ARCHITECTURE.md §2.3).
    /// Transitions are owned exclusively by <see cref="SessionController"/>.
    /// </summary>
    public enum SessionState
    {
        /// <summary>Idle screen; no participant data exists.</summary>
        Welcome,

        /// <summary>Operator fills in the registration form.</summary>
        Registration,

        /// <summary>Participant chooses the gender variant of the item set.</summary>
        GenderSelection,

        /// <summary>Instructions are shown; waiting for the operator to start the game.</summary>
        Instructions,

        /// <summary>Gameplay director prepares the item set and the suitcase.</summary>
        Loading,

        /// <summary>VR countdown before interaction is enabled.</summary>
        Countdown,

        /// <summary>Timer runs, interaction enabled.</summary>
        Playing,

        /// <summary>Timer stopped, result snapshot built. Immediately followed by <see cref="Submitting"/>.</summary>
        Completed,

        /// <summary>Result is being sent to the backend (through the outbox).</summary>
        Submitting,

        /// <summary>The submission failed; the operator can retry or start a new participant.</summary>
        SubmissionFailed,

        /// <summary>Terminal state of a session (submitted or abandoned); waiting for reset.</summary>
        Finished,

        /// <summary>Unrecoverable application error reported by bootstrap or gameplay.</summary>
        Fatal
    }
}
