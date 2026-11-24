namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// The kinds of public landing page (docs/PHASE4_CONTRACT.md section 18).
    /// </summary>
    public enum PublicPageKindEnum
    {
        /// <summary>
        /// A directory venue (the property's venuesPath).
        /// </summary>
        Venue,

        /// <summary>
        /// A Public, published event (the property's eventsPath).
        /// </summary>
        Event,

        /// <summary>
        /// A member's opt-in profile (the property's profilesPath).
        /// </summary>
        Profile
    }
}
