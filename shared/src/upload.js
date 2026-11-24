import { collectEnumeration } from './enumeration.js';
/**
 * Media upload helper (docs/PHASE2_CONTRACT.md section 4), shared by the website and the mobile
 * app. It is environment agnostic: the caller supplies `slice(start, end)` (returns the bytes
 * for a range: `Blob.slice` on the web, a file read on mobile) and `put(request)` (sends one PUT
 * and resolves `{ etag }`; the web passes an XMLHttpRequest version for upload progress).
 *
 * Flow: create a session, PUT the bytes (one request, or parts up to `concurrency` at a time),
 * complete, then poll until the media is Ready or Failed. Interrupted uploads resume through
 * `POST /media/{id}/upload-urls`, which returns fresh URLs and the parts storage already has, so
 * only the missing parts are sent again.
 */

const MiB = 1024 * 1024;

/** Accepted content types per kind (section 4.1). */
export const MediaContentTypes = Object.freeze({
  Image: Object.freeze(['image/jpeg', 'image/png', 'image/webp']),
  Video: Object.freeze(['video/mp4', 'video/quicktime'])
});

/** Limits from the contract defaults (sections 4.1, 5.4, 7.5, 14). The server enforces the live values. */
export const DEFAULT_MEDIA_LIMITS = Object.freeze({
  maxImageBytes: 20 * MiB,
  maxImagePixels: 40000000,
  maxMemberVideoBytes: 2048 * MiB,
  maxMemberVideoSeconds: 300,
  maxAdminVideoBytes: 0,
  maxAdminVideoSeconds: 0,
  multipartThresholdBytes: 32 * MiB,
  maxPostAttachments: 10,
  maxVideosPerPost: 1,
  maxCommentAttachments: 1,
  maxAltTextLength: 1000
});

/** Polling cadence (section 4.5): images every 2 s for up to 60 s; video every 5 s while open. */
export const MEDIA_POLL = Object.freeze({
  Image: Object.freeze({ intervalMs: 2000, timeoutMs: 60000 }),
  Video: Object.freeze({ intervalMs: 5000, timeoutMs: null })
});

/**
 * Media kind for a content type, or null when not accepted.
 * @param {string} contentType
 * @returns {'Image'|'Video'|null}
 */
export function mediaKindFor(contentType) {
  const type = String(contentType || '').toLowerCase();
  if (MediaContentTypes.Image.includes(type)) return 'Image';
  if (MediaContentTypes.Video.includes(type)) return 'Video';
  return null;
}

/**
 * Check a file against the contract limits before creating a session. Returns `{ kind, problem }`
 * where `problem` is null or an error code the server would return: `UnsupportedMediaType`,
 * `TooLarge`, `VideoTooLong`, or `DurationRequired`.
 * @param {{ contentType: string, sizeBytes: number, durationSeconds?: number|null }} file
 * @param {{ isAdmin?: boolean, limits?: object }} [options]
 */
export function checkMediaFile({ contentType, sizeBytes, durationSeconds = null }, { isAdmin = false, limits = DEFAULT_MEDIA_LIMITS } = {}) {
  const kind = mediaKindFor(contentType);
  if (!kind) return { kind: null, problem: 'UnsupportedMediaType' };
  const size = Number(sizeBytes) || 0;
  if (size < 1) return { kind, problem: 'TooLarge' };
  if (kind === 'Image') return { kind, problem: size > limits.maxImageBytes ? 'TooLarge' : null };
  const maxBytes = isAdmin ? limits.maxAdminVideoBytes : limits.maxMemberVideoBytes;
  const maxSeconds = isAdmin ? limits.maxAdminVideoSeconds : limits.maxMemberVideoSeconds;
  if (maxBytes > 0 && size > maxBytes) return { kind, problem: 'TooLarge' };
  if (durationSeconds === null || durationSeconds === undefined || !Number.isFinite(Number(durationSeconds))) return { kind, problem: 'DurationRequired' };
  if (maxSeconds > 0 && Number(durationSeconds) > maxSeconds) return { kind, problem: 'VideoTooLong' };
  return { kind, problem: null };
}

/** Error for a failed storage PUT. `status` 0 means the network failed. */
export class UploadError extends Error {
  /**
   * @param {string} message
   * @param {{ status?: number, code?: string|null }} [options]
   */
  constructor(message, { status = 0, code = null } = {}) {
    super(message);
    this.name = 'UploadError';
    this.status = status;
    this.code = code;
  }
}

function abortError() {
  const err = new Error('The upload was cancelled.');
  err.name = 'AbortError';
  return err;
}

function throwIfAborted(signal) {
  if (signal && signal.aborted) throw abortError();
}

function isAbort(err) {
  return Boolean(err && err.name === 'AbortError');
}

/** Storage failures worth retrying with fresh URLs: network, expired signatures, throttling, server errors. */
function isRetryable(err) {
  if (!err || isAbort(err)) return false;
  const status = Number(err.status) || 0;
  return status === 0 || status === 403 || status === 408 || status === 429 || status >= 500 || err.code === 'UploadIncomplete';
}

