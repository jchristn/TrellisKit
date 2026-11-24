/**
 * Labels and tags on every object (docs/ENUMERATION_AND_METADATA.md section 1).
 *
 * `labels` is a list of strings (case-insensitively unique, at most 50, each 1 to 64 characters of
 * letters, digits, spaces, and `_ - . : /`). `tags` maps keys (same rules) to values (0 to 512
 * characters), at most 50 keys. Anything starting with `staff:` is staff-only metadata.
 */

export const METADATA_MAX_LABELS = 50;
export const METADATA_MAX_TAGS = 50;
export const METADATA_LABEL_MAX_LENGTH = 64;
export const METADATA_TAG_VALUE_MAX_LENGTH = 512;
export const STAFF_PREFIX = 'staff:';

const NAME = /^[\p{L}\p{N} _\-.:/]{1,64}$/u;

/** Whether a label or tag key is staff-only (`staff:` prefix, case-insensitive). */
export function isStaffKey(value) {
  return String(value || '').trim().toLowerCase().startsWith(STAFF_PREFIX);
}

/** Trim a label or tag key. */
export function normalizeMetadataName(value) {
  return String(value === null || value === undefined ? '' : value).trim();
}

/** Whether a trimmed label or tag key uses the allowed characters and length. */
export function isValidMetadataName(value) {
  return NAME.test(normalizeMetadataName(value));
}

/**
 * Validate and clean metadata. Returns `{ labels, tags, problems }` where `problems` lists codes:
 * `labelInvalid:<label>`, `labelsTooMany`, `tagKeyInvalid:<key>`, `tagValueTooLong:<key>`,
 * `tagsTooMany`, `staffNotAllowed:<name>` (non-staff only). Duplicate labels and keys
 * (case-insensitive) are merged, keeping the first casing.
 * @param {{ labels?: string[], tags?: Record<string,string> }} metadata
 * @param {{ isStaff?: boolean }} [options]
 */
export function validateMetadata({ labels = [], tags = {} } = {}, { isStaff = false } = {}) {
  const problems = [];
  const outLabels = [];
  const seen = new Set();
  for (const raw of labels || []) {
    const label = normalizeMetadataName(raw);
    if (!label) continue;
    if (!isValidMetadataName(label)) { problems.push('labelInvalid:' + label); continue; }
    if (!isStaff && isStaffKey(label)) { problems.push('staffNotAllowed:' + label); continue; }
    if (seen.has(label.toLowerCase())) continue;
    seen.add(label.toLowerCase());
    outLabels.push(label);
  }
  if (outLabels.length > METADATA_MAX_LABELS) problems.push('labelsTooMany');
  const outTags = {};
  const keys = new Set();
  for (const [rawKey, rawValue] of Object.entries(tags || {})) {
    const key = normalizeMetadataName(rawKey);
    if (!key) continue;
    if (!isValidMetadataName(key)) { problems.push('tagKeyInvalid:' + key); continue; }
    if (!isStaff && isStaffKey(key)) { problems.push('staffNotAllowed:' + key); continue; }
    if (keys.has(key.toLowerCase())) continue;
    const value = rawValue === null || rawValue === undefined ? '' : String(rawValue);
    if (value.length > METADATA_TAG_VALUE_MAX_LENGTH) { problems.push('tagValueTooLong:' + key); continue; }
    keys.add(key.toLowerCase());
    outTags[key] = value;
  }
  if (keys.size > METADATA_MAX_TAGS) problems.push('tagsTooMany');
  return { labels: outLabels, tags: outTags, problems };
}

/**
 * Remove staff-only metadata (what a non-staff viewer may see).
 * @param {{ labels?: string[], tags?: Record<string,string> }} metadata
 */
export function stripStaffMetadata({ labels = [], tags = {} } = {}) {
  return {
    labels: (labels || []).filter((l) => !isStaffKey(l)),
    tags: Object.fromEntries(Object.entries(tags || {}).filter(([k]) => !isStaffKey(k)))
  };
}

/**
 * Split metadata into the public part and the staff-only part.
 * @param {{ labels?: string[], tags?: Record<string,string> }} metadata
 */
export function splitStaffMetadata({ labels = [], tags = {} } = {}) {
  const staffTags = Object.fromEntries(Object.entries(tags || {}).filter(([k]) => isStaffKey(k)));
  return {
    publicPart: stripStaffMetadata({ labels, tags }),
    staffPart: { labels: (labels || []).filter((l) => isStaffKey(l)), tags: staffTags }
  };
}

/**
 * Metadata a member editor should send: the edited public part plus any staff-only metadata the
 * object already had is not sent (the server keeps it; members never send `staff:` keys).
 * For staff editors the whole edited set is sent.
 * @param {{ labels: string[], tags: Record<string,string> }} edited
 * @param {{ isStaff?: boolean }} [options]
 */
export function metadataForSave(edited, { isStaff = false } = {}) {
  const { labels, tags } = validateMetadata(edited, { isStaff });
  return isStaff ? { labels, tags } : stripStaffMetadata({ labels, tags });
}

/**
 * Labels and tags of a payload, always as `{ labels: [], tags: {} }` (payloads always include them,
 * but older servers may not).
 * @param {object} source
 */
export function metadataOf(source) {
  return {
    labels: Array.isArray(source && source.labels) ? source.labels : [],
    tags: source && source.tags && typeof source.tags === 'object' && !Array.isArray(source.tags) ? source.tags : {}
  };
}
