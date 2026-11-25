namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Saves and private collections (REST_API.md, Saves).
    /// </summary>
    public class SavesSettings
    {
        #region Public-Members

        /// <summary>
        /// Most saves one member may keep. Default 2000. Minimum 100, maximum 20000.
        /// </summary>
        public int MaxPerMember
        {
            get { return _MaxPerMember; }
            set { _MaxPerMember = Math.Clamp(value, MinMaxPerMember, MaxMaxPerMember); }
        }

        /// <summary>
        /// Most collections one member may keep. Default 50. Minimum 1, maximum 500.
        /// </summary>
        public int MaxCollectionsPerMember
        {
            get { return _MaxCollectionsPerMember; }
            set { _MaxCollectionsPerMember = Math.Clamp(value, MinMaxCollectionsPerMember, MaxMaxCollectionsPerMember); }
        }

        /// <summary>
        /// Smallest value of <see cref="MaxPerMember"/>.
        /// </summary>
        public const int MinMaxPerMember = 100;

        /// <summary>
        /// Largest value of <see cref="MaxPerMember"/>.
        /// </summary>
        public const int MaxMaxPerMember = 20000;

        /// <summary>
        /// Smallest value of <see cref="MaxCollectionsPerMember"/>.
        /// </summary>
        public const int MinMaxCollectionsPerMember = 1;

        /// <summary>
        /// Largest value of <see cref="MaxCollectionsPerMember"/>.
        /// </summary>
        public const int MaxMaxCollectionsPerMember = 500;

        #endregion

        #region Private-Members

        private int _MaxPerMember = 2000;
        private int _MaxCollectionsPerMember = 50;

        #endregion
    }
}
