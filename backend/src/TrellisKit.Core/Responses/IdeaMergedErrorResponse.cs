namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// 409 IdeaMerged body: the error plus the idea the target was merged into.
    /// </summary>
    public class IdeaMergedErrorResponse : ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// The idea to vote or comment on instead.
        /// </summary>
        public IdeaMergedRefView MergedInto
        {
            get { return _MergedInto; }
            set { _MergedInto = value ?? new IdeaMergedRefView(); }
        }

        #endregion

        #region Private-Members

        private IdeaMergedRefView _MergedInto = new IdeaMergedRefView();

        #endregion
    }
}
