namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Configurable profile field limits (docs/MULTIPROPERTY_CONTRACT.md section 12).
    /// </summary>
    public class ProfileFieldSettings
    {
        #region Public-Members

        /// <summary>
        /// Most non-built-in fields per property. Default 50. Minimum 1, maximum 100.
        /// </summary>
        public int MaxFields
        {
            get { return _MaxFields; }
            set { _MaxFields = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Most enabled fields shown on member cards. Default 3. Minimum 0, maximum 5.
        /// </summary>
        public int MaxCardFields
        {
            get { return _MaxCardFields; }
            set { _MaxCardFields = Math.Clamp(value, 0, 5); }
        }

        /// <summary>
        /// Most profile filters in one member enumeration. Default 10. Minimum 1, maximum 20.
        /// </summary>
        public int MaxFiltersPerQuery
        {
            get { return _MaxFiltersPerQuery; }
            set { _MaxFiltersPerQuery = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Compatibility mode of the Phase 1 to 2 profile fields (rank, rankLevel, affiliation, city, region, country,
        /// goals, goalsOther, rankHistory). Default ReadWrite.
        /// </summary>
        public LegacyFieldsModeEnum LegacyFieldsMode { get; set; } = LegacyFieldsModeEnum.ReadWrite;

        #endregion

        #region Private-Members

        private int _MaxFields = 50;
        private int _MaxCardFields = 3;
        private int _MaxFiltersPerQuery = 10;

        #endregion
    }
}
