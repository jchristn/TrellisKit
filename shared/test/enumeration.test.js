import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  createCursorPager, createEnumerationQuery, isStaffKey, metadataForSave, metadataOf, normalizeEnumerationResult, orderingToSort, parseEnumerationParams,
  sortToOrdering, splitStaffMetadata, stripStaffMetadata, toEnumerationBody, toEnumerationParams, toEnumerationSearch, validateMetadata
} from '../src/index.js';

test('createEnumerationQuery clamps, cleans, and drops empty values', () => {
  assert.deepEqual(createEnumerationQuery({}), { maxResults: 25 });
  assert.equal(createEnumerationQuery({ maxResults: 5000 }).maxResults, 100);
  assert.equal(createEnumerationQuery({ maxResults: 5000 }, { maxLimit: 500 }).maxResults, 500);
  assert.equal(createEnumerationQuery({ maxResults: 0 }).maxResults, 1);
  assert.deepEqual(createEnumerationQuery({ continuationToken: 't', skip: 50 }), { maxResults: 25, continuationToken: 't' });
  assert.deepEqual(createEnumerationQuery({ skip: 50, search: '  welcome ', labels: ['Topics', 'topics', ' ', 'x'], tags: { level: 'beginner', has: null }, filters: { state: '', pinned: true, kind: 'Video' } }), {
    maxResults: 25, skip: 50, search: 'welcome', labels: ['Topics', 'x'], labelMatch: 'All', tags: { level: 'beginner', has: '*' }, filters: { pinned: true, kind: 'Video' }
  });
  assert.equal(createEnumerationQuery({ labels: ['a'], labelMatch: 'Any' }).labelMatch, 'Any');
  assert.equal(createEnumerationQuery({ labels: Array.from({ length: 30 }, (_, i) => 'l' + i) }).labels.length, 20);
});

test('GET form repeats labels, uses tag.<key>, and flattens filters; POST form keeps structure', () => {
  const q = { maxResults: 10, ordering: 'CreatedAscending', labels: ['topics', 'staff:featured'], labelMatch: 'Any', tags: { level: 'beginner', 'staff:review': '*' }, filters: { state: 'Published' } };
  assert.deepEqual(toEnumerationParams(q), { maxResults: 10, ordering: 'CreatedAscending', labels: ['topics', 'staff:featured'], labelMatch: 'Any', 'tag.level': 'beginner', 'tag.staff:review': '*', state: 'Published' });
  assert.equal(toEnumerationSearch(q), 'maxResults=10&ordering=CreatedAscending&labels=topics&labels=staff%3Afeatured&labelMatch=Any&tag.level=beginner&tag.staff%3Areview=*&state=Published');
  assert.deepEqual(toEnumerationBody(q), { maxResults: 10, ordering: 'CreatedAscending', labels: ['topics', 'staff:featured'], labelMatch: 'Any', tags: { level: 'beginner', 'staff:review': '*' }, filters: { state: 'Published' } });
});

test('the query-string form round-trips through parseEnumerationParams', () => {
  const q = { maxResults: 50, skip: 100, ordering: 'Top', search: 'welcome', labels: ['a', 'b'], labelMatch: 'Any', tags: { level: 'x', has: '*' }, filters: { kind: 'Image' } };
  const parsed = parseEnumerationParams(toEnumerationSearch(q, { maxLimit: 500 }), { filterKeys: ['kind'] });
  assert.deepEqual(createEnumerationQuery(parsed, { maxLimit: 500 }), createEnumerationQuery(q, { maxLimit: 500 }));
  const noPaging = parseEnumerationParams('maxResults=9&labels=z&other=1&kind=Video', { filterKeys: ['kind'], paging: false });
  assert.deepEqual(noPaging, { labels: ['z'], tags: {}, filters: { kind: 'Video' } });
  const prefixed = parseEnumerationParams('m.labels=a&m.tag.k=v&labels=ignored', { prefix: 'm.' });
  assert.deepEqual(prefixed.labels, ['a']);
  assert.deepEqual(prefixed.tags, { k: 'v' });
});

