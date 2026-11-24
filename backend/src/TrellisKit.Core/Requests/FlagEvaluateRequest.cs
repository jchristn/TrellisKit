namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/app/flags/{id}/evaluate.
    /// </summary>
    public class FlagEvaluateRequest
    {
        /// <summary>
        /// Caller attributes.
        /// </summary>
        public FlagEvaluationSubject As { get; set; } = new FlagEvaluationSubject();

        /// <summary>
        /// Evaluate the draft instead of the published definition.
        /// </summary>
        public bool UseDraft { get; set; } = true;
    }
}
