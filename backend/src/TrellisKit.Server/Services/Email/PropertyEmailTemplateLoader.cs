namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Loads a property's email templates from JSON files in its email/templates folder (docs/TRELLISKIT.md section
    /// 12.2): one <see cref="PropertyEmailTemplateDocument"/> per file named key.json. A file with a problem is skipped
    /// and reported; it never stops the server.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class PropertyEmailTemplateLoader
    {
        #region Public-Members

        /// <summary>
        /// Folder under the property directory.
        /// </summary>
        public const string Folder = "email/templates";

        /// <summary>
        /// Most template files read.
        /// </summary>
        public const int MaxFiles = 200;

        /// <summary>
        /// Largest template file in bytes.
        /// </summary>
        public const int MaxFileBytes = 65536;

        /// <summary>
        /// Most blocks in one template.
        /// </summary>
        public const int MaxBlocks = 50;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly HashSet<string> _Types = new HashSet<string>(StringComparer.Ordinal)
        {
            "body", "button", "paragraph", "subheading", "callout", "notice", "list", "fields", "code", "markdown", "divider", "details"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the templates of a property directory.
        /// </summary>
        /// <param name="propertyDirectory">Property directory (null or missing folder loads none).</param>
        /// <param name="existing">Engine and registered templates (replacement targets).</param>
        /// <returns>Templates and problems.</returns>
        /// <exception cref="ArgumentNullException">existing is null.</exception>
        public static PropertyEmailTemplateLoadResult Load(string? propertyDirectory, IReadOnlyList<EmailTemplate> existing)
        {
            ArgumentNullException.ThrowIfNull(existing);
            PropertyEmailTemplateLoadResult result = new PropertyEmailTemplateLoadResult();
            if (String.IsNullOrWhiteSpace(propertyDirectory)) return result;
            string folder = Path.Combine(propertyDirectory, "email", "templates");
            if (!Directory.Exists(folder)) return result;
            List<string> files = Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count > MaxFiles) result.Problems.Add(Folder + ": more than " + MaxFiles + " files; only the first " + MaxFiles + " are read.");
            foreach (string file in files.Take(MaxFiles))
            {
                string name = Folder + "/" + Path.GetFileName(file);
                try
                {
                    if (new FileInfo(file).Length > MaxFileBytes)
                    {
                        result.Problems.Add(name + ": larger than " + MaxFileBytes + " bytes.");
                        continue;
                    }
                    PropertyEmailTemplateDocument? document = JsonSerializer.Deserialize<PropertyEmailTemplateDocument>(File.ReadAllText(file), _Json);
                    if (document == null)
                    {
                        result.Problems.Add(name + ": empty.");
                        continue;
                    }
                    List<string> problems = new List<string>();
                    PropertyEmailTemplate? template = Build(Path.GetFileNameWithoutExtension(file), document, existing, problems);
                    if (template == null || problems.Count > 0)
                    {
                        foreach (string problem in problems) result.Problems.Add(name + ": " + problem);
                        continue;
                    }
                    result.Templates.Add(template);
                }
                catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
                {
                    result.Problems.Add(name + ": " + e.Message);
                }
            }
            return result;
        }

        /// <summary>
        /// Validate one document and build its template.
        /// </summary>
        /// <param name="key">Key (the file name without .json).</param>
        /// <param name="document">Document.</param>
        /// <param name="existing">Engine and registered templates.</param>
        /// <param name="problems">Problems found (appended).</param>
        /// <returns>Template, or null when the document cannot be used.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static PropertyEmailTemplate? Build(string key, PropertyEmailTemplateDocument document, IReadOnlyList<EmailTemplate> existing, List<string> problems)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(existing);
            ArgumentNullException.ThrowIfNull(problems);
            if (!EmailTemplateCatalog.IsValidKey(key))
            {
                problems.Add("the file name must be a template key (a lowercase letter, then lowercase letters, digits, and underscores).");
                return null;
            }
            if (document.Key != null && !String.Equals(document.Key.Trim(), key, StringComparison.Ordinal))
            {
                problems.Add("key " + document.Key + " does not match the file name.");
                return null;
            }
            EmailTemplate? replaces = existing.FirstOrDefault(t => String.Equals(t.Key, key, StringComparison.Ordinal));
            if (replaces != null && replaces.Recipient == EmailRecipientEnum.PlatformAdmins)
            {
                problems.Add("platform templates cannot be replaced by a property.");
                return null;
            }
            EmailCategoryEnum category = EmailCategoryEnum.Optional;
            EmailRecipientEnum recipient = EmailRecipientEnum.Member;
            if (replaces == null)
            {
                if (!Enum.TryParse(document.Category?.Trim(), true, out category) || !Enum.IsDefined(category)) problems.Add("category must be Essential, Optional, or Staff.");
                if (!Enum.TryParse(document.Recipient?.Trim(), true, out recipient) || !Enum.IsDefined(recipient) || recipient == EmailRecipientEnum.PlatformAdmins)
                    problems.Add("recipient must be Member, Staff, or Moderators.");
                if (String.IsNullOrWhiteSpace(document.Subject)) problems.Add("subject is required for a new template.");
                if (String.IsNullOrWhiteSpace(document.Heading)) problems.Add("heading is required for a new template.");
            }
            if (document.Samples != null)
                foreach (string token in document.Samples.Keys.Where(k => !Regex.IsMatch(k ?? String.Empty, "^[A-Za-z][A-Za-z0-9]*$")))
                    problems.Add("sample token " + token + " is not a valid token name.");
            if (problems.Count > 0) return null;

            PropertyEmailTemplate template = new PropertyEmailTemplate(key, document, replaces, category, recipient);
            Dictionary<string, string> errors = EmailRenderer.Validate(template, new EmailTemplateOverride
            {
                Subject = template.DefaultSubject,
                Preheader = template.DefaultPreheader,
                Heading = template.DefaultHeading,
                Body = template.DefaultBody
            });
            foreach (KeyValuePair<string, string> error in errors) problems.Add(error.Key + ": " + error.Value);
            if (template.ButtonLabel != null) Tokens(template, "buttonLabel", template.ButtonLabel, problems);
            if (!template.Tokens.Contains(template.ButtonToken)) problems.Add("buttonToken " + template.ButtonToken + " is not a token of this template.");
            foreach (string markdown in template.MarkdownTokens.Where(t => !template.Tokens.Contains(t))) problems.Add("markdownTokens: " + markdown + " is not a token of this template.");

            List<PropertyEmailBlockDocument> blocks = document.Blocks ?? new List<PropertyEmailBlockDocument>();
            if (blocks.Count > MaxBlocks) problems.Add("at most " + MaxBlocks + " blocks.");
            for (int i = 0; i < blocks.Count && i < MaxBlocks; i++)
            {
                PropertyEmailBlockDocument? block = blocks[i];
                string where = "blocks[" + i + "]";
                if (block == null)
                {
                    problems.Add(where + ": empty block.");
                    continue;
                }
                string type = PropertyEmailTemplate.Normalize(block.Type);
                if (!_Types.Contains(type))
                {
                    problems.Add(where + ": unknown type " + block.Type + "; use " + String.Join(", ", _Types) + ".");
                    continue;
                }
                if ((type == "paragraph" || type == "subheading" || type == "callout" || type == "code") && String.IsNullOrWhiteSpace(block.Text)) problems.Add(where + ": text is required.");
                if (type == "callout" && PropertyEmailTemplate.ParseTone(block.Tone) == null) problems.Add(where + ": tone must be Info, Success, Warning, or Danger.");
                if (type == "list" && (block.Items == null || block.Items.Count == 0)) problems.Add(where + ": items are required.");
                if (type == "fields" && (block.Fields == null || block.Fields.Count == 0)) problems.Add(where + ": fields are required.");
                if (type == "markdown" && (block.Token == null || !template.Tokens.Contains(block.Token))) problems.Add(where + ": token must be a token of this template.");
                if ((type == "button" || type == "notice") && block.UrlToken != null && !template.Tokens.Contains(block.UrlToken)) problems.Add(where + ": urlToken must be a token of this template.");
                if (type == "notice" && String.IsNullOrWhiteSpace(block.Text) && block.Label == null) problems.Add(where + ": text or label is required.");
                if (type == "button" && block.Label == null && block.UrlToken != null && template.ButtonLabel == null) problems.Add(where + ": label is required when the template has no button.");
                Tokens(template, where + ".text", block.Text, problems);
                Tokens(template, where + ".label", block.Label, problems);
                foreach (string item in block.Items ?? new List<string>()) Tokens(template, where + ".items", item, problems);
                foreach (EmailFieldDocument field in (block.Fields ?? new List<EmailFieldDocument>()).Where(f => f != null))
                {
                    Tokens(template, where + ".fields", field.Label, problems);
                    Tokens(template, where + ".fields", field.Value, problems);
                }
                if (Total(block) > EmailRenderer.MaxBodyLength) problems.Add(where + ": at most " + EmailRenderer.MaxBodyLength + " characters.");
            }
            return problems.Count > 0 ? null : template;
        }

        #endregion

        #region Private-Methods

        private static void Tokens(EmailTemplate template, string field, string? text, List<string> problems)
        {
            List<string> unknown = EmailRenderer.UnknownTokens(template, text);
            if (unknown.Count > 0) problems.Add(field + ": unknown token " + String.Join(", ", unknown.Select(u => "{" + u + "}")) + ".");
        }

        private static int Total(PropertyEmailBlockDocument block)
        {
            int total = (block.Text ?? String.Empty).Length + (block.Label ?? String.Empty).Length;
            foreach (string item in block.Items ?? new List<string>()) total += (item ?? String.Empty).Length;
            foreach (EmailFieldDocument field in (block.Fields ?? new List<EmailFieldDocument>()).Where(f => f != null)) total += (field.Label ?? String.Empty).Length + (field.Value ?? String.Empty).Length;
            return total;
        }

        #endregion
    }
}
