namespace TrellisKit.Server.Services.Ai
{
    using System.Threading.Tasks;

    /// <summary>
    /// A sink that drops every event (non-streaming JSON replies).
    /// </summary>
    public sealed class NullChatEventSink : IChatEventSink
    {
        #region Public-Members

        /// <summary>
        /// Shared instance.
        /// </summary>
        public static readonly NullChatEventSink Instance = new NullChatEventSink();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task EmitAsync(string name, object data)
        {
            return Task.CompletedTask;
        }

        #endregion
    }
}
