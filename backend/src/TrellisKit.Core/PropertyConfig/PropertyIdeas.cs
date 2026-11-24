namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Optional ideas section of property.json (docs/PHASE5_CONTRACT.md section 2.4): the idea statuses seeded when the
    /// property has none. When absent the engine defaults apply (<see cref="Defaults"/>).
    /// </summary>
    public class PropertyIdeas
    {
        #region Public-Members

        /// <summary>
        /// Statuses in display order, or null for the engine defaults.
        /// </summary>
        public List<PropertyIdeaStatus>? Statuses { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The engine default statuses: UnderReview (initial), Planned, InProgress, Shipped (closed), Declined (closed).
        /// </summary>
        /// <returns>New list.</returns>
        public static List<PropertyIdeaStatus> Defaults()
        {
            return new List<PropertyIdeaStatus>
            {
                new PropertyIdeaStatus { Key = "UnderReview", Label = "Under review", Color = "#607D8B", IsInitial = true },
                new PropertyIdeaStatus { Key = "Planned", Label = "Planned", Color = "#1565C0" },
                new PropertyIdeaStatus { Key = "InProgress", Label = "In progress", Color = "#EF6C00" },
                new PropertyIdeaStatus { Key = "Shipped", Label = "Shipped", Color = "#2E7D32", IsClosed = true },
                new PropertyIdeaStatus { Key = "Declined", Label = "Declined", Color = "#9E9E9E", IsClosed = true }
            };
        }

        #endregion
    }
}
