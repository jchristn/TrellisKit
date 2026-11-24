import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { bboxChanged, directoryConfig, formatBbox, formatViewport, parseBbox, parseViewport } from '@trelliskit/shared';
import Icon from '../Icon.jsx';

const STORE = 'tk.mapSearch.';

function readStored(kind) {
  try {
    const raw = window.localStorage.getItem(STORE + kind);
    return raw ? parseViewport(raw) : null;
  } catch {
    return null;
  }
}

function store(kind, viewport) {
  const text = formatViewport(viewport);
  if (!text) return;
  try {
    window.localStorage.setItem(STORE + kind, text);
  } catch {
    // Storage is a convenience only.
  }
}

/**
 * URL state of a map-first search: `at=lat,lng,zoom` (the viewport, written with replace after
 * every move) and `bbox=s,w,n,e` (the searched area, pushed by "Search this area" so back and
 * forward work). The first viewport is the URL's, else the last one this browser used for `kind`,
 * else the property's `directory.map.defaultCenter` and `defaultZoom`. When the URL has no searched
 * area yet, the first bounds the map reports become it (replace).
 *
 * @param {string} kind `venues` or `events` (the storage key of the last viewport)
 * @param {object} property
 */
export function useMapSearch(kind, property) {
  const [searchParams, setSearchParams] = useSearchParams();
  const config = directoryConfig(property).map;
  const atParam = searchParams.get('at');
  const bboxParam = searchParams.get('bbox');
  const urlViewport = useMemo(() => parseViewport(atParam), [atParam]);
  const bbox = useMemo(() => parseBbox(bboxParam), [bboxParam]);
  const [initial] = useState(() => urlViewport || readStored(kind) || { lat: config.defaultCenter.lat, lng: config.defaultCenter.lng, zoom: config.defaultZoom });
  const [current, setCurrent] = useState(null);
  const [focus, setFocus] = useState(() => (!urlViewport && bbox ? { bbox } : null));
  const pending = useRef(false);
  const paramsRef = useRef(searchParams);
  paramsRef.current = searchParams;

  const write = useCallback((changes, replace) => {
    const before = paramsRef.current;
    const next = new URLSearchParams(before);
    for (const [k, value] of Object.entries(changes)) {
      if (value) next.set(k, value);
      else next.delete(k);
    }
    if (next.toString() === before.toString()) return;
    paramsRef.current = next;
    setSearchParams(next, { replace });
  }, [setSearchParams]);

  const report = useCallback((view, box) => {
    if (!box) return;
    setCurrent(box);
    if (view) store(kind, view);
    const changes = { at: formatViewport(view) };
    let replace = true;
    if (!parseBbox(paramsRef.current.get('bbox'))) changes.bbox = formatBbox(box);
    if (pending.current) {
      pending.current = false;
      changes.bbox = formatBbox(box);
      replace = false;
    }
    write(changes, replace);
  }, [kind, write]);

  const onReady = useCallback((box, view) => report(view, box), [report]);
  const onMove = useCallback(({ viewport, bbox: box }) => report(viewport, box), [report]);
  const searchArea = useCallback(() => {
    if (current) write({ bbox: formatBbox(current) }, false);
  }, [current, write]);
  /** Move the map to a place (`{ bbox }` or `{ lat, lng, zoom }`) and search the area it lands on. */
  const goTo = useCallback((place) => {
    pending.current = true;
    setFocus({ ...place });
  }, []);

  return {
    viewport: urlViewport || initial,
    bbox,
    current,
    focus,
    onReady,
    onMove,
    goTo,
    searchArea,
    showSearchArea: Boolean(current && bbox && bboxChanged(current, bbox))
  };
}

/**
 * Rows of a map search's results, kept in sync with the map: hovering or focusing a row reports
 * its id (the map highlights the marker), the hovered or selected row is highlighted, and a newly
 * selected row scrolls into view.
 */
