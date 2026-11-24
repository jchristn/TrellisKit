namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/me/age-attestation.
    /// </summary>
    public class AgeAttestationRequest
    {
        #region Public-Members

        /// <summary>
        /// Date of birth as YYYY-MM-DD; never stored.
        /// </summary>
        public string? DateOfBirth { get; set; } = null;

        /// <summary>
        /// Whether the member checked the terms checkbox (accepts the documents it covers after a successful
        /// attestation). Default false.
        /// </summary>
        public bool AcceptTerms { get; set; } = false;

        /// <summary>
        /// Documents accepted with their own checkbox, recorded after a successful attestation, or null.
        /// </summary>
        public System.Collections.Generic.List<DocumentAcceptanceItem>? AcceptedDocuments { get; set; } = null;

        #endregion
    }
}
