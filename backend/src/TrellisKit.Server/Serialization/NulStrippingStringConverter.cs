namespace TrellisKit.Server.Serialization
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads JSON strings with NUL characters removed. Storage cannot hold NUL, so stripping it on the way in makes every
    /// response echo exactly what is stored. Writes strings unchanged.
    /// Thread safety: stateless; safe to share.
    /// </summary>
    public class NulStrippingStringConverter : JsonConverter<string>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (value == null || value.IndexOf('\0') < 0) return value;
            return value.Replace("\0", String.Empty, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value);
        }

        #endregion
    }
}
