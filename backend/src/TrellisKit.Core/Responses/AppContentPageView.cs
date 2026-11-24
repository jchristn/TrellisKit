namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A page of published app content (POST /api/v1/app/strings, banners, and assets enumerate) with the content version
    /// the client records after a full refresh.
    /// </summary>
    /// <typeparam name="T">Object type.</typeparam>
    public class AppContentPageView<T> : EnumerationResult<T>
    {
        /// <summary>
        /// Current content version.
        /// </summary>
        public long ContentVersion { get; set; } = 0;

        /// <summary>
        /// Copy a page with new objects and the content version.
        /// </summary>
        /// <typeparam name="TIn">Source object type.</typeparam>
        /// <param name="page">Source page.</param>
        /// <param name="objects">Objects.</param>
        /// <param name="contentVersion">Content version.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static AppContentPageView<T> From<TIn>(EnumerationResult<TIn> page, List<T> objects, long contentVersion)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(objects);
            return new AppContentPageView<T>
            {
                Success = page.Success,
                MaxResults = page.MaxResults,
                Skip = page.Skip,
                ContinuationToken = page.ContinuationToken,
                EndOfResults = page.EndOfResults,
                TotalRecords = page.TotalRecords,
                RecordsRemaining = page.RecordsRemaining,
                TimestampUtc = page.TimestampUtc,
                Objects = objects,
                ContentVersion = contentVersion
            };
        }
    }
}
