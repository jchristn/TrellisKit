/** Default minimum member age in years, used when no property value is supplied. */
export const MinimumAgeYears = 18;

/**
 * Whether a string looks like an email address. Server-side validation is authoritative.
 * @param {string} value
 * @returns {boolean}
 */
export function isEmail(value) {
  return typeof value === 'string' && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
}

/**
 * Whether a handle is valid: 3 to 30 characters, letters, digits, underscore, or period.
 * @param {string} value
 * @returns {boolean}
 */
export function isHandle(value) {
  return typeof value === 'string' && /^[A-Za-z0-9_.]{3,30}$/.test(value);
}

/**
 * A handle suggestion from a display name, or from the part of an email address before the @ when
 * there is no name: lowercase letters, digits, periods, and underscores, 3 to 30 characters, with
 * the words of a name joined by periods (for example `sam.rivera`). Returns '' when nothing usable
 * is left.
 * @param {string} [displayName]
 * @param {string} [email]
 * @returns {string}
 */
export function suggestHandle(displayName, email) {
  const source = String(displayName || '').trim() || String(email || '').split('@')[0];
  let handle = source.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9_.]+/g, '.').replace(/\.{2,}/g, '.').replace(/^[._]+|[._]+$/g, '');
  if (!handle) return '';
  if (handle.length < 3) handle = (handle + '.member').slice(0, 30);
  return handle.slice(0, 30).replace(/[._]+$/g, '');
}

/**
 * The next suggestion when a suggested handle is taken: the same stem with a short random number.
 * @param {string} handle The taken handle.
 * @param {() => number} [random] Random source in [0, 1) (for tests).
 * @returns {string}
 */
export function numberedHandle(handle, random = Math.random) {
  const stem = String(handle || '').replace(/\d+$/, '').slice(0, 26);
  return stem + String(Math.floor(10 + random() * 990));
}

/**
 * Whether a person born on the given date is at least the minimum age today.
 * @param {Date} dateOfBirth
 * @param {Date} [today]
 * @param {number} [minimumAge] the property's `membership.minimumAge`
 * @returns {boolean}
 */
export function isAdult(dateOfBirth, today = new Date(), minimumAge = MinimumAgeYears) {
  if (!(dateOfBirth instanceof Date) || Number.isNaN(dateOfBirth.getTime())) return false;
  let age = today.getFullYear() - dateOfBirth.getFullYear();
  const beforeBirthday =
    today.getMonth() < dateOfBirth.getMonth() ||
    (today.getMonth() === dateOfBirth.getMonth() && today.getDate() < dateOfBirth.getDate());
  if (beforeBirthday) age -= 1;
  return age >= minimumAge;
}
