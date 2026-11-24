namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A bounded preview of counts (at most 10 objects) with the total distinct values.
    /// </summary>
    public class UpdateCountPreview
    {
        /// <summary>
        /// At most 10 rows, highest first.
        /// </summary>
        public List<UpdateCountRow> Objects { get; set; } = new List<UpdateCountRow>();

        /// <summary>
        /// Distinct values.
        /// </summary>
        public long TotalRecords { get; set; } = 0;
    }
}
