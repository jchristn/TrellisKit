import { collectEnumeration } from '@trelliskit/shared';
import { useState } from 'react';
import { View } from 'react-native';
import { useAuth } from '../../auth/AuthProvider';
import { describeFeatureError } from '../../content/phase4';
import { roundedCurrentPosition } from '../../native/location';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Sheet from '../Sheet';
import TextField from '../TextField';

/**
 * Place lookup and "Near me" for a map search: a typed place goes through the server's geocoder
 * (the provider key stays server-side) and picking a candidate calls `onPlace(candidate)`; Near me
 * explains first, asks for the foreground location permission, and calls `onPosition` with the
 * position rounded to about 1 km. `t` is the filled directory strings.
 */
export default function PlaceLookup({ t, onPlace, onPosition, testID }) {
  const theme = useTheme();
  const { api } = useAuth();
  const [text, setText] = useState('');
  const [places, setPlaces] = useState(null);
  const [error, setError] = useState(null);
  const [prompt, setPrompt] = useState(false);
  const [note, setNote] = useState(null);

  async function search() {
    const query = text.trim();
    if (query.length < 2) return;
    setError(null);
    try {
      const result = await collectEnumeration((page, signal) => api.geocodePlace(query, page, signal), { maxResults: 5 });
      setPlaces(result.objects);
    } catch (e) {
      setPlaces(null);
      setError(e && e.code === 'IntegrationNotConfigured' ? t.placeSearchFailed : describeFeatureError(e, 'places', t));
    }
  }

  async function locate() {
    setPrompt(false);
    setNote(null);
    const result = await roundedCurrentPosition();
    if (result.status === 'ok') onPosition(result.position);
    else setNote(result.status === 'denied' ? t.locationDenied : t.locationFailed);
  }

  return (
    <View style={{ gap: theme.spacing.sm }}>
      <View style={{ flexDirection: 'row', alignItems: 'flex-end', gap: theme.spacing.sm }}>
        <TextField label={t.searchPlace} hideLabel placeholder={t.searchPlace} compact value={text} onChangeText={setText} maxLength={200} returnKeyType="search" onSubmitEditing={search} style={{ flex: 1 }} testID={testID + '-place'} />
        <Button label={t.nearMe} accessibilityHint={t.nearMeHint} variant="secondary" onPress={() => setPrompt(true)} style={{ alignSelf: 'auto' }} testID={testID + '-near-me'} />
      </View>
      {note ? (
        <AppText color="warning" accessibilityRole="alert" testID={testID + '-location-note'}>
          {note}
        </AppText>
      ) : null}
      {error ? (
        <AppText color="danger" accessibilityRole="alert" testID={testID + '-place-error'}>
          {error}
        </AppText>
      ) : null}
      {places ? (
        places.length ? (
          places.map((candidate, i) => (
            <Button
              key={candidate.label + i}
              label={candidate.label}
              variant="ghost"
              onPress={() => {
                setPlaces(null);
                onPlace(candidate);
              }}
              style={{ alignSelf: 'auto' }}
              testID={testID + '-place-' + i}
            />
          ))
        ) : (
          <AppText color="textMuted">{t.noPlaces}</AppText>
        )
      ) : null}
      <Sheet visible={prompt} title={t.locationPrompt.title} onClose={() => setPrompt(false)} scroll={false} testID={testID + '-location-sheet'}>
        <View style={{ gap: theme.spacing.md }}>
          <AppText>{t.locationPrompt.body}</AppText>
          <Button label={t.locationPrompt.allow} onPress={locate} testID={testID + '-location-allow'} />
          <Button label={t.locationPrompt.notNow} variant="ghost" onPress={() => setPrompt(false)} testID={testID + '-location-cancel'} />
        </View>
      </Sheet>
    </View>
  );
}

/** The camera target of a geocoder candidate: its box when it has one, else its point at a town zoom. */
export function placeTarget(candidate) {
  const b = candidate && candidate.bbox;
  if (b && [b.south, b.west, b.north, b.east].every((x) => Number.isFinite(Number(x))) && b.south < b.north) return { bbox: { south: Number(b.south), west: Number(b.west), north: Number(b.north), east: Number(b.east) } };
  return { lat: Number(candidate.lat), lng: Number(candidate.lng), zoom: 11 };
}
