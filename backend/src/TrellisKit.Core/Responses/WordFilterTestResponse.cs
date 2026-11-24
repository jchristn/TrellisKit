namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of POST /api/v1/admin/moderation/word-filter/test.
    /// </summary>
    public class WordFilterTestResponse
    {
        #region Public-Members

        /// <summary>
        /// Matches in text order. Never null.
        /// </summary>
        public List<WordFilterMatchView> Matches
        {
            get { return _Matches; }
            set { _Matches = value ?? new List<WordFilterMatchView>(); }
        }

        #endregion

        #region Private-Members

        private List<WordFilterMatchView> _Matches = new List<WordFilterMatchView>();

        #endregion
    }
}
