namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// POST /api/v1/me/export (202): the queued export.
    /// </summary>
    public class AccountExportCreatedResponse
    {
        #region Public-Members

        /// <summary>
        /// Export identifier; poll GET /api/v1/me/export/{exportId}.
        /// </summary>
        public string ExportId { get; set; } = String.Empty;

        /// <summary>
        /// Status (Pending).
        /// </summary>
        public AccountExportStatusEnum Status { get; set; } = AccountExportStatusEnum.Pending;

        #endregion
    }
}
