namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// One part of a multipart/mixed manifest response (manifest, extensions, directive, or certificate_chain).
    /// </summary>
    internal sealed class UpdateManifestPart
    {
        internal string Name { get; }

        internal string ContentType { get; }

        internal byte[] Body { get; }

        internal string? Signature { get; }

        internal UpdateManifestPart(string name, string contentType, byte[] body, string? signature)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
            Body = body ?? throw new ArgumentNullException(nameof(body));
            Signature = signature;
        }
    }
}
