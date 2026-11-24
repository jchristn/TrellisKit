namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed filters of the update group enumeration.
    /// </summary>
    public class UpdateGroupFilter
    {
        /// <summary>
        /// ios or android, or null.
        /// </summary>
        public string? Platform { get; set; } = null;

        /// <summary>
        /// Channel, or null.
        /// </summary>
        public string? Channel { get; set; } = null;

        /// <summary>
        /// Runtime version, or null.
        /// </summary>
        public string? RuntimeVersion { get; set; } = null;

        /// <summary>
        /// Status, or null.
        /// </summary>
        public UpdateGroupStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Kind, or null.
        /// </summary>
        public UpdateGroupKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Critical flag, or null.
        /// </summary>
        public bool? Critical { get; set; } = null;

        /// <summary>
        /// Git commit prefix, or null.
        /// </summary>
        public string? GitCommit { get; set; } = null;
    }
}
