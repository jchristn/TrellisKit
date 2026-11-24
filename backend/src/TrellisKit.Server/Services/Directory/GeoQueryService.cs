namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The map enumeration (docs/PHASE4_CONTRACT.md sections 3.3 and 3.4): every visible venue of the viewport as a point
    /// while they fit in the configured feature budget, otherwise server-side grid clusters (GROUP BY floor(lat / cell), floor(lng / cell)) with
    /// a cell size chosen from the zoom. LocalityOnly venues are never on the map; Approximate venues sit at their
    /// rounded position. Generated features use standard keyset pages. Thread safe.
    /// </summary>
    public class GeoQueryService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public GeoQueryService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/directory/map/enumerate (and the public form).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Body (bbox required, zoom, maxResults, labels, tags, filters).</param>
        /// <param name="publicRoute">Public anonymous route (Public, Active venues only).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of generated features with exact feature totals and matched source count.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidGeo, 429 RateLimited.</exception>
        public async Task<EnumerationResult<MapFeatureView>> EnumerateAsync(ContentActor actor, MapEnumerationQuery query, bool publicRoute, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(query);
            if (!publicRoute) _Services.Limits.EnforceMember(actor, "directory-map", _Services.Settings.Directory.MapPerMinute, RateLimitService.Minute);
            GeoBox box = DirectoryFilters.ValidateBox(query.Bbox, "bbox");
            int zoom = Math.Clamp(query.Zoom ?? 2, 0, 22);
            int maxFeatures = await _Services.Directory.Config.MaxFeaturesAsync(token).ConfigureAwait(false);
            int max = maxFeatures;
            string scope = MapFeatureBuilder.Scope(box, zoom, max, actor, publicRoute);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.Map, actor.IsDirectoryStaff && !publicRoute, scope);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            VenueFilter filter = new VenueFilter
            {
                Box = box,
                ExcludeLocalityOnly = true,
                Programs = DirectoryFilters.Strings(query, "programs"),
                CountryCode = query.FilterString("countryCode"),
                Region = query.FilterString("region"),
                Locality = query.FilterString("locality"),
                VisitorPolicies = DirectoryFilters.Enums<VisitorPolicyEnum>(query, "visitorPolicy", errors),
                VerifiedOnly = query.FilterBool("verifiedOnly", errors) ?? false,
                InstructorId = query.FilterString("instructorId"),
                AudienceFilter = DirectoryFilters.Enums<ContentAudienceEnum>(query, "audience", errors),
                Audiences = publicRoute ? new List<ContentAudienceEnum> { ContentAudienceEnum.Public } : DirectoryAccess.VisibleAudiences(actor),
                Statuses = new List<VenueStatusEnum> { VenueStatusEnum.Active },
                LiveOnly = true
            };
            DirectoryFilters.ThrowIfAny(errors);
            Stopwatch watch = Stopwatch.StartNew();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("directory.map"))
            {
                long count = await _Services.Database.DirectoryListings.Venues.CountMapAsync(request, filter, token).ConfigureAwait(false);
                List<MapFeatureView> features = new List<MapFeatureView>();
                string mode;
                if (count <= max)
                {
                    mode = "points";
                    List<Venue> venues = await _Services.Database.DirectoryListings.Venues.MapPointsAsync(request, filter, max, token).ConfigureAwait(false);
                    features.AddRange(await PointsAsync(actor, venues, publicRoute, token).ConfigureAwait(false));
                }
                else
                {
                    mode = "clusters";
                    double cell = GeoMath.CellSize(box, zoom, max);
                    List<MapCluster> cells = await _Services.Database.DirectoryListings.Venues.MapClustersAsync(request, filter, cell, max, token).ConfigureAwait(false);
                    List<string> singles = cells.Where(c => c.ItemId != null).Select(c => c.ItemId!).ToList();
                    Dictionary<string, MapFeatureView> points = (await PointsAsync(actor, await _Services.Database.DirectoryListings.Venues.ReadManyAsync(singles, token).ConfigureAwait(false), publicRoute, token).ConfigureAwait(false))
                        .ToDictionary(p => p.Venue!.Id, StringComparer.Ordinal);
                    features.AddRange(MapFeatureBuilder.Merge(cells, points));
                }
                activity?.SetTag("map.mode", mode);
                TrellisKitTelemetry.Add(DirectoryTelemetry.MapQueries, 1, "mode", mode);
                TrellisKitTelemetry.Record(DirectoryTelemetry.QueryDuration, watch.Elapsed.TotalSeconds, "kind", "map");
                return MapFeatureBuilder.Page(request, features, count, scope);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<List<MapFeatureView>> PointsAsync(ContentActor actor, List<Venue> venues, bool publicRoute, CancellationToken token)
        {
            List<MapFeatureView> result = new List<MapFeatureView>();
            if (venues.Count == 0) return result;
            Dictionary<string, List<MediaView>> photos = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Venue, venues.Select(v => v.Id), publicRoute, false, token).ConfigureAwait(false);
            bool staff = actor.IsDirectoryStaff && !publicRoute;
            foreach (Venue venue in venues.OrderBy(v => v.Id, StringComparer.Ordinal))
            {
                if (venue.AddressVisibility == AddressVisibilityEnum.LocalityOnly && !staff) continue;
                GeoView geo = DirectoryViewBuilder.Geo(venue, false);
                if (staff) geo.Source = venue.GeoSource;
                string? thumb = photos.TryGetValue(venue.Id, out List<MediaView>? list) && list.Count > 0 ? MapFeatureBuilder.ThumbUrl(list[0]) : null;
                result.Add(new MapFeatureView
                {
                    CreatedUtc = venue.CreatedUtc,
                    Kind = MapFeatureKindEnum.Point,
                    Venue = new MapVenueView { Id = venue.Id, Name = venue.Name, Slug = venue.Slug, Geo = geo, Programs = venue.Programs, IsVerified = venue.IsVerified, CoverThumbUrl = thumb }
                });
            }
            return result;
        }

        #endregion
    }
}
