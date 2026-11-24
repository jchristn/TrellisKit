namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>A standard enumeration with scalar or bounded single-object context.</summary>
    /// <typeparam name="T">Enumerated object type.</typeparam>
    public class CatalogEnumerationResponse<T> : EnumerationResult<T>
    {
        /// <summary>Supplementary context, serialized alongside the standard paging fields.</summary>
        [JsonExtensionData]
        public Dictionary<string, object?> Context { get; set; } = new Dictionary<string, object?>();

        /// <summary>Preserve a page exactly and add its non-collection context.</summary>
        /// <param name="page">The standard enumeration page.</param>
        /// <param name="context">Optional scalar or bounded object context.</param>
        /// <returns>The same page with additional context.</returns>
        /// <exception cref="ArgumentNullException">The page is null.</exception>
        public static CatalogEnumerationResponse<T> From(EnumerationResult<T> page, Dictionary<string, object?>? context = null)
        {
            ArgumentNullException.ThrowIfNull(page);
            return new CatalogEnumerationResponse<T> { Success = page.Success, MaxResults = page.MaxResults, Skip = page.Skip, ContinuationToken = page.ContinuationToken, EndOfResults = page.EndOfResults, TotalRecords = page.TotalRecords, RecordsRemaining = page.RecordsRemaining, TimestampUtc = page.TimestampUtc, Objects = page.Objects, Context = context ?? new Dictionary<string, object?>() };
        }
    }
}
