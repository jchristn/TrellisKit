namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of 403 DocumentAcceptanceRequired: the member must accept the listed documents (POST
    /// /api/v1/me/documents/accept) before continuing.
    /// </summary>
    public class DocumentAcceptanceRequiredResponse : ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Keys of the documents to accept (bounded by property.json, at most 30).
        /// </summary>
        public List<string> Documents { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty response.
        /// </summary>
        public DocumentAcceptanceRequiredResponse()
        {
        }

        /// <summary>
        /// Instantiate the response.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="documents">Document keys.</param>
        public DocumentAcceptanceRequiredResponse(string message, List<string> documents) : base("DocumentAcceptanceRequired", message)
        {
            Documents = documents ?? new List<string>();
        }

        #endregion
    }
}
