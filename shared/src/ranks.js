/**
 * Verified rank helpers shared by the web and mobile apps (docs/TRELLISKIT.md, Ranks, curricula, and
 * attendance). A property's Rank profile field is the rank system: options are ordered lowest to highest,
 * `rank.maxLevel` caps levels and `options[].maxLevel` overrides it per option, and `rank.awarding` holds the
 * awarding rules the server enforces. These helpers mirror the server so clients can label ranks, show
 * eligibility, and grey out awards the server would refuse; the server stays authoritative.
 */

/** Status of a member's verified rank row. */
export const RankStatus = Object.freeze({ Verified: 'Verified', Revoked: 'Revoked', Disputed: 'Disputed' });

/** Where an effective rank comes from. */
export const RankSource = Object.freeze({ Verified: 'Verified', SelfReported: 'SelfReported', None: 'None' });

/** Award history actions. */
export const RankPromotionAction = Object.freeze({ Award: 'Award', Revoke: 'Revoke' });

/** Curriculum item types. */
export const CurriculumItemType = Object.freeze({ Lesson: 'Lesson', Series: 'Series', CatalogEntry: 'CatalogEntry', Custom: 'Custom' });

/** A member's state of one curriculum item. */
export const CurriculumItemState = Object.freeze({ Incomplete: 'Incomplete', Completed: 'Completed', SelfComplete: 'SelfComplete', Verified: 'Verified' });

/** Curriculum statuses and visibilities. */
export const CurriculumStatus = Object.freeze({ Draft: 'Draft', Published: 'Published', Archived: 'Archived' });
export const CurriculumVisibility = Object.freeze({ Members: 'Members', Public: 'Public', Hidden: 'Hidden' });

/** Attendance targets and methods. */
export const AttendanceTargetType = Object.freeze({ Event: 'Event', VenueSession: 'VenueSession' });
export const AttendanceMethod = Object.freeze({ QR: 'QR', Staff: 'Staff', SelfWithCode: 'SelfWithCode' });

/** Server bounds of the rank routes. */
export const RANK_LIMITS = Object.freeze({ maxRankFields: 10, historyPreview: 20, maxLineageEntries: 50, maxRequirements: 200, maxCurriculumItems: 500, noteMax: 500, lineageNameMax: 120, customTextMax: 300 });

function options(field) {
  return (field && Array.isArray(field.options) ? field.options : []).filter(Boolean);
}

function clampLevel(n, max) {
  const level = Number.isInteger(n) ? n : 0;
  return Math.min(Math.max(level, 0), max);
}

/** Position of an option key in a Rank field (case-insensitive), or -1. */
export function rankIndex(field, key) {
  if (key === null || key === undefined || String(key).trim() === '') return -1;
  const lower = String(key).trim().toLowerCase();
  return options(field).findIndex((o) => String(o.key).toLowerCase() === lower);
}

/** The level maximum of an option: its `maxLevel`, else the field's `rank.maxLevel`, clamped to 0..20. */
export function optionMaxLevel(field, key) {
  const option = options(field)[rankIndex(field, key)];
  const max = option && Number.isInteger(option.maxLevel) ? option.maxLevel : Number(field && field.rank && field.rank.maxLevel) || 0;
  return Math.min(Math.max(max, 0), 20);
}

/**
 * Display text of a rank, for example "Gold, 2 Stars" (the server's rankLabel). The level part is omitted
 * at level 0 or when the field has no level label.
 */
export function rankText(field, key, level = 0) {
  const option = options(field)[rankIndex(field, key)];
  let text = option ? option.label || option.key : String(key || '');
  const levelLabel = String((field && field.rank && field.rank.levelLabel) || '').trim();
  const n = Number.isInteger(level) ? level : 0;
  if (n > 0 && levelLabel) text += ', ' + n + ' ' + levelLabel;
  return text;
}

/**
 * Level choices of one option for pickers, using the option's own maxLevel:
 * `[{ level: 0, label: 'Gold' }, { level: 1, label: 'Gold, 1 Stars' }, ...]`.
 */
export function rankLevelChoices(field, key) {
  if (rankIndex(field, key) < 0) return [];
  const max = optionMaxLevel(field, key);
  const result = [];
  for (let level = 0; level <= max; level++) result.push({ level, label: rankText(field, key, level) });
  return result;
}

/**
 * The rank after an option and level: the next level while below the option's maximum, else level 0 of the
 * next option that is not retired; null at the highest rank or for an unknown key.
 */
