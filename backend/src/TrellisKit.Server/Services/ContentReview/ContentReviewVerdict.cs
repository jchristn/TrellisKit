namespace TrellisKit.Server.Services.ContentReview
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The reviewer's verdict on one content changelog row, parsed leniently from the model's reply (one JSON object
    /// with verdict, categories, severity, and note; see the contentReview.classifier prompt). Immutable after parsing.
    /// </summary>
    public sealed class ContentReviewVerdict
    {
        #region Public-Members

        /// <summary>
        /// Clean or Flagged.
        /// </summary>
        public ContentReviewStatusEnum Status { get; private set; } = ContentReviewStatusEnum.Clean;

        /// <summary>
        /// Reported categories (empty when Clean).
        /// </summary>
        public List<ContentReviewCategoryEnum> Categories { get; private set; } = new List<ContentReviewCategoryEnum>();

        /// <summary>
        /// Reported severity (None when Clean; at least Low when Flagged).
        /// </summary>
        public ContentReviewSeverityEnum Severity { get; private set; } = ContentReviewSeverityEnum.None;

        /// <summary>
        /// The reviewer's note, or null.
        /// </summary>
        public string? Note { get; private set; } = null;

        /// <summary>
        /// Longest note kept (characters).
        /// </summary>
        public const int MaxNoteLength = 500;

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, ContentReviewCategoryEnum> _Aliases = new Dictionary<string, ContentReviewCategoryEnum>(StringComparer.OrdinalIgnoreCase)
        {
            { "sexual", ContentReviewCategoryEnum.SexualContent },
            { "sexualcontent", ContentReviewCategoryEnum.SexualContent },
            { "nudity", ContentReviewCategoryEnum.SexualContent },
            { "harassment", ContentReviewCategoryEnum.HarassmentAndHate },
            { "hate", ContentReviewCategoryEnum.HarassmentAndHate },
            { "hatespeech", ContentReviewCategoryEnum.HarassmentAndHate },
            { "harassmentandhate", ContentReviewCategoryEnum.HarassmentAndHate },
            { "bullying", ContentReviewCategoryEnum.HarassmentAndHate },
            { "threat", ContentReviewCategoryEnum.HarassmentAndHate },
            { "threats", ContentReviewCategoryEnum.HarassmentAndHate },
            { "violence", ContentReviewCategoryEnum.Violence },
            { "illegal", ContentReviewCategoryEnum.IllegalActivity },
            { "illegalactivity", ContentReviewCategoryEnum.IllegalActivity },
            { "selfharm", ContentReviewCategoryEnum.SelfHarm },
            { "suicide", ContentReviewCategoryEnum.SelfHarm },
            { "spam", ContentReviewCategoryEnum.Spam },
            { "scam", ContentReviewCategoryEnum.Scam },
            { "fraud", ContentReviewCategoryEnum.Scam },
            { "phishing", ContentReviewCategoryEnum.Scam },
            { "personaldata", ContentReviewCategoryEnum.PersonalData },
            { "pii", ContentReviewCategoryEnum.PersonalData },
            { "privacy", ContentReviewCategoryEnum.PersonalData },
            { "doxxing", ContentReviewCategoryEnum.PersonalData }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a reply. Accepts surrounding prose or a code fence around the JSON object, category and severity names
        /// in any case and spacing, and the older Allow, Flag, and Block verdict words.
        /// </summary>
        /// <param name="reply">Model reply.</param>
        /// <param name="verdict">The verdict, or null when the reply has no readable verdict.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParse(string? reply, out ContentReviewVerdict? verdict)
        {
            verdict = null;
            if (String.IsNullOrWhiteSpace(reply)) return false;
            int start = reply.IndexOf('{');
            int end = reply.LastIndexOf('}');
            if (start < 0 || end <= start) return false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(reply.Substring(start, end - start + 1)))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                    string? word = Text(doc.RootElement, "verdict");
                    if (word == null) return false;
                    bool? flagged = Flagged(word);
                    if (flagged == null) return false;
                    List<ContentReviewCategoryEnum> categories = CategoriesOf(doc.RootElement);
                    ContentReviewSeverityEnum severity = SeverityOf(Text(doc.RootElement, "severity"));
                    string? note = Text(doc.RootElement, "note");
                    bool isFlagged = flagged.Value || (categories.Count > 0 && severity > ContentReviewSeverityEnum.None);
                    verdict = new ContentReviewVerdict
                    {
                        Status = isFlagged ? ContentReviewStatusEnum.Flagged : ContentReviewStatusEnum.Clean,
                        Categories = isFlagged ? categories : new List<ContentReviewCategoryEnum>(),
                        Severity = isFlagged ? (severity == ContentReviewSeverityEnum.None ? ContentReviewSeverityEnum.Low : severity) : ContentReviewSeverityEnum.None,
                        Note = String.IsNullOrWhiteSpace(note) ? null : (note.Trim().Length > MaxNoteLength ? note.Trim().Substring(0, MaxNoteLength) : note.Trim())
                    };
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// A category from a name or alias, or null.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Category, or null.</returns>
        public static ContentReviewCategoryEnum? CategoryOf(string? name)
        {
            string key = Key(name);
            if (key.Length == 0) return null;
            if (Enum.TryParse(key, true, out ContentReviewCategoryEnum parsed) && Enum.IsDefined(parsed)) return parsed;
            return _Aliases.TryGetValue(key, out ContentReviewCategoryEnum alias) ? alias : null;
        }

        #endregion

        #region Private-Methods

        private static bool? Flagged(string word)
        {
            switch (Key(word).ToLowerInvariant())
            {
                case "clean":
                case "allow":
                case "allowed":
                case "ok":
                case "safe":
                    return false;
                case "flagged":
                case "flag":
                case "block":
                case "blocked":
                case "violation":
                    return true;
                default:
                    return null;
            }
        }

        private static List<ContentReviewCategoryEnum> CategoriesOf(JsonElement root)
        {
            List<string> names = new List<string>();
            if (TryGet(root, "categories", out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Array)
                    names.AddRange(value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? String.Empty));
                else if (value.ValueKind == JsonValueKind.String)
                    names.AddRange((value.GetString() ?? String.Empty).Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries));
            }
            return names.Select(CategoryOf).Where(c => c != null).Select(c => c!.Value).Distinct().OrderBy(c => c).ToList();
        }

        private static ContentReviewSeverityEnum SeverityOf(string? value)
        {
            string key = Key(value);
            if (Enum.TryParse(key, true, out ContentReviewSeverityEnum parsed) && Enum.IsDefined(parsed)) return parsed;
            switch (key.ToLowerInvariant())
            {
                case "minor":
                    return ContentReviewSeverityEnum.Low;
                case "moderate":
                    return ContentReviewSeverityEnum.Medium;
                case "severe":
                case "serious":
                    return ContentReviewSeverityEnum.High;
                case "urgent":
                    return ContentReviewSeverityEnum.Critical;
                default:
                    return ContentReviewSeverityEnum.None;
            }
        }

        private static string? Text(JsonElement root, string name)
        {
            if (!TryGet(root, name, out JsonElement value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Null ? null : value.ToString();
        }

        private static bool TryGet(JsonElement root, string name, out JsonElement value)
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (String.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static string Key(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            return new string(value.Where(Char.IsLetter).ToArray());
        }

        #endregion
    }
}
