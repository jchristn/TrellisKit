import { Platform } from 'react-native';
import SharedCredentials from '../../modules/shared-credentials';

/**
 * After a successful email sign-in or sign-up, ask iOS to offer saving the login to the iCloud Keychain for the
 * property's website (domains.web), which the app declares as its webcredentials associated domain. iOS shows its
 * own prompt (nothing when the login is already saved). React Native's text fields do not reliably trigger the
 * automatic save prompt, so the app asks explicitly. A no-op on Android (autofill services save from the annotated
 * fields), without the native module, or without a web domain. Never throws.
 * @param {object} property Active property.
 * @param {string} email
 * @param {string} password
 * @returns {Promise<boolean>} Whether the credential was saved.
 */
export async function offerToSavePassword(property, email, password) {
  const domain = property && property.domains && property.domains.web;
  if (Platform.OS !== 'ios' || !SharedCredentials || !domain || !email || !password) return false;
  try {
    return Boolean(await SharedCredentials.save(domain, email, password));
  } catch {
    return false;
  }
}
