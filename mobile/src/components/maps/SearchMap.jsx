import { MapProvider, mapFeatureItem, toBbox } from '@trelliskit/shared';
import { useEffect, useRef } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { longitudeDeltaForZoom, mapModule, regionToBbox, zoomForLongitudeDelta } from '../../native/maps';
import { useTheme } from '../../theme';
import AppText from '../AppText';

function Pin({ feature, clusterLabel, onPress, selected, theme }) {
  if (feature.kind === 'Cluster') {
    return (
      <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={clusterLabel(feature.count)} testID={'map-cluster-' + feature.lat + '-' + feature.lng} style={[styles.cluster, { backgroundColor: theme.colors.primary }]}>
        <AppText style={{ color: theme.colors.onPrimary || '#FFFFFF', fontWeight: '700' }}>{String(feature.count)}</AppText>
      </Pressable>
    );
  }
  const item = mapFeatureItem(feature);
  const filled = selected || item.isVerified;
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={item.name || item.title}
      accessibilityState={{ selected }}
      testID={'map-point-' + item.id}
      style={[selected ? styles.selected : styles.point, { backgroundColor: filled ? theme.colors.primary : theme.colors.surface, borderColor: selected ? theme.colors.text : theme.colors.primary }]}
    />
  );
}

function featurePosition(feature) {
  return feature.kind === 'Cluster' ? { lat: feature.lat, lng: feature.lng } : mapFeatureItem(feature).geo;
}

function featureKey(feature) {
  return feature.kind === 'Cluster' ? 'c:' + feature.lat + ':' + feature.lng : 'p:' + mapFeatureItem(feature).id;
}

/** Only features a map can draw: clusters and points whose venue or event has a position. */
export function drawableFeatures(objects) {
  return (Array.isArray(objects) ? objects : []).filter((f) => {
    if (!f) return false;
    if (f.kind === 'Cluster') return Number.isFinite(Number(f.lat)) && Number.isFinite(Number(f.lng));
    const item = mapFeatureItem(f);
    return Boolean(item && item.geo && item.geo.lat !== null && item.geo.lat !== undefined);
  });
}

/**
 * The search map (docs/PHASE4_CONTRACT.md sections 3.3 and 3.6): MapLibre with the property's style
 * (dark style in dark mode) or the platform map, drawing the server's map features (venue or event
 * points and grid clusters) as markers. Moving the camera reports the visible box and zoom;
 * tapping a cluster zooms to its box (`onCluster` runs too); tapping a point calls `onPressItem`.
 * `focus` moves the camera: `{ bbox }` fits a box, `{ lat, lng, zoom }` centers; a new `seq`
 * repeats a move. The attribution is always shown. When the native module is missing or the
 * style fails to load, `onUnavailable` runs so the screen falls back to its list.
 * @param {object} props
 * @param {string} props.provider MapProvider value (not None).
 * @param {object} props.config `directoryConfig(property).map`.
 * @param {object[]} props.features MapFeature objects.
 * @param {(view: { bbox: object, zoom: number }) => void} props.onViewChange
 * @param {(item: object) => void} props.onPressItem
 * @param {() => void} props.onUnavailable
 * @param {(n: number) => string} props.clusterLabel Accessibility label of a cluster.
 * @param {{ lat: number, lng: number, zoom?: number }} [props.initial] First camera (default: the configured center and zoom).
 * @param {object|null} [props.focus]
 * @param {(bbox: object) => void} [props.onCluster]
 * @param {string|null} [props.selectedId]
 * @param {string} [props.accessibilityLabel]
 */
