namespace MultiTravel.Core.Xr
{
    /// <summary>
    /// Operator-facing control of the player's rig, implemented in the Gameplay assembly and registered in
    /// <c>AppServices</c> so the operator screen can recenter the participant without referencing XR packages.
    /// </summary>
    public interface IXrRigControl
    {
        /// <summary>Moves/rotates the rig so the headset is on the floor mark facing the suitcase.</summary>
        void Recenter();

        /// <summary>Current floor height correction in metres (positive raises the virtual floor relative to the player).</summary>
        float FloorOffset { get; }

        /// <summary>Adjusts the floor correction by <paramref name="deltaMetres"/>, clamped to the configured range; persisted per station.</summary>
        void AdjustFloorOffset(float deltaMetres);
    }
}
