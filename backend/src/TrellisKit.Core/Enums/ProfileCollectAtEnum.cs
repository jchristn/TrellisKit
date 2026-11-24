namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// When a profile field is collected.
    /// </summary>
    public enum ProfileCollectAtEnum
    {
        /// <summary>
        /// Asked during onboarding.
        /// </summary>
        SignUp,

        /// <summary>
        /// Optional at sign-up, editable later.
        /// </summary>
        Profile,

        /// <summary>
        /// Set by staff.
        /// </summary>
        AdminOnly
    }
}
