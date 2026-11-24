namespace TrellisKit.Server.Services.Ai
{
    using System;

    /// <summary>
    /// What a job tool handler returns: whether it accepted the call, the message the agent reads, and the structured
    /// data the job keeps.
    /// </summary>
    public class AgentToolResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the handler accepted (and stored) the call.
        /// </summary>
        public bool Accepted { get; set; } = false;

        /// <summary>
        /// Message returned to the agent (for example "Recorded." or why the call was refused). Never null.
        /// </summary>
        public string Message
        {
            get { return _Message; }
            set { _Message = value ?? String.Empty; }
        }

        /// <summary>
        /// Structured data the handler kept, as JSON text, or null. Returned to the job handler in the agent outcome.
        /// </summary>
        public string? DataJson { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Message = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// An accepted call.
        /// </summary>
        /// <param name="message">Message for the agent, or null for "Recorded.".</param>
        /// <param name="dataJson">Structured data kept, or null.</param>
        /// <returns>Result.</returns>
        public static AgentToolResult Accept(string? message = null, string? dataJson = null)
        {
            return new AgentToolResult { Accepted = true, Message = message ?? "Recorded.", DataJson = dataJson };
        }

        /// <summary>
        /// A refused call; the agent reads the message and may retry with corrected arguments.
        /// </summary>
        /// <param name="message">Why the call was refused.</param>
        /// <returns>Result.</returns>
        public static AgentToolResult Reject(string message)
        {
            return new AgentToolResult { Accepted = false, Message = message ?? "Rejected." };
        }

        #endregion
    }
}
