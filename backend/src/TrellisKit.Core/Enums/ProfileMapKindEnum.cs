namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Which payload a profile map is built for (docs/MULTIPROPERTY_CONTRACT.md section 9.8).
    /// </summary>
    public enum ProfileMapKindEnum
    {
        /// <summary>
        /// Me: every field visible to the member, every key present (null when unset).
        /// </summary>
        Me,

        /// <summary>
        /// Public profile: fields visible to the viewer with showOnProfile and a value.
        /// </summary>
        Profile,

        /// <summary>
        /// Member card (UserSummary): fields visible to the viewer with showOnCard and a value, at most
        /// profileFields.maxCardFields.
        /// </summary>
        Card,

        /// <summary>
        /// Admin member detail: every enabled field, every key present.
        /// </summary>
        Staff,

        /// <summary>
        /// Account export: every value, disabled fields included, except Staff visibility fields.
        /// </summary>
        Export
    }
}
