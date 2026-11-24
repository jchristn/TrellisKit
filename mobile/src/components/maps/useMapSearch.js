import { bboxChanged, collectEnumeration } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';
import { mapSearchSessions as sessions } from './mapSearchSessions';
import { drawableFeatures } from './SearchMap';

export { resetMapSearchSessions } from './mapSearchSessions';

/** The center and zoom of a box (handles boxes across the antimeridian). */
export function viewportFromBbox(bbox, zoom) {
  if (!bbox) return null;
  const width = (((bbox.east - bbox.west) % 360) + 360) % 360;
  let lng = bbox.west + width / 2;
  if (lng > 180) lng -= 360;
  return { lat: (bbox.south + bbox.north) / 2, lng, zoom };
}

/**
 * Map-first search state (docs/PHASE4_CONTRACT.md section 16): the visible box (`view`), the box
 * the results were searched for (`searched`), and camera moves (`focus`). The first reported view
 * is searched at once; later moves only offer "Search this area" (`showSearchArea`) until the
 * member taps it. Moves the app starts (a place, near me, a cluster) search the area they land on.
 * The camera and the searched area survive leaving and reopening the screen for the session.
 * @param {string} key Session key, for example 'places' or 'events'.
 * @param {object} config `directoryConfig(property).map`.
 */
export function useMapSearch(key, config) {
  const saved = sessions.get(key) || {};
  const [initial] = useState(() => saved.viewport || { lat: config.defaultCenter.lat, lng: config.defaultCenter.lng, zoom: config.defaultZoom });
  const [view, setView] = useState(saved.view || null);
  const [searched, setSearchedState] = useState(saved.searched || null);
  const [searchedZoom, setSearchedZoom] = useState(saved.searchedZoom === undefined ? config.defaultZoom : saved.searchedZoom);
  const [focus, setFocus] = useState(null);
  const searchedRef = useRef(saved.searched || null);
  const pending = useRef(false);
  const seq = useRef(0);

  const remember = useCallback(
    (changes) => {
      sessions.set(key, { ...(sessions.get(key) || {}), ...changes });
    },
    [key]
  );
  const setSearched = useCallback(
    (bbox, zoom) => {
      searchedRef.current = bbox;
      setSearchedState(bbox);
      setSearchedZoom(zoom);
      remember({ searched: bbox, searchedZoom: zoom });
    },
    [remember]
  );

  const onViewChange = useCallback(
    (next) => {
      if (!next || !next.bbox) return;
      setView(next);
      remember({ view: next, viewport: viewportFromBbox(next.bbox, next.zoom) });
      if (!searchedRef.current || pending.current) {
        pending.current = false;
        setSearched(next.bbox, next.zoom);
      }
    },
    [remember, setSearched]
  );

  const searchArea = useCallback(() => {
    if (view && view.bbox) setSearched(view.bbox, view.zoom);
  }, [view, setSearched]);

  /** Move the camera (`{ lat, lng, zoom }` or `{ bbox }`) and search where it lands. */
  const moveTo = useCallback((target) => {
    if (!target) return;
    pending.current = true;
    seq.current += 1;
    setFocus({ ...target, seq: seq.current });
  }, []);

  /** A cluster tap: the map zooms itself; search the area it lands on. */
  const expectMove = useCallback(() => {
    pending.current = true;
  }, []);

  return {
    initial,
    view,
    searched,
    searchedZoom,
    focus,
    onViewChange,
    searchArea,
    moveTo,
    expectMove,
    showSearchArea: Boolean(view && searched && bboxChanged(searched, view.bbox))
  };
}

/**
 * Map features for the searched area: `fetcher({ bbox, zoom }, signal)` runs when the area, the
 * zoom it was searched at, or `key` (the filters) change. Returns `{ features, error }`.
 */
export function useMapFeatures(fetcher, searched, zoom, key, enabled = true) {
  const [features, setFeatures] = useState([]);
  const [error, setError] = useState(null);
  const fetchRef = useRef(fetcher);
  useEffect(() => {
    fetchRef.current = fetcher;
  });
  const boxKey = searched ? JSON.stringify(searched) : '';
  useEffect(() => {
    if (!enabled || !searched) return undefined;
    const controller = typeof AbortController === 'function' ? new AbortController() : null;
    let active = true;
    collectEnumeration((page, signal) => fetchRef.current(page, signal), { bbox: searched, zoom, maxResults: 100 }, { signal: controller?.signal })
      .then((result) => {
        if (!active) return;
        setError(null);
        setFeatures(drawableFeatures(result && result.objects));
      })
      .catch((e) => {
        if (!active || (e && e.name === 'AbortError')) return;
        setError(e);
      });
    return () => {
      active = false;
      if (controller) controller.abort();
    };
    // boxKey stands for searched.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [boxKey, zoom, key, enabled]);
  return { features, error };
}