test('normalizeEnumerationResult accepts EnumerationResult, CursorPage, and page-number shapes', () => {
  const r = normalizeEnumerationResult({ success: true, maxResults: 2, skip: 0, continuationToken: 'n', endOfResults: false, totalRecords: 5, recordsRemaining: 3, objects: [1, 2], unreadCount: 4 });
  assert.deepEqual([r.objects, r.continuationToken, r.endOfResults, r.totalRecords, r.extra], [[1, 2], 'n', false, 5, { unreadCount: 4 }]);
  const end = normalizeEnumerationResult({ objects: [], continuationToken: null, endOfResults: true });
  assert.equal(end.endOfResults, true);
  const cursor = normalizeEnumerationResult({ items: [1], nextCursor: 'c', commentCount: 9 });
  assert.deepEqual([cursor.objects, cursor.continuationToken, cursor.endOfResults, cursor.extra], [[1], 'c', false, { commentCount: 9 }]);
  const paged = normalizeEnumerationResult({ pageNumber: 2, pageSize: 10, totalCount: 25, totalPages: 3, items: [1, 2, 3] });
  assert.deepEqual([paged.skip, paged.totalRecords, paged.recordsRemaining, paged.endOfResults], [10, 25, 12, false]);
});

test('sort strings and ordering names convert both ways', () => {
  assert.equal(sortToOrdering('-createdUtc'), 'CreatedDescending');
  assert.equal(sortToOrdering('displayName'), 'DisplayNameAscending');
  assert.equal(sortToOrdering('Top'), 'Top');
  assert.equal(sortToOrdering(''), '');
  assert.equal(orderingToSort('CreatedDescending', ['createdUtc']), '-createdUtc');
  assert.equal(orderingToSort('DisplayNameAscending'), 'displayName');
});

test('the pager follows continuation tokens until endOfResults', async () => {
  const pages = { '': { objects: [{ id: 'a' }], continuationToken: 't1', endOfResults: false, totalRecords: 2 }, t1: { objects: [{ id: 'b' }], continuationToken: null, endOfResults: true } };
  const seen = [];
  const pager = createCursorPager(async ({ cursor }) => { seen.push(cursor); return pages[cursor || '']; });
  await pager.loadMore();
  assert.equal(pager.getState().totalRecords, 2);
  await pager.loadMore();
  await pager.loadMore();
  assert.deepEqual(seen, [null, 't1']);
  assert.deepEqual(pager.getState().items.map((x) => x.id), ['a', 'b']);
});

test('metadata validation, staff-only handling, and save shapes', () => {
  assert.ok(isStaffKey('Staff:review'));
  assert.ok(!isStaffKey('staffing'));
  const v = validateMetadata({ labels: ['Topics', 'topics', 'bad!label', 'x'.repeat(65), 'staff:featured'], tags: { level: 'beginner', 'bad key!': 'v', long: 'x'.repeat(513), 'staff:review': 'pending' } });
  assert.deepEqual(v.labels, ['Topics']);
  assert.deepEqual(v.tags, { level: 'beginner' });
  assert.deepEqual(v.problems.sort(), ['labelInvalid:bad!label', 'labelInvalid:' + 'x'.repeat(65), 'staffNotAllowed:staff:featured', 'staffNotAllowed:staff:review', 'tagKeyInvalid:bad key!', 'tagValueTooLong:long'].sort());
  const staff = validateMetadata({ labels: ['staff:featured'], tags: { 'staff:review': 'pending' } }, { isStaff: true });
  assert.deepEqual(staff.problems, []);
  assert.equal(validateMetadata({ labels: Array.from({ length: 51 }, (_, i) => 'l' + i) }).problems.includes('labelsTooMany'), true);
  assert.deepEqual(stripStaffMetadata({ labels: ['a', 'staff:b'], tags: { k: 'v', 'staff:x': 'y' } }), { labels: ['a'], tags: { k: 'v' } });
  assert.deepEqual(splitStaffMetadata({ labels: ['a', 'staff:b'], tags: { 'staff:x': 'y' } }).staffPart, { labels: ['staff:b'], tags: { 'staff:x': 'y' } });
  assert.deepEqual(metadataForSave({ labels: ['a', 'staff:b'], tags: {} }), { labels: ['a'], tags: {} });
  assert.deepEqual(metadataForSave({ labels: ['a', 'staff:b'], tags: {} }, { isStaff: true }), { labels: ['a', 'staff:b'], tags: {} });
  assert.deepEqual(metadataOf({}), { labels: [], tags: {} });
});

test('boolean filters are sent as JSON booleans; URL strings convert; unchecked false is dropped', () => {
  assert.deepEqual(createEnumerationQuery({ filters: { a: true, b: 'true', c: 'false', d: false, e: 'Video' } }).filters, { a: true, b: true, c: false, e: 'Video' });
  assert.deepEqual(toEnumerationParams({ filters: { a: true, c: 'false' } }), { maxResults: 25, a: true, c: false });
  assert.equal(toEnumerationSearch({ filters: { a: true, c: 'false' } }), 'maxResults=25&a=true&c=false');
});
