namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Validation helpers used by configuration setters.
    /// Thread safety: stateless and safe to call from any thread.
    /// </summary>
    public static class Guard
    {
        /// <summary>
        /// Return the value when it lies within the inclusive range, otherwise throw.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <param name="minimum">Inclusive minimum.</param>
        /// <param name="maximum">Inclusive maximum.</param>
        /// <param name="name">Name of the setting, used in the error message.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is outside the range.</exception>
        public static int Range(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(name, value, name + " must be between " + minimum + " and " + maximum + ".");
            }

            return value;
        }

        /// <summary>
        /// Return the value when it lies within the inclusive range, otherwise throw.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <param name="minimum">Inclusive minimum.</param>
        /// <param name="maximum">Inclusive maximum.</param>
        /// <param name="name">Name of the setting, used in the error message.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is outside the range.</exception>
        public static double Range(double value, double minimum, double maximum, string name)
        {
            if (Double.IsNaN(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(name, value, name + " must be between " + minimum + " and " + maximum + ".");
            }

            return value;
        }

        /// <summary>
        /// Return the string when it is not null, empty, or whitespace, otherwise throw.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <param name="name">Name of the setting, used in the error message.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentNullException">The value is null, empty, or whitespace.</exception>
        public static string NotEmpty(string? value, string name)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentNullException(name, name + " must not be empty.");
            }

            return value;
        }

        /// <summary>
        /// Return the list when it is not null, otherwise throw.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <param name="name">Name of the setting, used in the error message.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public static List<string> NotNull(List<string>? value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name, name + " must not be null.");
            }

            return value;
        }
    }
}
