// Seeding context: the API client, the seed file, resolved members, and the summary counters.

import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { ApiError } from './client.mjs';

export class Context {
  constructor({ api, mailpit, log, seed, seedFile, webBaseUrl, options }) {
    this.api = api;
    this.mailpit = mailpit;
    this.log = log;
    this.seed = seed;
    this.seedFile = seedFile;
    this.seedDir = path.dirname(seedFile);
    this.webBaseUrl = webBaseUrl;
    this.options = options;
    this.namespace = seed.namespace ?? 'seed';
    this.property = null;
    this.members = new Map();
    this.ids = new Map();
    this.counts = new Map();
    this.warnings = [];
    this.gaps = [];
  }

  // Tags that mark an object as seeded with a stable key.
  tags(key, extra = {}) {
    return { ...(extra ?? {}), [this.namespace]: key };
  }

  labels(extra = []) {
    return [...(extra ?? [])];
  }

  count(type, outcome = 'created', n = 1) {
    const k = `${type}|${outcome}`;
    this.counts.set(k, (this.counts.get(k) ?? 0) + n);
  }

  warn(message) {
    this.warnings.push(message);
    this.log.warn(message);
  }

  gap(message) {
    if (!this.gaps.includes(message)) this.gaps.push(message);
    this.log.warn(`API gap: ${message}`);
  }

  setId(type, key, id) {
    if (!this.ids.has(type)) this.ids.set(type, new Map());
    this.ids.get(type).set(key, id);
  }

  id(type, key) {
    return this.ids.get(type)?.get(key) ?? null;
  }

  member(key) {
    const m = this.members.get(key);
    if (!m) throw new Error(`Unknown member key "${key}" in the seed file`);
    return m;
  }

  session(key) {
    const m = this.member(key);
    if (!m.session) throw new Error(`Member "${key}" has no session (account step failed?)`);
    return m.session;
  }

  // The member session used for staff writes that need a signed-in SystemAdmin.
  get adminSession() {
    const key = this.seed.accounts?.adminMember;
    if (key && this.members.get(key)?.session) return this.members.get(key).session;
    for (const m of this.members.values()) if ((m.spec.roles ?? []).some((r) => r.role === 'SystemAdmin') && m.session) return m.session;
    throw new Error('The seed file needs a member with the SystemAdmin role (accounts.adminMember)');
  }

  // Find seeded objects by their stable key through an enumerate route.
  async findByTag(enumeratePath, key, { auth = 'admin', filters, extra } = {}) {
    const body = { maxResults: 10, tags: { [this.namespace]: key }, ...(extra ?? {}) };
    if (filters) body.filters = filters;
    const data = await this.api.post(enumeratePath, body, { auth });
    return data.objects ?? [];
  }

  async assetPath(assetKey) {
    const asset = (this.seed.assets ?? []).find((a) => a.key === assetKey);
    if (!asset) throw new Error(`Unknown asset "${assetKey}"`);
    const file = path.resolve(this.seedDir, asset.file);
    try {
      await stat(file);
    } catch {
      return { asset, file: null };
    }
    return { asset, file };
  }

  // Upload a seed asset as `session` and return the media id (waits until Ready when asked).
  async uploadAsset(session, assetKey, { labels = [], tags = {}, purpose, scopeId, uploadPath = '/api/v1/media/uploads', completePrefix = '/api/v1/media', waitReady = false } = {}) {
    const { asset, file } = await this.assetPath(assetKey);
    if (!file) {
      this.warn(`asset ${assetKey} (${asset.file}) is missing; run generate-assets.mjs first`);
      return null;
    }
    const bytes = await readFile(file);
    const ext = path.extname(file).toLowerCase();
    const contentType = { '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.png': 'image/png', '.webp': 'image/webp', '.mp4': 'video/mp4', '.mov': 'video/quicktime' }[ext];
    const kind = contentType.startsWith('video/') ? 'Video' : 'Image';
    const body = { kind, contentType, sizeBytes: bytes.length, fileName: path.basename(file), labels, tags };
    if (kind === 'Video') body.durationSeconds = asset.durationSeconds ?? 8;
    if (purpose) body.purpose = purpose;
    if (scopeId) body.scopeId = scopeId;
    const sessionData = await this.api.post(uploadPath, body, { auth: session });
    const mediaId = sessionData.media.id;
    const single = sessionData.upload?.single;
    if (!single) throw new Error(`Upload of ${assetKey} needs multipart; keep seed assets under the multipart threshold`);
    const headers = { ...(single.headers ?? { 'Content-Type': contentType }) };
    // Local storage signs URLs back to the API itself: in MultiProperty that request needs the same property headers.
    if (single.url.startsWith(this.api.baseUrl)) {
      if (this.api.appId) headers['X-TK-App'] = this.api.appId;
      if (this.api.propertyId) headers['X-TK-Property'] = this.api.propertyId;
    }
    const res = await fetch(single.url, { method: single.method ?? 'PUT', headers, body: bytes });
    if (!res.ok) throw new Error(`PUT upload of ${assetKey} failed with ${res.status}: ${await res.text()}`);
    await this.api.post(`${completePrefix}/${mediaId}/complete`, {}, { auth: session });
    this.count(kind === 'Video' ? 'media (video)' : 'media (image)');
    if (waitReady) await this.waitMediaReady(session, mediaId);
    return mediaId;
  }

  async waitMediaReady(session, mediaId, timeoutMs = 180000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      try {
        const media = await this.api.get(`/api/v1/media/${mediaId}`, { auth: session });
        if (media.status === 'Ready') return media;
        if (media.status === 'Failed') throw new Error(`media ${mediaId} failed: ${media.failureReason}`);
      } catch (err) {
        if (!(err instanceof ApiError) || err.status !== 404) throw err;
      }
      await sleep(2000);
    }
    this.warn(`media ${mediaId} not Ready after ${timeoutMs / 1000}s (continuing)`);
    return null;
  }
}
