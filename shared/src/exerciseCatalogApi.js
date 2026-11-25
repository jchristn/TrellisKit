import { toEnumerationBody } from './enumeration.js';

const entry = (id, prefix = '') => prefix + '/catalog/entries/' + encodeURIComponent(id);
const body = (q) => toEnumerationBody(q || {}, { maxLimit: 100 });

/** Reviewed movement catalog; independent of the editorial series and lesson library. */
export const exerciseCatalogMethods = {
  adminEnumerateCatalogImportSources(q, signal) { return this.post('/admin/catalog/import-sources/enumerate', body(q), { signal }); },
  adminGetCatalogImportSource(id, signal) { return this.get('/admin/catalog/import-sources/' + encodeURIComponent(id), { signal }); },
  adminCreateCatalogImportSource(data) { return this.post('/admin/catalog/import-sources', data); },
  adminUpdateCatalogImportSource(id, data) { return this.put('/admin/catalog/import-sources/' + encodeURIComponent(id), data); },
  adminReviewCatalogImportSource(id, data) { return this.post('/admin/catalog/import-sources/' + encodeURIComponent(id) + '/review', data); },
  getExerciseCatalogConfig(signal) { return this.get('/catalog/config', { signal }); },
  enumerateCatalogEntries(q, signal) { return this.post('/catalog/entries/enumerate', body(q), { signal }); },
  getCatalogEntry(id, signal) { return this.get(entry(id), { signal }); },
  enumeratePublicCatalogEntries(q, signal) { return this.post('/public/catalog/entries/enumerate', body(q), { signal, retryOnUnauthorized: false }); },
  getPublicCatalogEntry(id, signal) { return this.get(entry(id, '/public'), { signal, retryOnUnauthorized: false }); },
  enumerateCatalogRelationships(id, q, signal) { return this.post(entry(id) + '/relationships/enumerate', body(q), { signal }); },
  enumeratePublicCatalogRelationships(id, q, signal) { return this.post(entry(id, '/public') + '/relationships/enumerate', body(q), { signal, retryOnUnauthorized: false }); },
  adminEnumerateCatalogEntries(q, signal) { return this.post('/admin/catalog/entries/enumerate', body(q), { signal }); },
  adminGetCatalogEntry(id, signal) { return this.get(entry(id, '/admin'), { signal }); },
  adminCreateCatalogEntry(data) { return this.post('/admin/catalog/entries', data); },
  adminUpdateCatalogEntry(id, data) { return this.put(entry(id, '/admin'), data); },
  adminSubmitCatalogEntry(id, data) { return this.post(entry(id, '/admin') + '/submit', data); },
  adminApproveCatalogEntry(id, data) { return this.post(entry(id, '/admin') + '/approve', data); },
  adminRejectCatalogEntry(id, data) { return this.post(entry(id, '/admin') + '/reject', data); },
  adminArchiveCatalogEntry(id, data) { return this.post(entry(id, '/admin') + '/archive', data); },
  adminEnumerateCatalogRelationships(id, q, signal) { return this.post(entry(id, '/admin') + '/relationships/enumerate', body(q), { signal }); },
  adminSetCatalogRelationships(id, data) { return this.put(entry(id, '/admin') + '/relationships', data); }
};
