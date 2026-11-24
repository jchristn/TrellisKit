import { directoryConfig, formatRankValue, profileFieldsOf } from '@trelliskit/shared';
import { useMemo } from 'react';
import { useProperty } from '../property';
import strings, { fillPlaces } from '../strings';
import { describeContentError } from './errors';

/**
 * Phase 4 helpers shared by the directory, events, and library screens
 * (docs/PHASE4_CONTRACT.md sections 0.2 and 0.3).
 */

/**
 * The directory strings with the configured terms filled in: `{venue}`, `{venues}`,
 * `{instructor}`, `{instructors}` come from GET /api/v1/property `directory`.
 * @param {object} property
 */
export function directoryStrings(property) {
  return fillPlaces(property);
}

/** `directoryStrings` for the current property, memoized. */
export function useDirectoryStrings() {
  const property = useProperty();
  return useMemo(() => directoryStrings(property), [property]);
}

/**
 * Member-facing text for an error from a Phase 4 route: the feature's own codes (`EventFull`,
 * `DownloadLimitReached`, `SubmissionLimitReached`, the `FeatureDisabled` kill switch), else the
 * Phase 2 copy.
 * @param {any} error ApiError
 * @param {'places'|'events'|'library'} section
 * @param {object} [sectionStrings] Filled strings for the section (the directory needs its terms).
 */
export function describeFeatureError(error, section, sectionStrings) {
  const table = ((sectionStrings || strings[section]) || {}).errors || {};
  if (error && error.code && table[error.code]) return table[error.code];
  if (error && error.status === 503 && table.FeatureDisabled) return table.FeatureDisabled;
  return describeContentError(error);
}

/** Whether an error is the feature's kill switch (`503 FeatureDisabled`). */
export function isFeatureDisabled(error) {
  return Boolean(error && (error.code === 'FeatureDisabled' || (error.status === 503 && !error.code)));
}

/**
 * Whether a Phase 4 feature is switched on for the property (`features.<key>`, absent means on).
 * When off, every entry point is hidden.
 * @param {object} property
 * @param {'directory'|'events'|'library'} key
 */
export function featureOn(property, key) {
  const features = property && property.features;
  return !(features && features[key] === false);
}

/** "5:42" or "1:02:03" for a number of seconds. */
export function formatClock(totalSeconds) {
  const n = Math.max(0, Math.floor(Number(totalSeconds) || 0));
  const h = Math.floor(n / 3600);
  const m = Math.floor((n % 3600) / 60);
  const s = n % 60;
  const pad = (v) => String(v).padStart(2, '0');
  return h ? h + ':' + pad(m) + ':' + pad(s) : m + ':' + pad(s);
}

/** "12 min" or "1 h 5 min" for a duration. */
export function formatDurationShort(totalSeconds) {
  const minutes = Math.max(1, Math.round((Number(totalSeconds) || 0) / 60));
  if (minutes < 60) return minutes + ' min';
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m ? h + ' h ' + m + ' min' : h + ' h';
}

/**
 * An instructor's rank text with the property's rank field (`directory.instructorRankFieldKey`),
 * or '' when rank is off or unset.
 * @param {object} property
 * @param {{ rank?: string|null, rankLevel?: number|null }} instructor
 */
export function instructorRankText(property, instructor) {
  if (!instructor || !instructor.rank) return '';
  const key = directoryConfig(property).instructorRankFieldKey;
  const field = key ? profileFieldsOf(property).find((f) => f && f.key === key) : null;
  return field ? formatRankValue(field, { key: instructor.rank, level: instructor.rankLevel || 0 }) : String(instructor.rank);
}

/** Best URL of an image Media variant (WebP, else JPEG), or null. */
export function imageUrl(media, name = 'medium') {
  const variants = media && media.image && media.image.variants;
  const v = variants && (variants[name] || variants.medium || variants.large || variants.thumb);
  return v ? v.webpUrl || v.jpegUrl || null : null;
}

/**
 * URL of a `Document` Media (PDF or WebVTT, docs/PHASE4_CONTRACT.md section 5.3). The Phase 4
 * Media shape names it `document.url`; `fileUrl` and `url` are accepted too.
 */
export function documentUrl(media) {
  if (!media) return null;
  return (media.document && (media.document.url || media.document.fileUrl)) || media.fileUrl || media.url || null;
}

/** The device's IANA time zone. */
export function deviceTimeZone() {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

/** "1.2 GB", "340 MB", or "12 KB". */
export function formatBytes(bytes) {
  const n = Math.max(0, Number(bytes) || 0);
  if (n >= 1e9) return (n / 1e9).toFixed(1) + ' GB';
  if (n >= 1e6) return Math.round(n / 1e6) + ' MB';
  return Math.max(1, Math.round(n / 1e3)) + ' KB';
}
