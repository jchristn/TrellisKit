import { useCallback, useEffect, useState } from 'react';
import { directoryTerms, eventPath, fillDirectory, instructorPath, lessonPath, normalizeEnumerationResult, seriesPath, venuePath } from '@trelliskit/shared';
import strings from '../strings.js';
import { useProperty } from '../context/PropertyContext.jsx';

/** Website routes of Phase 4 objects (the app paths of docs/PHASE4_CONTRACT.md section 16 with `/app`). */
export const appPaths = Object.freeze({
  library: '/app/library',
  series: (idOrSlug) => '/app' + seriesPath(idOrSlug),
  lesson: (idOrSlug) => '/app' + lessonPath(idOrSlug),
  events: '/app/events',
  event: (idOrSlug) => '/app' + eventPath(idOrSlug),
  proposeEvent: '/app/events/propose',
  myProposals: '/app/events/proposals',
  directory: '/app/directory',
  venue: (idOrSlug) => '/app' + venuePath(idOrSlug),
  instructor: (idOrSlug) => '/app' + instructorPath(idOrSlug),
  submit: (type, targetId) => '/app/directory/submit?type=' + encodeURIComponent(type) + (targetId ? '&target=' + encodeURIComponent(targetId) : ''),
  mySubmissions: '/app/directory/submissions'
});

/** Error text for the Phase 4 codes (section 0.3) on top of the composer map. */
export function phase4Errors() {
  return { ...strings.composer.errors, ...strings.phase4Errors };
}

/**
 * The configured directory terms and a filler for `{venue}`, `{venues}`, `{instructor}`,
 * `{instructors}` (and `...Lower`) plus the property tokens (`{name}`).
 */
export function useDirectoryTerms() {
  const { property, fill } = useProperty();
  const terms = directoryTerms(property);
  const d = useCallback((template, extra) => fill(fillDirectory(template, terms, extra)), [fill, terms]);
  return { terms, d };
}

/**
 * Follow continuation tokens for a small bounded list (occurrences, programs, levels, categories):
 * at most `maxPages` pages of `pageSize`, never an unbounded fetch.
 * @param {(query: object, signal: AbortSignal) => Promise<any>} fetchPage stable (useCallback)
 * @param {{ pageSize?: number, maxPages?: number, enabled?: boolean }} [options]
 */
export function useBoundedList(fetchPage, { pageSize = 100, maxPages = 5, enabled = true } = {}) {
  const [state, setState] = useState({ items: [], loading: enabled, error: null, truncated: false });
  const [nonce, setNonce] = useState(0);
  useEffect(() => {
    if (!enabled) return undefined;
    const controller = new AbortController();
    let active = true;
    setState((s) => ({ ...s, loading: true, error: null }));
    (async () => {
      const items = [];
      let token = null;
      let pages = 0;
      do {
        const page = normalizeEnumerationResult(await fetchPage({ maxResults: pageSize, continuationToken: token }, controller.signal));
        items.push(...page.objects);
        token = page.endOfResults ? null : page.continuationToken;
        pages += 1;
      } while (token && pages < maxPages);
      return { items, truncated: Boolean(token) };
    })().then(
      (result) => { if (active) setState({ ...result, loading: false, error: null }); },
      (error) => { if (active) setState((s) => ({ ...s, loading: false, error })); }
    );
    return () => {
      active = false;
      controller.abort();
    };
  }, [fetchPage, pageSize, maxPages, enabled, nonce]);
  return { ...state, reload: () => setNonce((n) => n + 1) };
}

/** Save text as a file download in the browser (ICS and CSV exports). */
export function downloadText(text, filename, type = 'text/plain') {
  if (typeof window === 'undefined' || !window.URL || typeof window.URL.createObjectURL !== 'function') return false;
  const blob = new Blob([text], { type });
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => window.URL.revokeObjectURL(url), 0);
  return true;
}

/** The URL of a Media object's file (documents, captions, images): `document.url`, `fileUrl`, `url`, or the image variant (the order mobile uses too). */
export function mediaFileUrl(media) {
  if (!media) return null;
  const url = (media.document && media.document.url) || media.fileUrl || media.url || (media.image && (media.image.url || media.image.large)) || (media.variants && media.variants.large) || null;
  return typeof url === 'string' && (url.startsWith('/') || /^https:\/\//i.test(url) || /^http:\/\/localhost/i.test(url)) ? url : null;
}

/** A thumbnail URL of a Media object (cover photos, posters), or null. */
export function mediaThumbUrl(media) {
  if (!media) return null;
  const url = media.thumbUrl || media.posterThumbUrl || (media.image && (media.image.thumbUrl || media.image.small)) || (media.video && media.video.posterUrl) || media.url || null;
  return typeof url === 'string' && (url.startsWith('/') || /^https?:\/\//i.test(url)) ? url : null;
}
