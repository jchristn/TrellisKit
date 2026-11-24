import Constants from 'expo-constants';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import { appIdFor } from '../api/identity';
import { getBundledProperty } from '../property/property';
import { getInstallId } from './installId';

/**
 * Runtime update request headers (docs/LIVE_UPDATES_CONTRACT.md decisions 13 and 15).
 *
 * The binary's embedded headers are the same for every channel (scripts/updates.js: the channel
 * is a placeholder and the install id is empty), so one runtime version serves every channel and
 * the server can promote an update between channels. Before each check the app replaces them with
 * `Updates.setUpdateRequestHeadersOverride` (expo-updates 58; the override is persisted natively
 * and used by the next check, including crash recovery checks on later launches):
 *
 *   expo-channel-name  the build channel
 *   X-TK-Property      the bundled property id
 *   X-TK-App           the iOS bundle id or Android package
 *   X-TK-Install-Id    the install id (src/updates/installId.js)
 *
 * The override replaces the whole header map and may only use keys the binary embeds, so all four
 * are always sent together.
 *
 * Where the build channel comes from: `extra.updates.channel` of the embedded app config. A
 * downloaded update carries the config captured at publish (scripts/publish-update.js removes the
 * channel from it, and a promoted update was published for another channel), so the channel seen
 * on an embedded launch is saved per binary and reused while running downloaded updates.
 */

/** Secure-store key for the saved build channel. */
export const BuildChannelKey = 'tk.updates.buildChannel';

/** Channel used when nothing else is known (the server's default for a missing header). */
export const DefaultChannel = 'production';

const ChannelPattern = /^[a-z0-9][a-z0-9-]{0,63}$/;

function isChannel(value) {
  return typeof value === 'string' && ChannelPattern.test(value);
}

/** The installed binary, so a saved channel is not reused after a different binary is installed. */
export function binaryKey(constants = Constants) {
  return String(constants.nativeAppVersion || '') + '(' + String(constants.nativeBuildVersion || '') + ')';
}

function configChannel(constants) {
  const extra = (constants.expoConfig && constants.expoConfig.extra) || {};
  const value = extra.updates && extra.updates.channel;
  return isChannel(value) ? value : null;
}

/**
 * The build channel of this binary.
 * @param {object} options
 * @param {object|null} options.updates expo-updates module.
 * @param {object} [options.constants] expo-constants default export.
 * @param {object} [options.store] expo-secure-store.
 * @returns {Promise<string>}
 */
export async function resolveBuildChannel({ updates, constants = Constants, store = SecureStore }) {
  const fromConfig = configChannel(constants);
  const binary = binaryKey(constants);
  if (updates && updates.isEmbeddedLaunch && fromConfig) {
    try {
      await store.setItemAsync(BuildChannelKey, JSON.stringify({ channel: fromConfig, binary }));
    } catch {
      // Not saved: the next embedded launch saves it.
    }
    return fromConfig;
  }
  let saved = null;
  try {
    saved = JSON.parse((await store.getItemAsync(BuildChannelKey)) || 'null');
  } catch {
    saved = null;
  }
  if (saved && saved.binary === binary && isChannel(saved.channel)) return saved.channel;
  return fromConfig || (saved && isChannel(saved.channel) ? saved.channel : DefaultChannel);
}

/**
 * The header map for update checks.
 * @param {object} options
 * @param {string} options.channel
 * @param {string} options.installId
 * @param {object} [options.property]
 * @param {string} [options.os]
 * @returns {Record<string, string>}
 */
export function updateRequestHeaders({ channel, installId, property = getBundledProperty(), os = Platform.OS }) {
  return {
    'expo-channel-name': channel,
    'X-TK-Property': (property && property.id) || '',
    'X-TK-App': appIdFor(property, os),
    'X-TK-Install-Id': installId || ''
  };
}

/**
 * Apply the runtime headers to expo-updates. Never throws: a binary built before these headers
 * existed rejects the override (its embedded keys differ) and keeps its embedded headers.
 * @param {object} options
 * @param {object|null} options.updates expo-updates module.
 * @param {object} [options.constants]
 * @param {object} [options.store]
 * @param {() => Promise<string>} [options.installId]
 * @returns {Promise<{ applied: boolean, headers: Record<string, string>|null, error: string|null }>}
 */
export async function applyUpdateRequestHeaders({ updates, constants = Constants, store = SecureStore, installId = getInstallId }) {
  if (!updates || typeof updates.setUpdateRequestHeadersOverride !== 'function') return { applied: false, headers: null, error: 'unsupported' };
  let headers = null;
  try {
    const [channel, id] = await Promise.all([resolveBuildChannel({ updates, constants, store }), installId()]);
    headers = updateRequestHeaders({ channel, installId: id });
    updates.setUpdateRequestHeadersOverride(headers);
    return { applied: true, headers, error: null };
  } catch (error) {
    return { applied: false, headers, error: (error && error.message) || String(error) };
  }
}
