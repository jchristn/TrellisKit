import { describeError } from '../auth/errors';
import strings from '../strings';

/**
 * Member-facing text for Phase 2 error codes (docs/PHASE2_CONTRACT.md section 1.2), falling back
 * to the Phase 1 copy for everything else.
 * @param {any} error ApiError or UploadError.
 */
export function describeContentError(error) {
  const e = strings.errors;
  if (!error) return e.generic;
  switch (error.code) {
    case 'NotFound':
      return e.notFound;
    case 'Muted':
      return error.message && /\d/.test(error.message) ? error.message : e.muted;
    case 'PostingNotAllowed':
      return e.postingNotAllowed;
    case 'MediaNotAllowed':
      return e.mediaNotAllowed;
    case 'VideoUploadNotAllowed':
      return e.videoUploadNotAllowed;
    case 'MediaLimitExceeded':
      return e.mediaLimitExceeded;
    case 'VideoTooLong':
      return e.videoTooLong;
    case 'ContentFiltered':
      return e.contentFiltered;
    case 'CannotBlockSelf':
      return e.cannotBlockSelf;
    case 'CannotBlockStaff':
      return strings.block.staff;
    case 'CannotReportSelf':
      return e.cannotReportSelf;
    case 'InteractionBlocked':
      return e.interactionBlocked;
    case 'CommentsDisabled':
      return e.commentsDisabled;
    case 'ParentUnavailable':
      return e.parentUnavailable;
    case 'MediaNotAttachable':
      return e.mediaNotAttachable;
    case 'UploadExpired':
      return e.uploadExpired;
    case 'UploadIncomplete':
      return e.uploadIncomplete;
    case 'TooLarge':
      return e.mediaTooLarge;
    case 'UnsupportedMediaType':
    case 'UnsupportedFormat':
      return e.unsupportedMediaType;
    case 'PinLimitReached':
      return e.pinLimitReached;
    case 'LinkPreviewUnavailable':
      return e.linkPreviewUnavailable;
    case 'Unreadable':
      return e.unreadable;
    case 'StorageRejected':
      return e.storageRejected;
    default:
      break;
  }
  if (error.code && e.failureReasons[error.code]) return e.failureReasons[error.code];
  if (error.code && strings.forums.errors[error.code]) return strings.forums.errors[error.code];
  if (error.status === 404) return e.notFound;
  return describeError(error);
}

/** Field errors keyed by field (for example `body`, `title`) from a ValidationFailed or ContentFiltered response. */
export function contentFieldErrors(error) {
  const raw = error && error.body && error.body.fieldErrors;
  if (!raw || typeof raw !== 'object') return {};
  const out = {};
  for (const [key, value] of Object.entries(raw)) out[key] = Array.isArray(value) ? value.join(' ') : String(value);
  return out;
}
