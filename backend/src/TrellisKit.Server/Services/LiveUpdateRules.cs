namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;

    /// <summary>
    /// Shared live update constants and pure helpers: platforms, built-in channels, percentages in basis points,
    /// deterministic rollout buckets, hashes, and payload mapping.
    /// Thread safety: stateless.
    /// </summary>
    public static class LiveUpdateRules
    {
        #region Public-Members

        /// <summary>
        /// Supported platforms.
        /// </summary>
        public static readonly IReadOnlyList<string> Platforms = new List<string> { "ios", "android" };

        /// <summary>
        /// Built-in channels of every property.
        /// </summary>
        public static readonly IReadOnlyList<string> BuiltInChannels = new List<string> { "production", "preview", "development" };

        /// <summary>
        /// The development channel: its published config has no API URL, so it is never promoted.
        /// </summary>
        public const string DevelopmentChannel = "development";

        /// <summary>
        /// The production channel (deploy keys never publish to it).
        /// </summary>
        public const string ProductionChannel = "production";

        /// <summary>
        /// Custom channel names.
        /// </summary>
        public static readonly Regex ChannelPattern = new Regex("^[a-z][a-z0-9-]{1,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// SHA-256 hex.
        /// </summary>
        public static readonly Regex Sha256HexPattern = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// MD5 hex (Expo asset keys).
        /// </summary>
        public static readonly Regex Md5HexPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Git commits.
        /// </summary>
        public static readonly Regex GitCommitPattern = new Regex("^[0-9a-fA-F]{7,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// UUIDs.
        /// </summary>
        public static readonly Regex UuidPattern = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a platform is supported.
        /// </summary>
        /// <param name="platform">Value.</param>
        /// <returns>True for ios and android.</returns>
        public static bool IsPlatform(string? platform)
        {
            return platform == "ios" || platform == "android";
        }

        /// <summary>
        /// A percentage (0 to 100, two decimals) in basis points, or -1 when out of range.
        /// </summary>
        /// <param name="percentage">Percentage.</param>
        /// <returns>0 to 10000, or -1.</returns>
        public static int ToBasisPoints(decimal percentage)
        {
            if (percentage < 0 || percentage > 100) return -1;
            decimal scaled = percentage * 100m;
            if (scaled != Math.Truncate(scaled)) return -1;
            return (int)scaled;
        }

        /// <summary>
        /// Basis points as a percentage.
        /// </summary>
        /// <param name="basisPoints">Basis points.</param>
        /// <returns>Percentage.</returns>
        public static decimal ToPercent(int basisPoints)
        {
            return basisPoints / 100m;
        }

        /// <summary>
        /// Deterministic rollout bucket 0 to 9999: the first 4 bytes of SHA-256("&lt;scope&gt;:&lt;subject&gt;") as an
        /// unsigned big-endian integer, mod 10000 (sections 7.1 and 12.3).
        /// </summary>
        /// <param name="scope">Group id, or flagKey:ruleId.</param>
        /// <param name="subject">Install, visitor, or member id.</param>
        /// <returns>Bucket.</returns>
        public static int Bucket(string scope, string subject)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes((scope ?? String.Empty) + ":" + (subject ?? String.Empty)));
            uint value = ((uint)hash[0] << 24) | ((uint)hash[1] << 16) | ((uint)hash[2] << 8) | hash[3];
            return (int)(value % 10000u);
        }

        /// <summary>
        /// Whether an install is inside a rollout. An install without an id is inside only 100 percent rollouts.
        /// </summary>
        /// <param name="groupId">Group id.</param>
        /// <param name="installId">Install id, or null.</param>
        /// <param name="basisPoints">Rollout in basis points.</param>
        /// <returns>True when inside.</returns>
        public static bool InRollout(string groupId, string? installId, int basisPoints)
        {
            if (basisPoints >= 10000) return true;
            if (String.IsNullOrEmpty(installId) || basisPoints <= 0) return false;
            return Bucket(groupId, installId) < basisPoints;
        }

        /// <summary>
        /// SHA-256 hex of a base64url (no padding) hash, or null when malformed.
        /// </summary>
        /// <param name="hash">Base64url SHA-256.</param>
        /// <returns>Lowercase hex, or null.</returns>
        public static string? HexOfBase64Url(string? hash)
        {
            if (String.IsNullOrEmpty(hash) || hash.Length != 43) return null;
            try
            {
                string padded = hash.Replace('-', '+').Replace('_', '/') + "=";
                byte[] bytes = Convert.FromBase64String(padded);
                return bytes.Length == 32 ? Convert.ToHexString(bytes).ToLowerInvariant() : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Base64url (no padding) of bytes.
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        /// <returns>Text.</returns>
        public static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Manifest createdAt text (millisecond precision, UTC).
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>For example 2026-12-01T17:00:00.000Z.</returns>
        public static string Iso(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Truncate to whole milliseconds.
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>Truncated UTC time.</returns>
        public static DateTime Milliseconds(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
            return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
        }

        /// <summary>
        /// Creator of an action from the request context.
        /// </summary>
        /// <param name="context">Request context, or null for the server.</param>
        /// <returns>Creator.</returns>
        public static UpdateCreatorView CreatorOf(RequestContext? context)
        {
            if (context == null) return new UpdateCreatorView { Type = UpdateCreatorTypeEnum.System, Name = "system" };
            if (!String.IsNullOrEmpty(context.DeployKeyId)) return new UpdateCreatorView { Type = UpdateCreatorTypeEnum.DeployKey, Id = context.DeployKeyId, Name = context.PrincipalName ?? "deploy key" };
            if (!String.IsNullOrEmpty(context.UserId)) return new UpdateCreatorView { Type = UpdateCreatorTypeEnum.Member, Id = context.UserId, Name = context.PrincipalName ?? "member" };
            return new UpdateCreatorView { Type = UpdateCreatorTypeEnum.AdminToken, Id = context.PlatformAdminId, Name = context.PrincipalName ?? "admin" };
        }

        /// <summary>
        /// Telemetry credential label.
        /// </summary>
        /// <param name="context">Request context.</param>
        /// <returns>member, admin_token, or deploy_key.</returns>
        public static string CredentialLabel(RequestContext? context)
        {
            UpdateCreatorView creator = CreatorOf(context);
            return creator.Type == UpdateCreatorTypeEnum.DeployKey ? "deploy_key" : creator.Type == UpdateCreatorTypeEnum.Member ? "member" : "admin_token";
        }

        /// <summary>
        /// Payload of one update.
        /// </summary>
        /// <param name="update">Update.</param>
        /// <returns>View.</returns>
        public static UpdateView ViewOf(UpdateRecord update)
        {
            ArgumentNullException.ThrowIfNull(update);
            return new UpdateView
            {
                Id = update.Id,
                Uuid = update.Uuid,
                Platform = update.Platform,
                RuntimeVersion = update.RuntimeVersion,
                Status = update.Status,
                CreatedAt = update.CreatedAt,
                LaunchAssetSizeBytes = update.LaunchAssetSizeBytes,
                AssetCount = update.AssetCount,
                TotalBytes = update.TotalBytes,
                RolloutPercentage = ToPercent(update.RolloutBasisPoints)
            };
        }

        #endregion
    }
}
