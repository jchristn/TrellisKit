namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Helpers;

    /// <summary>
    /// Validates a <see cref="PropertyConfiguration"/>: schema version, required fields, unique keys, color format,
    /// positive price, and ranges. Each problem names the JSON path of the offending field.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class PropertyValidator
    {
        #region Public-Members

        /// <summary>
        /// The property.json schema version this engine understands.
        /// </summary>
        public const int SupportedSchemaVersion = 1;

        /// <summary>
        /// Most documents a property may declare (property.json documents).
        /// </summary>
        public const int MaxDocuments = 30;

        #endregion

        #region Private-Members

        private static readonly Regex _Key = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _ReactionKey = new Regex("^[a-z][a-z0-9]{1,19}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Color = new Regex("^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Domain = new Regex("^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*(:[0-9]{1,5})?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Email = new Regex("^[^@\\s]+@[^@\\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _PathWord = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Slug = new Regex("^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly string[] _Intervals = new[] { "Year", "Month" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a property.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <returns>Problems, empty when valid. Never null.</returns>
        /// <exception cref="ArgumentNullException">The property is null.</exception>
        public static List<string> Validate(PropertyConfiguration property)
        {
            ArgumentNullException.ThrowIfNull(property);
            List<string> problems = new List<string>();

            if (property.SchemaVersion != SupportedSchemaVersion)
                problems.Add("schemaVersion must be " + SupportedSchemaVersion + " (found " + property.SchemaVersion + ")");
            if (!PropertyIds.IsValid(property.Id))
                problems.Add("id is required and must be a generated property id of the form app_{k-sortable}_{random} (for example app_mwcnf2ia_Yk3hhcUgbET5kqGV)");
            if (property.Slug != null && !PropertyIds.IsValidSlug(property.Slug))
                problems.Add("slug must be lowercase letters, digits, and hyphens (1 to 63 characters) and not a reserved name");
            Required(property.Name, "name", problems);
            Required(property.ShortName, "shortName", problems);

            if (property.Domains == null) problems.Add("domains is required");
            else
            {
                Domain(property.Domains.Web, "domains.web", true, problems);
                Domain(property.Domains.Api, "domains.api", true, problems);
                Domain(property.Domains.Media, "domains.media", false, problems);
                List<string> redirects = property.Domains.Redirects ?? new List<string>();
                for (int i = 0; i < redirects.Count; i++) Domain(redirects[i], "domains.redirects[" + i + "]", true, problems);
            }

            if (property.Contact == null) problems.Add("contact is required");
            else
            {
                EmailAddress(property.Contact.SupportEmail, "contact.supportEmail", problems);
                Required(property.Contact.EmailFromName, "contact.emailFromName", problems);
                EmailAddress(property.Contact.EmailFromAddress, "contact.emailFromAddress", problems);
            }

            ValidateBrand(property.Brand, problems);
            ValidateMembership(property.Membership, property.Access?.Model != Enums.AccessModelEnum.Open, problems);
            ValidateAccess(property.Access, problems);
            ValidateProfile(property.Profile, problems);
            ValidateProfileFields(property.ProfileFields, problems);
            ValidateReactions(property.Reactions, problems);
            ValidateDirectory(property.DirectorySection, problems);
            ValidateEvents(property.EventsSection, problems);
            ValidateLibrary(property.LibrarySection, problems);
            ValidatePublicPages(property.PublicPages, problems);
            ValidateIdeas(property.Ideas, problems);
            ValidateSupport(property.Support, problems);
            ValidateDocuments(property.Documents, problems);

            if (property.Features == null) problems.Add("features is required");
            if (property.Terminology == null) problems.Add("terminology is required");
            return problems;
        }

        /// <summary>
        /// Whether a value is a hex color such as #1F5FBF (3, 6, or 8 digits).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsColor(string? value)
        {
            return !String.IsNullOrEmpty(value) && _Color.IsMatch(value);
        }

        #endregion

        #region Private-Methods

        private static void ValidateBrand(PropertyBrand? brand, List<string> problems)
        {
            if (brand == null)
            {
                problems.Add("brand is required");
                return;
            }
            BrandPath(brand.Logo, "brand.logo", true, problems);
            BrandPath(brand.Icon, "brand.icon", true, problems);
            BrandPath(brand.IconIco, "brand.iconIco", false, problems);
            if (brand.Colors == null)
            {
                problems.Add("brand.colors is required");
                return;
            }
            Scheme(brand.Colors.Light, "brand.colors.light", problems);
            Scheme(brand.Colors.Dark, "brand.colors.dark", problems);
        }

        private static void Scheme(PropertyColorScheme? scheme, string path, List<string> problems)
        {
            if (scheme == null)
            {
                problems.Add(path + " is required");
                return;
            }
            Color(scheme.Primary, path + ".primary", problems);
            Color(scheme.PrimaryHover, path + ".primaryHover", problems);
            Color(scheme.OnPrimary, path + ".onPrimary", problems);
            Color(scheme.Accent, path + ".accent", problems);
            Color(scheme.OnAccent, path + ".onAccent", problems);
            Color(scheme.AccentSoft, path + ".accentSoft", problems);
            if (!String.IsNullOrEmpty(scheme.OnAccentSoft)) Color(scheme.OnAccentSoft, path + ".onAccentSoft", problems);
            Color(scheme.ThemeColor, path + ".themeColor", problems);
        }

        private static void ValidateMembership(PropertyMembership? membership, bool paid, List<string> problems)
        {
            if (membership == null)
            {
                problems.Add("membership is required");
                return;
            }
            if (paid)
            {
                // The Open access model sells nothing, so only the minimum age and trial bounds apply there.
                Required(membership.ProductName, "membership.productName", problems);
                if (membership.PriceUsdCents <= 0) problems.Add("membership.priceUsdCents must be a positive number of cents");
                if (!_Intervals.Contains(membership.Interval ?? String.Empty, StringComparer.Ordinal)) problems.Add("membership.interval must be Year or Month");
                Required(membership.StoreProductId, "membership.storeProductId", problems);
            }
            if (membership.MinimumAge < 13 || membership.MinimumAge > 120) problems.Add("membership.minimumAge must be 13 to 120");
            if (membership.TrialDays < 0 || membership.TrialDays > 365) problems.Add("membership.trialDays must be 0 to 365");
        }

        private static void ValidateAccess(PropertyAccess? access, List<string> problems)
        {
            if (access == null) return;
            foreach (string problem in TrellisKit.Core.Models.AccessPolicy.Validate(access.Model, access.GatedFeatures, access.DefaultAudience).Values) problems.Add("access." + problem);
        }

        private static void ValidateProfileFields(List<TrellisKit.Core.Models.ProfileField>? fields, List<string> problems)
        {
            if (fields == null) return;
            if (fields.Count > TrellisKit.Core.Helpers.ProfileFieldRules.HardMaxFields) problems.Add("profileFields lists more than " + TrellisKit.Core.Helpers.ProfileFieldRules.HardMaxFields + " fields");
            List<TrellisKit.Core.Models.ProfileField> custom = fields.Where(f => f != null && !TrellisKit.Core.Helpers.ProfileFieldRules.BuiltInKeys.Contains(f.Key, StringComparer.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i] == null)
                {
                    problems.Add("profileFields[" + i + "] is required");
                    continue;
                }
                if (!custom.Contains(fields[i])) continue;
                foreach (KeyValuePair<string, string> error in TrellisKit.Core.Helpers.ProfileFieldRules.Validate(fields[i], custom, "profileFields[" + i + "]."))
                    problems.Add(error.Key + ": " + error.Value);
            }
            if (custom.Count(f => f.Enabled && f.ShowOnCard) > 5) problems.Add("profileFields: at most 5 enabled fields may set showOnCard");
        }

        private static void ValidateProfile(PropertyProfile? profile, List<string> problems)
        {
            if (profile == null)
            {
                problems.Add("profile is required");
                return;
            }

            if (profile.Affiliation == null) problems.Add("profile.affiliation is required");
            else if (profile.Affiliation.Enabled) Required(profile.Affiliation.Label, "profile.affiliation.label", problems);

            if (profile.Rank == null) problems.Add("profile.rank is required");
            else if (profile.Rank.Enabled)
            {
                Required(profile.Rank.Label, "profile.rank.label", problems);
                if (profile.Rank.MaxLevel < 0 || profile.Rank.MaxLevel > 100) problems.Add("profile.rank.maxLevel must be 0 to 100");
                if (profile.Rank.MaxLevel > 0) Required(profile.Rank.LevelLabel, "profile.rank.levelLabel", problems);
                List<PropertyRankValue> values = profile.Rank.Values ?? new List<PropertyRankValue>();
                if (values.Count == 0) problems.Add("profile.rank.values must list at least one rank when profile.rank is enabled");
                HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < values.Count; i++)
                {
                    string path = "profile.rank.values[" + i + "]";
                    PropertyRankValue value = values[i];
                    if (value == null)
                    {
                        problems.Add(path + " is required");
                        continue;
                    }
                    Key(value.Key, path + ".key", keys, problems);
                    Required(value.Label, path + ".label", problems);
                    Color(value.Color, path + ".color", problems);
                }
            }

            if (profile.RankHistory == null) problems.Add("profile.rankHistory is required");
            else if (profile.RankHistory.Enabled)
            {
                Required(profile.RankHistory.Label, "profile.rankHistory.label", problems);
                if (profile.RankHistory.MaxEntries < 1 || profile.RankHistory.MaxEntries > 100) problems.Add("profile.rankHistory.maxEntries must be 1 to 100");
                if (profile.Rank != null && !profile.Rank.Enabled) problems.Add("profile.rankHistory cannot be enabled while profile.rank is disabled");
            }

            if (profile.Goals == null) problems.Add("profile.goals is required");
            else if (profile.Goals.Enabled)
            {
                Required(profile.Goals.Label, "profile.goals.label", problems);
                List<PropertyGoalValue> values = profile.Goals.Values ?? new List<PropertyGoalValue>();
                if (values.Count == 0) problems.Add("profile.goals.values must list at least one goal when profile.goals is enabled");
                HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < values.Count; i++)
                {
                    string path = "profile.goals.values[" + i + "]";
                    PropertyGoalValue value = values[i];
                    if (value == null)
                    {
                        problems.Add(path + " is required");
                        continue;
                    }
                    Key(value.Key, path + ".key", keys, problems);
                    Required(value.Label, path + ".label", problems);
                }
            }
        }

        private static void ValidateReactions(List<PropertyReaction>? reactions, List<string> problems)
        {
            if (reactions == null)
            {
                problems.Add("reactions is required");
                return;
            }
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reactions.Count; i++)
            {
                string path = "reactions[" + i + "]";
                PropertyReaction reaction = reactions[i];
                if (reaction == null)
                {
                    problems.Add(path + " is required");
                    continue;
                }
                Key(reaction.Key, path + ".key", keys, problems);
                if (!String.IsNullOrEmpty(reaction.Key) && !_ReactionKey.IsMatch(reaction.Key)) problems.Add(path + ".key must be 2 to 20 lowercase letters and digits starting with a letter (found \"" + reaction.Key + "\")");
                Required(reaction.Emoji, path + ".emoji", problems);
                Required(reaction.Label, path + ".label", problems);
            }
        }

        private static void ValidateDirectory(PropertyDirectorySection? directory, List<string> problems)
        {
            if (directory == null) return;
            Label(directory.VenueLabel, "directory.venueLabel", 40, problems);
            Label(directory.VenueLabelPlural, "directory.venueLabelPlural", 40, problems);
            Label(directory.InstructorLabel, "directory.instructorLabel", 40, problems);
            Label(directory.InstructorLabelPlural, "directory.instructorLabelPlural", 40, problems);
            if (directory.InstructorRankFieldKey != null && !_Key.IsMatch(directory.InstructorRankFieldKey))
                problems.Add("directory.instructorRankFieldKey must be 1 to 64 letters, digits, underscores, or hyphens");
            Options(directory.Programs, "directory.programs", 50, false, problems);
        }

        private static void ValidateEvents(PropertyEventsSection? events, List<string> problems)
        {
            if (events == null) return;
            Options(events.Categories, "events.categories", 50, true, problems);
        }

        private static void ValidatePublicPages(PropertyPublicPages? pages, List<string> problems)
        {
            if (pages == null) return;
            List<string> seen = new List<string>();
            void Path(string value, string path)
            {
                if (value.Length < 2 || value.Length > 40 || !_PathWord.IsMatch(value)) problems.Add(path + " must be 2 to 40 lowercase letters and digits with single hyphens (found \"" + value + "\")");
                else if (PropertyPublicPages.ReservedPaths.Contains(value, StringComparer.Ordinal)) problems.Add(path + " must not be a reserved path (" + value + ")");
                else if (seen.Contains(value, StringComparer.Ordinal)) problems.Add(path + " must differ from the other public page paths (" + value + ")");
                seen.Add(value);
            }
            Path(pages.VenuesPath, "publicPages.venuesPath");
            Path(pages.EventsPath, "publicPages.eventsPath");
            Path(pages.ProfilesPath, "publicPages.profilesPath");
        }

        private static void ValidateLibrary(PropertyLibrarySection? library, List<string> problems)
        {
            if (library == null) return;
            string source = library.LevelSource ?? String.Empty;
            if (source != "None" && source != "ProfileField" && source != "Custom")
                problems.Add("library.levelSource must be None, ProfileField, or Custom (found \"" + source + "\")");
            if (source == "ProfileField" && (String.IsNullOrWhiteSpace(library.LevelFieldKey) || !_Key.IsMatch(library.LevelFieldKey)))
                problems.Add("library.levelFieldKey is required when library.levelSource is ProfileField");
            Options(library.Levels, "library.levels", 50, true, problems);
        }

        private static void Label(string? value, string path, int max, List<string> problems)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Trim().Length > max) problems.Add(path + " is required and must be 1 to " + max + " characters");
        }

        private static void Options(List<PropertyOptionSeed>? options, string path, int max, bool allowColor, List<string> problems)
        {
            if (options == null) return;
            if (options.Count > max) problems.Add(path + " may hold at most " + max + " options (found " + options.Count + ")");
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < options.Count; i++)
            {
                string item = path + "[" + i + "]";
                PropertyOptionSeed option = options[i];
                if (option == null)
                {
                    problems.Add(item + " is required");
                    continue;
                }
                Key(option.Key, item + ".key", keys, problems);
                Label(option.Label, item + ".label", 80, problems);
                if (option.Color != null)
                {
                    if (!allowColor) problems.Add(item + ".color is not supported here");
                    else Color(option.Color, item + ".color", problems);
                }
            }
        }


        private static void ValidateIdeas(PropertyIdeas? ideas, List<string> problems)
        {
            if (ideas == null || ideas.Statuses == null) return;
            if (ideas.Statuses.Count == 0)
            {
                problems.Add("ideas.statuses must list at least one status");
                return;
            }
            if (ideas.Statuses.Count > 50) problems.Add("ideas.statuses may list at most 50 statuses");
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int initial = 0;
            for (int i = 0; i < ideas.Statuses.Count; i++)
            {
                string path = "ideas.statuses[" + i + "]";
                PropertyIdeaStatus status = ideas.Statuses[i];
                if (status == null)
                {
                    problems.Add(path + " is required");
                    continue;
                }
                Key(status.Key, path + ".key", keys, problems);
                Required(status.Label, path + ".label", problems);
                if (status.Label != null && status.Label.Trim().Length > 60) problems.Add(path + ".label must be at most 60 characters");
                if (status.Color != null) Color(status.Color, path + ".color", problems);
                if (status.IsInitial)
                {
                    initial++;
                    if (status.IsClosed) problems.Add(path + " cannot be both initial and closed");
                }
            }
            if (initial != 1) problems.Add("ideas.statuses must mark exactly one status isInitial (found " + initial + ")");
        }

        private static void ValidateSupport(PropertySupport? support, List<string> problems)
        {
            if (support?.TicketTypes == null) return;
            if (support.TicketTypes.Count > 50) problems.Add("support.ticketTypes has more than 50 entries");
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < support.TicketTypes.Count; i++)
            {
                string path = "support.ticketTypes[" + i + "]";
                PropertyTicketTypeSeed type = support.TicketTypes[i];
                if (type == null)
                {
                    problems.Add(path + " is required");
                    continue;
                }
                Key(type.Key, path + ".key", keys, problems);
                Required(type.Label, path + ".label", problems);
                List<string> recipients = type.StaffRecipients ?? new List<string>();
                if (recipients.Count > 10) problems.Add(path + ".staffRecipients has more than 10 addresses");
                for (int r = 0; r < recipients.Count; r++) EmailAddress(recipients[r], path + ".staffRecipients[" + r + "]", problems);
            }
        }

        private static void ValidateDocuments(List<PropertyDocument>? documents, List<string> problems)
        {
            if (documents == null) return;
            if (documents.Count > MaxDocuments) problems.Add("documents has more than " + MaxDocuments + " entries");
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> slugs = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < documents.Count; i++)
            {
                string path = "documents[" + i + "]";
                PropertyDocument document = documents[i];
                if (document == null)
                {
                    problems.Add(path + " is required");
                    continue;
                }
                Key(document.Key, path + ".key", keys, problems);
                if (String.Equals(document.Key, "acceptances", StringComparison.OrdinalIgnoreCase)) problems.Add(path + ".key acceptances is reserved");
                Required(document.Title, path + ".title", problems);
                if (document.Title != null && document.Title.Trim().Length > 120) problems.Add(path + ".title must be at most 120 characters");
                if (!PropertyLoader.IsSafeDocumentPath(document.File)) problems.Add(path + ".file must be a relative path to a .md file inside the property folder (found \"" + (document.File ?? String.Empty) + "\")");
                Required(document.Version, path + ".version", problems);
                if (document.Version != null && document.Version.Trim().Length > 32) problems.Add(path + ".version must be at most 32 characters");
                if (String.IsNullOrEmpty(document.Slug) || !_Slug.IsMatch(document.Slug)) problems.Add(path + ".slug is required and must be lowercase letters, digits, and hyphens (1 to 64 characters)");
                else if (!slugs.Add(document.Slug)) problems.Add(path + ".slug duplicates slug " + document.Slug);
                if (document.Description != null && document.Description.Trim().Length > 300) problems.Add(path + ".description must be at most 300 characters");
                if (document.SeparateCheckbox && document.Acceptance != Enums.DocumentAcceptanceModeEnum.RequiredAtSignup) problems.Add(path + ".separateCheckbox applies only to acceptance RequiredAtSignup");
                if (document.ReacceptOnChange && !document.IsGating) problems.Add(path + ".reacceptOnChange applies only to acceptance RequiredAtSignup or AcceptedWithTerms");
            }
        }

        private static void Required(string? value, string path, List<string> problems)
        {
            if (String.IsNullOrWhiteSpace(value)) problems.Add(path + " is required");
        }

        private static void Key(string? value, string path, HashSet<string> seen, List<string> problems)
        {
            if (String.IsNullOrWhiteSpace(value) || !_Key.IsMatch(value))
            {
                problems.Add(path + " is required and must be 1 to 64 letters, digits, underscores, or hyphens");
                return;
            }
            if (!seen.Add(value)) problems.Add(path + " duplicates key " + value);
        }

        private static void Color(string? value, string path, List<string> problems)
        {
            if (!IsColor(value)) problems.Add(path + " must be a hex color such as #1F5FBF (found \"" + (value ?? String.Empty) + "\")");
        }

        private static void Domain(string? value, string path, bool required, List<string> problems)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                if (required) problems.Add(path + " is required");
                return;
            }
            if (!_Domain.IsMatch(value)) problems.Add(path + " must be a host name without scheme or path (found \"" + value + "\")");
        }

        private static void EmailAddress(string? value, string path, List<string> problems)
        {
            if (String.IsNullOrWhiteSpace(value) || !_Email.IsMatch(value.Trim())) problems.Add(path + " must be an email address");
        }

        private static void BrandPath(string? value, string path, bool required, List<string> problems)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                if (required) problems.Add(path + " is required");
                return;
            }
            string normalized = value.Replace('\\', '/');
            string file = normalized.StartsWith(PropertyLoader.BrandFolder + "/", StringComparison.Ordinal) ? normalized.Substring(PropertyLoader.BrandFolder.Length + 1) : String.Empty;
            if (!PropertyLoader.IsSafeAssetName(file)) problems.Add(path + " must be a file directly under " + PropertyLoader.BrandFolder + "/ (found \"" + value + "\")");
        }

        #endregion
    }
}
