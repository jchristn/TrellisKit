import { collectEnumeration } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { media as mediaApi } from '../api/community';
import { describeAsset, PollIntervals, UploadError, UploadJob, UploadStatus, validateFile } from './upload';

/** Statuses that block publishing until they settle. */
const BUSY = [UploadStatus.Preparing, UploadStatus.Uploading, UploadStatus.Completing, UploadStatus.Interrupted];

/**
 * Upload state for one composer: start uploads for picked assets, show per-file progress, resume
 * interrupted transfers when the app returns to the foreground, and poll processing media
 * (images every 2 seconds for up to a minute, video every 5 seconds while the composer is open).
 *
 * Attachments that already exist (editing a post) are passed as `initial` Media and show as
 * Ready or Processing without an upload.
 *
 * @param {object} options
 * @param {object} options.api MobileApiClient.
 * @param {object[]} [options.initial] Existing Media.
 * @param {object} [options.transport] Storage transport (tests).
 * @param {string} [options.purpose] Upload purpose sent with each upload session (`Message`, `Ticket`).
 */
export function useUploads({ api, initial = [], transport, purpose }) {
  const [items, setItems] = useState(() =>
    initial.map((m) => ({ localId: m.id, status: m.status === 'Ready' ? UploadStatus.Ready : m.status === 'Failed' ? UploadStatus.Failed : UploadStatus.Processing, progress: 1, file: { kind: m.kind }, media: m, error: null, existing: true }))
  );
  const jobs = useRef(new Map());
  const startedAt = useRef(new Map());
  const itemsRef = useRef(items);
  useEffect(() => {
    itemsRef.current = items;
  }, [items]);

  const update = useCallback((state) => {
    setItems((current) => current.map((item) => (item.localId === state.localId ? { ...item, ...state } : item)));
  }, []);

  const add = useCallback(
    (assets) => {
      const added = [];
      for (const asset of assets || []) {
        const file = describeAsset(asset, transport);
        const invalid = validateFile(file);
        const job = new UploadJob({ api, file, transport, onChange: update, purpose });
        jobs.current.set(job.state.localId, job);
        const state = invalid ? { ...job.state, status: UploadStatus.Failed, error: invalid } : job.state;
        added.push(state);
        if (!invalid) startedAt.current.set(job.state.localId, Date.now());
      }
      setItems((current) => current.concat(added));
      for (const state of added) {
        if (state.status !== UploadStatus.Failed) jobs.current.get(state.localId).start();
      }
      return added;
    },
    [api, transport, update, purpose]
  );

  const remove = useCallback(
    (localId) => {
      const job = jobs.current.get(localId);
      if (job) job.cancel();
      jobs.current.delete(localId);
      const item = itemsRef.current.find((i) => i.localId === localId);
      setItems((current) => current.filter((i) => i.localId !== localId));
      // Unattached uploads are deleted right away; attached media is detached by saving the edit.
      if (item && !item.existing && item.media && item.media.id) mediaApi.remove(api, item.media.id).catch(() => {});
    },
    [api]
  );

  /** Forget every item without deleting its media (the media was just attached to a message). */
  const clear = useCallback(() => {
    jobs.current.clear();
    startedAt.current.clear();
    setItems([]);
  }, []);

  const setAltText = useCallback((localId, altText) => {
    setItems((current) => current.map((i) => (i.localId === localId ? { ...i, altText } : i)));
  }, []);

  /** Labels and tags for one attachment, saved with PUT /media/{id} when the post is saved. */
  const setMetadata = useCallback((localId, metadata) => {
    setItems((current) => current.map((i) => (i.localId === localId ? { ...i, metadata } : i)));
  }, []);

  const resumeInterrupted = useCallback(() => {
    for (const item of itemsRef.current) {
      if (item.status === UploadStatus.Interrupted) {
        const job = jobs.current.get(item.localId);
        if (job) job.resume();
      }
    }
  }, []);

  useEffect(() => {
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') resumeInterrupted();
    });
    return () => sub.remove();
  }, [resumeInterrupted]);

  // Poll Processing media until Ready or Failed.
  const processing = items.filter((i) => i.status === UploadStatus.Processing && i.media && i.media.id);
  const hasVideo = processing.some((i) => i.media.kind === 'Video' || (i.file && i.file.kind === 'Video'));
  const processingKey = processing.map((i) => i.media.id).join(',');
  useEffect(() => {
    if (!processingKey) return undefined;
    const interval = hasVideo ? PollIntervals.Video : PollIntervals.Image;
    const timer = setInterval(async () => {
      const ids = processingKey.split(',');
      const now = Date.now();
      try {
        const result = await collectEnumeration((q) => mediaApi.getBatch(api, ids, q), { maxResults: 20 });
        const byId = new Map(result.objects.map((m) => [m.id, m]));
        setItems((current) =>
          current.map((item) => {
            if (item.status !== UploadStatus.Processing || !item.media) return item;
            const next = byId.get(item.media.id);
            if (next && next.status === 'Ready') return { ...item, media: next, status: UploadStatus.Ready };
            if (next && next.status === 'Failed') return { ...item, media: next, status: UploadStatus.Failed, error: new UploadError(next.failureReason || 'ProcessingError', next.failureReason || 'ProcessingError') };
            const started = startedAt.current.get(item.localId);
            if (item.media.kind !== 'Video' && started && now - started > PollIntervals.imageLimitMs) return { ...item, stalled: true };
            return item;
          })
        );
      } catch {
        // Keep polling; the next tick tries again.
      }
    }, interval);
    return () => clearInterval(timer);
  }, [api, processingKey, hasVideo]);

  const busy = items.some((i) => BUSY.includes(i.status));
  const failed = items.some((i) => i.status === UploadStatus.Failed);
  const mediaIds = items.filter((i) => i.media && i.media.id && (i.status === UploadStatus.Ready || i.status === UploadStatus.Processing)).map((i) => i.media.id);

  return { items, add, remove, clear, setAltText, setMetadata, resumeInterrupted, busy, failed, mediaIds };
}

/**
 * The composer error to show: the "wait for uploads" notice only while an upload is still in
 * flight or failed, so it clears itself once the uploads settle instead of lingering until the
 * next submit.
 * @param {string|null} error
 * @param {{ busy: boolean, failed: boolean }} uploads
 * @param {string} waitMessage The composer's wait-for-uploads string.
 */
export function composerError(error, uploads, waitMessage) {
  if (error && error === waitMessage && !(uploads && (uploads.busy || uploads.failed))) return null;
  return error;
}
