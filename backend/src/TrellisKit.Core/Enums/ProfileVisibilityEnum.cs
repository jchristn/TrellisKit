namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who may see a profile field's value (docs/MULTIPROPERTY_CONTRACT.md section 9.8).
    /// </summary>
    public enum ProfileVisibilityEnum
    {
        /// <summary>
        /// Anyone who can see the profile.
        /// </summary>
        Public,

        /// <summary>
        /// Members and staff.
        /// </summary>
        Members,

        /// <summary>
        /// Staff only.
        /// </summary>
        Staff,

        /// <summary>
        /// The member and staff.
        /// </summary>
        Private
    }
}
