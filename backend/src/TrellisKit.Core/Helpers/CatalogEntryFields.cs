namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>Known ordered list fields of a catalog entry. Stateless.</summary>
    public static class CatalogEntryFields
    {
        /// <summary>Return references to every typed child list.</summary>
        /// <param name="entry">Entry.</param>
        /// <returns>Field-to-list mapping.</returns>
        public static Dictionary<string, List<string>> Lists(CatalogEntryData entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return new Dictionary<string,List<string>>(StringComparer.Ordinal)
            {
                { "aliases", entry.Aliases },
                { "primaryMuscles", entry.PrimaryMuscles },
                { "secondaryMuscles", entry.SecondaryMuscles },
                { "equipment", entry.Equipment },
                { "instructions", entry.Instructions },
                { "cues", entry.Cues },
                { "commonErrors", entry.CommonErrors },
                { "regressions", entry.Regressions },
                { "progressions", entry.Progressions },
                { "substitutions", entry.Substitutions },
                { "contraindications", entry.Contraindications },
                { "lessonIds", entry.LessonIds },
            };
        }
    }
}
