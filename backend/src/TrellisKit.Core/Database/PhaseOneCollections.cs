namespace TrellisKit.Core.Database
{
    using System;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the Phase 0 and Phase 1 lists (docs/ENUMERATION_AND_METADATA.md section 4). Each list
    /// is defined once here and used by the matching database method and service. Table aliases: users u,
    /// subscriptions s, subscriptionevents se, signupcodes c, signupcoderedemptions r, entitlements en, authsessions a,
    /// auditrecords ar, requesthistory rh.
    /// </summary>
    public static class PhaseOneCollections
    {
        #region Public-Members

        /// <summary>
        /// Filter names of the request history list (its time window is the standard createdAfterUtc and createdBeforeUtc).
        /// </summary>
        public static readonly string[] RequestHistoryFilters = new[] { "method", "statusCode", "pathContains", "userId", "routeTemplate", "statusClass", "errorCode", "clientApp", "clientPlatform", "clientVersion" };

        /// <summary>
        /// GET /api/v1/admin/members: search over displayName, handle, and email; filters membership (Active, Inactive,
        /// Paid, Free), rank, role, banned. LastSeenDescending puts members who were never seen last.
        /// </summary>
        public static readonly EnumerationCollection AdminMembers = new EnumerationCollection("admin-members", TargetTypeEnum.User, "u.id", "u.createdutc", true)
            .Order("DisplayNameAscending", "lower(u.displayname)", false, EnumerationKeyKindEnum.Text)
            .Order("DisplayNameDescending", "lower(u.displayname)", true, EnumerationKeyKindEnum.Text)
            .Order("LastSeenDescending", "COALESCE(u.lastseenutc, TIMESTAMPTZ '1970-01-01 00:00:00+00')", true, EnumerationKeyKindEnum.Time)
            .Search("u.displayname", "u.handle", "u.email")
            .Filters("membership", "rank", "role", "banned", "profile");

        /// <summary>
        /// GET /api/v1/admin/subscriptions and a member's subscriptions: search over member name, member email, and the
        /// store subscription id; filters store, status, userId.
        /// </summary>
        public static readonly EnumerationCollection Subscriptions = new EnumerationCollection("admin-subscriptions", TargetTypeEnum.Subscription, "s.id", "s.createdutc", true)
            .Order("CurrentPeriodEndDescending", "COALESCE(s.currentperiodendutc, s.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Order("LastEventDescending", "COALESCE(s.lasteventutc, s.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("s.storesubscriptionid", "u.displayname", "u.email")
            .Filters("store", "status", "userId");

        /// <summary>
        /// GET /api/v1/admin/subscriptions/{id}/events (created = received time); filter outcome.
        /// </summary>
        public static readonly EnumerationCollection SubscriptionEvents = new EnumerationCollection("admin-subscription-events", null, "se.id", "se.receivedutc", true)
            .Search("se.eventtype")
            .Filters("outcome");

        /// <summary>
        /// GET /api/v1/admin/signup-codes: search over code and name; filters status, batchId.
        /// </summary>
        public static readonly EnumerationCollection SignupCodes = new EnumerationCollection("admin-signup-codes", TargetTypeEnum.SignupCode, "c.id", "c.createdutc", true)
            .Order("CodeAscending", "c.code", false, EnumerationKeyKindEnum.Text)
            .Order("NameAscending", "lower(c.name)", false, EnumerationKeyKindEnum.Text)
            .Order("RedemptionCountDescending", "c.redemptioncount", true, EnumerationKeyKindEnum.Number)
            .Order("LastRedeemedDescending", "COALESCE(c.lastredeemedutc, '1970-01-01T00:00:00.000Z')", true, EnumerationKeyKindEnum.Time)
            .Order("ExpiresAscending", "COALESCE(c.expiresutc, '9999-12-31T23:59:59.000Z')", false, EnumerationKeyKindEnum.Time)
            .Search("c.code", "c.name")
            .Filters("status", "batchId");

        /// <summary>
        /// A sign-up code's redemptions and a member's redemptions (created = redeemed time): search over member name and
        /// email; filter status (Active, Expired, Revoked).
        /// </summary>
        public static readonly EnumerationCollection Redemptions = new EnumerationCollection("admin-redemptions", TargetTypeEnum.SignupCodeRedemption, "r.id", "r.redeemedutc", true)
            .Order("RedeemedDescending", "r.redeemedutc", true, EnumerationKeyKindEnum.Time)
            .Order("MembershipEndsDescending", "COALESCE(r.membershipendsutc, '9999-12-31T23:59:59.000Z')", true, EnumerationKeyKindEnum.Time)
            .Order("MembershipEndsAscending", "COALESCE(r.membershipendsutc, '9999-12-31T23:59:59.000Z')", false, EnumerationKeyKindEnum.Time)
            .Search("u.displayname", "u.email")
            .Filters("status");

        /// <summary>
        /// A member's entitlements for staff (comps and membership history): filters source, status.
        /// </summary>
        public static readonly EnumerationCollection AdminEntitlements = new EnumerationCollection("admin-entitlements", TargetTypeEnum.Entitlement, "en.id", "en.createdutc", true)
            .Order("StartsDescending", "en.startsutc", true, EnumerationKeyKindEnum.Time)
            .Order("StartsAscending", "en.startsutc", false, EnumerationKeyKindEnum.Time)
            .Search("en.reason")
            .Filters("source", "status");

        /// <summary>
        /// GET /api/v1/membership/history (the caller's own entitlements, member limits): filters source, status.
        /// </summary>
        public static readonly EnumerationCollection MembershipHistory = new EnumerationCollection("membership-history", TargetTypeEnum.Entitlement, "en.id", "en.createdutc", false)
            .Order("StartsDescending", "en.startsutc", true, EnumerationKeyKindEnum.Time)
            .Order("StartsAscending", "en.startsutc", false, EnumerationKeyKindEnum.Time)
            .Filters("source", "status")
            .DefaultTo("StartsDescending");

        /// <summary>
        /// A member's sessions for staff: active sessions unless the includeInactive filter is true.
        /// </summary>
        public static readonly EnumerationCollection Sessions = new EnumerationCollection("admin-sessions", TargetTypeEnum.Session, "a.id", "a.createdutc", true)
            .Order("LastUsedDescending", "COALESCE(a.lastusedutc, a.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("a.useragent", "a.sourceip")
            .Filters("includeInactive");

        /// <summary>
        /// GET /api/v1/admin/audit: search over action, actor name, and summary; filters actorUserId, targetType,
        /// targetId, action; the time window is the standard createdAfterUtc and createdBeforeUtc.
        /// </summary>
        public static readonly EnumerationCollection Audit = new EnumerationCollection("admin-audit", TargetTypeEnum.AuditRecord, "ar.id", "ar.createdutc", true)
            .Search("ar.action", "ar.actorname", "ar.summary")
            .Filters("actorUserId", "targetType", "targetId", "action");

        /// <summary>
        /// GET /api/v1/request-history: search over path and principal name; filters method, statusCode, pathContains,
        /// userId; the time window is the standard createdAfterUtc and createdBeforeUtc.
        /// </summary>
        public static readonly EnumerationCollection RequestHistory = new EnumerationCollection("request-history", TargetTypeEnum.RequestHistory, "rh.id", "rh.createdutc", true)
            .Search("rh.path", "rh.principalname")
            .Filters(RequestHistoryFilters);

        #endregion
    }
}
