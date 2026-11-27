namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Enums;
    using TrellisKit.Server.Services.Email.Blocks;

    /// <summary>
    /// The details sections of a staff email (docs/EMAIL.md, "Staff email details"): request details, member details, and the
    /// template's own sections, stored as JSON in the <see cref="Token"/> token so a queued message renders the same on
    /// every delivery attempt. Only <see cref="EmailCategoryEnum.Staff"/> templates render it; the queue drops the token
    /// from every other template. Values are literal text: the layout HTML-encodes them and braces are neutralized so a
    /// value never fills a placeholder. Thread safety: not thread safe.
    /// </summary>
    public class StaffEmailDetails
    {
        #region Public-Members

        /// <summary>
        /// Token that carries the serialized sections.
        /// </summary>
        public const string Token = "details";

        /// <summary>
        /// Maximum sections rendered (8).
        /// </summary>
        public const int MaxSections = 8;

        /// <summary>
        /// Maximum rows rendered per section (20).
        /// </summary>
        public const int MaxRows = 20;

        /// <summary>
        /// Maximum characters of a label (120).
        /// </summary>
        public const int MaxLabel = 120;

        /// <summary>
        /// Maximum characters of a value (400).
        /// </summary>
        public const int MaxValue = 400;

        /// <summary>
        /// Sections in order; never null.
        /// </summary>
        public IReadOnlyList<StaffEmailSection> Sections
        {
            get { return _Sections; }
        }

        #endregion

        #region Private-Members

        private readonly List<StaffEmailSection> _Sections = new List<StaffEmailSection>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a section when it has a title and at least one row. Null is ignored.
        /// </summary>
        /// <param name="section">Section, or null.</param>
        /// <returns>This instance.</returns>
        public StaffEmailDetails Add(StaffEmailSection? section)
        {
            if (section != null && !String.IsNullOrWhiteSpace(section.Title) && section.Rows.Count > 0 && _Sections.Count < MaxSections) _Sections.Add(section);
            return this;
        }

        /// <summary>
        /// Serialize for the <see cref="Token"/> token. Empty when there are no sections.
        /// </summary>
        /// <returns>JSON array of { title, rows: [ { label, value } ] }, or empty.</returns>
        public string Serialize()
        {
            if (_Sections.Count == 0) return String.Empty;
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            foreach (StaffEmailSection section in _Sections)
            {
                List<Dictionary<string, string>> rows = section.Rows.Take(MaxRows).Select(r => new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "label", Clip(r.Label, MaxLabel) },
                    { "value", Clip(r.Value, MaxValue) }
                }).ToList();
                list.Add(new Dictionary<string, object>(StringComparer.Ordinal) { { "title", Clip(section.Title, MaxLabel) }, { "rows", rows } });
            }
            return JsonSerializer.Serialize(list);
        }

        /// <summary>
        /// Parse a serialized value. Malformed input yields no sections; limits are enforced.
        /// </summary>
        /// <param name="json">Serialized sections, or null.</param>
        /// <returns>Sections; never null.</returns>
        public static List<StaffEmailSection> Parse(string? json)
        {
            List<StaffEmailSection> sections = new List<StaffEmailSection>();
            if (String.IsNullOrWhiteSpace(json)) return sections;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Array) return sections;
                    foreach (JsonElement item in document.RootElement.EnumerateArray())
                    {
                        if (sections.Count >= MaxSections) break;
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        StaffEmailSection section = new StaffEmailSection(Text(item, "title", MaxLabel));
                        if (item.TryGetProperty("rows", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement row in rows.EnumerateArray())
                            {
                                if (section.Rows.Count >= MaxRows) break;
                                if (row.ValueKind != JsonValueKind.Object) continue;
                                section.Row(Text(row, "label", MaxLabel), Text(row, "value", MaxValue));
                            }
                        }
                        if (section.Title.Length > 0 && section.Rows.Count > 0) sections.Add(section);
                    }
                }
            }
            catch (JsonException)
            {
                sections.Clear();
            }
            return sections;
        }

        /// <summary>
        /// Render the <see cref="Token"/> sections as a subheading and a table each. Renders nothing unless the template
        /// is in the <see cref="EmailCategoryEnum.Staff"/> category.
        /// </summary>
        /// <param name="email">Composer.</param>
        /// <exception cref="ArgumentNullException">email is null.</exception>
        public static void Render(EmailComposer email)
        {
            ArgumentNullException.ThrowIfNull(email);
            if (email.Template.Category != EmailCategoryEnum.Staff) return;
            foreach (StaffEmailSection section in Parse(email.Token(Token)))
            {
                email.Subheading(Literal(section.Title));
                email.Fields(section.Rows.Select(r => new EmailField(Literal(r.Label), Literal(r.Value))).ToArray());
            }
        }

        /// <summary>
        /// Neutralize {token} placeholders in literal text (composer blocks fill placeholders).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Text with braces replaced by parentheses.</returns>
        public static string Literal(string? text)
        {
            return (text ?? String.Empty).Replace("{", "(", StringComparison.Ordinal).Replace("}", ")", StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static string Text(JsonElement element, string name, int max)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return String.Empty;
            return Clip(value.GetString(), max);
        }

        private static string Clip(string? text, int max)
        {
            string value = (text ?? String.Empty).Trim();
            return value.Length > max ? value.Substring(0, max) : value;
        }

        #endregion
    }
}
