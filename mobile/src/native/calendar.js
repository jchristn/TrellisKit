import { formatAddress } from '@trelliskit/shared';
import * as Calendar from 'expo-calendar';
import * as SecureStore from 'expo-secure-store';
import { Linking, Platform } from 'react-native';

/**
 * "Add to calendar" with expo-calendar (docs/PHASE4_CONTRACT.md section 4.5): after the calendar
 * permission prompt (add-only, write-only access on iOS 17 and later) the event is created in the device's default calendar from the same fields
 * as the ICS file, and the device event id is stored locally (keyed by the `evt_` id) so a later
 * change can be applied with one tap. Each occurrence of a recurring event is its own event.
 *
 * With add-only access (iOS 17 and later) the app cannot read events back, so it cannot update or
 * find the copy it added: the stored id is then the record that this occurrence was added on this
 * device, adding it again is refused (`alreadyAdded`) instead of creating a duplicate, and the
 * member is offered to open the calendar at the event's date. With full access the copy is updated
 * in place as before.
 */

const KEY_PREFIX = 'tk.calendarEvent.';

function storageKey(eventId) {
  return KEY_PREFIX + String(eventId).replace(/[^A-Za-z0-9._-]/g, '_');
}

/** The stored device calendar event id for an event, or null. */
export async function storedCalendarEventId(eventId) {
  try {
    return (await SecureStore.getItemAsync(storageKey(eventId))) || null;
  } catch {
    return null;
  }
}

/** Forget the device calendar event id (sign-out). */
export async function forgetCalendarEvent(eventId) {
  try {
    await SecureStore.deleteItemAsync(storageKey(eventId));
  } catch {
    // Nothing stored.
  }
}

/**
 * The device calendar fields for an Event payload: title, start and end (all-day events as
 * dates), the event's zone, the location respecting `addressVisibility`, notes with the summary and
 * the event link, and one alarm per reminder offset.
 * @param {object} event
 * @param {{ url?: string|null }} [options]
 */
export function calendarDetails(event, { url = null } = {}) {
  const location = event.location || {};
  const place = location.venue ? location.venue.name : location.name;
  const address = location.address ? formatAddress(location.address, location.addressVisibility) : '';
  const where = [place, address].filter(Boolean).join(', ') || (event.format === 'Online' && event.online ? event.online.platformLabel || '' : '');
  const notes = [event.summary, url].filter(Boolean).join('\n\n');
  const details = {
    title: event.title,
    startDate: new Date(event.startUtc),
    endDate: new Date(event.endUtc),
    allDay: Boolean(event.isAllDay),
    timeZone: event.timeZone || undefined,
    location: where || undefined,
    notes: notes || undefined,
    alarms: (event.reminderOffsetsMinutes || []).slice(0, 3).map((minutes) => ({ relativeOffset: -Math.abs(minutes) }))
  };
  if (url) details.url = url;
  return details;
}

/** Whether the OS supports add-only (write-only) calendar access: iOS 17 and later. */
export function supportsWriteOnlyAccess(os = Platform.OS, version = Platform.Version) {
  if (os !== 'ios') return false;
  const major = parseInt(String(version || ''), 10);
  return Number.isFinite(major) && major >= 17;
}

/**
 * Whether the app holds add-only (write-only) calendar access now: it may add events but not read
 * or update them. False with full access, without any access, and where add-only access does not exist.
 * @param {{ os?: string, version?: string|number }} [platform] Defaults to the running device.
 * @returns {Promise<boolean>}
 */
export async function hasAddOnlyAccess({ os = Platform.OS, version = Platform.Version } = {}) {
  if (!supportsWriteOnlyAccess(os, version)) return false;
  try {
    const full = await Calendar.getCalendarPermissions();
    if (full && full.granted) return false;
  } catch {
    // Check add-only access below.
  }
  try {
    const writeOnly = await Calendar.getCalendarPermissions(true);
    return Boolean(writeOnly && writeOnly.granted);
  } catch {
    return false;
  }
}

