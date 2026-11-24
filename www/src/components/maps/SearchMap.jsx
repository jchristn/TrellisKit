import { useEffect, useRef, useState } from 'react';
import { collectEnumeration, directoryConfig, mapAttributionHtml, mapFeaturesToGeoJson, toBbox, webMapStyle } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useTheme } from '../../context/ThemeContext.jsx';
import { Loading } from '../States.jsx';

const EMPTY = { type: 'FeatureCollection', features: [] };

/** A literal `text-font` stack the loaded style's own symbol layers use (a Regular face when there is one), or null. */
export function styleFont(map) {
  try {
    const layers = (map.getStyle() && map.getStyle().layers) || [];
    const stacks = [];
    for (const layer of layers) {
      const font = layer.type === 'symbol' && layer.layout && layer.layout['text-font'];
      if (Array.isArray(font) && font.length && font.every((f) => typeof f === 'string')) stacks.push(font);
    }
    return stacks.find((f) => /regular/i.test(f[0])) || stacks[0] || null;
  } catch {
    return null;
  }
}

/** Load MapLibre GL JS on demand (a separate chunk; tests inject a fake or mock the module). */
export function loadMapLibre() {
  return Promise.all([import('maplibre-gl'), import('maplibre-gl/dist/maplibre-gl.css').catch(() => null)]).then(([mod]) => mod.default || mod);
}

/** GeoJSON of the server's `Cluster` features (each zooms to its own box when clicked). */
export function clusterCollection(objects) {
  return {
    type: 'FeatureCollection',
    features: (objects || []).filter((f) => f && f.kind === 'Cluster').map((f, i) => ({
      type: 'Feature', id: 'c' + i, geometry: { type: 'Point', coordinates: [Number(f.lng), Number(f.lat)] },
      properties: { count: f.count, south: f.bbox ? f.bbox.south : f.lat, west: f.bbox ? f.bbox.west : f.lng, north: f.bbox ? f.bbox.north : f.lat, east: f.bbox ? f.bbox.east : f.lng }
    }))
  };
}

function viewportOf(map) {
  if (!map || typeof map.getCenter !== 'function') return null;
  const c = map.getCenter();
  if (!c) return null;
  return { lat: Number(c.lat), lng: Number(c.lng), zoom: Number(map.getZoom()) };
}

function differs(a, b) {
  if (!a || !b) return false;
  return Math.abs(a.lat - b.lat) > 1e-3 || Math.abs(a.lng - b.lng) > 1e-3 || Math.abs(a.zoom - b.zoom) > 0.1;
}

/**
 * A search map (docs/PHASE4_CONTRACT.md sections 3.3 and 3.6): MapLibre GL JS with the property's
 * style (dark style in dark mode), the attribution, and the server's map features for the visible
 * area (points with client-side clustering, plus server clusters that zoom to their box). Venues use
 * the source `venues`, events the source `events`. The viewport is controlled: when `viewport`
 * differs from the map (for example after back navigation) the map jumps there. Every finished move
 * reports `{ viewport, bbox }`. `focus` (`{ bbox }` or `{ lat, lng, zoom }`, a new object per request)
 * moves the map to a place. Hovered and selected points are highlighted with feature state. Renders
 * nothing without a style; calls `onFailed` when the map cannot load.
 *
 * @param {{ property: object, kind?: 'venues'|'events', fetchFeatures: (query: object, signal: AbortSignal) => Promise<any>, query?: object,
 *   viewport?: { lat: number, lng: number, zoom: number }|null, focus?: object|null, onMove?: (move: { viewport: object, bbox: object }) => void,
 *   onReady?: (bbox: object) => void, hoveredId?: string|null, selectedId?: string|null, onHover?: (id: string|null) => void, onSelect?: (id: string) => void,
 *   onFailed?: () => void, label?: string, loader?: () => Promise<any>, testId?: string }} props
 */
