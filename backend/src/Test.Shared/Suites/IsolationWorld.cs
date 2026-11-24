namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The data PropertyIsolationSuite creates in property A (the sample) and property B (the second fixture).
    /// </summary>
    internal sealed class IsolationWorld
    {
        internal string A { get; set; } = String.Empty;

        internal string B { get; set; } = String.Empty;

        internal string BMarker { get; set; } = String.Empty;

        internal string AMember { get; set; } = String.Empty;

        internal string APoster { get; set; } = String.Empty;

        internal string BMemberId { get; set; } = String.Empty;

        internal string BMedia { get; set; } = String.Empty;

        internal string BPost { get; set; } = String.Empty;

        internal string BCode { get; set; } = String.Empty;

        internal string BBroadcast { get; set; } = String.Empty;

        internal string BSegment { get; set; } = String.Empty;

        internal List<string> BIds { get; } = new List<string>();

        internal List<string> ForeignIds()
        {
            return BIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
