namespace TrellisKit.Core.Models
{
    /// <summary>One source-reviewed apparatus configuration. An explicit request object replaces this whole group.</summary>
    public sealed class CatalogApparatusSetup
    {
        /// <summary>Configured equipment key also selected by the entry, at most 64 characters; null for an empty group.</summary>
        public string? ApparatusKey { get; set; }
        /// <summary>Literal manufacturer, NFC and trimmed, at most 120 characters; null if unspecified.</summary>
        public string? Manufacturer { get; set; }
        /// <summary>Literal model, NFC and trimmed, at most 120 characters; requires manufacturer when present.</summary>
        public string? Model { get; set; }
        /// <summary>Source-reviewed setup text, NFC and trimmed, at most 500 characters; null if unspecified.</summary>
        public string? SetupText { get; set; }
        /// <summary>Literal spring setup, NFC and trimmed, at most 500 characters; requires apparatus, manufacturer and model.</summary>
        public string? SpringSetupText { get; set; }
    }
}
