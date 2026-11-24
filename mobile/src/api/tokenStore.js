import * as SecureStore from 'expo-secure-store';

/** Secure-store key holding the rotating refresh token. */
export const RefreshTokenKey = 'tk.refreshToken';

const OPTIONS = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK };

/**
 * Persistent storage for the refresh token, backed by the iOS Keychain and Android Keystore
 * through expo-secure-store. The access token is never written here; it lives in memory only.
 */
export const tokenStore = {
  /** @returns {Promise<string|null>} */
  async getRefreshToken() {
    try {
      return await SecureStore.getItemAsync(RefreshTokenKey, OPTIONS);
    } catch {
      return null;
    }
  },

  /** @param {string|null} token */
  async setRefreshToken(token) {
    if (!token) {
      await this.clear();
      return;
    }
    await SecureStore.setItemAsync(RefreshTokenKey, token, OPTIONS);
  },

  async clear() {
    try {
      await SecureStore.deleteItemAsync(RefreshTokenKey, OPTIONS);
    } catch {
      // Nothing stored or the keychain is unavailable; either way there is no token.
    }
  }
};

/**
 * Small per-device flags (for example "the notification prompt was shown"). Stored in
 * secure-store because it is the only key-value store this app ships; the values are not secret.
 */
export const deviceFlags = {
  /** @param {string} key */
  async get(key) {
    try {
      return (await SecureStore.getItemAsync(key)) === '1';
    } catch {
      return false;
    }
  },

  /** @param {string} key */
  async set(key) {
    try {
      await SecureStore.setItemAsync(key, '1');
    } catch {
      // Losing this flag only means the prompt may show again.
    }
  }
};
