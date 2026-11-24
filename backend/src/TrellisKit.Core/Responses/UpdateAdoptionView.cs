namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/update-groups/{id}/adoption.
    /// </summary>
    public class UpdateAdoptionView
    {
        /// <summary>
        /// Window in days.
        /// </summary>
        public int Days { get; set; } = 0;

        /// <summary>
        /// Per platform (ios, android).
        /// </summary>
        public Dictionary<string, UpdatePlatformAdoption> ByPlatform { get; set; } = new Dictionary<string, UpdatePlatformAdoption>(StringComparer.Ordinal);

        /// <summary>
        /// Per day (at most 90).
        /// </summary>
        public List<UpdateAdoptionDay> ByDay { get; set; } = new List<UpdateAdoptionDay>();

        /// <summary>
        /// Installs on the group by native app version (at most 10).
        /// </summary>
        public UpdateCountPreview ByBinaryVersion { get; set; } = new UpdateCountPreview();
    }
}
