/**
 * Time zone choices for pickers. Uses the runtime's IANA list (`Intl.supportedValuesOf`) when it
 * has one; otherwise (for example on Hermes) a list of common zones covering every UTC offset.
 */

/** Common IANA zones, used when the runtime cannot list them. */
export const COMMON_TIME_ZONES = Object.freeze([
  'Pacific/Midway', 'Pacific/Honolulu', 'America/Anchorage', 'America/Los_Angeles', 'America/Vancouver', 'America/Tijuana', 'America/Phoenix', 'America/Denver', 'America/Edmonton',
  'America/Chicago', 'America/Mexico_City', 'America/Winnipeg', 'America/New_York', 'America/Toronto', 'America/Bogota', 'America/Lima', 'America/Halifax', 'America/Caracas',
  'America/Santiago', 'America/St_Johns', 'America/Sao_Paulo', 'America/Argentina/Buenos_Aires', 'America/Montevideo', 'Atlantic/South_Georgia', 'Atlantic/Azores', 'Atlantic/Reykjavik',
  'UTC', 'Europe/London', 'Europe/Dublin', 'Europe/Lisbon', 'Africa/Casablanca', 'Africa/Lagos', 'Europe/Paris', 'Europe/Berlin', 'Europe/Madrid', 'Europe/Rome', 'Europe/Amsterdam',
  'Europe/Stockholm', 'Europe/Warsaw', 'Africa/Johannesburg', 'Africa/Cairo', 'Europe/Athens', 'Europe/Helsinki', 'Europe/Kyiv', 'Asia/Jerusalem', 'Europe/Istanbul', 'Europe/Moscow',
  'Asia/Riyadh', 'Africa/Nairobi', 'Asia/Tehran', 'Asia/Dubai', 'Asia/Kabul', 'Asia/Karachi', 'Asia/Tashkent', 'Asia/Kolkata', 'Asia/Kathmandu', 'Asia/Dhaka', 'Asia/Yangon', 'Asia/Bangkok',
  'Asia/Jakarta', 'Asia/Ho_Chi_Minh', 'Asia/Shanghai', 'Asia/Hong_Kong', 'Asia/Singapore', 'Asia/Manila', 'Australia/Perth', 'Asia/Taipei', 'Asia/Seoul', 'Asia/Tokyo', 'Australia/Darwin',
  'Australia/Adelaide', 'Australia/Brisbane', 'Australia/Sydney', 'Australia/Melbourne', 'Pacific/Guam', 'Pacific/Noumea', 'Pacific/Auckland', 'Pacific/Fiji', 'Pacific/Tongatapu', 'Pacific/Kiritimati'
]);

/**
 * Sorted, distinct IANA time zones for a picker, always including `current` and `device` when given.
 * @param {string} [current] The value already chosen.
 * @param {string} [device] The device's zone.
 * @returns {string[]}
 */
export function timeZoneChoices(current, device) {
  let zones = [];
  try {
    if (typeof Intl !== 'undefined' && typeof Intl.supportedValuesOf === 'function') zones = Intl.supportedValuesOf('timeZone');
  } catch {
    zones = [];
  }
  const all = new Set(zones.length ? zones : COMMON_TIME_ZONES);
  all.add('UTC');
  if (current) all.add(current);
  if (device) all.add(device);
  return Array.from(all).sort();
}
