namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/email/templates (fixed-bound by the engine catalog).
    /// </summary>
    public class EmailTemplateList
    {
        #region Public-Members

        /// <summary>
        /// Templates.
        /// </summary>
        public List<EmailTemplateView> Templates
        {
            get { return _Templates; }
            set { _Templates = value ?? new List<EmailTemplateView>(); }
        }

        #endregion

        #region Private-Members

        private List<EmailTemplateView> _Templates = new List<EmailTemplateView>();

        #endregion
    }
}
