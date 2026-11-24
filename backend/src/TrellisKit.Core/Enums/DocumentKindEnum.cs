namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What a property document is (property.json documents[].kind; docs/TRELLISKIT.md section 14).
    /// </summary>
    public enum DocumentKindEnum
    {
        /// <summary>
        /// Terms of service or terms of use.
        /// </summary>
        Terms,

        /// <summary>
        /// Privacy policy.
        /// </summary>
        Privacy,

        /// <summary>
        /// Community guidelines or conduct rules.
        /// </summary>
        Guidelines,

        /// <summary>
        /// Refund policy.
        /// </summary>
        Refund,

        /// <summary>
        /// Subscription or billing terms.
        /// </summary>
        Subscription,

        /// <summary>
        /// Assumption of risk, release, waiver, or participation agreement.
        /// </summary>
        Waiver,

        /// <summary>
        /// How to delete an account (an informational page the app stores require).
        /// </summary>
        AccountDeletion,

        /// <summary>
        /// Any other document.
        /// </summary>
        Other
    }
}
