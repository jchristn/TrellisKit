import { MapProvider } from '@trelliskit/shared';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { longitudeDeltaForZoom, mapModule } from '../../native/maps';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import TextField from '../TextField';

/** Decimal places kept for a placed pin (about 10 cm). */
export const PinDecimals = 6;

function round(value) {
  const factor = 10 ** PinDecimals;
  return Math.round(Number(value) * factor) / factor;
}

/**
 * The position of a map long-press event, from MapLibre (`nativeEvent.lngLat`, or a GeoJSON
 * feature with `geometry.coordinates`) or react-native-maps (`nativeEvent.coordinate`), rounded
 * to `PinDecimals`. Null when the event carries no usable position.
 * @param {any} event
 * @returns {{ lat: number, lng: number }|null}
 */
export function pinFromPressEvent(event) {
  const native = event && event.nativeEvent ? event.nativeEvent : event;
  if (!native) return null;
  let lat = null;
  let lng = null;
  if (native.coordinate && typeof native.coordinate === 'object') {
    lat = native.coordinate.latitude;
    lng = native.coordinate.longitude;
  } else if (Array.isArray(native.lngLat)) {
    [lng, lat] = native.lngLat;
  } else if (native.geometry && Array.isArray(native.geometry.coordinates)) {
    [lng, lat] = native.geometry.coordinates;
  }
  return validPin(lat, lng);
}

/** `{ lat, lng }` rounded, or null when either is missing or out of range. */
export function validPin(lat, lng) {
  const a = Number(lat);
  const b = Number(lng);
  if (lat === null || lat === undefined || lng === null || lng === undefined || lat === '' || lng === '') return null;
  if (!Number.isFinite(a) || !Number.isFinite(b) || a < -90 || a > 90 || b < -180 || b > 180) return null;
  return { lat: round(a), lng: round(b) };
}

/**
 * Coordinate text problems: `{ lat?: true, lng?: true }` for typed values that are not valid
 * coordinates. Leaving both fields blank is allowed (the pin stays unset).
 */
export function pinTextProblems(latText, lngText) {
  const latBlank = !String(latText || '').trim();
  const lngBlank = !String(lngText || '').trim();
  if (latBlank && lngBlank) return {};
  const out = {};
  const a = Number(latText);
  const b = Number(lngText);
  if (latBlank || !Number.isFinite(a) || a < -90 || a > 90) out.lat = true;
  if (lngBlank || !Number.isFinite(b) || b < -180 || b > 180) out.lng = true;
  return out;
}

/**
 * Place a venue's pin (docs/PHASE4_CONTRACT.md section 3.5): touch and hold the map (MapLibre or
 * the platform map) to drop the pin there. Latitude and longitude fields are always offered too:
 * they are the only input when the map provider is `None` or the native module is missing, and
 * the accessible way to place or correct a pin everywhere. Controlled: `value` is `{ lat, lng }`
 * or null.
 * @param {object} props
 * @param {string} props.provider MapProvider value.
 * @param {object} props.config `directoryConfig(property).map`.
 * @param {{ lat: number, lng: number }|null} props.value
 * @param {(pin: { lat: number, lng: number }|null) => void} props.onChange
 * @param {object} props.terms Filled directory strings (`terms.submit`).
 */
export default function PinPicker({ provider, config, value, onChange, terms, testID = 'pin-picker' }) {
  const theme = useTheme();
  const t = terms.submit;
  const [latText, setLatText] = useState(value ? String(value.lat) : '');
  const [lngText, setLngText] = useState(value ? String(value.lng) : '');
  const mod = provider && provider !== MapProvider.None ? mapModule(provider) : null;
  const problems = pinTextProblems(latText, lngText);

  function place(pin) {
    if (!pin) return;
    setLatText(String(pin.lat));
    setLngText(String(pin.lng));
    onChange(pin);
  }

  function typed(nextLat, nextLng) {
    setLatText(nextLat);
    setLngText(nextLng);
    onChange(validPin(nextLat.trim(), nextLng.trim()));
  }

  function clear() {
    setLatText('');
    setLngText('');
    onChange(null);
  }

  const center = value || (config && config.defaultCenter) || { lat: 0, lng: 0 };
  const zoom = value ? Math.max((config && config.defaultZoom) || 2, 14) : (config && config.defaultZoom) || 2;
  let map = null;
  if (mod && provider === MapProvider.MapLibre) {
    const { Map, Camera, Marker } = mod;
    const style = theme.dark && config.darkStyleUrl ? config.darkStyleUrl : config.styleUrl;
    map = (
      <Map style={styles.fill} mapStyle={style} attribution logo={false} onLongPress={(e) => place(pinFromPressEvent(e))} testID={testID + '-map'}>
        <Camera initialViewState={{ center: [center.lng, center.lat], zoom }} maxZoom={config.maxZoom} />
        {value ? (
          <Marker id="pin" lngLat={[value.lng, value.lat]} anchor="bottom">
            <View style={[styles.pin, { backgroundColor: theme.colors.primary, borderColor: theme.colors.surface }]} testID={testID + '-marker'} />
          </Marker>
        ) : null}
      </Map>
    );
  } else if (mod && provider === MapProvider.PlatformNative) {
    const MapView = mod.default || mod.MapView;
    const { Marker } = mod;
    const delta = longitudeDeltaForZoom(zoom);
    map = (
      <MapView style={styles.fill} initialRegion={{ latitude: center.lat, longitude: center.lng, latitudeDelta: Math.min(170, delta), longitudeDelta: delta }} userInterfaceStyle={theme.dark ? 'dark' : 'light'} onLongPress={(e) => place(pinFromPressEvent(e))} testID={testID + '-map'}>
        {value ? (
          <Marker coordinate={{ latitude: value.lat, longitude: value.lng }} tracksViewChanges={false}>
            <View style={[styles.pin, { backgroundColor: theme.colors.primary, borderColor: theme.colors.surface }]} testID={testID + '-marker'} />
          </Marker>
        ) : null}
      </MapView>
    );
  }

  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      <AppText variant="label">{t.pinTitle}</AppText>
      <AppText color="textMuted">{map ? t.pinHint : t.pinManualHint}</AppText>
      {map ? (
        <View style={[styles.mapBox, { borderColor: theme.colors.border, borderRadius: theme.radii.md }]} accessible={false} accessibilityLabel={value ? t.pinMap(value.lat, value.lng) : t.pinMap(null, null)}>
          {map}
        </View>
      ) : null}
      <AppText variant="caption" color="textMuted" accessibilityLiveRegion="polite" testID={testID + '-status'}>
        {value ? t.pinPlaced(value.lat, value.lng) : t.pinNone}
      </AppText>
      <View style={{ flexDirection: 'row', gap: theme.spacing.sm }}>
        <View style={{ flex: 1 }}>
          <TextField label={t.latitude} value={latText} onChangeText={(v) => typed(v, lngText)} keyboardType="numbers-and-punctuation" error={problems.lat ? t.latitudeInvalid : null} testID={testID + '-lat'} />
        </View>
        <View style={{ flex: 1 }}>
          <TextField label={t.longitude} value={lngText} onChangeText={(v) => typed(latText, v)} keyboardType="numbers-and-punctuation" error={problems.lng ? t.longitudeInvalid : null} testID={testID + '-lng'} />
        </View>
      </View>
      {value ? <Button label={t.pinClear} variant="ghost" onPress={clear} testID={testID + '-clear'} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  mapBox: { height: 220, overflow: 'hidden', borderWidth: 1 },
  pin: { width: 22, height: 22, borderRadius: 11, borderWidth: 3 }
});
