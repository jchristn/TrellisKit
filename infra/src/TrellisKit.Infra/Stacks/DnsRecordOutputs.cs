namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// Stack outputs that list the DNS records an operator creates at an external DNS provider (external DNS mode).
    /// Every output value reads "name TYPE value". Records needed before certificates can be issued are named
    /// DnsValidation* (see <see cref="CertificateRequester"/>); every other record is named DnsRecord*.
    /// Thread safety: stateless.
    /// </summary>
    public static class DnsRecordOutputs
    {
        /// <summary>
        /// Prefix of the output keys of service records (website, API, media, redirect, platform, email).
        /// </summary>
        public static readonly string RecordPrefix = "DnsRecord";

        /// <summary>
        /// Prefix of the output keys of certificate validation records.
        /// </summary>
        public static readonly string ValidationPrefix = "DnsValidation";

        /// <summary>
        /// Add a record output.
        /// </summary>
        /// <param name="stack">Stack that owns the output.</param>
        /// <param name="id">Output key suffix, for example WebApex (the key becomes DnsRecordWebApex).</param>
        /// <param name="name">Record name (host).</param>
        /// <param name="type">Record type, for example CNAME.</param>
        /// <param name="value">Record value (may be a token).</param>
        /// <param name="description">What the record is for.</param>
        /// <returns>The output.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null or empty.</exception>
        public static CfnOutput Add(Stack stack, string id, string name, string type, string value, string description)
        {
            ArgumentNullException.ThrowIfNull(stack);
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrWhiteSpace(type)) throw new ArgumentNullException(nameof(type));
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(value));

            return new CfnOutput(stack, RecordPrefix + id, new CfnOutputProps
            {
                Value = name + " " + type + " " + value,
                Description = "Create at your DNS provider: " + (description ?? String.Empty)
            });
        }

        /// <summary>
        /// Add the output of a record that points a host at a target host name (a CloudFront distribution or the load
        /// balancer). A host that is a configured domain itself (the web domain or a vanity domain in production) is
        /// usually the apex of its DNS zone, which cannot hold a CNAME, so its type reads ALIAS.
        /// </summary>
        /// <param name="stack">Stack that owns the output.</param>
        /// <param name="id">Output key suffix.</param>
        /// <param name="config">Environment configuration (decides whether the host is a zone apex).</param>
        /// <param name="host">Record name.</param>
        /// <param name="target">Target host name (token).</param>
        /// <param name="purpose">What the host serves.</param>
        /// <returns>The output.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null or empty.</exception>
        public static CfnOutput AddTarget(Stack stack, string id, EnvironmentConfig config, string host, string target, string purpose)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (IsZoneApex(config, host))
            {
                return Add(stack, id, host, "ALIAS", target, purpose + ". This is the apex of its zone, where a CNAME is not allowed: use your provider's "
                    + "ALIAS, ANAME, or CNAME flattening record (a plain CNAME works only if this name is not the zone apex at your provider).");
            }

            return Add(stack, id, host, "CNAME", target, purpose + ".");
        }

        /// <summary>
        /// Whether a host is one of the property's configured domains (web domain or a vanity domain), which normally
        /// is the apex of a zone at the registrar. Development hosts carry the dev. prefix and never are.
        /// </summary>
        /// <param name="config">Environment configuration.</param>
        /// <param name="host">Host name.</param>
        /// <returns>True for a likely zone apex.</returns>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public static bool IsZoneApex(EnvironmentConfig config, string host)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (String.IsNullOrWhiteSpace(host)) return false;
            if (host == config.Property.WebDomain) return true;
            foreach (string domain in config.Property.RedirectDomains)
            {
                if (host == domain) return true;
            }

            return false;
        }
    }
}
