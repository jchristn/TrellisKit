namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The pieces the venue map and the events map share (docs/PHASE4_CONTRACT.md sections 3.3 and 4.2): the cluster
    /// feature of a grid cell, the merge of cells with their one-item points, and a cover thumbnail URL.
    /// </summary>
    public static class MapFeatureBuilder
    {
        #region Public-Methods

        /// <summary>Bind cursors and feature identities to the viewport, cell budget and caller visibility.</summary>
        public static string Scope(GeoBox box, int zoom, int budget, ContentActor actor, bool publicRoute)
        {
            return JsonSerializer.Serialize(new { box, zoom, budget, actor.UserId, actor.IsAnonymous, actor.HasMembership, actor.IsDirectoryStaff, actor.IsEventStaff, publicRoute });
        }

        /// <summary>Page an independently bounded feature set after SQL has applied source filters.</summary>
        public static CatalogEnumerationResponse<MapFeatureView> Page(EnumerationRequest source, List<MapFeatureView> features, long matchedRecords, string scope)
        {
            foreach (MapFeatureView feature in features)
            {
                string identity = feature.Venue?.Id ?? feature.Event?.Id ?? feature.Id;
                feature.Id = "map_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "|" + identity))).ToLowerInvariant();
            }
            // Search, date and metadata filters describe the underlying records and have already run in SQL.
            // Preserve the validated position and fingerprint while avoiding a second filter over generated cells.
            EnumerationRequest pageRequest = new EnumerationRequest
            {
                Collection = source.Collection, Query = new EnumerationQuery(), Ordering = source.Ordering,
                MaxResults = source.MaxResults, Skip = source.Skip, AfterKey = source.AfterKey, AfterId = source.AfterId,
                Fingerprint = source.Fingerprint, CountTotals = true, IncludeStaffMetadata = source.IncludeStaffMetadata
            };
            string Key(MapFeatureView feature) => source.Ordering.Name.StartsWith("Start", StringComparison.Ordinal)
                ? (feature.Event?.StartUtc ?? DateTime.UnixEpoch).ToString("O", CultureInfo.InvariantCulture)
                : (feature.Venue?.Name ?? feature.Event?.Title ?? feature.Id).ToLowerInvariant();
            EnumerationResult<MapFeatureView> page = CatalogEnumeration.Page(pageRequest, features, feature => feature.Id,
                feature => feature.CreatedUtc, orderKey: Key);
            return CatalogEnumerationResponse<MapFeatureView>.From(page, new Dictionary<string, object?> { ["matchedRecords"] = matchedRecords });
        }

        /// <summary>
        /// The Cluster feature of a grid cell (center and bounds rounded to 5 places).
        /// </summary>
        /// <param name="cell">Cell.</param>
        /// <returns>Feature.</returns>
        /// <exception cref="ArgumentNullException">The cell is null.</exception>
        public static MapFeatureView Cluster(MapCluster cell)
        {
            ArgumentNullException.ThrowIfNull(cell);
            return new MapFeatureView
            {
                Id = "cell:" + cell.GridLatitude.ToString(CultureInfo.InvariantCulture) + ":" + cell.GridLongitude.ToString(CultureInfo.InvariantCulture),
                Kind = MapFeatureKindEnum.Cluster,
                Count = cell.Count,
                Lat = GeoMath.Round(cell.Lat, 5),
                Lng = GeoMath.Round(cell.Lng, 5),
                Bbox = new GeoBox { South = GeoMath.Round(cell.South, 5), West = GeoMath.Round(cell.West, 5), North = GeoMath.Round(cell.North, 5), East = GeoMath.Round(cell.East, 5) }
            };
        }

        /// <summary>
        /// Features of grid cells in order: a one-item cell becomes its point when one was built, every other cell a cluster.
        /// </summary>
        /// <param name="cells">Cells.</param>
        /// <param name="points">Points of one-item cells by item identifier.</param>
        /// <returns>Features.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static List<MapFeatureView> Merge(List<MapCluster> cells, Dictionary<string, MapFeatureView> points)
        {
            ArgumentNullException.ThrowIfNull(cells);
            ArgumentNullException.ThrowIfNull(points);
            List<MapFeatureView> features = new List<MapFeatureView>();
            foreach (MapCluster c in cells)
            {
                if (c.ItemId != null && points.TryGetValue(c.ItemId, out MapFeatureView? point)) features.Add(point);
                else if (c.ItemId == null) features.Add(Cluster(c));
            }
            return features;
        }

        /// <summary>
        /// The thumbnail URL of an image (a variant whose key contains "thumb", else the first variant; WebP first).
        /// </summary>
        /// <param name="media">Media, or null.</param>
        /// <returns>URL, or null.</returns>
        public static string? ThumbUrl(MediaView? media)
        {
            if (media == null || media.Image == null) return null;
            MediaVariantView? variant = media.Image.Variants.Where(p => p.Key.Contains("thumb", StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).FirstOrDefault() ?? media.Image.Variants.Values.FirstOrDefault();
            return variant?.WebpUrl ?? variant?.JpegUrl;
        }

        #endregion
    }
}
