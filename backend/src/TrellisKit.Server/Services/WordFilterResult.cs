namespace TrellisKit.Server.Services
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of a word filter check that passed (no Block match): the Flag matches to record once the content is
    /// saved (<see cref="WordFilterService.RecordFlagsAsync(WordFilterResult?, TrellisKit.Core.Enums.TargetTypeEnum, string, string?, System.Threading.CancellationToken)"/>).
    /// </summary>
    public class WordFilterResult
    {
        #region Public-Members

        /// <summary>
        /// Flag matches, one per distinct field and term. Never null.
        /// </summary>
        public List<WordFilterFieldMatch> Flags
        {
            get { return _Flags; }
            set { _Flags = value ?? new List<WordFilterFieldMatch>(); }
        }

        /// <summary>
        /// Whether any Flag term matched.
        /// </summary>
        public bool HasFlags
        {
            get { return _Flags.Count > 0; }
        }

        #endregion

        #region Private-Members

        private List<WordFilterFieldMatch> _Flags = new List<WordFilterFieldMatch>();

        #endregion
    }
}
