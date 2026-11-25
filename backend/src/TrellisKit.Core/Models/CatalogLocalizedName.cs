namespace TrellisKit.Core.Models
{
    using System;
    /// <summary>A source-reviewed localized name, not automatic translation.</summary>
    public sealed class CatalogLocalizedName
    {
        /// <summary>Configured language[-Script][-REGION] tag, at most 35 characters.</summary>
        public string LanguageTag { get; set; } = String.Empty;
        /// <summary>Unicode NFC text, 1 to 160 characters.</summary>
        public string Text { get; set; } = String.Empty;
    }
}
