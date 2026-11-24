namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The optional property.json library section (docs/PHASE4_CONTRACT.md section 0.2).
    /// </summary>
    public class PropertyLibrarySection
    {
        #region Public-Members

        /// <summary>
        /// Where library levels come from: None, ProfileField (the options of a Rank or SingleSelect profile field, in their order), or Custom (the levels list). Default None.
        /// </summary>
        public string LevelSource { get; set; } = "None";

        /// <summary>
        /// Profile field key used when LevelSource is ProfileField. Default null.
        /// </summary>
        public string? LevelFieldKey { get; set; } = null;

        /// <summary>
        /// Levels used when LevelSource is Custom (at most 50). Default empty.
        /// </summary>
        public List<PropertyOptionSeed> Levels { get; set; } = new List<PropertyOptionSeed>();

        #endregion
    }
}
