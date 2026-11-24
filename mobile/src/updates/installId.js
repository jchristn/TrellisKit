import * as Crypto from 'expo-crypto';
import * as SecureStore from 'expo-secure-store';

/**
 * Install id for live updates (docs/LIVE_UPDATES_CONTRACT.md decisions 6 and 13): a random UUID
 * created once per install and kept in secure storage, sent only on update checks
 * (`X-TK-Install-Id`) so staged rollouts bucket installs deterministically.
 *
 * It is deliberately not linked to the member: it never goes on API requests (identityHeaders
 * does not include it), never into telemetry with a member id, and signing in or out does not
 * touch it. A reinstall (which clears secure storage on Android, and on iOS when the keychain item
 * is not restored) produces a new id.
 */

/** Secure-store key. */
export const InstallIdKey = 'tk.installId';

const OPTIONS = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK };
const Valid = /^[A-Za-z0-9-]{1,64}$/;

let pending = null;

async function loadOrCreate() {
  try {
    const stored = await SecureStore.getItemAsync(InstallIdKey, OPTIONS);
    if (typeof stored === 'string' && Valid.test(stored)) return stored;
  } catch {
    // Unreadable storage: create a new id (kept for this session if it cannot be saved).
  }
  const id = Crypto.randomUUID();
  try {
    await SecureStore.setItemAsync(InstallIdKey, id, OPTIONS);
  } catch {
    // Not persisted: the id still works for this session, and the next launch tries again.
  }
  return id;
}

/**
 * The install id, created on first use.
 * @returns {Promise<string>}
 */
export function getInstallId() {
  if (!pending) {
    pending = loadOrCreate().catch((error) => {
      pending = null;
      throw error;
    });
  }
  return pending;
}

/** Forget the cached id (tests). */
export function resetInstallIdCache() {
  pending = null;
}
