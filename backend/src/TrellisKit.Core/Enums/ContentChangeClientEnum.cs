namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// The client a content change came from (X-TK-Client, else X-Client, else the User-Agent).
    /// </summary>
    public enum ContentChangeClientEnum
    {
        /// <summary>
        /// The website or web member app.
        /// </summary>
        Web,

        /// <summary>
        /// The iOS or Android app.
        /// </summary>
        Mobile,

        /// <summary>
        /// Any other HTTP caller (scripts, integrations, the API explorer).
        /// </summary>
        Api,

        /// <summary>
        /// No request: a background worker, job, or seed.
        /// </summary>
        System
    }
}
