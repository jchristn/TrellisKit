namespace TrellisKit.Server.Services.CatalogImport
{
    using System;

    /// <summary>One bounded source-key report. Contains no copied source content.</summary>
    public sealed class CatalogImportOutcome
    {
        /// <summary>Stable source record key.</summary>
        public string Key { get; set; } = String.Empty;
        /// <summary>CreatedDraft, RefreshedDraft, Unchanged, Preserved, Planned, Invalid or DuplicateName.</summary>
        public string Outcome { get; set; } = String.Empty;
        /// <summary>Existing or created catalog identifier, when known.</summary>
        public string? EntryId { get; set; }
        /// <summary>Safe validation code, never source body content.</summary>
        public string? ErrorCode { get; set; }
    }
}
