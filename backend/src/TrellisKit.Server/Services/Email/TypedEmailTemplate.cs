namespace TrellisKit.Server.Services.Email
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A template whose callers pass a typed context instead of a token dictionary (the pattern of a code-defined template
    /// with its own context class). <see cref="TokensFor"/> flattens the context into tokens, which are what the queue
    /// stores and what <see cref="EmailTemplate.Compose"/> reads, so a queued message renders the same on every retry.
    /// Send with <see cref="EmailService.SendAsync{TContext}"/> after registering the template with
    /// <see cref="EmailTemplateCatalog.Register"/>.
    /// </summary>
    /// <typeparam name="TContext">Context type.</typeparam>
    public abstract class TypedEmailTemplate<TContext> : EmailTemplate
    {
        #region Public-Methods

        /// <summary>
        /// Token values for a context. Keys must be tokens of this template (<see cref="EmailTemplate.Tokens"/>).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <returns>Token values.</returns>
        public abstract Dictionary<string, string> TokensFor(TContext context);

        #endregion
    }
}
