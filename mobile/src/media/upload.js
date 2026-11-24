import { media as mediaApi } from '../api/community';

/**
 * Media uploads through upload sessions (docs/PHASE2_CONTRACT.md section 4).
 *
 * 1. POST /media/uploads declares kind, type, size, file name, and (video) duration.
 * 2. The bytes go straight to storage: one PUT with exactly the listed headers (Single), or one
 *    PUT per part, up to three at a time, read from disk with expo-file-system (Multipart). Each
 *    PUT's ETag is kept verbatim. When storage is the API itself (Local storage mode,
 *    /api/v1/media/local-uploads/*), the PUT also carries the app's X-TK-Property and X-TK-App
 *    headers like every other API request (docs/TRELLISKIT.md section 10.1); presigned S3 URLs
 *    get only the listed headers.
 * 3. POST /media/{id}/complete (with the parts for multipart). The media is then Processing.
 * 4. The composer polls POST /media/enumerate until Ready or Failed.
 *
 * A transfer cut off by the network or by the app going to the background becomes
 * `Interrupted`; `resume()` asks for fresh URLs (POST /media/{id}/upload-urls), which also lists
 * the parts storage already has, and sends only the missing ones.
 */

export const UploadStatus = Object.freeze({
  Preparing: 'Preparing',
  Uploading: 'Uploading',
  Completing: 'Completing',
  Processing: 'Processing',
  Ready: 'Ready',
  Failed: 'Failed',
  Interrupted: 'Interrupted'
});

/** Content types the server accepts (contract 4.1). HEIC and GIF are not among them. */
export const ImageTypes = Object.freeze(['image/jpeg', 'image/png', 'image/webp']);
export const VideoTypes = Object.freeze(['video/mp4', 'video/quicktime']);

/** Parts sent in parallel for a multipart upload. */
export const PartConcurrency = 3;

