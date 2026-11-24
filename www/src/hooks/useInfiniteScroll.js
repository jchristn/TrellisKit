import { useEffect, useRef } from 'react';

/**
 * Call `onVisible` when the returned ref's element scrolls into view (IntersectionObserver).
 * Pages without IntersectionObserver fall back to the visible "Load more" button.
 * @param {() => void} onVisible
 * @param {{ enabled?: boolean, rootMargin?: string }} [options]
 */
export function useInfiniteScroll(onVisible, { enabled = true, rootMargin = '600px' } = {}) {
  const ref = useRef(null);
  const callback = useRef(onVisible);
  useEffect(() => {
    callback.current = onVisible;
  }, [onVisible]);
  useEffect(() => {
    const node = ref.current;
    if (!enabled || !node || typeof IntersectionObserver !== 'function') return undefined;
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting)) callback.current();
    }, { rootMargin });
    observer.observe(node);
    return () => observer.disconnect();
  }, [enabled, rootMargin]);
  return ref;
}

export default useInfiniteScroll;