/**
 * A `put` implementation over `fetch` (no upload progress; the web uses XMLHttpRequest instead).
 * @param {typeof fetch} [fetchImpl]
 */
export function createFetchPut(fetchImpl = globalThis.fetch && globalThis.fetch.bind(globalThis)) {
  return async ({ url, method = 'PUT', headers = {}, body, signal, onProgress }) => {
    let response;
    try {
      response = await fetchImpl(url, { method, headers, body, signal });
    } catch (err) {
      if (isAbort(err)) throw err;
      throw new UploadError('The upload could not reach storage.', { status: 0 });
    }
    if (!response.ok) throw new UploadError('Storage rejected the upload (status ' + response.status + ').', { status: response.status });
    if (onProgress && body && typeof body.size === 'number') onProgress(body.size);
    return { etag: response.headers.get('etag') };
  };
}

async function runPool(tasks, concurrency, signal) {
  let next = 0;
  let failure = null;
  const worker = async () => {
    while (next < tasks.length && !failure) {
      const task = tasks[next];
      next += 1;
      try {
        throwIfAborted(signal);
        await task();
      } catch (err) {
        failure = failure || err;
      }
    }
  };
  await Promise.all(Array.from({ length: Math.max(1, Math.min(concurrency, tasks.length)) }, worker));
  if (failure) throw failure;
}

function partRange(partNumber, partSize, total) {
  const start = (partNumber - 1) * partSize;
  return { start, end: Math.min(total, start + partSize) };
}

/**
 * Upload the bytes for a session and complete it, resuming through `upload-urls` on retryable
 * failures. Exported for resuming an interrupted upload (`resumeUpload`).
 */
async function transferAndComplete({ client, media, upload, size, slice, put, onProgress, concurrency, signal, maxAttempts }) {
  const done = new Map();
  const remember = (parts) => {
    for (const p of parts || []) if (p && p.partNumber && p.etag) done.set(p.partNumber, { etag: p.etag, sizeBytes: p.sizeBytes });
  };
  remember(upload.uploadedParts);
  let singleEtag = null;
  const inFlight = new Map();
  const report = (phase) => {
    if (!onProgress) return;
    let loaded = 0;
    if (upload.mode === 'Single') loaded = singleEtag ? size : (inFlight.get(0) || 0);
    else {
      const partSize = upload.multipart.partSizeBytes;
      for (const [n] of done) {
        const r = partRange(n, partSize, size);
        loaded += r.end - r.start;
      }
      for (const [, bytes] of inFlight) loaded += bytes;
    }
    onProgress({ phase, loadedBytes: Math.min(size, loaded), totalBytes: size, mediaId: media.id });
  };

  let attempt = 0;
  let current = upload;
  for (;;) {
    throwIfAborted(signal);
    try {
      if (current.mode === 'Single') {
        if (!singleEtag) {
          const single = current.single;
          report('uploading');
          const result = await put({
            url: single.url,
            method: single.method || 'PUT',
            headers: single.headers || {},
            body: slice(0, size),
            signal,
            onProgress: (bytes) => { inFlight.set(0, bytes); report('uploading'); }
          });
          inFlight.delete(0);
          singleEtag = (result && result.etag) || 'single';
          report('uploading');
        }
        report('completing');
        return await client.completeUpload(media.id);
      }

      const { partSizeBytes, parts } = current.multipart;
      const missing = parts.filter((p) => !done.has(p.partNumber));
      report('uploading');
      await runPool(missing.map((part) => async () => {
        const { start, end } = partRange(part.partNumber, partSizeBytes, size);
        const result = await put({
          url: part.url,
          method: part.method || 'PUT',
          headers: part.headers || {},
          body: slice(start, end),
          signal,
          onProgress: (bytes) => { inFlight.set(part.partNumber, bytes); report('uploading'); }
        });
        inFlight.delete(part.partNumber);
        if (!result || !result.etag) throw new UploadError('Storage did not return an ETag for part ' + part.partNumber + '.', { status: 0 });
        done.set(part.partNumber, { etag: result.etag, sizeBytes: end - start });
        report('uploading');
      }), concurrency, signal);
      report('completing');
      const sorted = Array.from(done.entries()).sort((a, b) => a[0] - b[0]).map(([partNumber, v]) => ({ partNumber, etag: v.etag }));
      return await client.completeUpload(media.id, sorted);
    } catch (err) {
      inFlight.clear();
      if (isAbort(err) || (signal && signal.aborted)) throw abortError();
      attempt += 1;
      if (attempt >= maxAttempts || !isRetryable(err)) throw err;
      const refreshed = await client.refreshUploadUrls(media.id);
      current = refreshed && refreshed.upload ? refreshed.upload : refreshed;
      upload = current;
      if (err && err.code === 'UploadIncomplete') {
        // Storage is the source of truth for what arrived: resend everything it does not list.
        singleEtag = null;
        done.clear();
      }
      remember(current.uploadedParts);
    }
  }
}