export function nextRank(field, key, level = 0) {
  const index = rankIndex(field, key);
  if (index < 0) return null;
  const list = options(field);
  const current = list[index];
  const n = Number.isInteger(level) ? level : 0;
  if (n < optionMaxLevel(field, current.key)) return { optionKey: current.key, level: n + 1, label: rankText(field, current.key, n + 1) };
  const next = list.slice(index + 1).find((o) => !o.retired);
  return next ? { optionKey: next.key, level: 0, label: rankText(field, next.key, 0) } : null;
}

/**
 * A member's effective rank in a field, as the server computes it: the verified rank row when its status is
 * Verified, otherwise the self-reported profile value `{ key, level }`.
 * @param {object} field Rank profile field.
 * @param {{ optionKey: string, level: number, status: string, awardedBy?: string, awardedUtc?: string }|null} memberRank
 * @param {{ key: string, level?: number }|null} profileValue
 */
export function effectiveRank(field, memberRank, profileValue) {
  const verified = Boolean(memberRank && memberRank.status === RankStatus.Verified);
  const key = verified ? memberRank.optionKey : profileValue && profileValue.key ? profileValue.key : null;
  if (!key) return { optionKey: null, level: 0, verified: false, source: RankSource.None, label: '' };
  const index = rankIndex(field, key);
  const option = options(field)[index];
  const max = optionMaxLevel(field, key);
  const raw = verified ? memberRank.level : profileValue.level;
  const level = index < 0 ? (Number.isInteger(raw) ? raw : 0) : clampLevel(raw, max);
  return {
    optionKey: option ? option.key : key,
    level,
    maxLevel: index < 0 ? 0 : max,
    color: option ? option.color || null : null,
    verified,
    source: verified ? RankSource.Verified : RankSource.SelfReported,
    awardedBy: verified ? memberRank.awardedBy || null : null,
    awardedUtc: verified ? memberRank.awardedUtc || null : null,
    label: rankText(field, key, level)
  };
}

/**
 * The verification badge to show next to a rank: 'verified', 'selfReported', or null when the rank has no
 * value or the server's `showVerificationLabels` is false.
 * @param {{ verified?: boolean, source?: string }|null} rank An EffectiveRankView or `effectiveRank` result.
 * @param {boolean} [showVerificationLabels] From GET /me/rank or GET /members/{id}/rank.
 */
export function verificationBadge(rank, showVerificationLabels = true) {
  if (!rank || showVerificationLabels === false) return null;
  if (rank.verified) return 'verified';
  return rank.source === RankSource.None || !rank.optionKey ? null : 'selfReported';
}

/**
 * Eligibility display of a `RankNextView` (GET /me/rank `fields[].next`, eligibility list `next`):
 * `{ state: 'eligible' | 'inProgress' | 'noRequirements' | 'highest', checks: [{ key, met, current, required }] }`.
 * Checks list only the configured requirements (days in grade, attendance, curriculum).
 */
export function eligibilityDisplay(next) {
  if (!next) return { state: 'highest', checks: [] };
  const requirement = next.requirement || null;
  if (!requirement) return { state: 'noRequirements', checks: [] };
  const checks = [];
  if (requirement.minimumDaysInGrade > 0) checks.push({ key: 'daysInGrade', met: Boolean(next.daysInGradeMet), current: next.daysInGrade ?? null, required: requirement.minimumDaysInGrade });
  if (requirement.minimumAttendance > 0) checks.push({ key: 'attendance', met: Boolean(next.attendanceMet), current: next.attendance || 0, required: requirement.minimumAttendance });
  if (next.curriculum) checks.push({ key: 'curriculum', met: Boolean(next.curriculumMet), current: next.curriculum.completedRequired || 0, required: next.curriculum.required || 0 });
  return { state: next.eligible ? 'eligible' : 'inProgress', checks };
}

/**
 * Whether the server's rank.awarding rules let an awarder award an option and level (mirrors
 * RankAwardRules.Allows). SystemAdmin is exempt; other awarders need a Verified rank in the field.
 * @param {object} field Rank profile field (with `rank.awarding`).
 * @param {{ isSystemAdmin?: boolean, rank?: { optionKey: string, level: number, status: string }|null }} awarder
 * @param {{ optionKey: string, level: number, status: string }|null} target The member's rank row, or null.
 */