const EXTENSION_TYPES = { jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp', mp4: 'video/mp4', m4v: 'video/mp4', mov: 'video/quicktime', heic: 'image/heic', heif: 'image/heif', gif: 'image/gif' };

function extensionOf(name) {
  const match = /\.([A-Za-z0-9]+)(?:\?.*)?$/.exec(String(name || ''));
  return match ? match[1].toLowerCase() : '';
}

/** Content type for a picked asset, from its MIME type or file extension. */
export function contentTypeOf(asset) {
  const declared = asset && typeof asset.mimeType === 'string' ? asset.mimeType.toLowerCase() : '';
  if (declared) return declared === 'image/jpg' ? 'image/jpeg' : declared;
  return EXTENSION_TYPES[extensionOf(asset && (asset.fileName || asset.uri))] || '';
}

/** Error with a contract-style code, for checks that fail before the server is involved. */
export class UploadError extends Error {
  constructor(message, code, status = 0) {
    super(message);
    this.name = 'UploadError';
    this.code = code;
    this.status = status;
  }
}

/** Default storage transport: expo-file-system for reading files, fetch and File.upload for PUTs. */
export const defaultTransport = {
  size(uri) {
    const { File } = require('expo-file-system');
    const file = new File(uri);
    return typeof file.size === 'number' ? file.size : null;
  },
  async readRange(uri, start, length) {
    const { File } = require('expo-file-system');
    const handle = new File(uri).open();
    try {
      handle.offset = start;
      return await handle.readBytes(length);
    } finally {
      handle.close();
    }
  },
  async putFile(uri, url, headers, onProgress) {
    const { File, UploadType } = require('expo-file-system');
    const result = await new File(uri).upload(url, {
      httpMethod: 'PUT',
      uploadType: UploadType.BINARY_CONTENT,
      headers: headers || {},
      sessionType: 'foreground',
      onProgress: (p) => onProgress && onProgress(p.bytesSent, p.totalBytes)
    });
    return { status: result.status, etag: headerValue(result.headers, 'etag') };
  },
  async putBytes(url, bytes, headers) {
    const response = await globalThis.fetch(url, { method: 'PUT', body: bytes, headers: headers || {} });
    return { status: response.status, etag: response.headers && response.headers.get ? response.headers.get('etag') : null };
  }
};

const LocalUploadPath = /\/api\/v1\/media\/local-uploads\//;

/**
 * Headers for one storage PUT: the session's listed headers, plus the API identification headers
 * when the URL is the API's own local upload route.
 * @param {object} api the API client (MobileApiClient exposes `identityHeaders()`)
 * @param {string} url
 * @param {Record<string, string>} [listed]
 * @returns {Record<string, string>}
 */
export function storageHeaders(api, url, listed) {
  const own = LocalUploadPath.test(String(url || '')) && api && typeof api.identityHeaders === 'function' ? api.identityHeaders() : {};
  return { ...own, ...(listed || {}) };
}

function headerValue(headers, name) {
  if (!headers) return null;
  for (const key of Object.keys(headers)) if (key.toLowerCase() === name) return headers[key];
  return null;
}

/**
 * Byte ranges of a multipart upload: every part is partSizeBytes except the last.
 * @returns {{ partNumber: number, start: number, length: number }[]}
 */
export function partRanges(sizeBytes, partSizeBytes, partCount) {
  const count = partCount || Math.ceil(sizeBytes / partSizeBytes);
  const out = [];
  for (let n = 1; n <= count; n += 1) {
    const start = (n - 1) * partSizeBytes;
    out.push({ partNumber: n, start, length: Math.max(0, Math.min(partSizeBytes, sizeBytes - start)) });
  }
  return out;
}

function isNetworkFailure(error) {
  if (!error) return false;
  if (error.status === 0 && (error.code === 'NetworkError' || error.code === 'Timeout' || error.code === 'Interrupted')) return true;
  return error instanceof TypeError || /network|abort|cancel/i.test(String(error.message || ''));
}

let localIds = 0;

/**
 * One file's upload. Listeners get a fresh state object on every change:
 * `{ localId, status, progress (0 to 1), file, media, error }`.
 */
export class UploadJob {
  /**
   * @param {object} options
   * @param {object} options.api MobileApiClient.
   * @param {{ uri: string, kind: 'Image'|'Video', contentType: string, sizeBytes: number, fileName?: string, durationSeconds?: number, width?: number, height?: number }} options.file
   * @param {(state: object) => void} [options.onChange]
   * @param {object} [options.transport] Storage transport (tests replace it).
   * @param {string} [options.purpose] Upload purpose (`Message` and `Ticket` keep the media private, docs/PHASE5_CONTRACT.md 3.7 and 6.1).
   */
  constructor({ api, file, onChange, transport = defaultTransport, purpose }) {
    localIds += 1;
    this.api = api;
    this.transport = transport;
    this.onChange = onChange || (() => {});
    this.etags = new Map();
    this.cancelled = false;
    this.purpose = purpose || null;
    this.state = { localId: 'upl_' + localIds, status: UploadStatus.Preparing, progress: 0, file, media: null, error: null };
  }

  _set(patch) {
    this.state = { ...this.state, ...patch };
    if (!this.cancelled) this.onChange(this.state);
  }

  /** Create the session, send the bytes, and complete. Never throws; failures land in state. */
  async start() {
    const { file } = this.state;
    let session;
    try {
      session = await mediaApi.createUpload(this.api, {
        kind: file.kind,
        contentType: file.contentType,
        sizeBytes: file.sizeBytes,
        fileName: file.fileName || undefined,
        durationSeconds: file.kind === 'Video' ? file.durationSeconds : undefined,
        ...(this.purpose ? { purpose: this.purpose } : {})
      });
    } catch (error) {
      this._set({ status: UploadStatus.Failed, error });
      return this.state;
    }
    this.upload = session.upload;
    this._set({ media: session.media, status: UploadStatus.Uploading });
    return this._transferAndComplete(session.upload, { retried: false });
  }

  /** Continue an interrupted upload with fresh URLs, sending only the missing parts. */
  async resume() {
    if (!this.state.media || this.cancelled) return this.state;
    if (![UploadStatus.Interrupted, UploadStatus.Uploading].includes(this.state.status)) return this.state;
    let refreshed;
    try {
      refreshed = await mediaApi.refreshUploadUrls(this.api, this.state.media.id);
    } catch (error) {
      if (isNetworkFailure(error)) this._set({ status: UploadStatus.Interrupted, error });
      else this._set({ status: UploadStatus.Failed, error });
      return this.state;
    }
    this._set({ status: UploadStatus.Uploading, error: null });
    return this._transferAndComplete(refreshed.upload || refreshed, { retried: true });
  }

  /** Stop reporting progress (the composer removed the attachment). */
  cancel() {
    this.cancelled = true;
  }

  async _transferAndComplete(upload, { retried }) {
    try {
      await this._transfer(upload);
    } catch (error) {
      if (this.cancelled) return this.state;
      if (isNetworkFailure(error)) {
        this._set({ status: UploadStatus.Interrupted, error });
        return this.state;
      }
      // Storage refused the PUT (for example an expired URL): get fresh URLs once.
      if (!retried && error && error.code === 'StorageRejected') return this.resume();
      this._set({ status: UploadStatus.Failed, error });
      return this.state;
    }
    if (this.cancelled) return this.state;
    this._set({ status: UploadStatus.Completing, progress: 1 });
    try {
      const mode = upload.mode;
      const parts = mode === 'Multipart' ? Array.from(this.etags.entries()).sort((a, b) => a[0] - b[0]).map(([partNumber, etag]) => ({ partNumber, etag })) : null;
      const completed = await mediaApi.complete(this.api, this.state.media.id, parts);
      this._applyMedia(completed);
    } catch (error) {
      if (error && error.code === 'UploadIncomplete' && !retried) {
        this._set({ status: UploadStatus.Interrupted });
        return this.resume();
      }
      this._set({ status: isNetworkFailure(error) ? UploadStatus.Interrupted : UploadStatus.Failed, error });
    }
    return this.state;
  }

  async _transfer(upload) {
    const { file } = this.state;
    if (upload.mode === 'Single' || upload.single) {
      const single = upload.single;
      const result = await this.transport.putFile(file.uri, single.url, storageHeaders(this.api, single.url, single.headers), (sent, total) => {
        const size = total > 0 ? total : file.sizeBytes;
        this._set({ progress: size ? Math.min(0.99, sent / size) : 0 });
      });
      if (!result || result.status < 200 || result.status >= 300) throw new UploadError('Storage rejected the upload.', 'StorageRejected', result ? result.status : 0);
      this.etags.set(1, result.etag || null);
      return;
    }

    const multipart = upload.multipart;
    const ranges = partRanges(file.sizeBytes, multipart.partSizeBytes, multipart.partCount);
    const urls = new Map((multipart.parts || []).map((p) => [p.partNumber, p.url]));
    for (const done of upload.uploadedParts || []) if (done && done.etag) this.etags.set(done.partNumber, done.etag);
    const pending = ranges.filter((r) => !this.etags.has(r.partNumber));
    const doneBytes = () => ranges.filter((r) => this.etags.has(r.partNumber)).reduce((n, r) => n + r.length, 0);
    this._set({ progress: file.sizeBytes ? doneBytes() / file.sizeBytes : 0 });

    let index = 0;
    const worker = async () => {
      while (index < pending.length && !this.cancelled) {
        const range = pending[index];
        index += 1;
        const url = urls.get(range.partNumber);
        if (!url) throw new UploadError('Missing a part URL.', 'StorageRejected');
        const bytes = await this.transport.readRange(file.uri, range.start, range.length);
        const result = await this.transport.putBytes(url, bytes, storageHeaders(this.api, url));
        if (!result || result.status < 200 || result.status >= 300) throw new UploadError('Storage rejected a part.', 'StorageRejected', result ? result.status : 0);
        this.etags.set(range.partNumber, result.etag);
        this._set({ progress: Math.min(0.99, doneBytes() / file.sizeBytes) });
      }
    };
    const workers = [];
    for (let n = 0; n < Math.min(PartConcurrency, pending.length); n += 1) workers.push(worker());
    await Promise.all(workers);
  }

  _applyMedia(next) {
    if (!next) return;
    const status = next.status === 'Ready' ? UploadStatus.Ready : next.status === 'Failed' ? UploadStatus.Failed : UploadStatus.Processing;
    this._set({ media: next, status, error: status === UploadStatus.Failed ? new UploadError(next.failureReason || 'ProcessingError', next.failureReason || 'ProcessingError') : this.state.error });
  }

  /** Apply a polled Media (POST /media/enumerate). */
  applyPolled(next) {
    if (next && this.state.media && next.id === this.state.media.id && [UploadStatus.Processing, UploadStatus.Completing].includes(this.state.status)) this._applyMedia(next);
  }
}

/**
 * Describe a picked asset for an upload session. Video duration comes from the picker
 * (milliseconds on both platforms) and is sent in seconds.
 * @param {object} asset expo-image-picker asset (after any HEIC conversion).
 * @param {object} [transport]
 */
export function describeAsset(asset, transport = defaultTransport) {
  const contentType = contentTypeOf(asset);
  const isVideo = asset.type === 'video' || contentType.startsWith('video/');
  let sizeBytes = typeof asset.fileSize === 'number' && asset.fileSize > 0 ? asset.fileSize : null;
  if (!sizeBytes) {
    try {
      sizeBytes = transport.size(asset.uri);
    } catch {
      sizeBytes = null;
    }
  }
  const durationSeconds = isVideo && typeof asset.duration === 'number' ? Math.round((asset.duration / 1000) * 10) / 10 : undefined;
  return {
    uri: asset.uri,
    kind: isVideo ? 'Video' : 'Image',
    contentType,
    sizeBytes: sizeBytes || 0,
    fileName: asset.fileName || undefined,
    durationSeconds,
    width: asset.width,
    height: asset.height
  };
}

/**
 * Check a described file against the types the server accepts.
 * @returns {UploadError|null}
 */
export function validateFile(file) {
  if (file.kind === 'Image' && !ImageTypes.includes(file.contentType)) return new UploadError('Unsupported image type.', 'UnsupportedMediaType', 415);
  if (file.kind === 'Video' && !VideoTypes.includes(file.contentType)) return new UploadError('Unsupported video type.', 'UnsupportedMediaType', 415);
  if (!file.sizeBytes) return new UploadError('The file could not be read.', 'Unreadable');
  return null;
}

/** Poll intervals (contract 4.5): images every 2 seconds for up to 60 seconds; video every 5 seconds. */
export const PollIntervals = Object.freeze({ Image: 2000, Video: 5000, imageLimitMs: 60000 });
