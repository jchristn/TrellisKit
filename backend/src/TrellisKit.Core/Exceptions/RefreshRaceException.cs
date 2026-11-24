namespace TrellisKit.Core.Exceptions
{
    using System;

    /// <summary>
    /// 401 InvalidRefreshToken for a refresh that lost a race with a concurrent refresh of the same token. The session
    /// stays valid, so callers must not clear the client's refresh cookie (the winner already set a new one).
    /// </summary>
    public class RefreshRaceException : ApiErrorException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message safe to show to the caller.</param>
        public RefreshRaceException(string message)
            : base(401, "InvalidRefreshToken", message)
        {
        }

        #endregion
    }
}
