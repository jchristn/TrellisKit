import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { DEFAULT_MEDIA_LIMITS, MEDIA_POLL, checkMediaFile, isMediaSettled, mediaKindFor, pollMedia, resumeUpload, uploadMedia } from '@trelliskit/shared';
import strings from '../../strings.js';
import { errorText } from '../../utils/engagement.js';
import { xhrPut } from '../../utils/xhrPut.js';

const c = strings.composer;
let nextKey = 1;

/** Read a video file's duration in seconds through a detached <video> element. */
export function readVideoDuration(file) {
  return new Promise((resolve) => {
    if (typeof document === 'undefined' || typeof URL === 'undefined' || !URL.createObjectURL) {
      resolve(null);
      return;
    }
    const url = URL.createObjectURL(file);
    const video = document.createElement('video');
    const done = (value) => {
      URL.revokeObjectURL(url);
      resolve(value);
    };
    video.preload = 'metadata';
    video.onloadedmetadata = () => done(Number.isFinite(video.duration) ? video.duration : null);
    video.onerror = () => done(null);
    video.src = url;
  });
}

function fromMedia(media) {
  return { key: 'm' + (nextKey++), media, mediaId: media.id, kind: media.kind, status: media.status === 'Ready' ? 'ready' : media.status === 'Failed' ? 'failed' : 'processing', progress: 1, error: '', altText: media.altText || '', existing: true };
}

/**
 * Attachments for a composer (contract section 4): validate files against the contract limits
 * and the member's permissions, upload each (session, PUT or multipart with progress, complete),
 * resume interrupted uploads, poll until Ready, edit alt text, reorder, and remove (deleting
 * unattached uploads). Existing attachments (editing) start Ready.
 *
 * @param {{ client: object, initial?: object[], maxCount: number, maxVideos?: number, allowMedia: boolean, allowVideo: boolean,
 *   isAdmin?: boolean, put?: Function, readDuration?: (file: File) => Promise<number|null>, sleep?: Function, onError?: (message: string) => void,
 *   purpose?: string, scopeId?: string|null }} options
 *
 * `purpose` and `scopeId` are added to the upload session request (Phase 5: `Message` with the
 * conversation id, `Ticket` with the ticket id) so the server stores the file under that scope.
 */
