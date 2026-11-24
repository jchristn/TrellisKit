namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The directory sections of the account export (docs/PHASE4_CONTRACT.md section 10): directorySubmissions and
    /// instructorLinks.
    /// </summary>
    public static class DirectoryExportSections
    {
        #region Public-Members

        /// <summary>
        /// Section names.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = new List<string> { "directorySubmissions", "instructorLinks" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the sections.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static void Register(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AccountExports.Register(new PagedExportSection<DirectorySubmissionExportItem>("directorySubmissions", (userId, after, limit, token) => SubmissionsAsync(services, userId, after, limit, token)));
            services.AccountExports.Register(new PagedExportSection<InstructorLinkExportItem>("instructorLinks", (userId, after, limit, token) => LinksAsync(services, userId, after, token)));
        }

        #endregion

        #region Private-Methods

        private static async Task<ContentExportPage<DirectorySubmissionExportItem>> SubmissionsAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<DirectorySubmission> rows = await services.Database.DirectoryListings.Submissions.ListByUserAsync(userId, false, after, limit, token).ConfigureAwait(false);
            List<DirectorySubmissionExportItem> items = rows.Select(s => new DirectorySubmissionExportItem
            {
                Id = s.Id,
                Type = s.Type,
                Status = s.Status,
                TargetType = s.TargetType,
                TargetId = s.TargetId,
                TargetName = s.TargetName,
                Note = s.Note,
                ReviewNote = s.ReviewerNote,
                Changes = DirectorySubmissionFields.Changes(s),
                CreatedUtc = s.CreatedUtc,
                DecidedUtc = s.DecidedUtc
            }).ToList();
            return new ContentExportPage<DirectorySubmissionExportItem> { Objects = items, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
        }

        private static async Task<ContentExportPage<InstructorLinkExportItem>> LinksAsync(ApplicationServices services, string userId, string? after, CancellationToken token)
        {
            if (after != null) return new ContentExportPage<InstructorLinkExportItem>();
            Instructor? linked = await services.Database.DirectoryListings.Instructors.ReadByLinkedUserAsync(userId, token).ConfigureAwait(false);
            List<InstructorLinkExportItem> items = linked == null ? new List<InstructorLinkExportItem>() : new List<InstructorLinkExportItem> { new InstructorLinkExportItem { InstructorId = linked.Id, DisplayName = linked.DisplayName, LinkedUtc = linked.LinkedUtc } };
            return new ContentExportPage<InstructorLinkExportItem> { Objects = items, NextAfterId = null };
        }

        #endregion
    }
}
