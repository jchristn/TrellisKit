namespace TrellisKit.Core.Models
{
    using System.Collections.Generic;
    /// <summary>Optional typed program descriptors. An explicit request object replaces this entire group.</summary>
    public sealed class CatalogModalityDetails
    {
        /// <summary>At most 10 localized names. Empty explicitly clears names.</summary>
        public List<CatalogLocalizedName> LocalizedNames { get; set; } = new List<CatalogLocalizedName>();
        /// <summary>Configured family key for the entry program, or null.</summary>
        public string? FamilyKey { get; set; }
        /// <summary>Configured attire key for the entry program, or null.</summary>
        public string? AttireKey { get; set; }
        /// <summary>Configured instructional content level, not a personal credential, or null.</summary>
        public string? LevelKey { get; set; }
        /// <summary>Configured notation system; required together with notation text, or null.</summary>
        public string? NotationSystemKey { get; set; }
        /// <summary>Optional notation in its declared system, normalized NFC and trimmed, 1 to 500 characters.</summary>
        public string? NotationText { get; set; }
    }
}
