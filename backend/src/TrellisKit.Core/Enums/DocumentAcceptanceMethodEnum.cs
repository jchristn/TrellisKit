namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a member accepted a property document.
    /// </summary>
    public enum DocumentAcceptanceMethodEnum
    {
        /// <summary>
        /// At sign-up or during the onboarding step (age attestation or the first acceptance after social sign-in).
        /// </summary>
        Signup,

        /// <summary>
        /// At the re-acceptance prompt after a newer version was published.
        /// </summary>
        Reacceptance
    }
}
