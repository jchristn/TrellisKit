namespace TrellisKit.Server.Services.CatalogGeneration
{
    using TrellisKit.Core.Requests;

    /// <summary>Closed model answer; unsupported topics produce no entry.</summary>
    public sealed class CatalogDraftReply
    {
        /// <summary>True only when the supplied source supports the requested movement.</summary>
        public bool Supported { get; set; }
        /// <summary>Draft fields, or null when unsupported. Identity, provenance and lifecycle are controlled by the server.</summary>
        public CatalogEntryRequest? Entry { get; set; }
    }
}
