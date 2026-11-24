namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Venues, their programs, social links, photos, slug redirects, links to instructors, and the spatial queries of
    /// docs/PHASE4_CONTRACT.md section 3.4 (bounding box prefilter and Haversine distance in plain SQL).
    /// </summary>
    public interface IVenueMethods
    {
        /// <summary>
        /// The statements that insert a venue and its child rows (for composing into a larger transaction).
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <returns>Statements.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        List<string> CreateStatements(Venue venue);

        /// <summary>
        /// The statements that update a venue and replace its child rows, recording the previous slug as a redirect when
        /// it changed.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <param name="previousSlug">Slug before the change, or null.</param>
        /// <returns>Statements.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        List<string> UpdateStatements(Venue venue, string? previousSlug);

        /// <summary>
        /// Create a venue.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored venue.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The slug is in use.</exception>
        Task<Venue> CreateAsync(Venue venue, CancellationToken token = default);

        /// <summary>
        /// Update a venue.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <param name="previousSlug">Slug before the change, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored venue, or null when it was deleted.</returns>
        /// <exception cref="ArgumentNullException">The venue is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The slug is in use.</exception>
        Task<Venue?> UpdateAsync(Venue venue, string? previousSlug, CancellationToken token = default);

        /// <summary>
        /// Read a live (not deleted) venue with its child lists.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venue, or null.</returns>
        Task<Venue?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a live venue by its slug or a previous slug.
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venue, or null.</returns>
        Task<Venue?> ReadBySlugAsync(string slug, CancellationToken token = default);

        /// <summary>
        /// Read live venues by identifier.
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues (missing ones are absent).</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        Task<List<Venue>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Enumerate venues with the filters, the near and bounding box queries, and the DistanceAscending and Relevance
        /// orderings (keyset continuation over (key, id)).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page (DistanceKm set on near queries).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<Venue>> EnumerateAsync(EnumerationRequest request, VenueFilter filter, CancellationToken token = default);

        /// <summary>
        /// Count the venues a map viewport holds.
        /// </summary>
        /// <param name="request">Normalized request (labels, tags, and search apply).</param>
        /// <param name="filter">Filters (Box required).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<long> CountMapAsync(EnumerationRequest request, VenueFilter filter, CancellationToken token = default);

        /// <summary>
        /// The venues of a map viewport (point mode), by identifier.
        /// </summary>
        /// <param name="request">Normalized request (labels, tags, and search apply).</param>
        /// <param name="filter">Filters (Box required).</param>
        /// <param name="limit">Most venues.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues with child lists.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<List<Venue>> MapPointsAsync(EnumerationRequest request, VenueFilter filter, int limit, CancellationToken token = default);

        /// <summary>
        /// Grid clusters of a map viewport: GROUP BY floor(lat / cell), floor(lng / cell).
        /// </summary>
        /// <param name="request">Normalized request (labels, tags, and search apply).</param>
        /// <param name="filter">Filters (Box required).</param>
        /// <param name="cellDegrees">Cell size in degrees.</param>
        /// <param name="limit">Most cells.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Cells (VenueId set when a cell holds one venue).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<List<MapCluster>> MapClustersAsync(EnumerationRequest request, VenueFilter filter, double cellDegrees, int limit, CancellationToken token = default);

        /// <summary>
        /// Set or clear verification.
        /// </summary>
        /// <param name="id">Venue.</param>
        /// <param name="verified">Verified.</param>
        /// <param name="byUserId">Staff member, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the venue exists.</returns>
        Task<bool> SetVerifiedAsync(string id, bool verified, string? byUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the lifecycle status.
        /// </summary>
        /// <param name="id">Venue.</param>
        /// <param name="status">Status.</param>
        /// <param name="reason">Reason (kept when closing), or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the venue exists.</returns>
        Task<bool> SetStatusAsync(string id, VenueStatusEnum status, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The statement that closes a venue (ReportClosed approvals).
        /// </summary>
        /// <param name="id">Venue.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Statement.</returns>
        string CloseStatement(string id, string? reason, DateTime nowUtc);

        /// <summary>
        /// Set the moderation state.
        /// </summary>
        /// <param name="id">Venue.</param>
        /// <param name="state">State.</param>
        /// <param name="byUserId">Moderator, or null.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the venue exists.</returns>
        Task<bool> SetModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the open report count.
        /// </summary>
        /// <param name="id">Venue.</param>
        /// <param name="count">Open reports.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetOpenReportCountAsync(string id, int count, CancellationToken token = default);

        /// <summary>
        /// Soft delete a venue: links to instructors are removed and the instructors' counters recomputed.
        /// </summary>
        /// <param name="id">Venue.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Store a geocoded position unless staff placed a pin since the request (the manual override rule).
        /// </summary>
        /// <param name="id">Venue.</param>
        /// <param name="lat">Latitude, or null when nothing matched.</param>
        /// <param name="lng">Longitude, or null.</param>
        /// <param name="timeZone">Time zone from the geocoder, or null (kept when the venue has one).</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when stored.</returns>
        Task<bool> SetGeocodedAsync(string id, double? lat, double? lng, string? timeZone, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Queue venues for background geocoding (GeocodeWorker). Manual pins are skipped.
        /// </summary>
        /// <param name="ids">Venues, or null for every live venue.</param>
        /// <param name="onlyMissing">Only venues without a position.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues queued.</returns>
        Task<int> QueueGeocodeAsync(List<string>? ids, bool onlyMissing, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Claim queued venues (the queue flag is cleared in the claim).
        /// </summary>
        /// <param name="limit">Most venues.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues.</returns>
        Task<List<Venue>> ClaimGeocodeAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Link an instructor to a venue (the role is replaced when linked) and recompute both counters.
        /// </summary>
        /// <param name="venueId">Venue.</param>
        /// <param name="instructorId">Instructor.</param>
        /// <param name="role">Role.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The link.</returns>
        Task<VenueInstructorLink> LinkAsync(string venueId, string instructorId, VenueInstructorRoleEnum role, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The statements that link an instructor to a venue (for composing into a larger transaction).
        /// </summary>
        /// <param name="venueId">Venue.</param>
        /// <param name="instructorId">Instructor.</param>
        /// <param name="role">Role.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Statements.</returns>
        List<string> LinkStatements(string venueId, string instructorId, VenueInstructorRoleEnum role, DateTime nowUtc);

        /// <summary>
        /// Remove a link and recompute both counters.
        /// </summary>
        /// <param name="venueId">Venue.</param>
        /// <param name="instructorId">Instructor.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a link was removed.</returns>
        Task<bool> UnlinkAsync(string venueId, string instructorId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The roles of instructors at a venue.
        /// </summary>
        /// <param name="venueId">Venue.</param>
        /// <param name="instructorIds">Instructors.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Roles by instructor id.</returns>
        Task<Dictionary<string, VenueInstructorRoleEnum>> RolesAsync(string venueId, IEnumerable<string> instructorIds, CancellationToken token = default);

        /// <summary>
        /// Duplicate candidates: live venues whose name contains a word, or that lie in a box, bounded.
        /// </summary>
        /// <param name="nameWords">Lowercase words of the proposed name (any matches).</param>
        /// <param name="box">Box around the proposed position, or null.</param>
        /// <param name="limit">Most candidates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues without child lists.</returns>
        Task<List<Venue>> DuplicateCandidatesAsync(IEnumerable<string> nameWords, GeoBox? box, int limit, CancellationToken token = default);

        /// <summary>
        /// A page of live venues by identifier (exports), every status.
        /// </summary>
        /// <param name="afterId">Exclusive position, or null.</param>
        /// <param name="limit">Most venues.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues with child lists.</returns>
        Task<List<Venue>> ListPageAsync(string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Directory counts for the admin dashboard.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics.</returns>
        Task<DirectoryMetricsView> MetricsAsync(DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One directory daily series: "submissions" (created), "approvals" (approved, by decision time), or "venues"
        /// (venues created, deleted excluded); one entry per day from the window start.
        /// </summary>
        /// <param name="series">Series name.</param>
        /// <param name="fromUtc">First day (UTC midnight).</param>
        /// <param name="days">Days in the window.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts per day.</returns>
        /// <exception cref="ArgumentException">Unknown series.</exception>
        Task<List<DailyCount>> DailyAsync(string series, DateTime fromUtc, int days, CancellationToken token = default);
    }
}
