import SearchMap from '../maps/SearchMap';

/**
 * The directory map (docs/PHASE4_CONTRACT.md sections 3.3 and 3.6): SearchMap with venue points.
 * Tapping a point opens the venue; tapping a cluster zooms to its box.
 * @param {object} props
 * @param {string} props.provider MapProvider value (not None).
 * @param {object} props.config `directoryConfig(property).map`.
 * @param {object[]} props.features MapFeature objects.
 * @param {(view: { bbox: object, zoom: number }) => void} props.onViewChange
 * @param {(venue: object) => void} props.onOpenVenue
 * @param {() => void} props.onUnavailable
 * @param {{ lat: number, lng: number }|null} [props.focus] Center on this position (near me, place search).
 * @param {object} props.terms Filled directory strings.
 */
export default function VenueMap({ provider, config, features, onViewChange, onOpenVenue, onUnavailable, focus = null, terms, testID = 'venue-map' }) {
  return (
    <SearchMap
      provider={provider}
      config={config}
      features={features}
      onViewChange={onViewChange}
      onPressItem={onOpenVenue}
      onUnavailable={onUnavailable}
      clusterLabel={terms.cluster}
      initial={focus ? { lat: focus.lat, lng: focus.lng, zoom: Math.max(config.defaultZoom, 10) } : null}
      testID={testID}
    />
  );
}