export function useMediaUploads({ client: baseClient, initial = [], maxCount, maxVideos = DEFAULT_MEDIA_LIMITS.maxVideosPerPost, allowMedia, allowVideo, isAdmin = false, put = xhrPut, readDuration = readVideoDuration, sleep, onError, purpose = null, scopeId = null }) {
  const client = useMemo(() => {
    if (!purpose || !baseClient) return baseClient;
    const scoped = Object.create(baseClient);
    scoped.createUpload = (body) => baseClient.createUpload({ ...body, purpose, ...(scopeId ? { scopeId } : {}) });
    return scoped;
  }, [baseClient, purpose, scopeId]);
  const [items, setItems] = useState(() => initial.map(fromMedia));
  const itemsRef = useRef(items);
  const controllers = useRef(new Map());
  const files = useRef(new Map());

  useEffect(() => {
    itemsRef.current = items;
  }, [items]);

  useEffect(() => () => {
    for (const controller of controllers.current.values()) controller.abort();
  }, []);

  const patch = useCallback((key, changes) => {
    setItems((list) => list.map((item) => (item.key === key ? { ...item, ...(typeof changes === 'function' ? changes(item) : changes) } : item)));
  }, []);

  const watch = useCallback((key, media) => {
    if (!media || isMediaSettled(media)) {
      patch(key, { media, status: media && media.status === 'Failed' ? 'failed' : 'ready', progress: 1 });
      return;
    }
    const controller = new AbortController();
    controllers.current.set(key, controller);
    const cadence = MEDIA_POLL[media.kind] || MEDIA_POLL.Image;
    patch(key, { media, status: 'processing', progress: 1 });
    pollMedia({ client, ids: [media.id], intervalMs: cadence.intervalMs, timeoutMs: cadence.timeoutMs, signal: controller.signal, sleep, onUpdate: (list) => {
      const latest = list[0];
      if (latest) patch(key, { media: latest, status: latest.status === 'Ready' ? 'ready' : latest.status === 'Failed' ? 'failed' : 'processing' });
    } }).catch(() => {}).finally(() => controllers.current.delete(key));
  }, [client, patch, sleep]);

  const run = useCallback(async (key, resume) => {
    const file = files.current.get(key);
    const item = itemsRef.current.find((x) => x.key === key);
    if (!file || !item) return;
    const controller = new AbortController();
    controllers.current.set(key, controller);
    patch(key, { status: 'creating', error: '', progress: 0 });
    const onProgress = (p) => patch(key, p.mediaId ? { status: p.phase, progress: p.totalBytes ? p.loadedBytes / p.totalBytes : 0, mediaId: p.mediaId } : { status: p.phase, progress: p.totalBytes ? p.loadedBytes / p.totalBytes : 0 });
    const common = { client, size: file.size, slice: (s, e) => file.slice(s, e), put, onProgress, signal: controller.signal };
    try {
      const media = resume && item.mediaId
        ? await resumeUpload({ ...common, mediaId: item.mediaId })
        : await uploadMedia({ ...common, contentType: file.type, fileName: file.name, kind: item.kind, durationSeconds: item.durationSeconds, onSession: (s) => patch(key, { mediaId: s.media.id, media: s.media }) });
      controllers.current.delete(key);
      watch(key, media);
    } catch (err) {
      controllers.current.delete(key);
      if (err && err.name === 'AbortError') return;
      const expired = err && err.code === 'UploadExpired';
      patch(key, (current) => ({ status: 'error', error: errorText(err, c.errors), canResume: Boolean(current.mediaId) && !expired, mediaId: expired ? null : current.mediaId }));
    }
  }, [client, put, patch, watch]);

  const addFiles = useCallback(async (fileList) => {
    const incoming = Array.from(fileList || []);
    const problems = [];
    const current = itemsRef.current;
    let count = current.length;
    let videos = current.filter((x) => x.kind === 'Video').length;
    const accepted = [];
    for (const file of incoming) {
      const kind = mediaKindFor(file.type);
      if (!allowMedia) { problems.push(c.mediaNotAllowed); break; }
      if (count >= maxCount) { problems.push(c.attachLimit(maxCount)); break; }
      if (kind === 'Video' && !allowVideo) { problems.push(c.videoNotAllowed); continue; }
      if (kind === 'Video' && videos >= maxVideos) { problems.push(c.oneVideo); continue; }
      const durationSeconds = kind === 'Video' ? await readDuration(file) : null;
      const check = checkMediaFile({ contentType: file.type, sizeBytes: file.size, durationSeconds }, { isAdmin });
      if (check.problem) { problems.push(file.name + ': ' + (c.errors[check.problem] || check.problem)); continue; }
      const key = 'u' + (nextKey++);
      files.current.set(key, file);
      accepted.push({ key, media: null, mediaId: null, kind, durationSeconds, fileName: file.name, status: 'creating', progress: 0, error: '', altText: '', existing: false });
      count += 1;
      if (kind === 'Video') videos += 1;
    }
    if (accepted.length) {
      itemsRef.current = [...itemsRef.current, ...accepted];
      setItems(itemsRef.current);
      for (const item of accepted) run(item.key, false);
    }
    if (problems.length && onError) onError(problems.join(' '));
  }, [allowMedia, allowVideo, isAdmin, maxCount, maxVideos, onError, readDuration, run]);

  const remove = useCallback((key) => {
    const item = itemsRef.current.find((x) => x.key === key);
    const controller = controllers.current.get(key);
    if (controller) controller.abort();
    controllers.current.delete(key);
    files.current.delete(key);
    setItems((list) => list.filter((x) => x.key !== key));
    // Uploads made in this composer are unattached until saved; delete them now. Existing
    // attachments are detached by saving the edited mediaIds instead.
    if (item && item.mediaId && !item.existing && typeof client.deleteMedia === 'function') client.deleteMedia(item.mediaId).catch(() => {});
  }, [client]);

  const retry = useCallback((key) => {
    const item = itemsRef.current.find((x) => x.key === key);
    if (item) run(key, Boolean(item.canResume));
  }, [run]);

  const move = useCallback((key, delta) => {
    setItems((list) => {
      const index = list.findIndex((x) => x.key === key);
      const target = index + delta;
      if (index < 0 || target < 0 || target >= list.length) return list;
      const next = list.slice();
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }, []);

  const setAlt = useCallback((key, altText) => patch(key, { altText }), [patch]);

  const saveAlt = useCallback(async (key) => {
    const item = itemsRef.current.find((x) => x.key === key);
    if (!item || !item.mediaId) return;
    if ((item.media && (item.media.altText || '')) === item.altText) return;
    try {
      const media = await client.updateMedia(item.mediaId, { altText: item.altText });
      if (media && media.id) patch(key, { media: { ...(item.media || {}), ...media } });
    } catch (err) {
      if (onError) onError(errorText(err, c.errors));
    }
  }, [client, onError, patch]);

  const reset = useCallback(() => {
    for (const controller of controllers.current.values()) controller.abort();
    controllers.current.clear();
    files.current.clear();
    setItems([]);
  }, []);

  const busy = items.some((x) => ['creating', 'uploading', 'completing'].includes(x.status));
  const hasErrors = items.some((x) => x.status === 'error');
  const mediaIds = items.filter((x) => x.mediaId && x.status !== 'error').map((x) => x.mediaId);

  return { items, addFiles, remove, retry, move, setAlt, saveAlt, reset, busy, hasErrors, mediaIds };
}

export default useMediaUploads;
