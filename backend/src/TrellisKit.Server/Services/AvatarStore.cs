namespace TrellisKit.Server.Services
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Disk object store for avatars under {storage.directory}/{propertyId}/avatars (every storage key is prefixed with
    /// the property id). Validates image types by magic bytes.
    /// Thread safety: safe to call concurrently; each upload writes a new, uniquely named file.
    /// </summary>
    public class AvatarStore
    {
        #region Private-Members

        private static readonly Regex _FileName = new Regex("^usr_[A-Za-z0-9_-]+_[0-9]+\\.(jpg|png|webp)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly StorageSettings _Settings;
        private readonly string _PropertyId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the store.
        /// </summary>
        /// <param name="settings">Storage settings.</param>
        /// <param name="propertyId">Owning property id.</param>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        /// <exception cref="ArgumentException">The property id is not a valid property id.</exception>
        public AvatarStore(StorageSettings settings, string propertyId)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _PropertyId = MediaKeys.Prefix(propertyId).TrimEnd('/');
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Detect an image type from its first bytes.
        /// </summary>
        /// <param name="data">Image bytes.</param>
        /// <returns>File extension (jpg, png, or webp), or null when the data is not one of those formats.</returns>
        public static string? SniffExtension(byte[] data)
        {
            if (data == null || data.Length < 12) return null;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "jpg";
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return "png";
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return "webp";
            return null;
        }

        /// <summary>
        /// Content type for an extension.
        /// </summary>
        /// <param name="extension">jpg, png, or webp.</param>
        /// <returns>MIME type.</returns>
        public static string ContentTypeFor(string extension)
        {
            switch (extension)
            {
                case "png":
                    return "image/png";
                case "webp":
                    return "image/webp";
                default:
                    return "image/jpeg";
            }
        }

        /// <summary>
        /// Whether a name is a valid stored avatar file name (prevents path traversal).
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidFileName(string? fileName)
        {
            return !String.IsNullOrEmpty(fileName) && _FileName.IsMatch(fileName);
        }

        /// <summary>
        /// Save an avatar.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="data">Image bytes.</param>
        /// <param name="extension">Extension from <see cref="SniffExtension"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stored file name.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<string> SaveAsync(string userId, byte[] data, string extension, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            ArgumentNullException.ThrowIfNull(data);
            Directory.CreateDirectory(AvatarDirectory());
            string name = userId + "_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "." + extension;
            string path = Path.Combine(AvatarDirectory(), name);
            string temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, data, token).ConfigureAwait(false);
            File.Move(temp, path, true);
            return name;
        }

        /// <summary>
        /// Full path of a stored avatar, or null when the name is invalid or the file does not exist.
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <returns>Path, or null.</returns>
        public string? Resolve(string? fileName)
        {
            if (!IsValidFileName(fileName)) return null;
            string path = Path.Combine(AvatarDirectory(), fileName!);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Delete a stored avatar. Missing files are ignored.
        /// </summary>
        /// <param name="fileName">File name, or null.</param>
        public void Delete(string? fileName)
        {
            string? path = Resolve(fileName);
            if (path == null) return;
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A stale avatar file is harmless; it is no longer referenced.
            }
        }

        #endregion

        #region Private-Methods

        private string AvatarDirectory()
        {
            return Path.Combine(Path.GetFullPath(_Settings.Directory), _PropertyId, "avatars");
        }

        #endregion
    }
}
