namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Structured training catalog CatalogEntryData contract. Values are validated by the catalog service.</summary>
    public class CatalogEntryData
    {
        /// <summary>Exercise or Technique.</summary>
        public string Kind { get; set; } = "Exercise";

        /// <summary>Typed program descriptors. Request omission or null preserves existing values on update; reads return an empty object for legacy entries.</summary>
        public CatalogModalityDetails? ModalityDetails { get; set; } = null;

        /// <summary>Optional apparatus context. Omission/null preserves on update; explicit object replaces; reads return an empty group for legacy entries.</summary>
        public CatalogApparatusSetup? ApparatusSetup { get; set; } = null;

        /// <summary>Name, 2 to 160 characters.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>URL slug, unique within the property; 3 to 80 lowercase letters/digits with single separating hyphens, not reserved.</summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>Summary, at most 2000 characters.</summary>
        public string? Summary { get; set; } = null;

        /// <summary>Directory program key.</summary>
        public string? Program { get; set; } = null;

        /// <summary>Beginner, Intermediate, Advanced, or AllLevels.</summary>
        public string Difficulty { get; set; } = "AllLevels";

        /// <summary>Optional descriptive value, at most 100 characters.</summary>
        public string? MovementPattern { get; set; } = null;

        /// <summary>Optional descriptive value, at most 100 characters.</summary>
        public string? Plane { get; set; } = null;

        /// <summary>Optional descriptive value, at most 100 characters.</summary>
        public string? Laterality { get; set; } = null;

        /// <summary>Optional descriptive value, at most 100 characters.</summary>
        public string? TechniqueType { get; set; } = null;

        /// <summary>Optional descriptive value, at most 100 characters.</summary>
        public string? FromPosition { get; set; } = null;

        /// <summary>Optional descriptive value, at most 100 characters.</summary>
        public string? ToPosition { get; set; } = null;

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> PrimaryMuscles { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> SecondaryMuscles { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> Equipment { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> Instructions { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> Cues { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> CommonErrors { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> Regressions { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> Progressions { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> Substitutions { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> Contraindications { get; set; } = new List<string>();

        /// <summary>Ordered values; at most thirty except instructions fifty and lesson references twenty.</summary>
        public List<string> LessonIds { get; set; } = new List<string>();

        /// <summary>At most twenty licensed source references.</summary>
        public List<CatalogSource> Sources { get; set; } = new List<CatalogSource>();

        /// <summary>Metadata labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Metadata tags.</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();

    }
}
