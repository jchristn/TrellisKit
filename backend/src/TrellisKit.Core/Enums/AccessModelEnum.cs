namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a property charges and what it gates (docs/TRELLISKIT.md section 7, docs/ACCESS_MODELS_CONTRACT.md).
    /// </summary>
    public enum AccessModelEnum
    {
        /// <summary>
        /// No payments: purchase, paywall, and sign-up code routes are not available and everything needs only a
        /// signed-in, age-attested account. A stored Members audience is treated as SignedIn.
        /// </summary>
        Open,

        /// <summary>
        /// Optional membership: features listed in gatedFeatures need a membership to write in, and each object's
        /// audience (Public, SignedIn, Members) decides who reads it.
        /// </summary>
        PartiallyGated,

        /// <summary>
        /// Required membership (the original behaviour): every content route needs an active membership. A stored
        /// SignedIn audience is treated as Members.
        /// </summary>
        FullyGated
    }
}
