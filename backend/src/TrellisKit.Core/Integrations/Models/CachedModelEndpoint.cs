namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Net.Http;
    using TrellisKit.Core.Integrations.Implementations;

    /// <summary>
    /// One endpoint version cached by the model client factory: its HTTP client, decrypted credentials, and redactor.
    /// </summary>
    internal sealed class CachedModelEndpoint
    {
        internal DateTime Version { get; }

        internal HttpClient Http { get; }

        internal EndpointCredentials Credentials { get; }

        internal AiSecretRedactor Redactor { get; }

        internal CachedModelEndpoint(DateTime version, HttpClient http, EndpointCredentials credentials, AiSecretRedactor redactor)
        {
            Version = version;
            Http = http;
            Credentials = credentials;
            Redactor = redactor;
        }
    }
}
