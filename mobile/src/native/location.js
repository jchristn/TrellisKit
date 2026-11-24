import { roundCoordinate } from '@trelliskit/shared';
import * as Location from 'expo-location';

/**
 * "Near me" position (docs/PHASE4_CONTRACT.md section 3.5, privacy): the foreground permission
 * only, asked after the app's own explanation, and the position rounded to 2 decimal places
 * (about 1 km) before anything leaves the device. Never stored.
 */

/** Whether foreground location permission is already granted (no prompt). */
export async function locationPermissionGranted() {
  try {
    const status = await Location.getForegroundPermissionsAsync();
    return Boolean(status && status.granted);
  } catch {
    return false;
  }
}

/**
 * Ask for foreground permission (the OS prompt) and read one rounded position.
 * @returns {Promise<{ status: 'ok', position: { lat: number, lng: number } } | { status: 'denied' | 'failed' }>}
 */
export async function roundedCurrentPosition() {
  let permission;
  try {
    permission = await Location.requestForegroundPermissionsAsync();
  } catch {
    return { status: 'failed' };
  }
  if (!permission || !permission.granted) return { status: 'denied' };
  try {
    const fix = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy ? Location.Accuracy.Balanced : 3 });
    const coords = fix && fix.coords;
    const lat = roundCoordinate(coords && coords.latitude, 2);
    const lng = roundCoordinate(coords && coords.longitude, 2);
    if (lat === null || lng === null) return { status: 'failed' };
    return { status: 'ok', position: { lat, lng } };
  } catch {
    return { status: 'failed' };
  }
}
