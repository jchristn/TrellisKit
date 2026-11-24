namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/signup-codes/batches.
    /// </summary>
    public class SignupCodeBatchResponse : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Batch identifier.
        /// </summary>
        public string BatchId { get; set; } = String.Empty;

        /// <summary>
        /// Generated codes. Never null.
        /// </summary>
        public List<SignupCodeView> Codes
        {
            get { return _Codes; }
            set { _Codes = value ?? new List<SignupCodeView>(); }
        }

        /// <summary>
        /// Labels visible to the caller (staff: entries only for staff). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags visible to the caller (staff: keys only for staff). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        private List<SignupCodeView> _Codes = new List<SignupCodeView>();

        #endregion
    }
}
