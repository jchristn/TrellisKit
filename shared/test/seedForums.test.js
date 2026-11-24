import assert from 'node:assert/strict';
import test from 'node:test';
import { seedForums } from '../../tools/seed/lib/steps/forums.mjs';

function context({ collision = false } = {}) {
  const calls = [];
  const ctx = {
    property: { features: { forums: true } }, seed: { forums: [{ key: 'lounge', slug: 'lounge', name: 'Lounge' }] }, members: new Map(),
    get adminSession() { throw new Error('A forums-only seed must not require a member session'); },
    api: {
      request: async (method, path, body) => { calls.push({ method, path, body }); return collision ? { error: 'SlugInUse' } : { data: { id: 'frm_new' } }; },
      enumerateAll: async (path, query) => { calls.push({ path, query }); return [{ id: 'frm_existing', slug: 'lounge' }]; },
      put: async (path, body) => calls.push({ method: 'PUT', path, body })
    },
    log: { info() {} }, warn() {}, findByTag: async () => [], tags: (key) => ({ seed: key }),
    setId: (type, key, id) => { ctx.result = { type, key, id }; }, count: (type, action) => { ctx.counted = { type, action }; }
  };
  return { ctx, calls };
}

test('a forums-only seed creates forums without a member session', async () => {
  const { ctx, calls } = context();
  await seedForums(ctx);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].path, '/api/v1/admin/forums');
  assert.equal(ctx.result.id, 'frm_new');
});

test('a forums-only seed adopts a conflicting slug through paginated admin enumeration', async () => {
  const { ctx, calls } = context({ collision: true });
  await seedForums(ctx);
  assert.equal(calls[1].path, '/api/v1/admin/forums/enumerate');
  assert.deepEqual(calls[1].query, { maxResults: 200 });
  assert.equal(calls[2].path, '/api/v1/admin/forums/frm_existing');
  assert.equal(ctx.result.id, 'frm_existing');
  assert.equal(ctx.counted.action, 'adopted');
});
