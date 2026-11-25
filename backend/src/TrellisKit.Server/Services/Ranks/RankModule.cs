namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Verified ranks, lineage, curricula, and attendance for one property (docs/TRELLISKIT.md, Ranks, curricula, and
    /// attendance): the services, the ranks.enabled gate, Rank field helpers, account export sections, and account
    /// deletion. Thread safety: safe for concurrent use after <see cref="Attach"/>.
    /// </summary>
    public class RankModule
    {
        #region Public-Members

        /// <summary>
        /// Most Rank fields the rank routes consider (enabled Rank profile fields in profile order).
        /// </summary>
        public const int MaxRankFields = 10;

        /// <summary>
        /// Effective ranks, awards, revokes, requirements, eligibility, and the profile field lock.
        /// </summary>
        public RankService Ranks { get; }

        /// <summary>
        /// Self-reported lineage and its verification.
        /// </summary>
        public LineageService Lineage { get; }

        /// <summary>
        /// Curricula and item progress.
        /// </summary>
        public CurriculumService Curricula { get; }

        /// <summary>
        /// Attendance check-ins and rotating codes.
        /// </summary>
        public AttendanceService Attendance { get; }

        /// <summary>
        /// Whether ranks.enabled is on.
        /// </summary>
        public bool Enabled
        {
            get { return _Services.Settings.Ranks.Enabled; }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public RankModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Ranks = new RankService(services, this);
            Lineage = new LineageService(services, this);
            Curricula = new CurriculumService(services, this);
            Attendance = new AttendanceService(services, this);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the account export sections (memberRanks, rankPromotions, lineage, curriculumProgress, attendance) and
        /// account deletion. Call once.
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            AccountExportService exports = _Services.AccountExports;
            exports.Register(new PagedExportSection<MemberRank>("memberRanks", async (userId, after, limit, token) =>
            {
                List<MemberRank> rows = after == null ? await _Services.Database.Ranks.ReadMemberRanksAsync(userId, token).ConfigureAwait(false) : new List<MemberRank>();
                return new ContentExportPage<MemberRank> { Objects = rows, NextAfterId = null };
            }));
            exports.Register(new PagedExportSection<RankPromotion>("rankPromotions", async (userId, after, limit, token) =>
            {
                List<RankPromotion> rows = await _Services.Database.Ranks.ListPromotionsForUserAsync(userId, after, limit, token).ConfigureAwait(false);
                return new ContentExportPage<RankPromotion> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
            }));
            exports.Register(new PagedExportSection<LineageEntry>("lineage", async (userId, after, limit, token) =>
            {
                List<LineageEntry> rows = after == null ? await _Services.Database.Ranks.ListLineageAsync(userId, null, token).ConfigureAwait(false) : new List<LineageEntry>();
                return new ContentExportPage<LineageEntry> { Objects = rows, NextAfterId = null };
            }));
            exports.Register(new PagedExportSection<CurriculumProgress>("curriculumProgress", async (userId, after, limit, token) =>
            {
                List<CurriculumProgress> rows = await _Services.Database.Curricula.ListProgressForUserAsync(userId, after, limit, token).ConfigureAwait(false);
                return new ContentExportPage<CurriculumProgress> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
            }));
            exports.Register(new PagedExportSection<AttendanceView>("attendance", async (userId, after, limit, token) =>
            {
                List<AttendanceView> rows = await _Services.Database.Attendance.ListForUserAsync(userId, after, limit, token).ConfigureAwait(false);
                return new ContentExportPage<AttendanceView> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
            }));
            _Services.Profiles.RankLock = Ranks.RequireEditableAsync;
            _Services.PropertyDecorators.Add((response, token) =>
            {
                if (response.Features != null)
                {
                    response.Features.Ranks = Enabled;
                    response.Features.RankVerificationLabels = _Services.Settings.Ranks.ShowVerificationLabels;
                }
                return Task.CompletedTask;
            });
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                await _Services.Database.Ranks.EraseForUserAsync(userId, token).ConfigureAwait(false);
                await _Services.Database.Curricula.EraseForUserAsync(userId, token).ConfigureAwait(false);
                await _Services.Database.Attendance.EraseForUserAsync(userId, token).ConfigureAwait(false);
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Throw 404 NotAvailable while ranks.enabled is off.
        /// </summary>
        /// <exception cref="ApiErrorException">404 NotAvailable.</exception>
        public void RequireEnabled()
        {
            if (!Enabled) throw new ApiErrorException(404, "NotAvailable", "Verified ranks are not available for this property.");
        }

        /// <summary>
        /// The enabled Rank profile fields in profile order (at most <see cref="MaxRankFields"/>).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Fields.</returns>
        public async Task<List<ProfileField>> RankFieldsAsync(CancellationToken token = default)
        {
            IReadOnlyList<ProfileField> all = await _Services.ProfileFields.AllAsync(token).ConfigureAwait(false);
            return all.Where(f => !f.BuiltIn && f.Enabled && f.Type == ProfileFieldTypeEnum.Rank).OrderBy(f => f.ProfileOrder).Take(MaxRankFields).ToList();
        }

        /// <summary>
        /// Resolve a Rank field by key (case-insensitive), or the first enabled Rank field when the key is empty.
        /// </summary>
        /// <param name="fieldKey">Key, or null.</param>
        /// <param name="field">Error field name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Field.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed when no such Rank field is enabled.</exception>
        public async Task<ProfileField> FieldAsync(string? fieldKey, string field, CancellationToken token = default)
        {
            List<ProfileField> fields = await RankFieldsAsync(token).ConfigureAwait(false);
            ProfileField? found = String.IsNullOrWhiteSpace(fieldKey) ? fields.FirstOrDefault() : fields.FirstOrDefault(f => String.Equals(f.Key, fieldKey.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found == null) throw ApiErrorException.Field(field, String.IsNullOrWhiteSpace(fieldKey) ? "This property has no enabled Rank profile field." : "Use the key of an enabled Rank profile field.");
            return found;
        }

        /// <summary>
        /// A member's effective rank in one field: the Verified rank row when one stands, otherwise the profile value
        /// (self-reported).
        /// </summary>
        /// <param name="field">Rank field.</param>
        /// <param name="rank">The member's rank row in the field, or null.</param>
        /// <param name="value">The member's profile value of the field, or null.</param>
        /// <param name="awarders">Awarder summaries by identifier (optional).</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">The field is null.</exception>
        public static EffectiveRankView Effective(ProfileField field, MemberRank? rank, ProfileValue? value, IReadOnlyDictionary<string, UserSummary>? awarders = null)
        {
            ArgumentNullException.ThrowIfNull(field);
            EffectiveRankView view = new EffectiveRankView
            {
                FieldKey = field.Key,
                FieldLabel = field.Label,
                LevelLabel = field.Rank?.LevelLabel ?? String.Empty,
                Status = rank?.Status
            };
            string? key;
            int level;
            if (rank != null && rank.Status == RankStatusEnum.Verified)
            {
                key = rank.OptionKey;
                level = rank.Level;
                view.Verified = true;
                view.Source = "Verified";
                view.AwardedBy = rank.AwardedBy;
                view.AwardedUtc = rank.AwardedUtc;
                if (rank.AwardedBy != null && awarders != null && awarders.TryGetValue(rank.AwardedBy, out UserSummary? awarder)) view.Awarder = awarder;
            }
            else
            {
                key = String.IsNullOrWhiteSpace(value?.ValueText) ? null : value!.ValueText;
                level = value?.ValueLevel ?? 0;
                view.Source = key == null ? "None" : "SelfReported";
            }
            if (key == null) return view;
            int index = RankAwardRules.IndexOf(field, key);
            ProfileFieldOption? option = index < 0 ? null : field.Options[index];
            view.OptionKey = option?.Key ?? key;
            view.OptionLabel = option?.Label ?? key;
            view.Color = option?.Color;
            view.Level = level;
            view.MaxLevel = option == null ? 0 : RankAwardRules.MaxLevelOf(field, option);
            view.RankLabel = RankAwardRules.Label(field, view.OptionKey, level);
            return view;
        }

        #endregion
    }
}
