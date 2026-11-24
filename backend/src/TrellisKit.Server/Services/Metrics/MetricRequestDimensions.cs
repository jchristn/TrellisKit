namespace TrellisKit.Server.Services.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Text.Json;

    /// <summary>Privacy-safe operational dimensions. Unknown resource segments are replaced; headers are bounded tokens.</summary>
    public static class MetricRequestDimensions
    {
        private static readonly HashSet<string> _Resources = new HashSet<string>(("api v1 admin platform public auth me property properties health request-history summary enumerate metrics query members users register login logout refresh verify-email password reset sign-in sign-up age-attestation profile appearance notifications read push tokens posts feed comments reactions forums threads library lessons series events rsvps directory venues instructors geocode search support tickets messages conversations media attachments uploads updates channels publish flags kill-switches broadcasts send jobs runs items retry ai endpoints assistants calls roles prompts test healthcheck billing membership subscriptions usage export settings audit documents accept acceptances versions banner assets strings webhook webhooks email mailgun unsubscribe").Split(' '),StringComparer.OrdinalIgnoreCase);
        /// <summary>Normalize a path without preserving ids, slugs, search terms, or unknown segments.</summary>
        public static string Route(string method,string? path)
        {
            string[] parts=(path??String.Empty).Split('?',2)[0].Split('/',StringSplitOptions.RemoveEmptyEntries).Take(12).Select(p=>_Resources.Contains(p)?p.ToLowerInvariant():"{id}").ToArray();
            return method.ToUpperInvariant()+" /"+String.Join('/',parts);
        }
        /// <summary>Read only a bounded error identifier from an error response; never store messages or field values.</summary>
        public static string Error(int status,string? response)
        {
            if(status<400||String.IsNullOrEmpty(response)||response.Length>65536)return String.Empty;
            try
            {
                using(JsonDocument document=JsonDocument.Parse(response))
                {
                    if(document.RootElement.ValueKind==JsonValueKind.Object&&document.RootElement.TryGetProperty("error",out JsonElement value)&&value.ValueKind==JsonValueKind.String)
                    {
                        string code=value.GetString()??String.Empty;
                        if(Regex.IsMatch(code,"^[A-Za-z][A-Za-z0-9_]{0,63}$",RegexOptions.CultureInvariant))return code;
                    }
                }
            }
            catch(JsonException) { }
            return String.Empty;
        }
        /// <summary>Read a safe bounded client token. Unknown punctuation yields unknown.</summary>
        public static string Header(NameValueCollection headers,string key)
        {
            string value=(headers[key]??String.Empty).Trim();
            return value.Length>0&&value.Length<=64&&Regex.IsMatch(value,"^[a-zA-Z0-9._-]+$",RegexOptions.CultureInvariant)?value:"unknown";
        }
    }
}