export function MapResultRows({ items, renderRow, hoveredId, selectedId, onHover }) {
  const listRef = useRef(null);
  useEffect(() => {
    if (!selectedId || !listRef.current) return;
    const row = listRef.current.querySelector('[data-result-id="' + String(selectedId).replace(/"/g, '') + '"]');
    if (row && typeof row.scrollIntoView === 'function') row.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [selectedId]);
  return (
    <div className="p4-list" ref={listRef}>
      {items.map((item) => {
        const lit = item.id === hoveredId || item.id === selectedId;
        return (
          <div
            key={item.id}
            data-result-id={item.id}
            data-testid={'result-' + item.id}
            className={'map-search-row' + (lit ? ' is-highlighted' : '') + (item.id === selectedId ? ' is-selected' : '')}
            onMouseEnter={() => onHover(item.id)}
            onMouseLeave={() => onHover(null)}
            onFocus={() => onHover(item.id)}
            onBlur={() => onHover(null)}
          >
            {renderRow(item)}
          </div>
        );
      })}
    </div>
  );
}

/**
 * The map-first search layout: a collapsible advanced search pane, the full-width map with a
 * floating "Search this area" button (shown after the map moved away from the searched area), and
 * the results pane beside the map on wide screens or as a bottom sheet on narrow ones.
 *
 * @param {{ map: any, results: any, advanced: any, advancedLabel: string, advancedCount?: number, showSearchArea: boolean, onSearchArea: () => void,
 *   searchAreaLabel: string, resultsLabel: string, resultsSummary: string, sheetOpen: boolean, onSheet: (open: boolean) => void, toolbar?: any, testId?: string }} props
 */
function MapSearchLayout({ map, results, advanced, advancedLabel, advancedCount = 0, showSearchArea, onSearchArea, searchAreaLabel, resultsLabel, resultsSummary, sheetOpen, onSheet, toolbar = null, testId = 'map-search' }) {
  const id = useId();
  const paneId = id + '-advanced';
  const resultsId = id + '-results';
  const [open, setOpen] = useState(false);
  const headingRef = useRef(null);
  const toggleRef = useRef(null);
  const toggle = () => {
    const next = !open;
    setOpen(next);
    if (next) setTimeout(() => headingRef.current && headingRef.current.focus(), 0);
  };
  return (
    <div className="map-search" data-testid={testId}>
      <div className="map-search-toolbar">
        <button ref={toggleRef} type="button" className={'button button-secondary button-sm' + (open ? ' active' : '')} aria-expanded={open} aria-controls={paneId} onClick={toggle} data-testid={testId + '-advanced-toggle'}>
          <Icon name="filter" size={14} /><span>{advancedLabel}</span>{advancedCount > 0 && <span className="badge-count" aria-hidden="true">{advancedCount}</span>}
        </button>
        {toolbar}
      </div>
      <div id={paneId} className="map-search-advanced panel" hidden={!open} role="group" aria-label={advancedLabel}>
        <h2 className="visually-hidden" tabIndex={-1} ref={headingRef}>{advancedLabel}</h2>
        {open && advanced}
      </div>
      <div className="map-search-body">
        <div className="map-search-map">
          {map}
          {showSearchArea && (
            <button type="button" className="button button-primary button-sm map-search-area" onClick={onSearchArea} data-testid={testId + '-area'}>
              <Icon name="search" size={14} /><span>{searchAreaLabel}</span>
            </button>
          )}
        </div>
        <section className={'map-search-results' + (sheetOpen ? ' open' : '')} aria-label={resultsLabel}>
          <button type="button" className="map-search-sheet-toggle" aria-expanded={sheetOpen} aria-controls={resultsId} onClick={() => onSheet(!sheetOpen)}>
            <Icon name={sheetOpen ? 'chevronDown' : 'chevronUp'} size={14} /><span>{resultsSummary}</span>
          </button>
          <p className="map-search-summary muted small" aria-live="polite">{resultsSummary}</p>
          <div id={resultsId} className="map-search-results-body">{results}</div>
        </section>
      </div>
    </div>
  );
}

export default MapSearchLayout;
