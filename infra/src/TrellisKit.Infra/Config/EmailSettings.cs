namespace TrellisKit.Infra.Config
{
    using System;

    /// <summary>
    /// Amazon SES identity settings.
    /// </summary>
    public class EmailSettings
    {
        private string _MailFromLabel = "mail";
        private string _DmarcPolicy = "quarantine";
        private string _DmarcReportLocalPart = "dmarc";
        private string _SmtpHostTemplate = "email-smtp.{0}.amazonaws.com";

        /// <summary>
        /// Label of the custom MAIL FROM domain under the primary domain (SPF aligns here). Default mail.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string MailFromLabel
        {
            get { return _MailFromLabel; }
            set { _MailFromLabel = Guard.NotEmpty(value, nameof(MailFromLabel)); }
        }

        /// <summary>
        /// DMARC policy: none, quarantine, or reject. Default quarantine.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a DMARC policy.</exception>
        public string DmarcPolicy
        {
            get { return _DmarcPolicy; }
            set
            {
                if (value != "none" && value != "quarantine" && value != "reject")
                {
                    throw new ArgumentOutOfRangeException(nameof(DmarcPolicy), value, "DmarcPolicy must be none, quarantine, or reject.");
                }

                _DmarcPolicy = value;
            }
        }

        /// <summary>
        /// Local part on the primary domain that receives DMARC aggregate reports. Default dmarc.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string DmarcReportLocalPart
        {
            get { return _DmarcReportLocalPart; }
            set { _DmarcReportLocalPart = Guard.NotEmpty(value, nameof(DmarcReportLocalPart)); }
        }

        /// <summary>
        /// SES SMTP endpoint template; {0} is the region. Default email-smtp.{0}.amazonaws.com.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string SmtpHostTemplate
        {
            get { return _SmtpHostTemplate; }
            set { _SmtpHostTemplate = Guard.NotEmpty(value, nameof(SmtpHostTemplate)); }
        }
    }
}