export function canAwardRank(field, awarder, target, optionKey, level = 0) {
  if (awarder && awarder.isSystemAdmin) return true;
  const own = awarder && awarder.rank;
  if (!own || own.status !== RankStatus.Verified) return false;
  const ownIndex = rankIndex(field, own.optionKey);
  const awarded = rankIndex(field, optionKey);
  if (ownIndex < 0 || awarded < 0) return false;
  const rules = (field && field.rank && field.rank.awarding) || {};
  if (rules.optionAwarderMinimum) {
    const minimum = rankIndex(field, rules.optionAwarderMinimum);
    if (minimum >= 0 && ownIndex < minimum) return false;
  }
  if (awarded < ownIndex) return true;
  if (awarded > ownIndex) return false;
  if ((Number.isInteger(level) ? level : 0) > own.level) return false;
  if (rules.allowEqualOption === true) return true;
  const holds = Boolean(target && target.status === RankStatus.Verified && rankIndex(field, target.optionKey) === awarded);
  return rules.allowEqualOptionLevels !== false && holds;
}

/** The target identifier of a venue schedule session: `{venueId}:{sessionIndex}` (zero-based position in `schedule.sessions`). */
export function venueSessionTargetId(venueId, sessionIndex) {
  return String(venueId) + ':' + Number(sessionIndex);
}

/** The web path a check-in QR code opens (`/app/check-in`); the mobile route is `/check-in`. */
export const CHECK_IN_WEB_PATH = '/app/check-in';
export const CHECK_IN_APP_PATH = '/check-in';

function checkInQuery(body) {
  const params = new URLSearchParams();
  params.set('t', body.targetType);
  params.set('id', body.targetId);
  if (body.occurrenceDate) params.set('d', body.occurrenceDate);
  if (body.code) params.set('c', body.code);
  return params.toString();
}

/**
 * The https universal link a check-in QR code encodes, on the property's web domain:
 * `https://{domains.web}/app/check-in?t={targetType}&id={targetId}&d={occurrenceDate}&c={code}`.
 * A phone camera opens it in the browser (the web app checks the member in) or, where the app
 * claims the domain, in the app. Returns null without a web domain or a complete code.
 * @param {object} property Property configuration (`domains.web`).
 * @param {{ targetType: string, targetId: string, occurrenceDate?: string, code: string }} code A check-in code response.
 */