function SearchMap({ property, kind = 'venues', fetchFeatures, query, viewport = null, focus = null, onMove, onReady, hoveredId = null, selectedId = null, onHover, onSelect, onFailed, label = strings.venues.mapTitle, loader = loadMapLibre, testId = 'search-map' }) {
  const { theme } = useTheme();
  const style = webMapStyle(property, { dark: theme === 'dark' });
  const config = directoryConfig(property).map;
  const container = useRef(null);
  const mapRef = useRef(null);
  const callbacks = useRef({});
  callbacks.current = { onMove, onReady, onHover, onSelect, onFailed, fetchFeatures };
  const initial = useRef(viewport);
  const highlighted = useRef([]);
  const queryKey = JSON.stringify(query || {});
  const [status, setStatus] = useState('loading');

  useEffect(() => {
    if (!style || !container.current) return undefined;
    let cancelled = false;
    let map = null;
    setStatus('loading');
    const start = initial.current || { lat: config.defaultCenter.lat, lng: config.defaultCenter.lng, zoom: config.defaultZoom };
    loader().then((maplibregl) => {
      if (cancelled || !container.current) return;
      map = new maplibregl.Map({ container: container.current, style, center: [start.lng, start.lat], zoom: start.zoom, maxZoom: config.maxZoom, attributionControl: false });
      mapRef.current = map;
      map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right');
      map.addControl(new maplibregl.AttributionControl({ compact: true, customAttribution: mapAttributionHtml(config.attribution) || undefined }), 'bottom-right');
      map.on('error', (e) => {
        if (e && e.error && /style/i.test(String(e.error.message || ''))) {
          setStatus('failed');
          if (callbacks.current.onFailed) callbacks.current.onFailed();
        }
      });
      map.on('load', () => {
        if (cancelled) return;
        const css = (name, fallback) => getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
        const accent = css('--color-accent', '#1F5FBF');
        // Dark mode: a brand's dark accent is a bright color meant for text and small marks. A filled count bubble in
        // it glares on a dark map, so the bubble takes the soft accent with the bright color as its ring and number.
        const dark = theme === 'dark';
        const bubble = dark ? css('--color-accent-soft', '#1c2440') : accent;
        const bubbleText = dark ? css('--brand-accent-soft-contrast', accent) : '#ffffff';
        const bubbleRing = dark ? { 'circle-stroke-width': 1.5, 'circle-stroke-color': bubbleText } : {};
        const lit = ['boolean', ['feature-state', 'highlight'], false];
        // Cluster counts must use a font the style's glyph server has: the MapLibre default font stack is missing on
        // most hosted styles, and a failed glyph request stops the whole source (points included) from drawing.
        const font = styleFont(map);
        map.addSource(kind, { type: 'geojson', data: EMPTY, cluster: true, clusterRadius: 48, clusterMaxZoom: 14, promoteId: 'id' });
        map.addSource('server-clusters', { type: 'geojson', data: EMPTY });
        map.addLayer({ id: kind + '-clusters', type: 'circle', source: kind, filter: ['has', 'point_count'], paint: { 'circle-color': bubble, 'circle-radius': ['step', ['get', 'point_count'], 16, 10, 22, 50, 28], 'circle-opacity': dark ? 0.95 : 0.85, ...bubbleRing } });
        map.addLayer({ id: kind + '-cluster-count', type: 'symbol', source: kind, filter: ['has', 'point_count'], layout: { 'text-field': ['get', 'point_count_abbreviated'], 'text-size': 12, ...(font ? { 'text-font': font } : {}) }, paint: { 'text-color': bubbleText } });
        map.addLayer({ id: kind + '-points', type: 'circle', source: kind, filter: ['!', ['has', 'point_count']], paint: { 'circle-color': accent, 'circle-radius': ['case', lit, 11, 7], 'circle-stroke-width': ['case', lit, 4, 2], 'circle-stroke-color': '#ffffff' } });
        map.addLayer({ id: 'server-clusters', type: 'circle', source: 'server-clusters', paint: { 'circle-color': bubble, 'circle-radius': 24, 'circle-opacity': dark ? 0.95 : 0.7, ...bubbleRing } });
        map.addLayer({ id: 'server-cluster-count', type: 'symbol', source: 'server-clusters', layout: { 'text-field': ['to-string', ['get', 'count']], 'text-size': 12, ...(font ? { 'text-font': font } : {}) }, paint: { 'text-color': bubbleText } });
        map.on('click', kind + '-points', (e) => {
          const feature = e.features && e.features[0];
          if (feature && callbacks.current.onSelect) callbacks.current.onSelect(feature.properties.id);
        });
        map.on('mousemove', kind + '-points', (e) => {
          const feature = e.features && e.features[0];
          if (feature && callbacks.current.onHover) callbacks.current.onHover(feature.properties.id);
        });
        map.on('mouseleave', kind + '-points', () => { if (callbacks.current.onHover) callbacks.current.onHover(null); });
        map.on('click', kind + '-clusters', (e) => {
          const feature = e.features && e.features[0];
          if (!feature) return;
          const source = map.getSource(kind);
          Promise.resolve(source.getClusterExpansionZoom(feature.properties.cluster_id)).then((zoom) => map.easeTo({ center: feature.geometry.coordinates, zoom }));
        });
        map.on('click', 'server-clusters', (e) => {
          const p = e.features && e.features[0] && e.features[0].properties;
          if (p) map.fitBounds([[p.west, p.south], [p.east, p.north]], { padding: 40, maxZoom: config.maxZoom });
        });
        for (const layer of [kind + '-points', kind + '-clusters', 'server-clusters']) {
          map.on('mouseenter', layer, () => { map.getCanvas().style.cursor = 'pointer'; });
          map.on('mouseleave', layer, () => { map.getCanvas().style.cursor = ''; });
        }
        setStatus('ready');
      });
    }, () => {
      if (cancelled) return;
      setStatus('failed');
      if (callbacks.current.onFailed) callbacks.current.onFailed();
    });
    return () => {
      cancelled = true;
      if (map) map.remove();
      mapRef.current = null;
    };
  }, [style, theme, kind, loader, config.defaultCenter.lat, config.defaultCenter.lng, config.defaultZoom, config.maxZoom, config.attribution]);

  // Features for the visible box on every move and whenever the query changes.
  useEffect(() => {
    const map = mapRef.current;
    if (status !== 'ready' || !map) return undefined;
    let active = true;
    let controller = null;
    let first = true;
    const refresh = () => {
      const bbox = toBbox(map.getBounds());
      if (!bbox) return;
      const view = viewportOf(map);
      if (first) {
        first = false;
        if (callbacks.current.onReady) callbacks.current.onReady(bbox, view);
      } else if (callbacks.current.onMove) callbacks.current.onMove({ viewport: view, bbox });
      if (controller) controller.abort();
      controller = new AbortController();
      const q = JSON.parse(queryKey);
      collectEnumeration((page, signal) => callbacks.current.fetchFeatures(page, signal), { ...q, bbox, zoom: map.getZoom(), maxResults: 100 }, { signal: controller.signal }).then((result) => {
        if (!active) return;
        const page = result;
        const points = map.getSource(kind);
        const clusters = map.getSource('server-clusters');
        if (points) points.setData(mapFeaturesToGeoJson(page.objects));
        if (clusters) clusters.setData(clusterCollection(page.objects));
      }, () => {});
    };
    refresh();
    map.on('moveend', refresh);
    return () => {
      active = false;
      if (controller) controller.abort();
      map.off('moveend', refresh);
    };
  }, [status, kind, queryKey, config.maxFeatures]);

  // Controlled viewport (back and forward navigation).
  useEffect(() => {
    const map = mapRef.current;
    if (status !== 'ready' || !map || !viewport || typeof map.jumpTo !== 'function') return;
    if (differs(viewportOf(map), viewport)) map.jumpTo({ center: [viewport.lng, viewport.lat], zoom: viewport.zoom });
  }, [status, viewport]);

  // Move to a place (place search, my location).
  useEffect(() => {
    const map = mapRef.current;
    if (status !== 'ready' || !map || !focus) return;
    const b = focus.bbox;
    if (b && [b.south, b.west, b.north, b.east].every((x) => Number.isFinite(Number(x))) && b.south !== b.north) map.fitBounds([[b.west, b.south], [b.east, b.north]], { padding: 40, maxZoom: 14 });
    else if (Number.isFinite(focus.lat) && Number.isFinite(focus.lng)) map.easeTo({ center: [focus.lng, focus.lat], zoom: Number.isFinite(focus.zoom) ? focus.zoom : 11 });
  }, [status, focus]);

  // Highlight the hovered and the selected point.
  useEffect(() => {
    const map = mapRef.current;
    if (status !== 'ready' || !map || typeof map.setFeatureState !== 'function') return;
    try {
      for (const id of highlighted.current) map.setFeatureState({ source: kind, id }, { highlight: false });
      const ids = [hoveredId, selectedId].filter(Boolean);
      for (const id of ids) map.setFeatureState({ source: kind, id }, { highlight: true });
      highlighted.current = ids;
    } catch {
      highlighted.current = [];
    }
  }, [status, kind, hoveredId, selectedId]);

  if (!style) return null;
  return (
    <div className="p4-map" data-testid={testId} aria-label={label} role="region">
      <div ref={container} className="p4-map-canvas" />
      {status === 'loading' && <div className="p4-map-status"><Loading inline label={strings.venues.mapLoading} /></div>}
    </div>
  );
}

export default SearchMap;
