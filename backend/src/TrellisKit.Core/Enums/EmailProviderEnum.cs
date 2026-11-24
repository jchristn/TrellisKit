namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Administrative email provider (docs/TRELLISKIT.md section 12.1).
    /// </summary>
    public enum EmailProviderEnum
    {
        /// <summary>
        /// Mailgun when its domain and API key are configured, else SMTP when a host is configured, else Log.
        /// </summary>
        Auto,

        /// <summary>
        /// Mailgun through SendWithMailgun.
        /// </summary>
        Mailgun,

        /// <summary>
        /// SMTP through MailKit (local development with Mailpit, and fallback).
        /// </summary>
        Smtp,

        /// <summary>
        /// No delivery: the message subject and links are written to the server log.
        /// </summary>
        Log
    }
}
