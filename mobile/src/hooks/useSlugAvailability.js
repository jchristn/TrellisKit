import { slugProblem } from '@trelliskit/shared';
import { useEffect, useState } from 'react';

/**
 * Live public page slug status: the shared format rules decide right away, then the server is
 * asked (after a short debounce) whether the slug is free for this kind of page.
 * @param {object} api MobileApiClient (with `checkPublicPageSlug`).
 * @param {'Venue'|'Event'|'Profile'} kind
 * @param {string} slug Slug being typed.
 * @param {{ id?: string|null, currentSlug?: string|null, delayMs?: number }} [options] `currentSlug` is the saved slug (always available to its owner).
 * @returns {'idle'|'checking'|'available'|'current'|'error'|'required'|'length'|'format'|'reserved'|'taken'}
 */
export function useSlugAvailability(api, kind, slug, { id = null, currentSlug = null, delayMs = 400 } = {}) {
  const value = String(slug || '').trim();
  const problem = slugProblem(value);
  const local = problem || (currentSlug && value === currentSlug ? 'current' : null);
  const [result, setResult] = useState({ slug: null, status: null });

  useEffect(() => {
    if (local) return undefined;
    let active = true;
    const timer = setTimeout(() => {
      const body = id ? { kind, slug: value, id } : { kind, slug: value };
      Promise.resolve(api.checkPublicPageSlug(body))
        .then((response) => {
          if (!active) return;
          const status = response && response.valid && response.available ? 'available' : (response && response.problem) || 'taken';
          setResult({ slug: value, status });
        })
        .catch(() => {
          if (active) setResult({ slug: value, status: 'error' });
        });
    }, delayMs);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [api, kind, id, value, local, delayMs]);

  if (local) return local;
  return result.slug === value ? result.status : 'checking';
}
