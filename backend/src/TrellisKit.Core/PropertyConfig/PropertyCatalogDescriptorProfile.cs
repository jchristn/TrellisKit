namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;
    /// <summary>Property-owned catalog vocabularies for one directory program.</summary>
    public sealed class PropertyCatalogDescriptorProfile
    {
        /// <summary>Unique configured directory program key.</summary>
        public string ProgramKey { get; set; } = String.Empty;
        /// <summary>Display label for attire, 1 to 80 characters. Defaults to Attire.</summary>
        public string AttireLabel { get; set; } = "Attire";
        /// <summary>Up to100 unique attire options. Defaults empty.</summary>
        public List<PropertyOptionSeed> Attires { get; set; } = new List<PropertyOptionSeed>();
        /// <summary>Display label for instructional content level, 1 to 80 characters. Defaults to Level.</summary>
        public string LevelLabel { get; set; } = "Level";
        /// <summary>Up to100 instructional level options, not personal credentials. Defaults empty.</summary>
        public List<PropertyOptionSeed> Levels { get; set; } = new List<PropertyOptionSeed>();
        /// <summary>Up to100 notation systems whose labels explain notation text. Defaults empty.</summary>
        public List<PropertyOptionSeed> NotationSystems { get; set; } = new List<PropertyOptionSeed>();
        /// <summary>Display label for family classification, 1 to 80 characters.</summary>
        public string FamilyLabel { get; set; } = "Family";
        /// <summary>Up to100 unique family options. Defaults empty.</summary>
        public List<PropertyOptionSeed> Families { get; set; } = new List<PropertyOptionSeed>();
        /// <summary>Up to20 language tag options; keys use language[-Script][-REGION]. Defaults empty.</summary>
        public List<PropertyOptionSeed> NameLanguages { get; set; } = new List<PropertyOptionSeed>();
    }
}
