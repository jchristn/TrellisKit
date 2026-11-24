/** Server limit for PUT /me/avatar. */
export const MaxAvatarBytes = 5 * 1024 * 1024;

/** Longest edge of the uploaded avatar, in pixels. */
export const AvatarEdge = 1024;

const QUALITIES = [0.8, 0.6, 0.4, 0.25];

/**
 * Let the member choose a square photo from the library.
 * @returns {Promise<{ uri: string, width?: number, height?: number }|null|'denied'>}
 */
export async function pickAvatarImage() {
  const ImagePicker = require('expo-image-picker');
  const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
  if (permission && permission.granted === false && permission.accessPrivileges !== 'limited') return 'denied';
  const result = await ImagePicker.launchImageLibraryAsync({ mediaTypes: ['images'], allowsEditing: true, aspect: [1, 1], quality: 1 });
  if (!result || result.canceled || !result.assets || !result.assets.length) return null;
  const asset = result.assets[0];
  return { uri: asset.uri, width: asset.width, height: asset.height };
}

/** Read a local file URI as a Blob. */
async function readBlob(uri) {
  const response = await globalThis.fetch(uri);
  return response.blob();
}

/**
 * Resize to at most 1024 pixels on the long edge and re-encode as JPEG, lowering quality until
 * the result is under the 5 MB server limit.
 * @param {{ uri: string, width?: number, height?: number }} image
 * @param {object} [deps] Overrides for tests.
 * @returns {Promise<{ body: Blob, contentType: 'image/jpeg' }>}
 */
export async function prepareAvatarUpload(image, { manipulator = require('expo-image-manipulator'), read = readBlob } = {}) {
  const { ImageManipulator, SaveFormat } = manipulator;
  const context = ImageManipulator.manipulate(image.uri);
  const wide = (image.width || 0) >= (image.height || 0);
  const longEdge = Math.max(image.width || 0, image.height || 0);
  if (!longEdge || longEdge > AvatarEdge) context.resize(wide ? { width: AvatarEdge } : { height: AvatarEdge });
  const rendered = await context.renderAsync();
  let body = null;
  for (const compress of QUALITIES) {
    const saved = await rendered.saveAsync({ compress, format: SaveFormat.JPEG });
    body = await read(saved.uri);
    if (!body || typeof body.size !== 'number' || body.size <= MaxAvatarBytes) break;
  }
  if (body && typeof body.size === 'number' && body.size > MaxAvatarBytes) {
    const error = new Error('The photo is too large.');
    error.code = 'TooLarge';
    throw error;
  }
  return { body, contentType: 'image/jpeg' };
}
