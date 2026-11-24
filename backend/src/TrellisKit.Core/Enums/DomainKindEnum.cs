namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Role of a registered property host name.
    /// </summary>
    public enum DomainKindEnum
    {
        /// <summary>
        /// The website host.
        /// </summary>
        Web,

        /// <summary>
        /// The API host.
        /// </summary>
        Api,

        /// <summary>
        /// The media delivery host.
        /// </summary>
        Media,

        /// <summary>
        /// A host that redirects to the website.
        /// </summary>
        Redirect,

        /// <summary>
        /// Another host the property is served on.
        /// </summary>
        Alias
    }
}
