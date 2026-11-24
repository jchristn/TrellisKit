import { isStaffKey } from './enumeration';

/**
 * Labels and tags on objects (docs/ENUMERATION_AND_METADATA.md section 1).
 */

export const MetadataLimits = Object.freeze({ labels: 50, tags: 50, length: 64, value: 512 });

const NAME = /^[A-Za-z0-9 _\-.:/]{1,64}$/;

/** Whether a label or tag key is well formed (1 to 64 of letters, digits, spaces, and _ - . : /). */
export function isValidName(text) {
  return typeof text === 'string' && NAME.test(text.trim()) && text.trim().length > 0;
}

/** An object's metadata, always present: `{ labels: [], tags: {} }`. */
export function metadataOf(object) {
  return {
    labels: object && Array.isArray(object.labels) ? object.labels.slice() : [],
    tags: object && object.tags && typeof object.tags === 'object' ? { ...object.tags } : {}
  };
}

/**
 * Metadata to send on create or update (replace semantics). Members never send `staff:` entries
 * (the server would ignore them); staff keep them. Labels are trimmed and de-duplicated
 * case-insensitively.
 */
export function metadataPayload(value, { staff = false } = {}) {
  const seen = new Set();
  const labels = [];
  for (const raw of (value && value.labels) || []) {
    const label = String(raw).trim();
    const key = label.toLowerCase();
    if (!label || seen.has(key) || (!staff && isStaffKey(label))) continue;
    seen.add(key);
    labels.push(label);
  }
  const tags = {};
  for (const [rawKey, rawValue] of Object.entries((value && value.tags) || {})) {
    const key = String(rawKey).trim();
    if (!key || (!staff && isStaffKey(key))) continue;
    tags[key] = String(rawValue == null ? '' : rawValue).slice(0, MetadataLimits.value);
  }
  return { labels: labels.slice(0, MetadataLimits.labels), tags };
}

/** Whether two metadata values differ. */
export function metadataChanged(a, b) {
  return JSON.stringify(metadataPayload(a, { staff: true })) !== JSON.stringify(metadataPayload(b, { staff: true }));
}
