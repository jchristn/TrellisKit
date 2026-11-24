namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Compatibility mode of the Phase 1 to 2 profile fields (docs/MULTIPROPERTY_CONTRACT.md section 9.14).
    /// </summary>
    public enum LegacyFieldsModeEnum
    {
        /// <summary>
        /// Legacy fields are served and accepted; dual writes.
        /// </summary>
        ReadWrite,

        /// <summary>
        /// Legacy writes are rejected; reads and dual writes continue.
        /// </summary>
        ReadOnly,

        /// <summary>
        /// Legacy fields are neither served nor written.
        /// </summary>
        Off
    }
}
