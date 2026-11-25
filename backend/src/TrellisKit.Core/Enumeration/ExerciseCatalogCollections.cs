namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>Bounded training catalog collections.</summary>
    public static class ExerciseCatalogCollections
    {
        /// <summary>Administrator source registry, bounded to 100 results per page.</summary>
        public static readonly EnumerationCollection ImportSources = new EnumerationCollection("catalog-import-sources", TargetTypeEnum.CatalogImportSource, "s.id", "s.createdutc", true)
            .Order("NameAscending", "lower(s.name)", false, EnumerationKeyKindEnum.Text).Search("s.name", "s.url").Filters("enabled", "reviewed").DefaultTo("NameAscending");
        /// <summary>Published catalog entries.</summary>
        public static readonly EnumerationCollection Entries = EntryCollection("catalog-entries", false);
        /// <summary>Every workflow state, library staff only.</summary>
        public static readonly EnumerationCollection AdminEntries = EntryCollection("admin-catalog-entries", true).Filters("status");
        /// <summary>Directed relationships.</summary>
        public static readonly EnumerationCollection Relationships = new EnumerationCollection("catalog-relationships", TargetTypeEnum.CatalogRelationship, "r.id", "r.createdutc", false)
            .Order("KindAscending", "r.kind", false, EnumerationKeyKindEnum.Text).Search("r.note", "target.name").Filters("kind").DefaultTo("KindAscending");

        private static EnumerationCollection EntryCollection(string name, bool admin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.CatalogEntry, "e.id", "e.createdutc", admin)
                .Order("NameAscending", "lower(e.name)", false, EnumerationKeyKindEnum.Text)
                .Order("UpdatedDescending", "e.updatedutc", true, EnumerationKeyKindEnum.Time)
                .Search("normalize(e.name,NFC)", "normalize(e.summary,NFC)", "COALESCE((SELECT string_agg(n.text, ' ') FROM cataloglocalizednames n WHERE n.propertyid=e.propertyid AND n.entryid=e.id),'')", "normalize(COALESCE((SELECT string_agg(a.value, ' ') FROM catalogentryvalues a WHERE a.propertyid = e.propertyid AND a.entryid = e.id AND a.field = 'aliases'), ''),NFC)")
                .Filters("kind", "program", "difficulty", "muscle", "equipment", "familyKey", "nameLanguage", "attireKey", "levelKey", "notationSystemKey").DefaultTo("NameAscending");
        }
    }
}
