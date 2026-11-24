import { useCallback, useMemo } from 'react';
import strings from '../../strings.js';
import SearchMap, { loadMapLibre } from '../maps/SearchMap.jsx';

export { loadMapLibre };

/**
 * The directory map (docs/PHASE4_CONTRACT.md sections 3.3 and 3.6): the search map over
 * POST /directory/map/enumerate with the venue source. Renders nothing when the provider is `None`
 * or no style is set, and calls `onFailed` when the style cannot load so the page falls back to the
 * list. Other props (viewport, onMove, hover and selection) pass through to SearchMap.
 *
 * @param {{ property: object, client: object, query: object, onSelect: (venueId: string) => void, onBounds?: (bbox: object) => void, onFailed?: () => void,
 *   center?: { lat: number, lng: number }|null, loader?: () => Promise<any> }} props
 */
function VenueMap({ property, client, query, onSelect, onBounds, onFailed, center = null, loader = loadMapLibre, onMove, onReady, ...rest }) {
  const fetchFeatures = useCallback((q, signal) => client.enumerateDirectoryMap(q, signal), [client]);
  const focus = useMemo(() => (center && Number.isFinite(center.lat) && Number.isFinite(center.lng) ? { lat: center.lat, lng: center.lng, zoom: 10 } : null), [center]);
  return (
    <SearchMap
      {...rest}
      property={property}
      kind="venues"
      fetchFeatures={fetchFeatures}
      query={query}
      focus={rest.focus || focus}
      onSelect={onSelect}
      onFailed={onFailed}
      onReady={(bbox, view) => { if (onBounds) onBounds(bbox); if (onReady) onReady(bbox, view); }}
      onMove={(move) => { if (onBounds) onBounds(move.bbox); if (onMove) onMove(move); }}
      label={rest.label || strings.venues.mapTitle}
      loader={loader}
      testId="venue-map"
    />
  );
}

export default VenueMap;
