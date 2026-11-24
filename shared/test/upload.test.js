import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ApiError, UploadError, checkMediaFile, createFetchPut, mediaKindFor, pollMedia, resumeUpload, uploadMedia } from '../src/index.js';

const MiB = 1024 * 1024;

/** A fake storage and API: multipart sessions with `partCount` parts; `failPut` decides failures. */
function fakeBackend({ size, partSize, mode = 'Multipart', failPut = () => null }) {
  const stored = new Map();
  const log = [];
  let urlVersion = 1;
  const partCount = Math.ceil(size / partSize);
  const upload = () => ({
    mode,
    expiresUtc: '2026-11-12T19:00:00Z',
    single: mode === 'Single' ? { method: 'PUT', url: 'https://store.test/single?v=' + urlVersion, headers: { 'Content-Type': 'video/mp4' } } : null,
    multipart: mode === 'Multipart' ? {
      partSizeBytes: partSize,
      partCount,
      parts: Array.from({ length: partCount }, (_, i) => ({ partNumber: i + 1, method: 'PUT', url: 'https://store.test/part?n=' + (i + 1) + '&v=' + urlVersion }))
    } : null,
    uploadedParts: Array.from(stored.entries()).map(([partNumber, v]) => ({ partNumber, etag: v.etag, sizeBytes: v.size }))
  });
  const client = {
    async createUpload(body) { log.push('create ' + body.kind + ' ' + body.sizeBytes); return { media: { id: 'med_1', status: 'Uploading' }, upload: upload() }; },
    async refreshUploadUrls(id) { log.push('refresh ' + id); urlVersion += 1; return { upload: upload() }; },
    async completeUpload(id, parts) {
      log.push('complete ' + (parts ? parts.map((p) => p.partNumber + '=' + p.etag).join(',') : 'single'));
      if (mode === 'Multipart' && (!parts || parts.length !== partCount)) throw new ApiError('Missing parts', { status: 409, code: 'UploadIncomplete' });
      return { id, status: 'Processing' };
    }
  };
  const put = async ({ url, body, headers, onProgress }) => {
    const n = mode === 'Single' ? 0 : Number(new URL(url).searchParams.get('n'));
    const failure = failPut(n, url);
    log.push('put ' + n + (failure ? ' fail ' + failure : ''));
    if (failure) throw new UploadError('failed', { status: failure === 'network' ? 0 : failure });
    if (mode === 'Single') assert.equal(headers['Content-Type'], 'video/mp4');
    if (onProgress) onProgress(body.length);
    stored.set(n, { etag: '"etag-' + n + '"', size: body.length });
    return { etag: '"etag-' + n + '"' };
  };
  const slice = (start, end) => ({ start, end, length: end - start });
  return { client, put, slice, log, stored };
}

test('mediaKindFor and checkMediaFile apply the contract limits', () => {
  assert.equal(mediaKindFor('image/webp'), 'Image');
  assert.equal(mediaKindFor('video/quicktime'), 'Video');
  assert.equal(mediaKindFor('image/heic'), null);
  assert.equal(checkMediaFile({ contentType: 'image/gif', sizeBytes: 10 }).problem, 'UnsupportedMediaType');
  assert.equal(checkMediaFile({ contentType: 'image/png', sizeBytes: 21 * MiB }).problem, 'TooLarge');
  assert.equal(checkMediaFile({ contentType: 'image/png', sizeBytes: MiB }).problem, null);
  assert.equal(checkMediaFile({ contentType: 'video/mp4', sizeBytes: MiB }).problem, 'DurationRequired');
  assert.equal(checkMediaFile({ contentType: 'video/mp4', sizeBytes: MiB, durationSeconds: 301 }).problem, 'VideoTooLong');
  assert.equal(checkMediaFile({ contentType: 'video/mp4', sizeBytes: 3000 * MiB, durationSeconds: 10 }).problem, 'TooLarge');
  assert.equal(checkMediaFile({ contentType: 'video/mp4', sizeBytes: 3000 * MiB, durationSeconds: 5000 }, { isAdmin: true }).problem, null);
});

test('multipart upload sends every part in parallel, reports progress, and completes in part order', async () => {
  const size = 5 * 16 * MiB + 100;
  const b = fakeBackend({ size, partSize: 16 * MiB });
  const progress = [];
  let session = null;
  const media = await uploadMedia({ client: b.client, size, contentType: 'video/mp4', durationSeconds: 12, fileName: 'clip.mp4', slice: b.slice, put: b.put, onSession: (s) => { session = s; }, onProgress: (p) => progress.push(p) });
  assert.equal(media.status, 'Processing');
  assert.equal(session.media.id, 'med_1');
  assert.equal(b.log[0], 'create Video ' + size);
  assert.equal(b.log.filter((l) => l.startsWith('put')).length, 6);
  assert.equal(b.log.at(-1), 'complete 1="etag-1",2="etag-2",3="etag-3",4="etag-4",5="etag-5",6="etag-6"');
  assert.equal(progress[0].phase, 'creating');
  assert.equal(progress.at(-1).phase, 'completing');
  assert.equal(progress.filter((p) => p.phase === 'uploading').at(-1).loadedBytes, size);
  const loaded = progress.filter((p) => p.phase === 'uploading').map((p) => p.loadedBytes);
  assert.ok(loaded.every((v, i) => i === 0 || v >= 0));
});