/**
 * Upload one file: create the session, send the bytes, complete. Resolves the Media returned by
 * `complete` (status Processing, or Ready when already processed). Use `pollMedia` afterwards.
 *
 * @param {object} options
 * @param {object} options.client ApiClient (createUpload, refreshUploadUrls, completeUpload)
 * @param {number} options.size Total bytes
 * @param {string} options.contentType
 * @param {string} [options.fileName]
 * @param {'Image'|'Video'} [options.kind] Defaults from the content type
 * @param {number|null} [options.durationSeconds] Required for video
 * @param {(start: number, end: number) => any} options.slice
 * @param {(request: { url: string, method: string, headers: object, body: any, signal?: AbortSignal, onProgress?: (bytes: number) => void }) => Promise<{ etag: string|null }>} options.put
 * @param {(progress: { phase: 'creating'|'uploading'|'completing', loadedBytes: number, totalBytes: number, mediaId: string|null }) => void} [options.onProgress]
 * @param {(session: { media: object, upload: object }) => void} [options.onSession] Called once the session exists (keep `media.id` to resume)
 * @param {number} [options.concurrency] Parallel part uploads (default 3)
 * @param {number} [options.maxAttempts] Attempts including resumes (default 4)
 * @param {AbortSignal} [options.signal]
 * @returns {Promise<object>} Media
 */
export async function uploadMedia({ client, size, contentType, fileName, kind, durationSeconds = null, slice, put, onProgress, onSession, concurrency = 3, maxAttempts = 4, signal }) {
  throwIfAborted(signal);
  if (onProgress) onProgress({ phase: 'creating', loadedBytes: 0, totalBytes: size, mediaId: null });
  const body = { kind: kind || mediaKindFor(contentType), contentType, sizeBytes: size };
  if (fileName) body.fileName = String(fileName).slice(0, 255);
  if (body.kind === 'Video') body.durationSeconds = durationSeconds;
  const session = await client.createUpload(body);
  if (onSession) onSession(session);
  return transferAndComplete({ client, media: session.media, upload: session.upload, size, slice, put, onProgress, concurrency, signal, maxAttempts });
}

/**
 * Resume an interrupted upload for an existing Uploading media id: fetch fresh URLs and the parts
 * storage already has, send only the missing ones, and complete.
 * @param {object} options Same as `uploadMedia` plus `mediaId`.
 * @returns {Promise<object>} Media
 */
export async function resumeUpload({ client, mediaId, size, slice, put, onProgress, concurrency = 3, maxAttempts = 4, signal }) {
  throwIfAborted(signal);
  const refreshed = await client.refreshUploadUrls(mediaId);
  const upload = refreshed && refreshed.upload ? refreshed.upload : refreshed;
  return transferAndComplete({ client, media: { id: mediaId }, upload, size, slice, put, onProgress, concurrency, signal, maxAttempts });
}

const defaultSleep = (ms, signal) => new Promise((resolve, reject) => {
  const timer = setTimeout(resolve, ms);
  if (signal) signal.addEventListener('abort', () => { clearTimeout(timer); reject(abortError()); }, { once: true });
});

/** Whether a media status is final. */
export function isMediaSettled(media) {
  return Boolean(media && (media.status === 'Ready' || media.status === 'Failed'));
}

/**
 * Poll `POST /media/enumerate` until every item is Ready or Failed, or until `timeoutMs` passes
 * (null polls until aborted). `onUpdate(items)` runs after every poll. Resolves
 * `{ items, timedOut }`.
 * @param {{ client: object, ids: string[], intervalMs?: number, timeoutMs?: number|null, onUpdate?: (items: object[]) => void, signal?: AbortSignal, sleep?: (ms: number, signal?: AbortSignal) => Promise<void>, now?: () => number }} options
 */
export async function pollMedia({ client, ids, intervalMs = MEDIA_POLL.Image.intervalMs, timeoutMs = MEDIA_POLL.Image.timeoutMs, onUpdate, signal, sleep = defaultSleep, now = () => Date.now() }) {
  const started = now();
  let items = [];
  const pending = new Set(ids);
  const latest = new Map();
  for (;;) {
    throwIfAborted(signal);
    const batch = Array.from(pending).slice(0, 20);
    const response = await collectEnumeration((query, pageSignal) => client.getMediaBatch(batch, query, pageSignal), { maxResults: 20 }, { signal });
    for (const item of response.objects) {
      latest.set(item.id, item);
      if (isMediaSettled(item)) pending.delete(item.id);
    }
    items = ids.map((id) => latest.get(id)).filter(Boolean);
    if (onUpdate) onUpdate(items);
    if (pending.size === 0) return { items, timedOut: false };
    if (timeoutMs !== null && timeoutMs !== undefined && now() - started + intervalMs > timeoutMs) return { items, timedOut: true };
    await sleep(intervalMs, signal);
  }
}
