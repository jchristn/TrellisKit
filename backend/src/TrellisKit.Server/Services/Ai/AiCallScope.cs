namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Threading;

    /// <summary>
    /// Ambient context of model calls on the current async flow: the job (key, run, item) and the task role (executor
    /// position, escalation) that every ledger row records, and a cost accumulator the job engine adds to the item's
    /// cost. Scopes nest; an inner scope inherits the outer job context. Thread safety: AsyncLocal per flow; the cost
    /// accumulator is safe for concurrent calls inside one scope.
    /// </summary>
    public sealed class AiCallScope : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The innermost scope on this flow, or null.
        /// </summary>
        public static AiCallScope? Current
        {
            get { return _Current.Value; }
        }

        /// <summary>
        /// Job key, or null.
        /// </summary>
        public string? JobKey { get; }

        /// <summary>
        /// Job run, or null.
        /// </summary>
        public string? JobRunId { get; }

        /// <summary>
        /// Job item, or null.
        /// </summary>
        public string? JobItemId { get; }

        /// <summary>
        /// Task role, or null.
        /// </summary>
        public string? Role { get; }

        /// <summary>
        /// Executor position within the role, or null.
        /// </summary>
        public int? ExecutorPosition { get; }

        /// <summary>
        /// Why earlier executors were passed over, or null.
        /// </summary>
        public string? EscalationReason { get; }

        /// <summary>
        /// Cost of the calls made in this scope and its inner scopes, in micro-dollars.
        /// </summary>
        public long CostMicros
        {
            get { return Interlocked.Read(ref _CostMicros); }
        }

        #endregion

        #region Private-Members

        private static readonly AsyncLocal<AiCallScope?> _Current = new AsyncLocal<AiCallScope?>();
        private readonly AiCallScope? _Parent;
        private long _CostMicros = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private AiCallScope(AiCallScope? parent, string? jobKey, string? jobRunId, string? jobItemId, string? role, int? position, string? escalation)
        {
            _Parent = parent;
            JobKey = jobKey ?? parent?.JobKey;
            JobRunId = jobRunId ?? parent?.JobRunId;
            JobItemId = jobItemId ?? parent?.JobItemId;
            Role = role ?? parent?.Role;
            ExecutorPosition = position ?? parent?.ExecutorPosition;
            EscalationReason = escalation;
        }

        /// <summary>
        /// Begin a job item scope.
        /// </summary>
        /// <param name="jobKey">Job key.</param>
        /// <param name="jobRunId">Run identifier.</param>
        /// <param name="jobItemId">Item identifier.</param>
        /// <returns>Scope; dispose to end it.</returns>
        public static AiCallScope BeginJob(string jobKey, string jobRunId, string jobItemId)
        {
            AiCallScope scope = new AiCallScope(_Current.Value, jobKey, jobRunId, jobItemId, null, null, null);
            _Current.Value = scope;
            return scope;
        }

        /// <summary>
        /// Begin a task role executor scope.
        /// </summary>
        /// <param name="role">Role key.</param>
        /// <param name="position">Executor position.</param>
        /// <param name="escalation">Why earlier executors were passed over, or null.</param>
        /// <returns>Scope; dispose to end it.</returns>
        public static AiCallScope BeginRole(string role, int position, string? escalation)
        {
            AiCallScope scope = new AiCallScope(_Current.Value, null, null, null, role, position, escalation);
            _Current.Value = scope;
            return scope;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add the cost of a call to this scope and its outer scopes.
        /// </summary>
        /// <param name="micros">Cost in micro-dollars (negative values are ignored).</param>
        public void AddCost(long micros)
        {
            if (micros <= 0) return;
            for (AiCallScope? s = this; s != null; s = s._Parent) Interlocked.Add(ref s._CostMicros, micros);
        }

        /// <summary>
        /// End the scope and restore the outer one.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            if (_Current.Value == this) _Current.Value = _Parent;
        }

        #endregion
    }
}