test('multipart resume: a failed part refreshes URLs and only the missing parts are sent again', async () => {
  const size = 4 * 16 * MiB;
  let failed = false;
  const b = fakeBackend({ size, partSize: 16 * MiB, failPut: (n) => (n === 3 && !failed ? (failed = true, 'network') : null) });
  const media = await uploadMedia({ client: b.client, size, contentType: 'video/mp4', durationSeconds: 30, slice: b.slice, put: b.put, concurrency: 1 });
  assert.equal(media.status, 'Processing');
  assert.deepEqual(b.log, [
    'create Video ' + size,
    'put 1', 'put 2', 'put 3 fail network',
    'refresh med_1',
    'put 3', 'put 4',
    'complete 1="etag-1",2="etag-2",3="etag-3",4="etag-4"'
  ]);
});

test('resumeUpload continues an interrupted session from the parts storage already has', async () => {
  const size = 3 * 16 * MiB;
  const b = fakeBackend({ size, partSize: 16 * MiB });
  b.stored.set(1, { etag: '"etag-1"', size: 16 * MiB });
  b.stored.set(2, { etag: '"etag-2"', size: 16 * MiB });
  const media = await resumeUpload({ client: b.client, mediaId: 'med_1', size, slice: b.slice, put: b.put });
  assert.equal(media.status, 'Processing');
  assert.deepEqual(b.log, ['refresh med_1', 'put 3', 'complete 1="etag-1",2="etag-2",3="etag-3"']);
});

test('expired part URLs (403) are refreshed; non-retryable failures and exhausted attempts surface', async () => {
  const size = 2 * 16 * MiB;
  let count = 0;
  const b = fakeBackend({ size, partSize: 16 * MiB, failPut: (n) => (n === 2 && count++ < 1 ? 403 : null) });
  await uploadMedia({ client: b.client, size, contentType: 'video/mp4', durationSeconds: 3, slice: b.slice, put: b.put, concurrency: 1 });
  assert.ok(b.log.includes('refresh med_1'));

  const rejected = fakeBackend({ size, partSize: 16 * MiB, failPut: () => 413 });
  await assert.rejects(uploadMedia({ client: rejected.client, size, contentType: 'video/mp4', durationSeconds: 3, slice: rejected.slice, put: rejected.put }), (err) => err.status === 413);
  assert.ok(!rejected.log.includes('refresh med_1'));

  const down = fakeBackend({ size, partSize: 16 * MiB, failPut: () => 'network' });
  await assert.rejects(uploadMedia({ client: down.client, size, contentType: 'video/mp4', durationSeconds: 3, slice: down.slice, put: down.put, maxAttempts: 3 }), (err) => err.status === 0);
  assert.equal(down.log.filter((l) => l === 'refresh med_1').length, 2);
});

test('single upload sends the session headers and completes without parts; cancel aborts', async () => {
  const b = fakeBackend({ size: 1000, partSize: 16 * MiB, mode: 'Single' });
  const media = await uploadMedia({ client: b.client, size: 1000, contentType: 'video/mp4', durationSeconds: 1, slice: b.slice, put: b.put });
  assert.equal(media.status, 'Processing');
  assert.deepEqual(b.log, ['create Video 1000', 'put 0', 'complete single']);

  const controller = new AbortController();
  controller.abort();
  await assert.rejects(uploadMedia({ client: b.client, size: 1000, contentType: 'image/png', slice: b.slice, put: b.put, signal: controller.signal }), (err) => err.name === 'AbortError');
});

test('createFetchPut reads the ETag and maps failures', async () => {
  const ok = createFetchPut(async () => new Response('', { status: 200, headers: { etag: '"abc"' } }));
  assert.deepEqual(await ok({ url: 'https://s.test', body: 'x' }), { etag: '"abc"' });
  const bad = createFetchPut(async () => new Response('', { status: 403 }));
  await assert.rejects(bad({ url: 'https://s.test', body: 'x' }), (err) => err instanceof UploadError && err.status === 403);
  const offline = createFetchPut(async () => { throw new TypeError('fetch failed'); });
  await assert.rejects(offline({ url: 'https://s.test', body: 'x' }), (err) => err.status === 0);
});

test('pollMedia polls until every item settles or the timeout passes', async () => {
  let round = 0;
  const client = {
    async getMediaBatch(ids) {
      round += 1;
      return { endOfResults: true, objects: ids.map((id) => ({ id, status: round >= 3 || id === 'med_b' ? 'Ready' : 'Processing' })) };
    }
  };
  const updates = [];
  const result = await pollMedia({ client, ids: ['med_a', 'med_b'], intervalMs: 1, timeoutMs: 1000, sleep: async () => {}, onUpdate: (items) => updates.push(items.map((x) => x.status).join(',')) });
  assert.equal(result.timedOut, false);
  assert.deepEqual(updates, ['Processing,Ready', 'Processing,Ready', 'Ready,Ready']);

  let clock = 0;
  const stuck = { async getMediaBatch(ids) { return { endOfResults: true, objects: ids.map((id) => ({ id, status: 'Processing' })) }; } };
  const timed = await pollMedia({ client: stuck, ids: ['med_a'], intervalMs: 2000, timeoutMs: 60000, sleep: async (ms) => { clock += ms; }, now: () => clock });
  assert.equal(timed.timedOut, true);
  assert.equal(timed.items[0].status, 'Processing');
});
