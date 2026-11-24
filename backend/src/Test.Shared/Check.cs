namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Assertion helpers. Failures throw <see cref="InvalidOperationException"/> with a descriptive message.
    /// </summary>
    public static class Check
    {
        #region Public-Methods

        /// <summary>
        /// Require a condition.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Require equality.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="what">Description of the value.</param>
        /// <exception cref="InvalidOperationException">The values differ.</exception>
        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(what + ": expected <" + expected + "> but was <" + actual + ">.");
        }

        /// <summary>
        /// Require that an action throws a specific exception type.
        /// </summary>
        /// <typeparam name="TException">Expected exception type.</typeparam>
        /// <param name="action">Action.</param>
        /// <param name="what">Description of the operation.</param>
        /// <returns>The exception.</returns>
        /// <exception cref="InvalidOperationException">Nothing, or a different exception, was thrown.</exception>
        public static TException Throws<TException>(Action action, string what) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException e)
            {
                return e;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(what + ": expected " + typeof(TException).Name + " but got " + e.GetType().Name + ": " + e.Message, e);
            }
            throw new InvalidOperationException(what + ": expected " + typeof(TException).Name + " but nothing was thrown.");
        }

        /// <summary>
        /// Require that an asynchronous operation throws a specific exception type.
        /// </summary>
        /// <typeparam name="TException">Expected exception type.</typeparam>
        /// <param name="action">Operation.</param>
        /// <param name="what">Description of the operation.</param>
        /// <returns>The exception.</returns>
        /// <exception cref="InvalidOperationException">Nothing, or a different exception, was thrown.</exception>
        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string what) where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException e)
            {
                return e;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(what + ": expected " + typeof(TException).Name + " but got " + e.GetType().Name + ": " + e.Message, e);
            }
            throw new InvalidOperationException(what + ": expected " + typeof(TException).Name + " but nothing was thrown.");
        }

        #endregion
    }
}
