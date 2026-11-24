namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How members accept a property document (property.json documents[].acceptance; docs/TRELLISKIT.md section 14).
    /// </summary>
    public enum DocumentAcceptanceModeEnum
    {
        /// <summary>
        /// Must be accepted to finish sign-up; with separateCheckbox it has its own required checkbox, otherwise the terms checkbox covers it.
        /// </summary>
        RequiredAtSignup,

        /// <summary>
        /// Accepted together with the terms through the sign-up terms checkbox.
        /// </summary>
        AcceptedWithTerms,

        /// <summary>
        /// Shown and linked on purchase screens; no separate acceptance.
        /// </summary>
        ShownAtPurchase,

        /// <summary>
        /// A public page only; never accepted.
        /// </summary>
        InformationOnly
    }
}
