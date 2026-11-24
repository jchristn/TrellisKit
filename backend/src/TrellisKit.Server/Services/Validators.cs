namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Field validation rules from the Phase 1 contract. Each method returns an error message, or null when valid.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class Validators
    {
        #region Public-Members

        /// <summary>
        /// Minimum password length.
        /// </summary>
        public const int MinPasswordLength = 10;

        /// <summary>
        /// Maximum password length.
        /// </summary>
        public const int MaxPasswordLength = 128;

        #endregion

        #region Private-Members

        private static readonly Regex _Email = new Regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Handle = new Regex("^[A-Za-z0-9_.]{3,30}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> _Countries = new HashSet<string>(
            ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD CF CG CH CI CK CL CM CN " +
            "CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM " +
            "HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK " +
            "ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW " +
            "SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI " +
            "VN VU WF WS YE YT ZA ZM ZW").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize an email address (trim and lower-case), or null when blank.
        /// </summary>
        /// <param name="email">Email.</param>
        /// <returns>Normalized email, or null.</returns>
        public static string? NormalizeEmail(string? email)
        {
            if (String.IsNullOrWhiteSpace(email)) return null;
            return email.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Validate an email address.
        /// </summary>
        /// <param name="email">Email.</param>
        /// <returns>Error, or null.</returns>
        public static string? Email(string? email)
        {
            if (String.IsNullOrWhiteSpace(email)) return "Enter your email address.";
            string trimmed = email.Trim();
            if (trimmed.Length > 254 || !_Email.IsMatch(trimmed)) return "Enter a valid email address.";
            return null;
        }

        /// <summary>
        /// Validate a password: 10 to 128 characters, not equal to the email.
        /// </summary>
        /// <param name="password">Password.</param>
        /// <param name="email">Account email, or null.</param>
        /// <returns>Error, or null.</returns>
        public static string? Password(string? password, string? email)
        {
            if (String.IsNullOrEmpty(password)) return "Enter a password.";
            if (password.Length < MinPasswordLength) return "Use at least " + MinPasswordLength + " characters.";
            if (password.Length > MaxPasswordLength) return "Use at most " + MaxPasswordLength + " characters.";
            if (email != null && String.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase)) return "Your password cannot be your email address.";
            return null;
        }

        /// <summary>
        /// Validate a display name (2 to 60 characters).
        /// </summary>
        /// <param name="displayName">Display name.</param>
        /// <returns>Error, or null.</returns>
        public static string? DisplayName(string? displayName)
        {
            int length = displayName?.Trim().Length ?? 0;
            if (length < 2 || length > 60) return "Use 2 to 60 characters.";
            return null;
        }

        /// <summary>
        /// Validate a handle (3 to 30 of A-Z a-z 0-9 _ and period).
        /// </summary>
        /// <param name="handle">Handle.</param>
        /// <returns>Error, or null.</returns>
        public static string? Handle(string? handle)
        {
            if (String.IsNullOrWhiteSpace(handle) || !_Handle.IsMatch(handle.Trim())) return "Use 3 to 30 letters, numbers, underscores, or periods.";
            return null;
        }

        /// <summary>
        /// Validate optional text length.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="max">Maximum characters.</param>
        /// <returns>Error, or null.</returns>
        public static string? MaxLength(string? value, int max)
        {
            if (value != null && value.Length > max) return "Use at most " + max + " characters.";
            return null;
        }

        /// <summary>
        /// Validate an ISO 3166-1 alpha-2 country code.
        /// </summary>
        /// <param name="country">Code.</param>
        /// <returns>Error, or null.</returns>
        public static string? Country(string? country)
        {
            if (country == null) return null;
            if (!_Countries.Contains(country.Trim().ToUpperInvariant())) return "Use a two-letter ISO 3166-1 country code, for example US.";
            return null;
        }

        /// <summary>
        /// Parse a YYYY-MM-DD date.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <param name="date">Parsed date.</param>
        /// <returns>True when valid.</returns>
        public static bool TryParseDate(string? value, out DateTime date)
        {
            return DateTime.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
        }

        /// <summary>
        /// Validate a date of birth (YYYY-MM-DD, in the past, plausible).
        /// </summary>
        /// <param name="value">Text.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="dateOfBirth">Parsed date.</param>
        /// <returns>Error, or null.</returns>
        public static string? DateOfBirth(string? value, DateTime nowUtc, out DateTime dateOfBirth)
        {
            if (!TryParseDate(value, out dateOfBirth)) return "Enter your date of birth as YYYY-MM-DD.";
            if (dateOfBirth.Date > nowUtc.Date || dateOfBirth.Year < nowUtc.Year - 130) return "Enter a valid date of birth.";
            return null;
        }

        /// <summary>
        /// Whether a date of birth is at least the minimum age before today.
        /// </summary>
        /// <param name="dateOfBirth">Date of birth.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="minimumAge">Minimum age in years (the property membership.minimumAge). Minimum 0.</param>
        /// <returns>True when old enough.</returns>
        public static bool IsAdult(DateTime dateOfBirth, DateTime nowUtc, int minimumAge)
        {
            return dateOfBirth.Date.AddYears(Math.Max(0, minimumAge)) <= nowUtc.Date;
        }

        #endregion
    }
}
