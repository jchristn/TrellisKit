namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Models;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The job tool loop of an API endpoint executor (docs/HARNESSES.md): the counterpart of a harness's own tool loop,
    /// so one job handler works with either kind of executor. Each round calls the endpoint through
    /// <see cref="ModelInvoker"/> with the job tools in <see cref="CompletionCall.Tools"/> (native tools or the JSON
    /// prompt protocol, by the endpoint's toolCalling), runs each requested call through
    /// <see cref="AgentToolSet.InvokeAsync"/>, appends the results, and calls again until the model answers without tools
    /// or the round limit is reached, then once more without tools. Every round is its own ledger row with the scope
    /// guard applied by the invoker. Thread safety: safe to call concurrently (state is per call).
    /// </summary>
    public class AgentToolLoop
    {
        #region Private-Members

        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">ai is null.</exception>
        public AgentToolLoop(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the loop.
        /// </summary>
        /// <param name="endpoint">Completion endpoint.</param>
        /// <param name="messages">Opening messages (system and user).</param>
        /// <param name="tools">Job tools.</param>
        /// <param name="maxRounds">Rounds that may call tools (1 to 20).</param>
        /// <param name="maxTokens">Output cap per round, or null.</param>
        /// <param name="effort">Reasoning effort, or null.</param>
        /// <param name="isTest">Test traffic.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome: the last round's text and status, with tokens and cost summed over every round.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public async Task<CompletionOutcome> RunAsync(ModelEndpoint endpoint, List<ChatMessage> messages, AgentToolSet tools, int maxRounds, int? maxTokens, ModelReasoningEffortEnum? effort, bool isTest, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(messages);
            ArgumentNullException.ThrowIfNull(tools);
            Stopwatch watch = Stopwatch.StartNew();
            int rounds = Math.Clamp(maxRounds, 1, 20);
            List<ToolDefinition> definitions = tools.Tools.Select(t => AssistantToolRegistry.Function(t.Name, t.Description, tools.Schema(t.Name))).ToList();
            List<ChatMessage> conversation = new List<ChatMessage>(messages);
            long cost = 0;
            int prompt = 0;
            int completion = 0;
            int reasoning = 0;
            CompletionOutcome last = new CompletionOutcome { Endpoint = endpoint, ErrorCode = "ModelUnavailable" };
            for (int round = 0; round <= rounds; round++)
            {
                token.ThrowIfCancellationRequested();
                bool offer = round < rounds;
                last = await _Ai.Invoker.CompleteAsync(new CompletionCall
                {
                    Endpoint = endpoint,
                    Messages = conversation,
                    MaxTokens = maxTokens,
                    Stream = false,
                    Purpose = AssistantPurposeEnum.Task,
                    IsTest = isTest,
                    ReasoningEffort = effort,
                    Tools = offer ? definitions : new List<ToolDefinition>()
                }, null, null, token).ConfigureAwait(false);
                cost += last.CostMicros;
                prompt += last.PromptTokens;
                completion += last.CompletionTokens;
                reasoning += last.ReasoningTokens;
                if (!last.Success || !offer || last.ToolCalls.Count == 0) break;
                conversation.Add(last.AssistantMessage ?? ChatMessage.AssistantToolCalls(last.ToolCalls));
                int index = 0;
                Stopwatch toolWatch = Stopwatch.StartNew();
                foreach (ToolCall requested in last.ToolCalls)
                {
                    AgentToolResult result = await tools.InvokeAsync(requested.Name, requested.ArgumentsJson, token).ConfigureAwait(false);
                    string id = String.IsNullOrEmpty(requested.Id) ? "call_" + round + "_" + index : requested.Id!;
                    conversation.Add(ChatMessage.ToolResult(id, requested.Name ?? String.Empty, (result.Accepted ? String.Empty : "Error: ") + result.Message));
                    index++;
                }
                // docs/AI_TELEMETRY.md, "Tool time": the tools a model call asked for are attributed to that call's row.
                await _Ai.Usage.RecordToolsAsync(last.CallId, last.Endpoint ?? endpoint, index, toolWatch.ElapsedMilliseconds).ConfigureAwait(false);
            }
            last.CostMicros = cost;
            last.PromptTokens = prompt;
            last.CompletionTokens = completion;
            last.ReasoningTokens = reasoning;
            last.LatencyMs = watch.ElapsedMilliseconds;
            last.ToolCalls = new List<ToolCall>();
            last.Endpoint ??= endpoint;
            return last;
        }

        #endregion
    }
}
