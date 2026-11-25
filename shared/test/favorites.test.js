import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, FavoriteTargetType, FavoriteTargetTypes, SAVE_STATE_MAX_TARGETS, createSaveStateBatcher, favoritesMethods, isFavoriteTargetType, saveAvailable, saveKey
} from '../src/index.js';

function recorder(respond) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    const call = { method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined };
    calls.push(call);
    const payload = respond ? respond(call) : { ok: true };
    return new Response(JSON.stringify(payload), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('saves ApiClient methods exist and map to their routes', async () => {
  const names = ['saveItem', 'unsaveItem', 'listSaves', 'saveState', 'listSaveCollections', 'createSaveCollection', 'updateSaveCollection', 'orderSaveCollections', 'deleteSaveCollection'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(favoritesMethods).length, names.length);

  const { calls, client } = recorder();
  await client.saveItem('Lesson', 'les 1', { note: 'Drill', collectionIds: ['fvc_1'], ignored: true });
  await client.saveItem('Thread', 'thr_1');
  await client.unsaveItem('Lesson', 'les 1');
  await client.listSaves({ maxResults: 500, filters: { targetTypes: ['Lesson'] }, labels: ['guard'] });
  await client.saveState([{ targetType: 'Event', targetId: 'evt_1' }]);
  await client.listSaveCollections({});
  await client.createSaveCollection({ name: 'Guard' });
  await client.updateSaveCollection('fvc_1', { name: 'Guard work' });
  await client.orderSaveCollections(['fvc_2', 'fvc_1']);
  await client.deleteSaveCollection('fvc_1');

  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'PUT /me/saves/Lesson/les%201',
    'PUT /me/saves/Thread/thr_1',
    'DELETE /me/saves/Lesson/les%201',
    'POST /me/saves/enumerate',
    'POST /me/saves/state',
    'POST /me/save-collections/enumerate',
    'POST /me/save-collections',
    'PUT /me/save-collections/fvc_1',
    'PUT /me/save-collections/order',
    'DELETE /me/save-collections/fvc_1'
  ]);
  assert.deepEqual(calls[0].body, { note: 'Drill', collectionIds: ['fvc_1'] });
  assert.deepEqual(calls[1].body, {});
  assert.equal(calls[3].body.maxResults, 100, 'member maximum');
  assert.deepEqual(calls[3].body.labels, ['guard']);
  assert.deepEqual(calls[4].body, { targets: [{ targetType: 'Event', targetId: 'evt_1' }] });
  assert.deepEqual(calls[8].body, { ids: ['fvc_2', 'fvc_1'] });
});

test('target types and helpers', () => {
  assert.deepEqual(FavoriteTargetTypes, ['Lesson', 'Series', 'CatalogEntry', 'Venue', 'Instructor', 'Event', 'Post', 'Thread']);
  assert.equal(FavoriteTargetType.CatalogEntry, 'CatalogEntry');
  assert.ok(isFavoriteTargetType('Venue'));
  assert.ok(!isFavoriteTargetType('venue'));
  assert.equal(saveKey('Lesson', 'les_1'), 'Lesson:les_1');
  assert.ok(saveAvailable({ target: { available: true } }));
  assert.ok(!saveAvailable({ target: { available: false } }));
  assert.ok(!saveAvailable(null));
});

test('state batcher shares one request per tick and caches answers', async () => {
  const requests = [];
  const api = {
    async saveState(targets) {
      requests.push(targets);
      return { items: targets.map((t) => ({ ...t, saved: t.targetId === 'les_1', favoriteId: t.targetId === 'les_1' ? 'fav_1' : null })) };
    }
  };
  const batcher = createSaveStateBatcher(api);
  const [a, b, again] = await Promise.all([batcher.get('Lesson', 'les_1'), batcher.get('Event', 'evt_1'), batcher.get('Lesson', 'les_1')]);
  assert.equal(requests.length, 1);
  assert.equal(requests[0].length, 2, 'duplicate lookups share one target');
  assert.deepEqual(a, { saved: true, favoriteId: 'fav_1' });
  assert.deepEqual(b, { saved: false, favoriteId: null });
  assert.deepEqual(again, a);
  await batcher.get('Lesson', 'les_1');
  assert.equal(requests.length, 1, 'cached');
  batcher.set('Event', 'evt_1', { saved: true, favoriteId: 'fav_2' });
  assert.deepEqual(batcher.peek('Event', 'evt_1'), { saved: true, favoriteId: 'fav_2' });
  batcher.invalidate('Lesson', 'les_1');
  await batcher.get('Lesson', 'les_1');
  assert.equal(requests.length, 2, 'refetched after invalidate');
  batcher.invalidate();
  assert.equal(batcher.peek('Event', 'evt_1'), undefined);
});

test('state batcher never sends more than 100 targets per request', async () => {
  const sizes = [];
  const api = { async saveState(targets) { sizes.push(targets.length); return { items: [] }; } };
  const batcher = createSaveStateBatcher(api);
  const lookups = [];
  for (let i = 0; i < 250; i++) lookups.push(batcher.get('Post', 'pst_' + i));
  await Promise.all(lookups);
  assert.deepEqual(sizes, [SAVE_STATE_MAX_TARGETS, SAVE_STATE_MAX_TARGETS, 50]);
  const capped = createSaveStateBatcher(api, { maxBatch: 1000 });
  const more = [];
  for (let i = 0; i < 101; i++) more.push(capped.get('Post', 'pst_x' + i));
  await Promise.all(more);
  assert.deepEqual(sizes.slice(3), [100, 1], 'maxBatch is clamped to the server bound');
});

test('state batcher rejects every waiter when the request fails and requires saveState', async () => {
  const api = { async saveState() { throw new Error('offline'); } };
  const batcher = createSaveStateBatcher(api);
  await assert.rejects(Promise.all([batcher.get('Lesson', 'a'), batcher.get('Lesson', 'b')]), /offline/);
  assert.equal(batcher.peek('Lesson', 'a'), undefined, 'failures are not cached');
  assert.throws(() => createSaveStateBatcher({}), TypeError);
});
