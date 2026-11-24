namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Plain spatial arithmetic for the directory (docs/PHASE4_CONTRACT.md section 3.4): Haversine distance, the
    /// bounding boxes of a radius (split across the antimeridian, open in longitude near the poles), grid cell sizes,
    /// and coordinate rounding. Thread safe (stateless).
    /// </summary>
    public static class GeoMath
    {
        #region Public-Members

        /// <summary>
        /// Mean Earth radius in kilometers (IUGG).
        /// </summary>
        public const double EarthRadiusKm = 6371.0088;

        /// <summary>
        /// Kilometers per degree of latitude.
        /// </summary>
        public const double KmPerDegree = 111.32;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Great circle distance between two points in kilometers.
        /// </summary>
        /// <param name="lat1">Latitude of the first point.</param>
        /// <param name="lng1">Longitude of the first point.</param>
        /// <param name="lat2">Latitude of the second point.</param>
        /// <param name="lng2">Longitude of the second point.</param>
        /// <returns>Distance in kilometers.</returns>
        public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
        {
            double dLat = Radians(lat2 - lat1);
            double dLng = Radians(lng2 - lng1);
            double a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
            return EarthRadiusKm * 2 * Math.Asin(Math.Sqrt(Math.Min(1.0, a)));
        }

        /// <summary>
        /// The boxes that cover a radius around a point: one box normally, two when the box crosses the antimeridian,
        /// and a full longitude band when it reaches a pole or spans the globe. West greater than East never occurs in
        /// the result (crossing boxes are split).
        /// </summary>
        /// <param name="near">Point and radius.</param>
        /// <returns>Boxes.</returns>
        /// <exception cref="ArgumentNullException">The point is null.</exception>
        public static List<GeoBox> NearBoxes(GeoNear near)
        {
            ArgumentNullException.ThrowIfNull(near);
            double latSpan = near.RadiusKm / KmPerDegree;
            double south = near.Lat - latSpan;
            double north = near.Lat + latSpan;
            List<GeoBox> boxes = new List<GeoBox>();
            if (south <= -90 || north >= 90)
            {
                boxes.Add(new GeoBox { South = Math.Max(-90, south), North = Math.Min(90, north), West = -180, East = 180 });
                return boxes;
            }
            double widest = Math.Max(Math.Abs(south), Math.Abs(north));
            double cos = Math.Cos(Radians(widest));
            double lngSpan = cos <= 1e-9 ? 360 : latSpan / cos;
            if (lngSpan >= 180)
            {
                boxes.Add(new GeoBox { South = south, North = north, West = -180, East = 180 });
                return boxes;
            }
            double west = near.Lng - lngSpan;
            double east = near.Lng + lngSpan;
            if (west < -180)
            {
                boxes.Add(new GeoBox { South = south, North = north, West = west + 360, East = 180 });
                boxes.Add(new GeoBox { South = south, North = north, West = -180, East = east });
            }
            else if (east > 180)
            {
                boxes.Add(new GeoBox { South = south, North = north, West = west, East = 180 });
                boxes.Add(new GeoBox { South = south, North = north, West = -180, East = east - 360 });
            }
            else
            {
                boxes.Add(new GeoBox { South = south, North = north, West = west, East = east });
            }
            return boxes;
        }

        /// <summary>
        /// Longitude width of a box in degrees (crossing boxes wrap).
        /// </summary>
        /// <param name="box">Box.</param>
        /// <returns>Width (0 to 360).</returns>
        /// <exception cref="ArgumentNullException">The box is null.</exception>
        public static double LngWidth(GeoBox box)
        {
            ArgumentNullException.ThrowIfNull(box);
            double width = box.East - box.West;
            if (width < 0) width += 360;
            return Math.Min(360, width);
        }

        /// <summary>
        /// The grid cell size for clustering: starts from the tile width of the zoom divided by 8 and doubles until the
        /// viewport holds at most maxCells cells.
        /// </summary>
        /// <param name="box">Viewport.</param>
        /// <param name="zoom">Zoom 0 to 22.</param>
        /// <param name="maxCells">Most cells.</param>
        /// <returns>Cell size in degrees.</returns>
        /// <exception cref="ArgumentNullException">The box is null.</exception>
        public static double CellSize(GeoBox box, int zoom, int maxCells)
        {
            ArgumentNullException.ThrowIfNull(box);
            double cell = 360.0 / Math.Pow(2, Math.Clamp(zoom, 0, 22)) / 8.0;
            double latSpan = Math.Max(1e-9, box.North - box.South);
            double lngSpan = Math.Max(1e-9, LngWidth(box));
            int limit = Math.Max(1, maxCells);
            for (int i = 0; i < 64; i++)
            {
                double cells = (Math.Floor(latSpan / cell) + 2) * (Math.Floor(lngSpan / cell) + 2);
                if (cells <= limit) break;
                cell *= 2;
            }
            return cell;
        }

        /// <summary>
        /// Round a coordinate.
        /// </summary>
        /// <param name="value">Coordinate.</param>
        /// <param name="decimals">Decimal places.</param>
        /// <returns>Rounded value.</returns>
        public static double Round(double value, int decimals)
        {
            return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Whether a point lies in a box (crossing boxes wrap).
        /// </summary>
        /// <param name="box">Box.</param>
        /// <param name="lat">Latitude.</param>
        /// <param name="lng">Longitude.</param>
        /// <returns>True when inside.</returns>
        /// <exception cref="ArgumentNullException">The box is null.</exception>
        public static bool Contains(GeoBox box, double lat, double lng)
        {
            ArgumentNullException.ThrowIfNull(box);
            if (lat < box.South || lat > box.North) return false;
            return box.West <= box.East ? lng >= box.West && lng <= box.East : lng >= box.West || lng <= box.East;
        }

        #endregion

        #region Private-Methods

        private static double Radians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        #endregion
    }
}
