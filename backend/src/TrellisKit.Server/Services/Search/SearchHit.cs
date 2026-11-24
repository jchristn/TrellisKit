namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// A search index hit.
    /// </summary>
    public class SearchHit
    {
        #region Public-Members

        /// <summary>
        /// Document identifier ("Type:id").
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Relevance score (higher is better).
        /// </summary>
        public double Score { get; set; } = 0;

        /// <summary>
        /// Object type part of the identifier.
        /// </summary>
        public string Type
        {
            get
            {
                int colon = Id.IndexOf(':');
                return colon > 0 ? Id.Substring(0, colon) : String.Empty;
            }
        }

        /// <summary>
        /// Object identifier part of the identifier.
        /// </summary>
        public string ObjectId
        {
            get
            {
                int colon = Id.IndexOf(':');
                return colon > 0 ? Id.Substring(colon + 1) : Id;
            }
        }

        #endregion
    }
}
