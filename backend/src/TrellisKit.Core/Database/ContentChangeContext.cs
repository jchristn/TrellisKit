namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Who is changing content in the current request, for the content changelog triggers (docs/INTEGRATION.md owner
    /// requirement R3). The request pipeline sets <see cref="Current"/> when it resolves a request's property services;
    /// the PostgreSQL driver copies it into the transaction-local settings trelliskit.actor and trelliskit.client before
    /// any statement that writes, so the triggers attribute the change. Without a context (workers, jobs, seeds) the
    /// triggers fall back to the row's author and the System client. Thread safety: immutable; the ambient value flows
    /// with the execution context.
    /// </summary>
    public sealed class ContentChangeContext
    {
        #region Public-Members

        /// <summary>
        /// Setting the triggers read for the actor.
        /// </summary>
        public const string ActorSetting = "trelliskit.actor";

        /// <summary>
        /// Setting the triggers read for the client.
        /// </summary>
        public const string ClientSetting = "trelliskit.client";

        /// <summary>
        /// Signed-in member making the change, or null (anonymous, or the break-glass admin token).
        /// </summary>
        public string? ActorUserId { get; }

        /// <summary>
        /// Client of the request.
        /// </summary>
        public ContentChangeClientEnum Client { get; }

        /// <summary>
        /// The context of the current request, or null outside a request. Set it from a synchronous method so it flows
        /// into the caller's remaining awaits.
        /// </summary>
        public static ContentChangeContext? Current
        {
            get { return _Current.Value; }
            set { _Current.Value = value; }
        }

        #endregion

        #region Private-Members

        private static readonly AsyncLocal<ContentChangeContext?> _Current = new AsyncLocal<ContentChangeContext?>();
        private static readonly string[] _WriteKeywords = new[] { "INSERT", "UPDATE", "DELETE" };
        private static readonly string[] _Watched = TrellisKit.Core.Database.Postgresql.Queries.ContentChangesSchema.Sources.Select(s => s.Table).Distinct(StringComparer.Ordinal).ToArray();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="actorUserId">Signed-in member, or null.</param>
        /// <param name="client">Client.</param>
        public ContentChangeContext(string? actorUserId, ContentChangeClientEnum client)
        {
            ActorUserId = String.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
            Client = client;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The client of a request from its headers: X-TK-Client, else X-Client (web or mobile, sent by the apps), else
        /// the User-Agent (browsers are Web, the native networking stacks are Mobile), else Api.
        /// </summary>
        /// <param name="tdClient">X-TK-Client header, or null.</param>
        /// <param name="client">X-Client header, or null.</param>
        /// <param name="userAgent">User-Agent header, or null.</param>
        /// <returns>Client.</returns>
        public static ContentChangeClientEnum ClientFrom(string? tdClient, string? client, string? userAgent)
        {
            ContentChangeClientEnum? named = Named(tdClient) ?? Named(client);
            if (named != null) return named.Value;
            string agent = userAgent ?? String.Empty;
            if (agent.Length == 0) return ContentChangeClientEnum.Api;
            if (agent.Contains("Expo", StringComparison.OrdinalIgnoreCase) || agent.Contains("okhttp", StringComparison.OrdinalIgnoreCase) ||
                agent.Contains("CFNetwork", StringComparison.OrdinalIgnoreCase) || agent.Contains("Dalvik", StringComparison.OrdinalIgnoreCase))
                return ContentChangeClientEnum.Mobile;
            if (agent.StartsWith("Mozilla/", StringComparison.OrdinalIgnoreCase)) return ContentChangeClientEnum.Web;
            return ContentChangeClientEnum.Api;
        }

        /// <summary>
        /// Whether any statement of a batch may write a watched member-content table: it contains INSERT, UPDATE, or
        /// DELETE and the name of a watched table anywhere, string literals included (a false positive only costs one
        /// extra round trip; reads and writes of other tables skip it).
        /// </summary>
        /// <param name="statements">Statements.</param>
        /// <returns>True when a statement may write.</returns>
        /// <exception cref="ArgumentNullException">statements is null.</exception>
        public static bool MayWrite(IEnumerable<string> statements)
        {
            ArgumentNullException.ThrowIfNull(statements);
            foreach (string statement in statements)
            {
                if (String.IsNullOrEmpty(statement)) continue;
                if (!_WriteKeywords.Any(k => statement.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                if (_Watched.Any(t => statement.Contains(t, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether this context attributes the same actor and client as another.
        /// </summary>
        /// <param name="actorUserId">Actor.</param>
        /// <param name="client">Client.</param>
        /// <returns>True when equal.</returns>
        public bool Matches(string? actorUserId, ContentChangeClientEnum client)
        {
            string? actor = String.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
            return Client == client && String.Equals(ActorUserId, actor, StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static ContentChangeClientEnum? Named(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            if (String.Equals(v, "web", StringComparison.OrdinalIgnoreCase) || String.Equals(v, "website", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(v, "embed", StringComparison.OrdinalIgnoreCase)) return ContentChangeClientEnum.Web;
            if (String.Equals(v, "mobile", StringComparison.OrdinalIgnoreCase) || String.Equals(v, "ios", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(v, "android", StringComparison.OrdinalIgnoreCase)) return ContentChangeClientEnum.Mobile;
            if (String.Equals(v, "api", StringComparison.OrdinalIgnoreCase)) return ContentChangeClientEnum.Api;
            return null;
        }

        #endregion
    }
}