/**
 * Open the device calendar at the event's start (iOS `calshow:`, Android the calendar time URI).
 * Add-only access cannot read the added event back, so the calendar opens on its date.
 * @param {object} event
 * @param {{ os?: string }} [options]
 * @returns {Promise<boolean>} Whether the calendar opened.
 */
export async function openInDeviceCalendar(event, { os = Platform.OS } = {}) {
  const start = new Date(event && event.startUtc).getTime();
  if (!Number.isFinite(start)) return false;
  // iOS counts seconds from 2001-01-01T00:00:00Z (978307200 seconds after the Unix epoch).
  const url = os === 'ios' ? 'calshow:' + Math.floor(start / 1000 - 978307200) : 'content://com.android.calendar/time/' + start;
  try {
    await Linking.openURL(url);
    return true;
  } catch {
    return false;
  }
}

/**
 * Ask for calendar access. On iOS 17 and later the app asks only to add events (write-only
 * access: it never reads the member's calendars); elsewhere, or when the write-only request is
 * not available, it falls back to the regular calendar permission.
 * @param {{ os?: string, version?: string|number }} [platform] Defaults to the running device.
 * @returns {Promise<{ permission: object|null, writeOnly: boolean }>}
 */
export async function requestAddAccess({ os = Platform.OS, version = Platform.Version } = {}) {
  if (supportsWriteOnlyAccess(os, version)) {
    // Full access granted before (for example on an older app version) keeps "Update in calendar" working.
    try {
      const full = await Calendar.getCalendarPermissions();
      if (full && full.granted) return { permission: full, writeOnly: false };
    } catch {
      // Ask for add-only access below.
    }
    try {
      const permission = await Calendar.requestCalendarPermissions(true);
      if (permission) return { permission, writeOnly: true };
    } catch {
      // An older expo-calendar or OS: fall back to the regular request.
    }
  }
  return { permission: await Calendar.requestCalendarPermissions(), writeOnly: false };
}

async function writableCalendar() {
  if (Platform.OS === 'ios' && typeof Calendar.getDefaultCalendarSync === 'function') {
    try {
      const calendar = Calendar.getDefaultCalendarSync();
      if (calendar) return calendar;
    } catch {
      // Fall through to the list.
    }
  }
  const calendars = await Calendar.getCalendars(Calendar.EntityTypes ? Calendar.EntityTypes.EVENT : undefined);
  const list = Array.isArray(calendars) ? calendars : [];
  return list.find((c) => c && c.allowsModifications && c.isPrimary) || list.find((c) => c && c.allowsModifications) || null;
}

/**
 * Add the event to the device calendar, or update the copy added before. With add-only access an
 * occurrence added before on this device is not added again (`alreadyAdded`).
 * @param {object} event
 * @param {{ url?: string|null }} [options]
 * @returns {Promise<'added'|'updated'|'alreadyAdded'|'denied'|'noCalendar'|'failed'>}
 */
export async function saveToDeviceCalendar(event, options = {}) {
  let access;
  try {
    access = await requestAddAccess();
  } catch {
    return 'failed';
  }
  const permission = access.permission;
  if (!permission || !permission.granted) return 'denied';
  const details = calendarDetails(event, options);
  try {
    const existingId = await storedCalendarEventId(event.id);
    // Write-only access cannot read events back, so the copy added before cannot be found or updated:
    // the stored id records that it was added, and a second copy is never created.
    if (access.writeOnly && existingId) return 'alreadyAdded';
    if (!access.writeOnly && existingId && Calendar.ExpoCalendarEvent && typeof Calendar.ExpoCalendarEvent.get === 'function') {
      try {
        const existing = await Calendar.ExpoCalendarEvent.get(existingId);
        if (existing) {
          await existing.update(details);
          return 'updated';
        }
      } catch {
        // The member deleted it on the device: create a new one.
      }
    }
    const calendar = await writableCalendar();
    if (!calendar) return 'noCalendar';
    const created = await calendar.createEvent(details);
    if (created && created.id) await SecureStore.setItemAsync(storageKey(event.id), String(created.id));
    return 'added';
  } catch {
    return 'failed';
  }
}
