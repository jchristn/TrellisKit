namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Issues the media grant cookies (contract section 4.7).
    /// Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface IMediaDeliverySigner
    {
        /// <summary>
        /// Delivery mode of the cookies.
        /// </summary>
        MediaDeliveryModeEnum Mode { get; }

        /// <summary>
        /// Create the grant cookies.
        /// </summary>
        /// <param name="userId">Member identifier (or a fixed name for the break-glass token).</param>
        /// <param name="expiresUtc">Grant expiry.</param>
        /// <param name="insecureLoopback">Whether the request came over plain http to a loopback host (Secure is then omitted in Local mode).</param>
        /// <returns>Cookies.</returns>
        /// <exception cref="ArgumentNullException">The user identifier is null or empty.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Signing is not configured.</exception>
        List<MediaCookie> CreateCookies(string userId, DateTime expiresUtc, bool insecureLoopback);
    }
}
