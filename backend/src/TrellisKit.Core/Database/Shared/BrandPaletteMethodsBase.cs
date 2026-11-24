namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared brand palette methods (tables propertybrandpalettes, propertybrandcolors, and propertybrandoverrides).
    /// </summary>
    internal abstract class BrandPaletteMethodsBase : MethodsBase, IBrandPaletteMethods
    {
        protected BrandPaletteMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<BrandPalette?> ReadCurrentAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM propertybrandpalettes WHERE " + Scope() + " ORDER BY version DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : await HydrateAsync(table.Rows[0], token).ConfigureAwait(false);
        }

        public async Task<BrandPalette?> ReadBeforeAsync(int version, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM propertybrandpalettes WHERE " + Scope() + " AND version < " + D.Num(version) + " ORDER BY version DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : await HydrateAsync(table.Rows[0], token).ConfigureAwait(false);
        }

        public async Task<BrandPalette?> ReadVersionAsync(int version, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM propertybrandpalettes WHERE " + Scope() + " AND version = " + D.Num(version) + " LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : await HydrateAsync(table.Rows[0], token).ConfigureAwait(false);
        }

        public async Task<EnumerationResult<BrandPalette>> EnumerateAsync(EnumerationRequest request, string? source, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (request.Query.Labels.Count > 0 || request.Query.Tags.Count > 0) conditions.Add("1 = 0");
            if (!String.IsNullOrWhiteSpace(source)) conditions.Add("p.source = " + D.Str(source.Trim()));
            string from = "(SELECT propertyid, version, source, note, seedhash, createdbyuserid, createdutc, CAST(version AS TEXT) AS vid FROM propertybrandpalettes WHERE " + Scope() + ") p";
            EnumerationResult<DataRow> page = await EnumerateAsync(request, "p.*", from, conditions, r => r, token).ConfigureAwait(false);
            List<BrandPalette> palettes = new List<BrandPalette>();
            foreach (DataRow row in page.Objects) palettes.Add(await HydrateAsync(row, token).ConfigureAwait(false));
            return page.With(palettes);
        }

        public async Task CreateAsync(BrandPalette palette, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(palette);
            string version = D.Num(palette.Version);
            List<string> statements = new List<string>
            {
                "INSERT INTO propertybrandpalettes (propertyid, version, source, note, seedhash, createdbyuserid, createdutc) VALUES (" +
                    Pid + ", " + version + ", " + D.Enum(palette.Source) + ", " + D.Str(palette.Note) + ", " + D.Str(palette.SeedHash) + ", " + D.Str(palette.CreatedByUserId) + ", " + D.Ts(palette.CreatedUtc) + ");"
            };
            foreach (string scheme in new[] { "light", "dark" })
            {
                BrandColorSet set = palette.SetOf(scheme);
                foreach (string key in BrandColorSet.Keys)
                {
                    statements.Add("INSERT INTO propertybrandcolors (propertyid, version, scheme, colorkey, value) VALUES (" + Pid + ", " + version + ", " + D.Str(scheme) + ", " + D.Str(key) + ", " + D.Str(set.Get(key)) + ");");
                }
            }
            foreach (BrandContrastOverride item in palette.Overrides)
            {
                statements.Add("INSERT INTO propertybrandoverrides (propertyid, version, scheme, pair, ratio, required, reason) VALUES (" + Pid + ", " + version + ", " + D.Str(item.Scheme) + ", " + D.Str(item.Pair) + ", " +
                    D.Num(item.Ratio) + ", " + D.Num(item.Required) + ", " + D.Str(item.Reason) + ");");
            }
            await UniqueTransactionAsync(statements, "Brand palette version " + palette.Version, token).ConfigureAwait(false);
        }

        public async Task<int> PruneAsync(int keep, CancellationToken token = default)
        {
            int retained = Math.Max(1, keep);
            DataTable cutoff = await QueryAsync("SELECT version FROM propertybrandpalettes WHERE " + Scope() + " ORDER BY version DESC LIMIT 1 OFFSET " + D.Num(retained - 1) + ";", token).ConfigureAwait(false);
            if (cutoff.Rows.Count == 0) return 0;
            int oldest = RowReader.GetInt(cutoff.Rows[0], "version");
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM propertybrandpalettes WHERE " + Scope() + " AND version < " + D.Num(oldest) + ";", token).ConfigureAwait(false);
            if (count == 0) return 0;
            await TransactionAsync(new List<string>
            {
                "DELETE FROM propertybrandoverrides WHERE " + Scope() + " AND version < " + D.Num(oldest) + ";",
                "DELETE FROM propertybrandcolors WHERE " + Scope() + " AND version < " + D.Num(oldest) + ";",
                "DELETE FROM propertybrandpalettes WHERE " + Scope() + " AND version < " + D.Num(oldest) + ";"
            }, token).ConfigureAwait(false);
            return (int)count;
        }

        public Task<long> CountAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM propertybrandpalettes WHERE " + Scope() + ";", token);
        }

        private async Task<BrandPalette> HydrateAsync(DataRow row, CancellationToken token)
        {
            int version = RowReader.GetInt(row, "version");
            BrandPalette palette = new BrandPalette
            {
                Version = version,
                Source = RowReader.GetEnum(row, "source", BrandPaletteSourceEnum.Seed),
                Note = RowReader.GetNullableString(row, "note"),
                SeedHash = RowReader.GetNullableString(row, "seedhash"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
            DataTable colors = await QueryAsync("SELECT scheme, colorkey, value FROM propertybrandcolors WHERE " + Scope() + " AND version = " + D.Num(version) + " ORDER BY scheme, colorkey LIMIT 64;", token).ConfigureAwait(false);
            foreach (DataRow color in colors.Rows)
            {
                string key = RowReader.GetString(color, "colorkey");
                if (!BrandColorSet.IsKey(key)) continue;
                palette.SetOf(RowReader.GetString(color, "scheme")).Set(key, RowReader.GetString(color, "value"));
            }
            DataTable overrides = await QueryAsync("SELECT scheme, pair, ratio, required, reason FROM propertybrandoverrides WHERE " + Scope() + " AND version = " + D.Num(version) + " ORDER BY scheme, pair LIMIT 64;", token).ConfigureAwait(false);
            palette.Overrides = overrides.Rows.Cast<DataRow>().Select(o => new BrandContrastOverride
            {
                Scheme = RowReader.GetString(o, "scheme"),
                Pair = RowReader.GetString(o, "pair"),
                Ratio = RowReader.GetDouble(o, "ratio"),
                Required = RowReader.GetDouble(o, "required"),
                Reason = RowReader.GetNullableString(o, "reason")
            }).ToList();
            return palette;
        }

        public async Task<MemberPaletteChoices?> ReadMemberChoicesAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM propertypalettechoices WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            MemberPaletteModeEnum mode;
            if (!Enum.TryParse(RowReader.GetString(row, "mode"), false, out mode)) mode = MemberPaletteModeEnum.All;
            string ids = RowReader.GetString(row, "presetids");
            return new MemberPaletteChoices
            {
                Mode = mode,
                PresetIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Revision = RowReader.GetInt(row, "revision"),
                UpdatedByUserId = RowReader.GetNullableString(row, "updatedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetNullableDateTime(row, "updatedutc")
            };
        }

        public async Task WriteMemberChoicesAsync(MemberPaletteChoices choices, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(choices);
            await QueryAsync("INSERT INTO propertypalettechoices (propertyid, mode, presetids, revision, updatedbyuserid, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Enum(choices.Mode) + ", " + D.Str(String.Join(",", choices.PresetIds)) + ", " + D.Num(choices.Revision) + ", " + D.Str(choices.UpdatedByUserId) + ", " +
                D.Ts(choices.CreatedUtc) + ", " + D.Ts(choices.UpdatedUtc) + ") ON CONFLICT (propertyid) DO UPDATE SET mode = EXCLUDED.mode, presetids = EXCLUDED.presetids, " +
                "revision = EXCLUDED.revision, updatedbyuserid = EXCLUDED.updatedbyuserid, updatedutc = EXCLUDED.updatedutc;", token).ConfigureAwait(false);
        }
    }
}