export function checkInLink(property, code) {
  const domain = String((property && property.domains && property.domains.web) || '').trim().replace(/^https?:\/\//i, '').replace(/\/+$/, '');
  if (!domain || !code || !code.targetType || !code.targetId || !code.code) return null;
  return 'https://' + domain + CHECK_IN_WEB_PATH + '?' + checkInQuery(code);
}

/** The mobile route of a check-in body: `/check-in?t=...&id=...&d=...&c=...`. */
export function checkInAppPath(body) {
  return CHECK_IN_APP_PATH + '?' + checkInQuery(body || {});
}

/**
 * Parse a check-in body from a scanned or opened payload, or null when the text is not one. Accepts
 * the universal link (`.../app/check-in?t=&id=&d=&c=`), the app path (`/check-in?...`), a query
 * object or string with the same short keys, and the server's compact `qrPayload`
 * (`checkin?targetType=...&targetId=...&occurrenceDate=...&code=...`). The code is optional so a
 * link without one opens code entry; `method` is QR when a code came with the payload.
 * @param {string|object} input
 * @returns {{ targetType: string, targetId: string, occurrenceDate?: string, code?: string, method?: string }|null}
 */
export function parseCheckInPayload(input) {
  let get;
  if (input && typeof input === 'object') {
    const first = (v) => (Array.isArray(v) ? v[0] : v);
    get = (key) => {
      const value = first(input[key]);
      return value === undefined || value === null ? null : String(value);
    };
  } else {
    const raw = String(input || '').trim();
    const question = raw.indexOf('?');
    if (question < 0) return null;
    const path = raw.slice(0, question).toLowerCase();
    if (!/(^|\/)check-?in$/.test(path)) return null;
    const params = new URLSearchParams(raw.slice(question + 1));
    get = (key) => params.get(key);
  }
  const pick = (short, long) => get(short) ?? get(long);
  const targetType = pick('t', 'targetType');
  const targetId = pick('id', 'targetId');
  const code = pick('c', 'code');
  const occurrenceDate = pick('d', 'occurrenceDate');
  if (!Object.values(AttendanceTargetType).includes(targetType) || !targetId || targetId.length > 200) return null;
  if (code && !/^\d{6}$/.test(code)) return null;
  if (occurrenceDate && !/^\d{4}-\d{2}-\d{2}$/.test(occurrenceDate)) return null;
  const body = { targetType, targetId };
  if (occurrenceDate) body.occurrenceDate = occurrenceDate;
  if (code) { body.code = code; body.method = AttendanceMethod.QR; }
  return body;
}

/** Today's date (or `date`) as `yyyy-MM-dd` in the device's time zone, for occurrence dates. */
export function localIsoDate(date = new Date()) {
  const d = date instanceof Date ? date : new Date(date);
  const pad = (n) => String(n).padStart(2, '0');
  return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
}

/** Whether verified ranks are on: `features.ranks` is true (a missing switch counts as off). */
export function ranksFeatureEnabled(property) {
  return Boolean(property && property.features && property.features.ranks === true);
}

/** Whether profiles and cards show "verified" and "self-reported" labels (`features.rankVerificationLabels`, default on). */
export function rankVerificationLabelsEnabled(property) {
  return ranksFeatureEnabled(property) && property.features.rankVerificationLabels !== false;
}

/**
 * Verification badges keyed by Rank field from a rank response (`GET /me/rank` `fields[].rank` or
 * `GET /members/{id}/rank` `ranks`): `{ [fieldKey]: 'verified' | 'selfReported' }`; empty when the
 * response turns labels off.
 */
export function rankBadgeMap(response) {
  const map = {};
  if (!response || response.showVerificationLabels === false) return map;
  const ranks = Array.isArray(response.ranks) ? response.ranks : Array.isArray(response.fields) ? response.fields.map((f) => f && f.rank) : [];
  for (const rank of ranks) {
    const badge = rank && rank.fieldKey ? verificationBadge(rank, true) : null;
    if (badge) map[rank.fieldKey] = badge;
  }
  return map;
}

/** Rank field keys the caller cannot edit because a verified rank stands (`GET /me/rank`). */
export function verifiedRankFieldKeys(response) {
  const fields = response && Array.isArray(response.fields) ? response.fields : [];
  return fields.map((f) => f && f.rank).filter((r) => r && r.verified && r.fieldKey).map((r) => r.fieldKey);
}

/**
 * Self-reported promoter entries (LineageEntryView) of one Rank field from a rank response
 * (`GET /me/rank` or `GET /members/{id}/rank`), in display order.
 */
export function promotersFor(response, fieldKey) {
  const list = response && Array.isArray(response.lineage) ? response.lineage : [];
  return list.filter((entry) => entry && (!fieldKey || entry.fieldKey === fieldKey));
}

/** Seconds left until a check-in code's `expiresUtc` (0 when past or unknown). */
export function secondsUntil(expiresUtc, now = Date.now()) {
  const at = Date.parse(expiresUtc || '');
  return Number.isFinite(at) ? Math.max(0, Math.ceil((at - now) / 1000)) : 0;
}

/** Whether the caller is rank staff (SystemAdmin or RankAwarder) from `Me.roles`. */
export function isRankStaff(me) {
  const roles = me && Array.isArray(me.roles) ? me.roles.map((r) => (typeof r === 'string' ? r : r && r.role)) : [];
  return roles.includes('SystemAdmin') || roles.includes('RankAwarder') || Boolean(me && me.isAdmin);
}

/** Whether a member can mark a curriculum item complete themselves (CatalogEntry and Custom). */
export function isSelfCompletable(item) {
  return Boolean(item) && (item.itemType === CurriculumItemType.CatalogEntry || item.itemType === CurriculumItemType.Custom);
}

/**
 * Group curriculum items by rank option in the field's order (items without a group last):
 * `[{ optionKey, label, items, completed, total }]`.
 */
export function groupCurriculumItems(field, items) {
  const groups = new Map();
  for (const item of items || []) {
    const key = item.optionKey || '';
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(item);
  }
  return Array.from(groups.entries())
    .map(([key, list]) => ({
      optionKey: key || null,
      label: key ? rankText(field, key, 0) : '',
      items: list,
      completed: list.filter((i) => i.satisfied).length,
      total: list.length
    }))
    .sort((a, b) => {
      const ia = a.optionKey ? rankIndex(field, a.optionKey) : Number.MAX_SAFE_INTEGER;
      const ib = b.optionKey ? rankIndex(field, b.optionKey) : Number.MAX_SAFE_INTEGER;
      return (ia < 0 ? Number.MAX_SAFE_INTEGER - 1 : ia) - (ib < 0 ? Number.MAX_SAFE_INTEGER - 1 : ib);
    });
}
