namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>Bounded profile multimedia enumeration.</summary>
    public static class ProfileMediaCollections
    {
        /// <summary>Profile photos and videos, with labels and tags.</summary>
        public static readonly EnumerationCollection Items = new EnumerationCollection("profile-media", TargetTypeEnum.ProfileMedia, "m.id", "m.createdutc", false)
            .Filters("kind").DefaultTo("CreatedDescending");
    }
}
