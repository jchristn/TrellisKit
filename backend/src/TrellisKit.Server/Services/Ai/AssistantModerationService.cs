namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// The moderation classifier (docs/AI_CONTRACT.md section 12.2, docs/INTEGRATION.md owner requirement R7): a fixed
    /// engine prompt classifies a member message or a finished reply as Allow, Flag, or Block with a category, through
    /// ChatAsync with 16 tokens and temperature 0. An assistant's own moderation model classifies when it has one;
    /// otherwise the Moderation task role does, and only when that role has no executors is classification skipped
    /// (recorded as verdict skipped, source none, on trelliskit.ai.moderation, and as an input stage on the reply).
    /// Classifier failures fail open (verdict Error, treated as Allow) and are counted.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AssistantModerationService
    {
        #region Public-Members

        /// <summary>
        /// Notice stored and shown in place of a withheld reply.
        /// </summary>
        public const string BlockedNotice = "This reply was withheld.";

        /// <summary>
        /// The task role that classifies text outside an assistant.
        /// </summary>
        public const string RoleName = "Moderation";

        /// <summary>
        /// Stage detail recorded when member input was not classified because the assistant has no moderation model and
        /// the Moderation role has no executors.
        /// </summary>
        public const string SkippedDetail = "Skipped: no moderation model and no Moderation role executors";

        #endregion

        #region Private-Members

        private static readonly string[] _Categories = new[] { "Harassment", "HateSpeech", "SexualContent", "Violence", "SelfHarm", "Illegal", "PromptInjection", "OffScope", "Other" };
        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AssistantModerationService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Classify text with the assistant's moderation endpoint, or through the Moderation task role when the
        /// assistant has no moderation model. Returns Allow without a call when the assistant's mode is Off, and Allow
        /// with Skipped set when it has no moderation model and the role has no executors.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="endpoints">Endpoints of the assistant, by id.</param>
        /// <param name="text">Text.</param>
        /// <param name="stage">input or output.</param>
        /// <param name="isTest">Admin test traffic.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome (never throws for classifier failures).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<ModerationOutcome> ClassifyAsync(Assistant assistant, Dictionary<string, ModelEndpoint> endpoints, string text, string stage, bool isTest, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(text);
            if (assistant.ModerationMode == AssistantModerationModeEnum.Off) return new ModerationOutcome { Verdict = AssistantModerationVerdictEnum.Allow };
            DateTime started = DateTime.UtcNow;
            if (!assistant.Models.TryGetValue(AssistantPurposeEnum.Moderation, out string? id) || !endpoints.TryGetValue(id, out ModelEndpoint? endpoint))
                return await FallbackAsync(assistant, text, stage, isTest, started, token).ConfigureAwait(false);
            CompletionOutcome outcome = await _Ai.Invoker.PromptAsync(endpoint, Wrap(text), await _Ai.Prompts.GetAsync(PromptCatalog.ModerationClassifier, token).ConfigureAwait(false), 16, 0, AssistantPurposeEnum.Moderation, assistant.Id, isTest, token).ConfigureAwait(false);
            ModerationOutcome result = outcome.Success ? Parse(outcome.Text) : new ModerationOutcome { Verdict = AssistantModerationVerdictEnum.Error };
            TrellisKitTelemetry.Add(AiTelemetry.Moderation, 1, "stage", stage, "verdict", AiTelemetry.Label(result.Verdict.ToString()), "source", "model");
            result.Stage = new AssistantMessageStage
            {
                Stage = "moderation." + stage,
                EndpointId = endpoint.Id,
                Model = endpoint.Model,
                StartedUtc = started,
                DurationMs = outcome.LatencyMs,
                Success = outcome.Success,
                Detail = result.Verdict + (result.Category != null ? " " + result.Category : String.Empty) + (assistant.ModerationMode == AssistantModerationModeEnum.LogOnly ? " (log only)" : String.Empty),
                PromptTokens = outcome.PromptTokens,
                CompletionTokens = outcome.CompletionTokens
            };
            return result;
        }

        /// <summary>
        /// Classify text through the Moderation task role with the same classifier prompt, wrapping, and parsing as
        /// <see cref="ClassifyAsync"/> (the path of features without an assistant, and of the scope benchmark).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="stage">input or output.</param>
        /// <param name="isTest">Test traffic.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome; Verdict Error when the role failed (see the role outcome for why).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<KeyValuePair<ModerationOutcome, RoleOutcome>> ClassifyRoleAsync(string text, string stage, bool isTest, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(stage);
            string prompt = await _Ai.Prompts.GetAsync(PromptCatalog.ModerationClassifier, token).ConfigureAwait(false);
            RoleOutcome role = await _Ai.Roles.PromptAsync(RoleName, Wrap(text), prompt, 16, token, isTest).ConfigureAwait(false);
            ModerationOutcome result = role.Success ? Parse(role.Text) : new ModerationOutcome { Verdict = AssistantModerationVerdictEnum.Error };
            // A role without executors is not a classifier failure; the caller decides whether that means skipped.
            if (role.ErrorCode != "NoExecutors")
                TrellisKitTelemetry.Add(AiTelemetry.Moderation, 1, "stage", stage, "verdict", AiTelemetry.Label(result.Verdict.ToString()), "source", "role");
            return new KeyValuePair<ModerationOutcome, RoleOutcome>(result, role);
        }

        /// <summary>
        /// Parse a classifier reply: the first of Allow, Flag, or Block (Allow when none) and the first known category.
        /// </summary>
        /// <param name="reply">Classifier reply, or null.</param>
        /// <returns>Outcome without a stage.</returns>
        public static ModerationOutcome Parse(string? reply)
        {
            string[] words = (reply ?? String.Empty).Replace(",", " ").Replace(".", " ").Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string verdict = words.FirstOrDefault(w => w.Equals("Allow", StringComparison.OrdinalIgnoreCase) || w.Equals("Flag", StringComparison.OrdinalIgnoreCase) || w.Equals("Block", StringComparison.OrdinalIgnoreCase)) ?? "Allow";
            return new ModerationOutcome
            {
                Verdict = Enum.Parse<AssistantModerationVerdictEnum>(verdict, true),
                Category = words.Select(w => _Categories.FirstOrDefault(c => c.Equals(w, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(c => c != null)
            };
        }

        #endregion

        #region Private-Methods

        private async Task<ModerationOutcome> FallbackAsync(Assistant assistant, string text, string stage, bool isTest, DateTime started, CancellationToken token)
        {
            KeyValuePair<ModerationOutcome, RoleOutcome> classified = await ClassifyRoleAsync(text, stage, isTest, token).ConfigureAwait(false);
            ModerationOutcome result = classified.Key;
            RoleOutcome role = classified.Value;
            string logOnly = assistant.ModerationMode == AssistantModerationModeEnum.LogOnly ? " (log only)" : String.Empty;
            if (role.ErrorCode == "NoExecutors")
            {
                // docs/INTEGRATION.md owner requirement R7: the skip is visible to administrators in metrics and, for
                // member input, as a stage of the reply.
                TrellisKitTelemetry.Add(AiTelemetry.Moderation, 1, "stage", stage, "verdict", "skipped", "source", "none");
                ModerationOutcome skipped = new ModerationOutcome { Verdict = AssistantModerationVerdictEnum.Allow, Skipped = true };
                if (stage == "input")
                {
                    skipped.Stage = new AssistantMessageStage
                    {
                        Stage = "moderation." + stage,
                        StartedUtc = started,
                        Success = true,
                        Detail = SkippedDetail
                    };
                }
                return skipped;
            }
            result.Stage = new AssistantMessageStage
            {
                Stage = "moderation." + stage,
                EndpointId = role.EndpointId,
                Model = role.Model,
                StartedUtc = started,
                DurationMs = role.LatencyMs,
                Success = role.Success,
                Detail = (role.Success ? result.Verdict + (result.Category != null ? " " + result.Category : String.Empty) : role.ErrorCode ?? "Error") + " (Moderation role)" + logOnly,
                PromptTokens = role.PromptTokens,
                CompletionTokens = role.CompletionTokens
            };
            return result;
        }

        private static string Wrap(string text)
        {
            string body = text.Length > 8000 ? text.Substring(0, 8000) : text;
            return "<<<TEXT\n" + body + "\nTEXT>>>";
        }

        #endregion
    }
}
