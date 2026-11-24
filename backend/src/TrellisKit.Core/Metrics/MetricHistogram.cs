namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>Mergeable logarithmic latency sketch: upper bounds in milliseconds, relative resolution 10 percent.</summary>
    public static class MetricHistogram
    {
        /// <summary>Return the bin for a nonnegative duration. Values above one day share the final bin.</summary>
        public static int Bin(double value)
        {
            if (!Double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            return value <= 1 ? 0 : Math.Min(192, (int)Math.Ceiling(Math.Log(value) / Math.Log(1.1)));
        }
        /// <summary>Upper bound for a bin, in milliseconds.</summary>
        public static double Upper(int bin) { return Math.Pow(1.1, Math.Clamp(bin, 0, 192)); }
        /// <summary>Merge counts, preserving each sample's weight.</summary>
        public static void Merge(IDictionary<int,long> target, IReadOnlyDictionary<int,long> source)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(source);
            foreach (KeyValuePair<int,long> item in source) target[item.Key] = target.TryGetValue(item.Key, out long n) ? checked(n + item.Value) : item.Value;
        }
        /// <summary>Weighted percentile upper bound, or null for no samples. Percentile is in (0,1].</summary>
        public static double? Percentile(IReadOnlyDictionary<int,long> bins, double percentile)
        {
            if (!Double.IsFinite(percentile) || percentile <= 0 || percentile > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
            long total = bins.Values.Sum();
            if (total == 0) return null;
            long target = (long)Math.Ceiling(total * percentile), seen = 0;
            foreach (KeyValuePair<int,long> bin in bins.OrderBy(b => b.Key)) { seen += bin.Value; if (seen >= target) return Upper(bin.Key); }
            return null;
        }
    }
}