export default function SearchMap({ provider, config, features, onViewChange, onPressItem, onUnavailable, clusterLabel, initial = null, focus = null, onCluster, selectedId = null, accessibilityLabel, testID = 'search-map' }) {
  const theme = useTheme();
  const mod = mapModule(provider);
  const cameraRef = useRef(null);
  const nativeMapRef = useRef(null);
  const start = initial || { ...config.defaultCenter, zoom: config.defaultZoom };
  const center = { lat: start.lat, lng: start.lng };
  const zoom = Number.isFinite(Number(start.zoom)) ? Number(start.zoom) : config.defaultZoom;

  useEffect(() => {
    if (!mod) onUnavailable();
  }, [mod, onUnavailable]);

  const fit = (bbox) => {
    if (!bbox) return;
    if (provider === MapProvider.MapLibre && cameraRef.current && cameraRef.current.fitBounds) cameraRef.current.fitBounds([bbox.west, bbox.south, bbox.east, bbox.north], { duration: 300 });
    else if (nativeMapRef.current && nativeMapRef.current.fitToCoordinates) {
      nativeMapRef.current.fitToCoordinates(
        [
          { latitude: bbox.south, longitude: bbox.west },
          { latitude: bbox.north, longitude: bbox.east }
        ],
        { animated: true }
      );
    }
  };
  const fitRef = useRef(fit);
  useEffect(() => {
    fitRef.current = fit;
  });

  const focusKey = focus ? JSON.stringify(focus) : '';
  useEffect(() => {
    if (!focus) return;
    if (focus.bbox) {
      fitRef.current(focus.bbox);
      return;
    }
    if (!Number.isFinite(Number(focus.lat)) || !Number.isFinite(Number(focus.lng))) return;
    const z = Number.isFinite(Number(focus.zoom)) ? Number(focus.zoom) : Math.max(config.defaultZoom, 11);
    if (provider === MapProvider.MapLibre && cameraRef.current && cameraRef.current.flyTo) cameraRef.current.flyTo({ center: [focus.lng, focus.lat], zoom: z, duration: 300 });
    else if (nativeMapRef.current && nativeMapRef.current.animateToRegion) {
      const delta = longitudeDeltaForZoom(z);
      nativeMapRef.current.animateToRegion({ latitude: focus.lat, longitude: focus.lng, latitudeDelta: Math.min(170, delta), longitudeDelta: delta }, 300);
    }
    // focusKey stands for focus.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusKey, provider, config.defaultZoom]);

  if (!mod) return null;

  const press = (feature) => {
    if (feature.kind === 'Cluster') {
      fit(feature.bbox);
      if (onCluster) onCluster(feature.bbox);
      return;
    }
    onPressItem(mapFeatureItem(feature));
  };
  const marker = (feature) => <Pin feature={feature} clusterLabel={clusterLabel} theme={theme} selected={feature.kind === 'Point' && mapFeatureItem(feature).id === selectedId} onPress={() => press(feature)} />;
  const attribution = config.attribution ? (
    <View pointerEvents="none" style={[styles.attribution, { backgroundColor: theme.colors.surface }]} testID={testID + '-attribution'}>
      <AppText variant="caption" color="textMuted">
        {String(config.attribution).replace(/\[([^\]]+)\]\([^)]+\)/g, (_match, text) => text)}
      </AppText>
    </View>
  ) : null;

  if (provider === MapProvider.MapLibre) {
    const { Map, Camera, Marker } = mod;
    const style = theme.dark && config.darkStyleUrl ? config.darkStyleUrl : config.styleUrl;
    return (
      <View style={styles.fill} testID={testID} accessibilityLabel={accessibilityLabel}>
        <Map
          style={styles.fill}
          mapStyle={style}
          attribution
          logo={false}
          onDidFailLoadingMap={onUnavailable}
          onRegionDidChange={(event) => {
            const state = event && event.nativeEvent ? event.nativeEvent : event;
            if (!state || !state.bounds) return;
            const [west, south, east, north] = state.bounds;
            onViewChange({ bbox: toBbox({ south, west, north, east }), zoom: Math.round(state.zoom) });
          }}
          testID={testID + '-maplibre'}
        >
          <Camera ref={cameraRef} initialViewState={{ center: [center.lng, center.lat], zoom }} maxZoom={config.maxZoom} />
          {features.map((feature) => {
            const pos = featurePosition(feature);
            return (
              <Marker key={featureKey(feature)} id={featureKey(feature)} lngLat={[pos.lng, pos.lat]} anchor="center">
                {marker(feature)}
              </Marker>
            );
          })}
        </Map>
        {attribution}
      </View>
    );
  }

  const MapView = mod.default || mod.MapView;
  const { Marker } = mod;
  const delta = longitudeDeltaForZoom(zoom);
  return (
    <View style={styles.fill} testID={testID} accessibilityLabel={accessibilityLabel}>
      <MapView
        ref={nativeMapRef}
        style={styles.fill}
        initialRegion={{ latitude: center.lat, longitude: center.lng, latitudeDelta: Math.min(170, delta), longitudeDelta: delta }}
        userInterfaceStyle={theme.dark ? 'dark' : 'light'}
        onRegionChangeComplete={(region) => onViewChange({ bbox: toBbox(regionToBbox(region)), zoom: zoomForLongitudeDelta(region.longitudeDelta) })}
        testID={testID + '-native'}
      >
        {features.map((feature) => {
          const pos = featurePosition(feature);
          return (
            <Marker key={featureKey(feature)} coordinate={{ latitude: pos.lat, longitude: pos.lng }} onPress={() => press(feature)} tracksViewChanges={false}>
              {marker(feature)}
            </Marker>
          );
        })}
      </MapView>
      {attribution}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  cluster: { minWidth: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 8 },
  point: { width: 22, height: 22, borderRadius: 11, borderWidth: 3 },
  selected: { width: 30, height: 30, borderRadius: 15, borderWidth: 4 },
  attribution: { position: 'absolute', right: 4, bottom: 4, paddingHorizontal: 6, paddingVertical: 2, borderRadius: 4, opacity: 0.85 }
});
