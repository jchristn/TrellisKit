import { Platform } from 'react-native';
import { contentTypeOf, ImageTypes } from './upload';

/**
 * Pick photos and videos for a post or comment, from the library or the camera
 * (expo-image-picker). On iOS the picker returns the compatible representation, so HEIC photos
 * arrive as JPEG; anything that still is not JPEG, PNG, or WebP is re-encoded to JPEG with
 * expo-image-manipulator (the server does not accept HEIC, contract 4.1).
 *
 * @param {object} options
 * @param {'library'|'camera'} options.source
 * @param {boolean} options.allowVideo
 * @param {number} options.limit How many items may still be added (library only).
 * @param {object} [deps] Overrides for tests.
 * @returns {Promise<object[]|'denied'|null>} Assets, 'denied' without permission, or null when cancelled.
 */
export async function pickMedia({ source, allowVideo, limit }, { picker = require('expo-image-picker'), manipulator = require('expo-image-manipulator'), os = Platform.OS } = {}) {
  const camera = source === 'camera';
  const permission = camera ? await picker.requestCameraPermissionsAsync() : await picker.requestMediaLibraryPermissionsAsync();
  if (permission && permission.granted === false && permission.accessPrivileges !== 'limited') return 'denied';
  const options = {
    mediaTypes: allowVideo ? ['images', 'videos'] : ['images'],
    quality: 1,
    exif: false,
    ...(os === 'ios' && picker.UIImagePickerPreferredAssetRepresentationMode ? { preferredAssetRepresentationMode: picker.UIImagePickerPreferredAssetRepresentationMode.Compatible } : null)
  };
  if (!camera && limit > 1) {
    options.allowsMultipleSelection = true;
    options.selectionLimit = limit;
    options.orderedSelection = true;
  }
  const result = camera ? await picker.launchCameraAsync(options) : await picker.launchImageLibraryAsync(options);
  if (!result || result.canceled || !Array.isArray(result.assets) || !result.assets.length) return null;
  const assets = [];
  for (const asset of result.assets.slice(0, Math.max(1, limit))) assets.push(await toUploadable(asset, manipulator));
  return assets;
}

/** Convert an image the server does not accept (HEIC, HEIF, GIF still frames) to JPEG. */
export async function toUploadable(asset, manipulator = require('expo-image-manipulator')) {
  const type = contentTypeOf(asset);
  const isVideo = asset.type === 'video' || type.startsWith('video/');
  if (isVideo || ImageTypes.includes(type)) return asset;
  const { ImageManipulator, SaveFormat } = manipulator;
  const rendered = await ImageManipulator.manipulate(asset.uri).renderAsync();
  const saved = await rendered.saveAsync({ compress: 0.9, format: SaveFormat.JPEG });
  return {
    ...asset,
    uri: saved.uri,
    width: saved.width || asset.width,
    height: saved.height || asset.height,
    mimeType: 'image/jpeg',
    fileName: asset.fileName ? asset.fileName.replace(/\.[^.]+$/, '') + '.jpg' : undefined,
    fileSize: undefined
  };
}
