import { toEnumerationBody } from './enumeration.js';
const enc = encodeURIComponent;

/** Owner-managed profile galleries. Media bytes use the standard upload API. */
export const profileMediaMethods = {
  enumerateProfileMedia(userId, query = {}, signal) { return this.post('/users/' + enc(userId) + '/media/enumerate', toEnumerationBody(query), { signal }); },
  addProfileMedia(userId, mediaIds, metadata = {}) { return this.post('/users/' + enc(userId) + '/media', { mediaIds, ...metadata }); },
  removeProfileMedia(userId, itemId) { return this.delete('/users/' + enc(userId) + '/media/' + enc(itemId)); }
};
