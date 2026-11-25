namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of the curriculum enumerations.
    /// </summary>
    public class CurriculumFilter
    {
        #region Public-Members

        /// <summary>
        /// Statuses to include (empty for every status).
        /// </summary>
        public List<CurriculumStatusEnum> Statuses { get; set; } = new List<CurriculumStatusEnum>();

        /// <summary>
        /// Visibilities to include (empty for every visibility).
        /// </summary>
        public List<CurriculumVisibilityEnum> Visibilities { get; set; } = new List<CurriculumVisibilityEnum>();

        /// <summary>
        /// Rank field key.
        /// </summary>
        public string? FieldKey { get; set; } = null;

        #endregion
    }
}
