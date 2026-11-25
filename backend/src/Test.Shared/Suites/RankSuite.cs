namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Verified ranks (docs/TRELLISKIT.md, Ranks, curricula, and attendance): self-reported versus verified, award
    /// validation, the rank.awarding rules matrix, per-option levels, the profile field lock, revoke, eligibility from
    /// attendance and time in grade, profile visibility, lineage, audit and notifications, role grants, export and
    /// deletion, and property isolation.
    /// </summary>
    public static class RankSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Ranks";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Verified ranks (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Rules.Matrix", "rank.awarding: strictly lower options, own option with allowEqualOption, own-option levels with allowEqualOptionLevels, optionAwarderMinimum, never above the awarder's level, no verified rank, SystemAdmin exempt", RulesMatrixAsync),
                SuiteCases.Unit(Id, "Rules.NextAndLabels", "Next rank walks levels up to the option's maxLevel, then the next non-retired option; labels add the level label", NextAsync),
                SuiteCases.Unit(Id, "Config.Validation", "options[].maxLevel must be 0 to rank.maxLevel and rank.awarding.optionAwarderMinimum must name an option", ConfigValidationAsync),
                SuiteCases.Http(Id, "Ranks.Disabled", "ranks.enabled false (default) answers 404 NotAvailable on every rank route and leaves the profile field editable", DisabledAsync),
                SuiteCases.Http(Id, "Ranks.SelfReportedVersusVerified", "Self-reported until awarded; the award sets the profile value, locks the field (409 RankVerified, unchanged values pass), notifies, and audits; revoke unlocks it as self-reported", VerifiedAsync),
                SuiteCases.Http(Id, "Ranks.AwardValidation", "Unknown option, retired option, level out of range (per-option maxLevel), self award, missing role, unknown member, unknown event, above own rank", ValidationAsync),
                SuiteCases.Http(Id, "Ranks.AwardingRulesHttp", "Configured rank.awarding rules apply to RankAwarder holders over HTTP and SystemAdmin is exempt", RulesHttpAsync),
                SuiteCases.Http(Id, "Ranks.Revoke", "Revoke needs a reason, answers 409 AlreadyRevoked twice, records a revoke row, and the awards list filters by status and action", RevokeAsync),
                SuiteCases.Http(Id, "Ranks.Eligibility", "Requirements (SystemAdmin only) drive eligibility from attendance and time in grade; eligibleOnly filters; pages are bounded", EligibilityAsync),
                SuiteCases.Http(Id, "Ranks.Visibility", "Member rank reads follow the field's profile visibility, hide blocked and unknown members, and show the verified flag", VisibilityAsync),
                SuiteCases.Http(Id, "Ranks.Lineage", "Lineage entries are validated and word filtered, notify a member promoter, keep verification when unchanged, are verified only by the promoter or a SystemAdmin, and reach the content changelog with promotion notes", LineageAsync),
                SuiteCases.Http(Id, "Ranks.RoleGrants", "RankAwarder is granted like LibraryEditor (moderators route with resourceType Ranks, or member roles) and listed with the staff grants", RoleGrantsAsync),
                SuiteCases.Http(Id, "Ranks.ExportAndDeletion", "Account export carries the rank sections and account deletion erases ranks, promotions, lineage, curriculum progress, and attendance", ExportAndDeletionAsync),
                SuiteCases.Http(Id, "Ranks.PropertyIsolation", "Rank, curriculum, and attendance rows of another property are never read", IsolationAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static ProfileField Field(ProfileRankAwarding? awarding = null)
        {
            ProfileField field = new ProfileField { Key = "rank", Label = "Level", Type = ProfileFieldTypeEnum.Rank, Rank = new ProfileRankSettings { LevelLabel = "Stars", MaxLevel = 3, Awarding = awarding } };
            field.Options.Add(new ProfileFieldOption { Key = "Beginner", Label = "Beginner", Color = "#999999", MaxLevel = 1 });
            field.Options.Add(new ProfileFieldOption { Key = "Intermediate", Label = "Intermediate", Color = "#1F5FBF" });
            field.Options.Add(new ProfileFieldOption { Key = "Advanced", Label = "Advanced", Color = "#111111" });
            return field;
        }

        private static MemberRank Verified(string option, int level)
        {
            return new MemberRank { OptionKey = option, Level = level, Status = RankStatusEnum.Verified };
        }

        private static Task RulesMatrixAsync(CancellationToken ct)
        {
            ProfileField defaults = Field();
            MemberRank intermediate2 = Verified("Intermediate", 2);
            MemberRank holderOfIntermediate = Verified("Intermediate", 0);
            MemberRank beginner = Verified("Beginner", 1);
            Check.True(RankAwardRules.Allows(defaults, false, intermediate2, null, "Beginner", 1), "Strictly lower option");
            Check.True(!RankAwardRules.Allows(defaults, false, intermediate2, beginner, "Intermediate", 0), "Own option refused by default (allowEqualOption false)");
            Check.True(RankAwardRules.Allows(defaults, false, intermediate2, holderOfIntermediate, "Intermediate", 1), "Own-option levels for a holder (allowEqualOptionLevels default true)");
            Check.True(RankAwardRules.Allows(defaults, false, intermediate2, holderOfIntermediate, "Intermediate", 2), "Own level allowed");
            Check.True(!RankAwardRules.Allows(defaults, false, intermediate2, holderOfIntermediate, "Intermediate", 3), "Never above the awarder's own level");
            Check.True(!RankAwardRules.Allows(defaults, false, intermediate2, null, "Advanced", 0), "Higher option refused");
            Check.True(!RankAwardRules.Allows(defaults, false, null, null, "Beginner", 0), "No verified rank: refused");
            Check.True(!RankAwardRules.Allows(defaults, false, new MemberRank { OptionKey = "Advanced", Level = 3, Status = RankStatusEnum.Revoked }, null, "Beginner", 0), "Revoked rank: refused");
            Check.True(!RankAwardRules.Allows(defaults, false, intermediate2, null, "Unknown", 0), "Unknown option refused");
            Check.True(RankAwardRules.Allows(defaults, true, null, null, "Advanced", 3), "SystemAdmin exempt");

            ProfileField equal = Field(new ProfileRankAwarding { AllowEqualOption = true });
            Check.True(RankAwardRules.Allows(equal, false, intermediate2, beginner, "Intermediate", 0), "allowEqualOption lets an awarder promote into their own option");
            Check.True(!RankAwardRules.Allows(equal, false, intermediate2, beginner, "Intermediate", 3), "allowEqualOption still caps at the awarder's level");

            ProfileField noLevels = Field(new ProfileRankAwarding { AllowEqualOptionLevels = false });
            Check.True(!RankAwardRules.Allows(noLevels, false, intermediate2, holderOfIntermediate, "Intermediate", 1), "allowEqualOptionLevels false refuses own-option levels");
            Check.True(RankAwardRules.Allows(noLevels, false, intermediate2, null, "Beginner", 0), "Lower options stay allowed");

            ProfileField minimum = Field(new ProfileRankAwarding { OptionAwarderMinimum = "Advanced" });
            Check.True(!RankAwardRules.Allows(minimum, false, intermediate2, null, "Beginner", 0), "optionAwarderMinimum refuses awarders below it");
            Check.True(RankAwardRules.Allows(minimum, false, Verified("Advanced", 0), null, "Intermediate", 2), "An awarder at the minimum awards lower options");
            Check.True(RankAwardRules.Allows(minimum, true, null, null, "Advanced", 3), "SystemAdmin exempt from the minimum");
            return Task.CompletedTask;
        }

        private static Task NextAsync(CancellationToken ct)
        {
            ProfileField field = Field();
            ProfileFieldOption? next = RankAwardRules.Next(field, "Beginner", 0, out int level);
            Check.Equal("Beginner", next?.Key, "Next level within the option");
            Check.Equal(1, level, "Level 1");
            next = RankAwardRules.Next(field, "Beginner", 1, out level);
            Check.Equal("Intermediate", next?.Key, "Per-option maxLevel 1 moves to the next option");
            Check.Equal(0, level, "Level 0 of the next option");
            next = RankAwardRules.Next(field, "Intermediate", 3, out level);
            Check.Equal("Advanced", next?.Key, "rank.maxLevel applies without an option maxLevel");
            field.Options[2].Retired = true;
            Check.True(RankAwardRules.Next(field, "Intermediate", 3, out level) == null, "Retired options are skipped (highest rank)");
            Check.Equal(1, RankAwardRules.MaxLevelOf(field, field.Options[0]), "Option maxLevel");
            Check.Equal(3, RankAwardRules.MaxLevelOf(field, field.Options[1]), "Field maxLevel");
            Check.Equal("Intermediate, 2 Stars", RankAwardRules.Label(field, "intermediate", 2), "Label with the level label");
            Check.Equal("Beginner", RankAwardRules.Label(field, "Beginner", 0), "Level 0 has no level text");
            return Task.CompletedTask;
        }

        private static Task ConfigValidationAsync(CancellationToken ct)
        {
            ProfileField ok = Field(new ProfileRankAwarding { OptionAwarderMinimum = "Advanced" });
            Check.Equal(0, ProfileFieldRules.Validate(ok, new List<ProfileField>(), "").Count, "Valid rank field: " + String.Join("; ", ProfileFieldRules.Validate(ok, new List<ProfileField>(), "").Select(e => e.Key + "=" + e.Value)));
            ProfileField over = Field();
            over.Options[1].MaxLevel = 4;
            Dictionary<string, string> errors = ProfileFieldRules.Validate(over, new List<ProfileField>(), "profileFields[0].");
            Check.True(errors.ContainsKey("profileFields[0].options[1].maxLevel"), "Option maxLevel above rank.maxLevel is refused");
            ProfileField unknown = Field(new ProfileRankAwarding { OptionAwarderMinimum = "Expert" });
            errors = ProfileFieldRules.Validate(unknown, new List<ProfileField>(), "");
            Check.True(errors.ContainsKey("rank.awarding.optionAwarderMinimum"), "Awarding keys must name options");
            ProfileField copy = ProfileFieldRules.Copy(ok);
            Check.Equal("Advanced", copy.Rank!.Awarding!.OptionAwarderMinimum, "Copy keeps the awarding rules");
            return Task.CompletedTask;
        }

        private static async Task DisabledAsync(HttpFixture f, CancellationToken ct)
        {
            ContentTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/me/rank", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members/" + member.Id + "/rank", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, RankTestKit.Awards, HttpFixture.AdminToken, new { userId = member.Id, optionKey = "Beginner" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, RankTestKit.Awards + "/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/lineage", member.AccessToken, new { entries = new object[0] }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/curricula/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/attendance/check-in", member.AccessToken, new { targetType = "Event", targetId = "evt_x", code = "000000" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await RankTestKit.SetProfileRankAsync(f, member, "Intermediate", 1, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!property.GetProperty("features").GetProperty("ranks").GetBoolean(), "features.ranks false");
            f.Settings.Ranks.Enabled = true;
            property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(property.GetProperty("features").GetProperty("ranks").GetBoolean(), "features.ranks follows ranks.enabled");
            Check.True(property.GetProperty("features").GetProperty("rankVerificationLabels").GetBoolean(), "features.rankVerificationLabels");
        }

        private static async Task VerifiedAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember awarder = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            await RankTestKit.SetProfileRankAsync(f, member, "Beginner", 1, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement mine = await RankTestKit.MineAsync(f, member, ct).ConfigureAwait(false);
            JsonElement rank = mine.GetProperty("rank");
            Check.Equal("SelfReported", Api.Str(rank, "source"), "Self-reported before an award");
            Check.True(!rank.GetProperty("verified").GetBoolean(), "Not verified");
            Check.Equal("Beginner", Api.Str(rank, "optionKey"), "Profile value is the effective rank");
            Check.Equal("Beginner", Api.Str(mine.GetProperty("next"), "optionKey"), "Next after Beginner 1 (rank.maxLevel 3) is Beginner 2");
            Check.Equal(2, mine.GetProperty("next").GetProperty("level").GetInt32(), "Next level 2");

            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, awarder.Id, "Advanced", 2, ct).ConfigureAwait(false);
            JsonElement award = await RankTestKit.AwardAsync(f, awarder.AccessToken, member.Id, "Intermediate", 1, ct, "Strong fundamentals").ConfigureAwait(false);
            Check.Equal("Intermediate, 1 Stars", Api.Str(award, "rankLabel"), "Award label");
            Check.Equal(awarder.Id, Api.Str(award, "actorId"), "Awarder recorded");
            rank = (await RankTestKit.MineAsync(f, member, ct).ConfigureAwait(false)).GetProperty("rank");
            Check.True(rank.GetProperty("verified").GetBoolean(), "Verified after the award");
            Check.Equal("Verified", Api.Str(rank, "source"), "Source Verified");
            Check.Equal(awarder.Id, Api.Str(rank, "awardedBy"), "Awarded by");
            JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Intermediate", Api.Str(me.GetProperty("profile").GetProperty("rank"), "key"), "The stored profile value is the awarded rank");
            Check.Equal(1, me.GetProperty("profile").GetProperty("rank").GetProperty("level").GetInt32(), "Awarded level stored");

            await RankTestKit.SetProfileRankAsync(f, member, "Advanced", 0, HttpStatusCode.Conflict, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { profile = new Dictionary<string, object> { { "rank", new { key = "Advanced", level = 0 } } } }, HttpStatusCode.Conflict, "RankVerified", ct).ConfigureAwait(false);
            await RankTestKit.SetProfileRankAsync(f, member, "Intermediate", 1, HttpStatusCode.OK, ct).ConfigureAwait(false);

            List<Notification> notes = await EventTestKit.NotificationsAsync(f, member.Id, NotificationTypeEnum.RankPromoted, ct).ConfigureAwait(false);
            Check.Equal(1, notes.Count, "RankPromoted notification");
            Check.True(notes[0].Title.StartsWith("You were promoted to Intermediate", StringComparison.Ordinal), "Title: " + notes[0].Title);
            string pid = RankTestKit.Pid(f);
            Check.Equal(1L, await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM emailmessages WHERE propertyid = '" + pid + "' AND template = 'rank_promoted' AND userid = '" + member.Id + "'", ct).ConfigureAwait(false), "rank_promoted email queued");
            Check.True(await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM auditrecords WHERE propertyid = '" + pid + "' AND action = 'rank.award' AND targetid = '" + member.Id + "'", ct).ConfigureAwait(false) == 1, "Audited rank.award");
            Check.True(await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM contentchanges WHERE propertyid = '" + pid + "' AND targettype = 'RankPromotion' AND targetid = '" + Api.Str(award, "id") + "'", ct).ConfigureAwait(false) == 1, "Promotion note in the content changelog");

            await Api.ExpectAsync(f, HttpMethod.Post, RankTestKit.Awards + "/" + Api.Str(award, "id") + "/revoke", awarder.AccessToken, new { reason = "Entered in error" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            rank = (await RankTestKit.MineAsync(f, member, ct).ConfigureAwait(false)).GetProperty("rank");
            Check.Equal("SelfReported", Api.Str(rank, "source"), "Self-reported after the revoke");
            Check.Equal("Revoked", Api.Str(rank, "status"), "Rank row Revoked");
            await RankTestKit.SetProfileRankAsync(f, member, "Advanced", 0, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement history = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/rank", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2L, history.GetProperty("historyTotal").GetInt64(), "Award and revoke in history");
            Check.Equal("Revoke", Api.Str(history.GetProperty("history")[0], "action"), "Newest first");
        }

        private static async Task ValidationAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember awarder = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            await RankTestKit.ConfigureAsync(f, new { levelLabel = "Stars", maxLevel = 3 }, ct, new int?[] { 1, null, 2 }).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, awarder.Id, "Advanced", 2, ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, member.Id, "Expert", 0, HttpStatusCode.BadRequest, "UnknownRankOption", ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, member.Id, "Beginner", 2, HttpStatusCode.BadRequest, "LevelOutOfRange", ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, member.Id, "Beginner", -1, HttpStatusCode.BadRequest, "LevelOutOfRange", ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, HttpFixture.AdminToken, member.Id, "Advanced", 3, HttpStatusCode.BadRequest, "LevelOutOfRange", ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, awarder.AccessToken, member.Id, "Beginner", 1, ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, awarder.Id, "Beginner", 0, HttpStatusCode.Forbidden, "SelfAward", ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, other.AccessToken, member.Id, "Beginner", 0, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, "usr_unknown", "Beginner", 0, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, member.Id, "Advanced", 0, HttpStatusCode.Forbidden, "AwardAboveOwnRank", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, RankTestKit.Awards, awarder.AccessToken, new { userId = member.Id, optionKey = "Beginner", eventId = "evt_unknown" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, RankTestKit.Awards, awarder.AccessToken, new { userId = member.Id, optionKey = "Beginner", note = new string('x', 501) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, RankTestKit.Awards, awarder.AccessToken, new { userId = member.Id, fieldKey = "missing", optionKey = "Beginner" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement mine = await RankTestKit.MineAsync(f, member, ct).ConfigureAwait(false);
            Check.Equal(1, mine.GetProperty("rank").GetProperty("maxLevel").GetInt32(), "Per-option maxLevel in the effective rank");
            Check.Equal("Intermediate", Api.Str(mine.GetProperty("next"), "optionKey"), "Next after Beginner 1 is Intermediate (option maxLevel 1)");
            await RankTestKit.ConfigureAsync(f, new { levelLabel = "Stars", maxLevel = 2 }, ct, new int?[] { 3, null, null }, HttpStatusCode.BadRequest).ConfigureAwait(false);
            await RankTestKit.ConfigureAsync(f, new { levelLabel = "Stars", maxLevel = 3, awarding = new { optionAwarderMinimum = "Expert" } }, ct, null, HttpStatusCode.BadRequest).ConfigureAwait(false);
        }

        private static async Task RulesHttpAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember awarder = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember holder = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember beginner = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember unverified = await RankTestKit.AwarderAsync(f, ct, "Unverified Awarder").ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, awarder.Id, "Intermediate", 2, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, holder.Id, "Intermediate", 0, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, awarder.AccessToken, beginner.Id, "Beginner", 2, ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, beginner.Id, "Intermediate", 0, HttpStatusCode.Forbidden, "AwardAboveOwnRank", ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, awarder.AccessToken, holder.Id, "Intermediate", 1, ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, holder.Id, "Intermediate", 3, HttpStatusCode.Forbidden, "AwardAboveOwnRank", ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, unverified.AccessToken, beginner.Id, "Beginner", 0, HttpStatusCode.Forbidden, "AwardAboveOwnRank", ct).ConfigureAwait(false);

            await RankTestKit.ConfigureAsync(f, new { levelLabel = "Stars", maxLevel = 3, awarding = new { allowEqualOption = true, allowEqualOptionLevels = true } }, ct).ConfigureAwait(false);
            ProfileField field = await RankTestKit.FieldAsync(f, ct).ConfigureAwait(false);
            Check.True(field.Rank?.Awarding?.AllowEqualOption == true, "rank.awarding stored and read back");
            await RankTestKit.AwardAsync(f, awarder.AccessToken, beginner.Id, "Intermediate", 0, ct).ConfigureAwait(false);

            await RankTestKit.ConfigureAsync(f, new { levelLabel = "Stars", maxLevel = 3, awarding = new { allowEqualOption = false, allowEqualOptionLevels = false } }, ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, holder.Id, "Intermediate", 2, HttpStatusCode.Forbidden, "AwardAboveOwnRank", ct).ConfigureAwait(false);

            await RankTestKit.ConfigureAsync(f, new { levelLabel = "Stars", maxLevel = 3, awarding = new { optionAwarderMinimum = "Advanced" } }, ct).ConfigureAwait(false);
            await RankTestKit.AwardFailsAsync(f, awarder.AccessToken, beginner.Id, "Beginner", 0, HttpStatusCode.Forbidden, "AwardAboveOwnRank", ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, admin.AccessToken, beginner.Id, "Advanced", 3, ct).ConfigureAwait(false);
        }

        private static async Task RevokeAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember awarder = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember other = await RankTestKit.AwarderAsync(f, ct, "Junior Awarder").ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, awarder.Id, "Advanced", 1, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, other.Id, "Beginner", 0, ct).ConfigureAwait(false);
            JsonElement first = await RankTestKit.AwardAsync(f, awarder.AccessToken, member.Id, "Beginner", 1, ct).ConfigureAwait(false);
            JsonElement second = await RankTestKit.AwardAsync(f, awarder.AccessToken, member.Id, "Intermediate", 0, ct).ConfigureAwait(false);
            string revokePath = RankTestKit.Awards + "/" + Api.Str(second, "id") + "/revoke";
            await Api.ExpectErrorAsync(f, HttpMethod.Post, revokePath, awarder.AccessToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, revokePath, other.AccessToken, new { reason = "No" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, revokePath, member.AccessToken, new { reason = "No" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, RankTestKit.Awards + "/rkp_unknown/revoke", awarder.AccessToken, new { reason = "No" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement revoke = await Api.ExpectAsync(f, HttpMethod.Post, revokePath, awarder.AccessToken, new { reason = "Promoted too early" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Revoke", Api.Str(revoke, "action"), "Revoke row");
            Check.Equal(Api.Str(second, "id"), Api.Str(revoke, "awardId"), "Names the award");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, revokePath, awarder.AccessToken, new { reason = "Again" }, HttpStatusCode.Conflict, "AlreadyRevoked", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, RankTestKit.Awards + "/" + Api.Str(first, "id") + "/revoke", HttpFixture.AdminToken, new { reason = "Superseded award" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement revoked = await Api.ExpectAsync(f, HttpMethod.Post, RankTestKit.Awards + "/enumerate", awarder.AccessToken, new { filters = new { userId = member.Id, status = "Revoked" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, revoked.GetProperty("objects").GetArrayLength(), "Both awards revoked");
            JsonElement revokes = await Api.ExpectAsync(f, HttpMethod.Post, RankTestKit.Awards + "/enumerate", awarder.AccessToken, new { maxResults = 1, filters = new { userId = member.Id, action = "Revoke" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, revokes.GetProperty("objects").GetArrayLength(), "Bounded page");
            Check.True(!revokes.GetProperty("endOfResults").GetBoolean(), "More revokes to read");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, RankTestKit.Awards + "/enumerate", awarder.AccessToken, new { filters = new { status = "Lost" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, RankTestKit.Awards + "/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            string pid = RankTestKit.Pid(f);
            Check.Equal(2L, await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM auditrecords WHERE propertyid = '" + pid + "' AND action = 'rank.revoke' AND targetid = '" + member.Id + "'", ct).ConfigureAwait(false), "Audited rank.revoke");
        }

        private static async Task EligibilityAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember awarder = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember ready = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember waiting = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, awarder.Id, "Advanced", 3, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, awarder.AccessToken, ready.Id, "Beginner", 3, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, awarder.AccessToken, waiting.Id, "Beginner", 3, ct).ConfigureAwait(false);
            object body = new { requirements = new object[] { new { optionKey = "Intermediate", level = 0, minimumDaysInGrade = 30, minimumAttendance = 1, notes = "Thirty days and one class", labels = new[] { "core" } } } };
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ranks/requirements", awarder.AccessToken, body, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ranks/requirements", HttpFixture.AdminToken, new { requirements = new object[] { new { optionKey = "Expert" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ranks/requirements", HttpFixture.AdminToken, new { requirements = new object[] { new { optionKey = "Intermediate", minimumDaysInGrade = 4000 } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ranks/requirements", HttpFixture.AdminToken,
                new { requirements = Enumerable.Range(0, 201).Select(i => new { optionKey = "Intermediate", level = 0 }).ToArray() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement stored = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ranks/requirements", HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, stored.GetProperty("updated").GetInt32(), "One requirement stored");
            JsonElement listed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ranks/requirements/enumerate", awarder.AccessToken, new { labels = new[] { "core" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, listed.GetProperty("objects").GetArrayLength(), "Requirement listed by label");

            JsonElement none = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ranks/eligibility/enumerate", awarder.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ContentTestKit.Ids(none).Select(i => i).Any(), "Nobody eligible yet");
            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ranks/eligibility/enumerate", awarder.AccessToken, new { filters = new { eligibleOnly = false, optionKey = "Beginner" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement readyRow = all.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "userId") == ready.Id);
            Check.True(!readyRow.GetProperty("next").GetProperty("attendanceMet").GetBoolean(), "Attendance not met yet");
            Check.True(!readyRow.GetProperty("next").GetProperty("daysInGradeMet").GetBoolean(), "Time in grade not met yet");

            string eventId = await AttendanceSuite.EventNowAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/attendance/check-in", awarder.AccessToken, new { targetType = "Event", targetId = eventId, userId = ready.Id }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string pid = RankTestKit.Pid(f);
            await RankTestKit.SqlAsync(f, "UPDATE memberranks SET awardedutc = awardedutc - INTERVAL '40 days' WHERE propertyid = '" + pid + "' AND userid IN ('" + ready.Id + "', '" + waiting.Id + "');", ct).ConfigureAwait(false);
            await RankTestKit.SqlAsync(f, "UPDATE attendance SET checkedinutc = checkedinutc + INTERVAL '1 second' WHERE propertyid = '" + pid + "' AND userid = '" + ready.Id + "';", ct).ConfigureAwait(false);
            JsonElement eligible = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ranks/eligibility/enumerate", awarder.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> ids = eligible.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "userId")).ToList();
            Check.True(ids.Contains(ready.Id), "Attendance and time in grade met");
            Check.True(!ids.Contains(waiting.Id), "No attendance: not eligible");
            JsonElement myRank = await RankTestKit.MineAsync(f, ready, ct).ConfigureAwait(false);
            Check.True(myRank.GetProperty("next").GetProperty("eligible").GetBoolean(), "GET /me/rank shows eligibility");
            Check.True(myRank.GetProperty("next").GetProperty("daysInGrade").GetInt32() >= 40, "Days in grade");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/ranks/eligibility/enumerate", ready.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement big = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/ranks/eligibility/enumerate", awarder.AccessToken, new { maxResults = 5000, filters = new { eligibleOnly = false } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(big.GetProperty("maxResults").GetInt32() <= 1000, "Page size clamped (" + big.GetProperty("maxResults").GetInt32() + ")");
        }

        private static async Task VisibilityAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember viewer = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, member.Id, "Intermediate", 2, ct).ConfigureAwait(false);
            JsonElement seen = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/" + member.Id + "/rank", viewer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, seen.GetProperty("ranks").GetArrayLength(), "Public field visible");
            Check.True(seen.GetProperty("ranks")[0].GetProperty("verified").GetBoolean(), "Verified flag");
            Check.True(seen.GetProperty("showVerificationLabels").GetBoolean(), "Labels on by default");
            f.Settings.Ranks.ShowVerificationLabels = false;
            seen = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/" + member.Id + "/rank", viewer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!seen.GetProperty("showVerificationLabels").GetBoolean(), "ranks.showVerificationLabels false");
            ProfileField field = await RankTestKit.FieldAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/profile-fields/" + field.Id, HttpFixture.AdminToken, new { visibility = "Staff", collectAt = "AdminOnly", editableBy = "Staff", validation = new { required = false } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            f.Services.ProfileFields.Invalidate();
            seen = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/" + member.Id + "/rank", viewer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, seen.GetProperty("ranks").GetArrayLength(), "Staff-only field hidden from members");
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            seen = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/" + member.Id + "/rank", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, seen.GetProperty("ranks").GetArrayLength(), "Staff see it");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + member.Id + "/block", viewer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members/" + member.Id + "/rank", viewer.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members/usr_unknown/rank", admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task LineageAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember promoter = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember stranger = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/lineage", member.AccessToken, new { entries = new object[] { new { optionKey = "Expert" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/lineage", member.AccessToken, new { entries = new object[] { new { optionKey = "Beginner", promotedByUserId = member.Id } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/lineage", member.AccessToken, new { entries = new object[] { new { optionKey = "Beginner", promotedByUserId = promoter.Id, promotedByName = "Both" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/lineage", member.AccessToken, new { entries = Enumerable.Range(0, 51).Select(i => new { optionKey = "Beginner" }).ToArray() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            object lineage = new { entries = new object[] { new { optionKey = "Beginner", promotedByName = "Visiting Coach", promotedUtc = "2024-01-15T00:00:00Z" }, new { optionKey = "Intermediate", promotedByUserId = promoter.Id, promotedUtc = "2025-06-01T00:00:00Z" } } };
            JsonElement put = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/lineage", member.AccessToken, lineage, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, put.GetProperty("entries").GetArrayLength(), "Two entries");
            string entryId = Api.Str(put.GetProperty("entries")[1], "id");
            List<Notification> requests = await EventTestKit.NotificationsAsync(f, promoter.Id, NotificationTypeEnum.LineageVerificationRequested, ct).ConfigureAwait(false);
            Check.Equal(1, requests.Count, "Promoter asked to verify");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/lineage/" + entryId + "/verify", stranger.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/lineage/" + entryId + "/verify", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/lineage/lin_unknown/verify", promoter.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement verified = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lineage/" + entryId + "/verify", promoter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(verified.GetProperty("verified").GetBoolean(), "Verified by the promoter");
            put = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/lineage", member.AccessToken, lineage, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(put.GetProperty("entries")[1].GetProperty("verified").GetBoolean(), "Unchanged entry keeps verification");
            Check.Equal(1, (await EventTestKit.NotificationsAsync(f, promoter.Id, NotificationTypeEnum.LineageVerificationRequested, ct).ConfigureAwait(false)).Count, "No second request for an unchanged entry");
            JsonElement profile = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/" + member.Id + "/rank", stranger.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, profile.GetProperty("lineage").GetArrayLength(), "Lineage on the member rank");
            string pid = RankTestKit.Pid(f);
            Check.True(await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM contentchanges WHERE propertyid = '" + pid + "' AND targettype = 'LineageEntry' AND authoruserid = '" + member.Id + "'", ct).ConfigureAwait(false) >= 1, "Lineage names in the content changelog");
            Check.True(await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM auditrecords WHERE propertyid = '" + pid + "' AND action = 'lineage.verify'", ct).ConfigureAwait(false) == 1, "Audited lineage.verify");
        }

        private static async Task RoleGrantsAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember granted = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember roled = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", HttpFixture.AdminToken, new { userId = granted.Id, resourceType = "Ranks", resourceId = "*" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement listed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators/enumerate", HttpFixture.AdminToken, new { filters = new { role = "RankAwarder" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(listed.GetProperty("objects").EnumerateArray().Any(o => o.ToString().Contains(granted.Id, StringComparison.Ordinal)), "Listed with the staff grants");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + roled.Id + "/roles", HttpFixture.AdminToken, new { roles = new[] { new { role = "RankAwarder" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<RoleAssignment> roles = await f.Database.RoleAssignments.EnumerateByUserAsync(roled.Id, ct).ConfigureAwait(false);
            Check.True(roles.Any(r => r.Role == RoleEnum.RankAwarder && r.ResourceType == ResourceTypeEnum.Ranks), "Member roles default the Ranks resource type");
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, granted.Id, "Advanced", 0, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, roled.Id, "Advanced", 0, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, granted.AccessToken, member.Id, "Beginner", 0, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, roled.AccessToken, member.Id, "Beginner", 1, ct).ConfigureAwait(false);
            JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", granted.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(me.GetProperty("roles").ToString().Contains("RankAwarder", StringComparison.Ordinal), "Role in /auth/me");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/moderators/" + granted.Id + "?resourceType=Ranks&resourceId=*", HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            f.Services.Tokens.InvalidateUser(granted.Id);
            await RankTestKit.AwardFailsAsync(f, granted.AccessToken, member.Id, "Beginner", 0, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        private static async Task ExportAndDeletionAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember leaving = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, leaving.Id, "Beginner", 1, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/lineage", leaving.AccessToken, new { entries = new object[] { new { optionKey = "Beginner", promotedByName = "Founding Coach" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string eventId = await AttendanceSuite.EventNowAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/attendance/check-in", HttpFixture.AdminToken, new { targetType = "Event", targetId = eventId, userId = leaving.Id }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            List<string> sections = f.Services.AccountExports.Sections.Select(s => s.Name).ToList();
            foreach (string section in new[] { "memberRanks", "rankPromotions", "lineage", "curriculumProgress", "attendance" }) Check.True(sections.Contains(section), "Export section " + section);
            string pid = RankTestKit.Pid(f);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", leaving.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            foreach (string table in new[] { "memberranks", "rankpromotions", "lineageentries", "curriculumprogress", "attendance" })
                Check.Equal(0L, await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM " + table + " WHERE propertyid = '" + pid + "' AND userid = '" + leaving.Id + "'", ct).ConfigureAwait(false), table + " erased");
        }

        private static async Task IsolationAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            DatabaseDriverBase other = f.Raw.ForProperty(PropertyIds.Generate());
            DateTime now = DateTime.UtcNow;
            RankPromotion foreign = new RankPromotion { Id = IdGenerator.Generate("rkp_"), UserId = member.Id, FieldKey = "rank", OptionKey = "Advanced", Level = 3, ActorId = null, CreatedUtc = now, UpdatedUtc = now };
            await other.Ranks.AwardAsync(foreign, new MemberRank { Id = IdGenerator.Generate("mrk_"), UserId = member.Id, FieldKey = "rank", OptionKey = "Advanced", Level = 3, PromotionId = foreign.Id, AwardedUtc = now, CreatedUtc = now, UpdatedUtc = now }, ct).ConfigureAwait(false);
            await other.Attendance.CreateAsync(new TrellisKit.Core.Responses.AttendanceView { Id = IdGenerator.Generate("att_"), UserId = member.Id, TargetType = AttendanceTargetTypeEnum.Event, TargetId = "evt_foreign", OccurrenceDate = "2026-01-01", CreatedUtc = now, CheckedInUtc = now }, ct).ConfigureAwait(false);
            await other.Curricula.CreateAsync(new Curriculum { Id = IdGenerator.Generate("cur_"), Name = "Foreign", Status = CurriculumStatusEnum.Published, Visibility = CurriculumVisibilityEnum.Public, CreatedUtc = now, UpdatedUtc = now }, ct).ConfigureAwait(false);
            Check.Equal(0, (await f.Database.Ranks.ReadMemberRanksAsync(member.Id, ct).ConfigureAwait(false)).Count, "No foreign rank rows");
            Check.True(await f.Database.Ranks.ReadPromotionAsync(foreign.Id, ct).ConfigureAwait(false) == null, "No foreign promotion");
            Check.Equal(0L, await f.Database.Attendance.CountForUserAsync(member.Id, null, ct).ConfigureAwait(false), "No foreign attendance");
            JsonElement mine = await RankTestKit.MineAsync(f, member, ct).ConfigureAwait(false);
            Check.True(!mine.GetProperty("rank").GetProperty("verified").GetBoolean(), "The other property's award does not verify here");
            JsonElement awards = await Api.ExpectAsync(f, HttpMethod.Post, RankTestKit.Awards + "/enumerate", HttpFixture.AdminToken, new { filters = new { userId = member.Id } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, awards.GetProperty("objects").GetArrayLength(), "Awards list is property scoped");
            JsonElement curricula = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/curricula/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!curricula.GetProperty("objects").EnumerateArray().Any(c => Api.Str(c, "name") == "Foreign"), "Curricula are property scoped");
        }

        #endregion
    }
}
