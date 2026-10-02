namespace MultiTravel.Core.Session
{
    /// <summary>Final outcome of a <see cref="ParticipantSession"/>.</summary>
    public enum SessionOutcome
    {
        /// <summary>The session has not reached a terminal state yet.</summary>
        InProgress,

        /// <summary>The game was completed and a result snapshot exists.</summary>
        Completed,

        /// <summary>The operator abandoned the session; no leaderboard submission is made for it.</summary>
        Abandoned
    }
}
