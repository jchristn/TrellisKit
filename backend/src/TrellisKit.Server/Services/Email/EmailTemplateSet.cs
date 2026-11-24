namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The templates one property sends: the engine catalog and registered templates (<see cref="EmailTemplateCatalog"/>)
    /// with the property's JSON templates layered on top (a property template replaces the catalog template with the same
    /// key; new keys are added after the catalog).
    /// Thread safety: immutable property layer; catalog reads are snapshots.
    /// </summary>
    public sealed class EmailTemplateSet
    {
        #region Public-Members

        /// <summary>
        /// The property's templates.
        /// </summary>
        public IReadOnlyList<PropertyEmailTemplate> PropertyTemplates { get; }

        /// <summary>
        /// Problems found while loading the property's template files.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>
        /// Every template the property sends.
        /// </summary>
        public IReadOnlyList<EmailTemplate> All
        {
            get
            {
                Dictionary<string, PropertyEmailTemplate> layer = PropertyTemplates.ToDictionary(t => t.Key, StringComparer.Ordinal);
                List<EmailTemplate> all = EmailTemplateCatalog.All.Select(t => layer.TryGetValue(t.Key, out PropertyEmailTemplate? own) ? (EmailTemplate)own : t).ToList();
                HashSet<string> keys = new HashSet<string>(all.Select(t => t.Key), StringComparer.Ordinal);
                all.AddRange(PropertyTemplates.Where(t => !keys.Contains(t.Key)));
                return all;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="propertyTemplates">Property templates (null for none).</param>
        /// <param name="problems">Load problems (null for none).</param>
        public EmailTemplateSet(IEnumerable<PropertyEmailTemplate>? propertyTemplates = null, IEnumerable<string>? problems = null)
        {
            PropertyTemplates = (propertyTemplates ?? Enumerable.Empty<PropertyEmailTemplate>()).Where(t => t != null).GroupBy(t => t.Key, StringComparer.Ordinal).Select(g => g.Last()).ToList();
            Problems = (problems ?? Enumerable.Empty<string>()).ToList();
        }

        /// <summary>
        /// Load a property's templates from its directory (see <see cref="PropertyEmailTemplateLoader"/>).
        /// </summary>
        /// <param name="propertyDirectory">Property directory, or null.</param>
        /// <returns>Template set.</returns>
        public static EmailTemplateSet Load(string? propertyDirectory)
        {
            PropertyEmailTemplateLoadResult result = PropertyEmailTemplateLoader.Load(propertyDirectory, EmailTemplateCatalog.All);
            return new EmailTemplateSet(result.Templates, result.Problems);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a template by key.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Template, or null.</returns>
        public EmailTemplate? Find(string? key)
        {
            if (String.IsNullOrWhiteSpace(key)) return null;
            string k = key.Trim();
            PropertyEmailTemplate? own = PropertyTemplates.FirstOrDefault(t => String.Equals(t.Key, k, StringComparison.Ordinal));
            return own ?? EmailTemplateCatalog.Find(k);
        }

        /// <summary>
        /// Find a template by key, throwing when it does not exist.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Template.</returns>
        /// <exception cref="ArgumentException">Unknown key.</exception>
        public EmailTemplate Get(string key)
        {
            return Find(key) ?? throw new ArgumentException("Unknown email template " + key + ".", nameof(key));
        }

        #endregion
    }
}
