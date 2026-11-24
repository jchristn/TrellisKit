import { useEffect, useRef, useState } from 'react';
import { directoryConfig, isValidPosition, roundCoordinate, webMapStyle } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useTheme } from '../../context/ThemeContext.jsx';
import { Loading } from '../States.jsx';
import { loadMapLibre } from './VenueMap.jsx';

const t = strings.pinMap;
const PIN_PLACES = 5;

function positionOf(pin) {
  if (!pin || pin.lat === '' || pin.lng === '' || pin.lat === null || pin.lng === null || pin.lat === undefined || pin.lng === undefined) return null;
  return isValidPosition(pin.lat, pin.lng) ? { lat: Number(pin.lat), lng: Number(pin.lng) } : null;
}

/** The pin fields' value for a map position, rounded to 5 decimal places (about 1 m). */
export function pinFromLngLat(lngLat) {
  return { lat: String(roundCoordinate(lngLat.lat, PIN_PLACES)), lng: String(roundCoordinate(lngLat.lng, PIN_PLACES)) };
}

/**
 * The pin editor map (docs/PHASE4_CONTRACT.md sections 3.6 and 3.7): MapLibre GL JS with the
 * property's style and one draggable marker. Dragging the marker, or clicking the map, places the
 * pin and reports it through `onChange` with the latitude and longitude as text (the numeric
 * fields stay the keyboard and screen reader path). Renders nothing when the map provider is
 * `None` or no style is set, and hides itself when the map cannot load.
 *
 * @param {{ property: object, pin: { lat: string, lng: string }, onChange: (pin: { lat: string, lng: string }) => void, disabled?: boolean, loader?: () => Promise<any> }} props
 */
function PinMap({ property, pin, onChange, disabled = false, loader = loadMapLibre }) {
  const { theme } = useTheme();
  const style = webMapStyle(property, { dark: theme === 'dark' });
  const config = directoryConfig(property).map;
  const container = useRef(null);
  const mapRef = useRef(null);
  const markerRef = useRef(null);
  const libRef = useRef(null);
  const callbacks = useRef({});
  callbacks.current = { onChange, disabled };
  const [status, setStatus] = useState('loading');
  const position = positionOf(pin);
  const initial = useRef(position);
  const centerLat = config.defaultCenter.lat;
  const centerLng = config.defaultCenter.lng;

  useEffect(() => {
    if (!style || !container.current) return undefined;
    let cancelled = false;
    let map = null;
    setStatus('loading');
    loader().then((maplibregl) => {
      if (cancelled || !container.current) return;
      libRef.current = maplibregl;
      const start = initial.current || { lat: centerLat, lng: centerLng };
      map = new maplibregl.Map({
        container: container.current,
        style,
        center: [start.lng, start.lat],
        zoom: initial.current ? Math.max(config.defaultZoom, 14) : config.defaultZoom,
        maxZoom: config.maxZoom,
        attributionControl: false
      });
      mapRef.current = map;
      map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right');
      if (maplibregl.AttributionControl) map.addControl(new maplibregl.AttributionControl({ compact: true, customAttribution: config.attribution || undefined }), 'bottom-right');
      map.on('click', (e) => {
        if (callbacks.current.disabled || !e || !e.lngLat) return;
        callbacks.current.onChange(pinFromLngLat(e.lngLat));
      });
      map.on('error', (e) => {
        if (e && e.error && /style/i.test(String(e.error.message || ''))) setStatus('failed');
      });
      map.on('load', () => { if (!cancelled) setStatus('ready'); });
    }, () => { if (!cancelled) setStatus('failed'); });
    return () => {
      cancelled = true;
      if (markerRef.current) markerRef.current.remove();
      markerRef.current = null;
      if (map) map.remove();
      mapRef.current = null;
    };
  }, [style, loader, centerLat, centerLng, config.defaultZoom, config.maxZoom, config.attribution]);

  const lat = position ? position.lat : null;
  const lng = position ? position.lng : null;
  useEffect(() => {
    const map = mapRef.current;
    const maplibregl = libRef.current;
    if (status !== 'ready' || !map || !maplibregl) return;
    if (lat === null || lng === null) {
      if (markerRef.current) markerRef.current.remove();
      markerRef.current = null;
      return;
    }
    if (!markerRef.current) {
      const marker = new maplibregl.Marker({ draggable: !disabled, color: getComputedStyle(document.documentElement).getPropertyValue('--color-accent').trim() || undefined });
      marker.setLngLat([lng, lat]).addTo(map);
      marker.on('dragend', () => callbacks.current.onChange(pinFromLngLat(marker.getLngLat())));
      markerRef.current = marker;
    } else {
      markerRef.current.setLngLat([lng, lat]);
      if (markerRef.current.setDraggable) markerRef.current.setDraggable(!disabled);
    }
  }, [status, lat, lng, disabled]);

  if (!style || status === 'failed') return null;
  return (
    <div className="p4-pin-map-wrap">
      <div className="p4-pin-map" data-testid="pin-map" role="region" aria-label={t.label}>
        <div ref={container} className="p4-map-canvas" />
        {status === 'loading' && <div className="p4-map-status"><Loading inline label={t.loading} /></div>}
      </div>
      <span className="muted small">{position ? t.hintPlaced : t.hintEmpty}</span>
    </div>
  );
}

export default PinMap;
