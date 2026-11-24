namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Cached provider model list of one endpoint version.
    /// </summary>
    internal sealed class ModelListSnapshot
    {
        internal DateTime Version { get; }

        internal DateTime ExpiresUtc { get; }

        internal List<ProviderModelView> Models { get; }

        internal ModelListSnapshot(DateTime version, DateTime expiresUtc, List<ProviderModelView> models)
        {
            Version = version;
            ExpiresUtc = expiresUtc;
            Models = models;
        }
    }
}
