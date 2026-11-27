namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Server.Services.Email.Blocks;

    /// <summary>
    /// One titled table of details on a staff email (docs/EMAIL.md, "Staff email details"): request details, member details, or a
    /// template-specific section. Rows with a blank value are dropped when the section is added to
    /// <see cref="StaffEmailDetails"/>. Thread safety: not thread safe.
    /// </summary>
    public class StaffEmailSection
    {
        #region Public-Members

        /// <summary>
        /// Section title, at most 120 characters when rendered.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Rows (label and value), at most 20 rendered; never null.
        /// </summary>
        public List<EmailField> Rows
        {
            get { return _Rows; }
            set { _Rows = value ?? new List<EmailField>(); }
        }

        #endregion

        #region Private-Members

        private List<EmailField> _Rows = new List<EmailField>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public StaffEmailSection()
        {
        }

        /// <summary>
        /// Instantiate with a title.
        /// </summary>
        /// <param name="title">Title.</param>
        public StaffEmailSection(string? title)
        {
            Title = title ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a row when the value is not blank.
        /// </summary>
        /// <param name="label">Label.</param>
        /// <param name="value">Value; null or blank adds nothing.</param>
        /// <returns>This section.</returns>
        public StaffEmailSection Row(string label, string? value)
        {
            if (!String.IsNullOrWhiteSpace(label) && !String.IsNullOrWhiteSpace(value)) _Rows.Add(new EmailField(label, value.Trim()));
            return this;
        }

        #endregion
    }
}
