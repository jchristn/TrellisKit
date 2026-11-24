import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, activeMarker, catalogAccess, featureAccess, signedOutBrowsable, createProgressQueue, createProgressReporter, findLevel, formatDuration, formatTotalDuration, groupByLevel, lessonPath,
  libraryConfig, libraryLevels, libraryMethods, progressPercent, progressState, resumePosition, seriesPath
} from '../src/index.js';
import { combineFakes, createLibraryFake, makeLesson, makeLessonProgress, makeProgress, makeSeries } from '../src/testing/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('library ApiClient methods exist and map to their routes', async () => {
  const names = ['enumerateSeries', 'getSeries', 'enumerateSeriesLessons', 'followSeries', 'unfollowSeries', 'enumerateLessons', 'getLesson', 'putLessonProgress', 'resetLessonProgress', 'deleteLessonProgress',
    'completeLesson', 'uncompleteLesson', 'enumerateLibraryLevels', 'enumerateLessonComments', 'createLessonComment', 'putLessonReaction', 'deleteLessonReaction', 'enumerateLessonReactions',
    'promoteLesson', 'unpromoteLesson', 'enumerateContinueWatching', 'enumerateMyLessonProgress', 'createLessonDownload', 'enumerateMyDownloads', 'renewLessonDownload', 'deleteLessonDownload',
    'enumeratePublicLessons', 'getPublicLesson', 'adminEnumerateSeries', 'adminCreateSeries', 'adminGetSeries', 'adminUpdateSeries', 'adminDeleteSeries', 'adminPublishSeries', 'adminArchiveSeries',
    'adminUnarchiveSeries', 'adminOrderSeries', 'adminEnumerateLessons', 'adminCreateLesson', 'adminGetLesson', 'adminUpdateLesson', 'adminDeleteLesson', 'adminPublishLesson', 'adminScheduleLesson',
    'adminArchiveLesson', 'adminUnarchiveLesson', 'adminMoveLesson', 'adminOrderLessons', 'adminEnumerateMemberLibraryProgress', 'adminEnumerateLessonViewers', 'adminLibraryMetrics',
    'adminGetLibrarySettings', 'adminUpdateLibrarySettings', 'adminEnumerateLibraryLevels', 'adminCreateLibraryLevel', 'adminUpdateLibraryLevel', 'adminDeleteLibraryLevel', 'adminOrderLibraryLevels',
    'grantLibraryEditor', 'revokeLibraryEditor'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(libraryMethods).length, names.length);

  const { calls, client } = recorder();
  await client.enumerateSeries({ maxResults: 500, labels: ['a'], filters: { levels: ['l1'], followedOnly: true } });
  await client.enumerateSeriesLessons('ser_1');
  await client.putLessonProgress('les_1', { positionSeconds: 10, playedSeconds: 5, clientUpdatedUtc: 'x', sessionId: 's' });
  await client.enumerateContinueWatching();
  await client.enumeratePublicLessons({ maxResults: 99 });
  await client.adminEnumerateLessons({ maxResults: 900, filters: { seriesId: 'ser_1' } });
  await client.adminScheduleLesson('les_1', '2026-12-01T00:00:00Z');
  await client.adminMoveLesson('les_1', { seriesId: 'ser_2', position: 3 });
  await client.adminOrderLessons('ser_1', ['les_2', 'les_1']);
  await client.adminLibraryMetrics({ filters: { days: 9999 } });
  await client.adminEnumerateMemberLibraryProgress('usr_9');
  await client.adminEnumerateLessonViewers('les_1');
  await client.grantLibraryEditor('usr_5');
  await client.revokeLibraryEditor('usr_5');
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /series/enumerate', 'POST /series/ser_1/lessons/enumerate', 'PUT /lessons/les_1/progress', 'POST /me/library/continue/enumerate', 'POST /public/lessons/enumerate',
    'POST /admin/lessons/enumerate', 'POST /admin/lessons/les_1/schedule', 'POST /admin/lessons/les_1/move', 'PUT /admin/series/ser_1/lessons/order', 'POST /admin/metrics/library/enumerate',
    'POST /admin/library/members/usr_9/progress/enumerate', 'POST /admin/lessons/les_1/viewers/enumerate', 'POST /admin/moderators', 'DELETE /admin/moderators/usr_5?resourceType=Library&resourceId=*'
  ]);
  assert.deepEqual(calls[0].body, { maxResults: 100, labels: ['a'], labelMatch: 'All', filters: { levels: ['l1'], followedOnly: true } });
  assert.equal(calls[3].body.maxResults, 10);
  assert.equal(calls[4].body.maxResults, 20);
  assert.equal(calls[5].body.maxResults, 500);
  assert.deepEqual(calls[6].body, { publishAtUtc: '2026-12-01T00:00:00Z' });
  assert.deepEqual(calls[7].body, { seriesId: 'ser_2', position: 3 });
  assert.deepEqual(calls[8].body, { lessonIds: ['les_2', 'les_1'] });
  assert.deepEqual(calls[12].body, { userId: 'usr_5', resourceType: 'Library', resourceId: '*' });
});

test('libraryConfig applies engine defaults and clamps', () => {
  assert.deepEqual(libraryConfig({}), { levelSource: 'None', levelFieldKey: null, levels: [], downloadsEnabled: false, progressIntervalSeconds: 15, completionThresholdPercent: 90, resumeTailSeconds: 10 });
  const c = libraryConfig({ library: { levelSource: 'Custom', progressIntervalSeconds: 1, completionThresholdPercent: 10, resumeTailSeconds: 500, downloadsEnabled: true } });
  assert.equal(c.progressIntervalSeconds, 5);
  assert.equal(c.completionThresholdPercent, 50);
  assert.equal(c.resumeTailSeconds, 120);
  assert.equal(c.downloadsEnabled, true);
});

test('levels come from the server list or the profile field options', () => {
  const custom = { library: { levelSource: 'Custom', levels: [{ key: 'a', label: 'Alpha', color: '#111111' }, { key: 'b', label: 'Beta' }] } };
  assert.deepEqual(libraryLevels(custom).map((l) => l.key), ['a', 'b']);
  assert.equal(findLevel(custom, 'b').label, 'Beta');
  assert.equal(findLevel(custom, 'zzz').label, 'zzz');
  assert.equal(findLevel(custom, null), null);
  const field = { library: { levelSource: 'ProfileField', levelFieldKey: 'tier' }, profileFields: [{ key: 'tier', type: 'SingleSelect', label: 'Tier', options: [{ key: 't1', label: 'One' }, { key: 't2', label: 'Two', isRetired: true }] }] };
  assert.deepEqual(libraryLevels(field).map((l) => l.key), ['t1']);
  assert.deepEqual(libraryLevels({ library: { levelSource: 'None', levels: [{ key: 'x' }] } }), []);
  const groups = groupByLevel(custom, [{ id: 's1', levels: [{ key: 'b' }] }, { id: 's2', levels: [] }, { id: 's3', levels: ['a'] }]);
  assert.deepEqual(groups.map((g) => [g.level && g.level.key, g.items.map((i) => i.id)]), [['a', ['s3']], ['b', ['s1']], [null, ['s2']]]);
});

test('display helpers', () => {
  assert.equal(formatDuration(342), '5:42');
  assert.equal(formatDuration(3723), '1:02:03');
  assert.equal(formatDuration(null), '');
  assert.equal(formatTotalDuration(3420), '57 min');
  assert.equal(formatTotalDuration(7500), '2 h 5 min');
  assert.equal(resumePosition({ positionSeconds: 151 }, 342, 10), 151);
  assert.equal(resumePosition({ positionSeconds: 335 }, 342, 10), 0);
  assert.equal(resumePosition(null, 342), 0);
  assert.equal(progressPercent({ percent: 140 }), 100);
  assert.equal(progressState(null), 'NotStarted');
  assert.equal(progressState({ completed: false, maxPositionSeconds: 4 }), 'InProgress');
  assert.equal(progressState({ completed: true }), 'Completed');
  assert.equal(seriesPath('ser_1'), '/series/ser_1');
  assert.equal(lessonPath('a b'), '/lessons/a%20b');
  assert.deepEqual(activeMarker([{ seconds: 95, label: 'B' }, { seconds: 0, label: 'A' }], 100), { seconds: 95, label: 'B' });
});

test('progress reporter throttles to the interval and counts only real playback', () => {
  let t = 0;
  const sent = [];
  const r = createProgressReporter({ send: (b) => sent.push(b), intervalSeconds: 15, durationSeconds: 300, now: () => t, sessionId: 's1' });
  r.play(0);
  for (let i = 1; i <= 14; i += 1) {
    t = i * 1000;
    r.timeUpdate(i);
  }
  // First report goes out immediately on the first update (nothing sent yet), then throttles.
  assert.equal(sent.length, 1);
  t = 16000;
  r.timeUpdate(16);
  assert.equal(sent.length, 2);
  assert.equal(sent[1].positionSeconds, 16);
  assert.ok(sent[1].playedSeconds >= 14 && sent[1].playedSeconds <= 16);
  assert.equal(sent[1].sessionId, 's1');
  // A seek ahead never counts as played.
  t = 17000;
  r.seeked(250);
  assert.equal(sent[2].positionSeconds, 250);
  assert.equal(sent[2].playedSeconds, 0);
  r.play(250);
  t = 18000;
  r.timeUpdate(251);
  t = 19000;
  r.timeUpdate(290); // jump of 39 s in 1 s: skipped, not played
  const paused = r.pause();
  assert.equal(paused.positionSeconds, 290);
  assert.ok(paused.playedSeconds <= 2, 'skip not counted: ' + paused.playedSeconds);
  // Positions clamp to the duration.
  t = 20000;
  r.seeked(9999);
  assert.equal(sent[sent.length - 1].positionSeconds, 300);
});

test('progress reporter clamps played seconds to twice the interval and respects the rate', () => {
  let t = 0;
  const sent = [];
  const r = createProgressReporter({ send: (b) => sent.push(b), intervalSeconds: 5, now: () => t });
  r.setRate(2);
  r.play(0);
  t = 1000;
  r.timeUpdate(2); // 2 s of media in 1 s at 2x is real playback
  assert.equal(sent[0].playedSeconds, 2);
  for (let i = 2; i <= 30; i += 1) {
    t = i * 1000;
    r.state; // eslint-friendly read
    r.timeUpdate(i * 2);
  }
  const all = sent.map((b) => b.playedSeconds);
  assert.ok(all.every((v) => v <= 10), 'clamped to 2x interval: ' + all.join(','));
  r.close();
  const count = sent.length;
  r.flush();
  assert.equal(sent.length, count, 'closed reporters stay silent');
  assert.throws(() => createProgressReporter({}), /send/);
});

test('offline progress queue replays in order, keeps retryable failures, and drops stale entries', async () => {
  const store = new Map();
  const storage = { getItem: async (k) => store.get(k) || null, setItem: async (k, v) => { store.set(k, v); }, removeItem: async (k) => { store.delete(k); } };
  let clock = Date.parse('2026-11-20T00:00:00Z');
  const q = createProgressQueue({ storage, now: () => clock });
  await q.enqueue('les_1', { positionSeconds: 10, playedSeconds: 10, clientUpdatedUtc: '2026-11-19T00:00:00Z' });
  await q.enqueue('les_1', { positionSeconds: 20, playedSeconds: 10, clientUpdatedUtc: '2026-11-19T00:01:00Z' });
  await q.enqueue('les_2', { positionSeconds: 5, playedSeconds: 5, clientUpdatedUtc: '2026-11-10T00:00:00Z' }); // 10 days old
  await q.enqueue('les_3', { positionSeconds: 5, playedSeconds: 5 });
  assert.equal(await q.size(), 3);

  const seen = [];
  let failNext = { status: 0 };
  const first = await q.replay(async (lessonId, body) => {
    if (lessonId === 'les_3' && failNext) {
      const e = failNext;
      failNext = null;
      throw e;
    }
    seen.push(lessonId + ':' + body.positionSeconds);
  });
  assert.deepEqual(seen, ['les_1:10', 'les_1:20']);
  assert.deepEqual(first, { sent: 2, dropped: 0, remaining: 1 }, 'the stale entry was pruned when the next one was queued');
  const second = await q.replay(async () => { throw Object.assign(new Error('gone'), { status: 404 }); });
  assert.deepEqual(second, { sent: 0, dropped: 1, remaining: 0 });
  assert.equal(store.size, 0);
  clock += 1;
  await q.enqueue('les_4', { positionSeconds: 1, playedSeconds: 1 });
  await q.clear();
  assert.equal(await q.size(), 0);
  assert.throws(() => createProgressQueue({}), /storage/);
});

test('catalog access: locked stubs for free accounts in PartiallyGated, public browsing signed out', () => {
  const partial = { access: { model: 'PartiallyGated', anonymousBrowsing: true, gatedFeatures: ['library'] } };
  const free = { accessTier: 'SignedIn' };
  const member = { accessTier: 'Member' };
  assert.deepEqual(catalogAccess(partial, free, 'library', 'read'), { allowed: true, requires: null, lockedStubs: true, publicOnly: false });
  assert.deepEqual(catalogAccess(partial, free, 'library', 'write'), { allowed: false, requires: 'Membership', lockedStubs: false, publicOnly: false });
  assert.equal(catalogAccess(partial, free, 'events', 'write').allowed, true);
  assert.equal(catalogAccess(partial, member, 'library', 'write').allowed, true);
  assert.deepEqual(catalogAccess(partial, null, 'directory', 'read'), { allowed: true, requires: null, lockedStubs: false, publicOnly: true });
  assert.equal(catalogAccess(partial, null, 'directory', 'write').requires, 'SignIn');
  const full = { access: { model: 'FullyGated', anonymousBrowsing: true } };
  assert.equal(catalogAccess(full, null, 'library', 'read').requires, 'SignIn');
  assert.equal(catalogAccess(full, free, 'library', 'read').requires, 'Membership');
  assert.equal(catalogAccess({ access: { model: 'Open' } }, free, 'library', 'write').allowed, true);
  // featureAccess agrees for the library and events (the directory key stays strict for the member profile directory).
  assert.equal(featureAccess(partial, free, 'library', 'read').allowed, true);
  assert.equal(featureAccess(partial, free, 'library', 'write').requires, 'Membership');
  assert.equal(signedOutBrowsable(partial, 'library'), true);
  assert.equal(signedOutBrowsable(partial, 'events'), true);
  assert.equal(featureAccess({ access: { model: 'PartiallyGated', gatedFeatures: ['directory'] } }, free, 'directory', 'read').allowed, false);
});

function libraryWorld() {
  const s1 = makeSeries('ser_1', { title: 'Getting started', levels: [{ key: 'a', label: 'Alpha' }], sortOrder: 20 });
  const s2 = makeSeries('ser_2', { title: 'Advanced', sortOrder: 10, labels: ['deep'] });
  const l1 = makeLesson('les_1', s1, { position: 1, durationSeconds: 100, video: { id: 'med_1', status: 'Ready', durationSeconds: 100 }, markers: [{ id: 'lmk_1', seconds: 0, label: 'Intro' }] });
  const l2 = makeLesson('les_2', s1, { position: 2, kind: 'Article', video: null, durationSeconds: 0 });
  const l3 = makeLesson('les_3', s2, { position: 1, locked: true, viewer: { canOpen: false, canPlay: false, requires: 'Membership' }, audience: 'Members' });
  const l4 = makeLesson('les_4', s2, { position: 2, audience: 'Public' });
  return createLibraryFake({ series: [s1, s2], lessons: [l1, l2, l3, l4], intervalSeconds: 15, levels: [{ id: 'llv_1', key: 'a', label: 'Alpha' }] });
}

test('library fake: series and lesson reads, filters, locked stubs, neighbors', async () => {
  const fake = libraryWorld();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const series = await client.enumerateSeries();
  assert.deepEqual(series.objects.map((s) => s.id), ['ser_2', 'ser_1']);
  assert.equal(series.objects[0].description, undefined);
  assert.deepEqual((await client.enumerateSeries({ labels: ['deep'] })).objects.map((s) => s.id), ['ser_2']);
  assert.deepEqual((await client.enumerateSeries({ filters: { levels: ['a'] } })).objects.map((s) => s.id), ['ser_1']);
  const lessons = await client.enumerateSeriesLessons('ser_2');
  assert.deepEqual(lessons.objects.map((l) => [l.id, l.locked]), [['les_3', true], ['les_4', false]]);
  const locked = await client.getLesson('les_3');
  assert.equal(locked.video, null);
  assert.deepEqual(locked.markers, []);
  const l1 = await client.getLesson('les_1');
  assert.equal(l1.next.id, 'les_2');
  assert.equal(l1.previous, null);
  const page1 = await client.enumerateLessons({ maxResults: 2 });
  assert.equal(page1.endOfResults, false);
  const page2 = await client.enumerateLessons({ maxResults: 2, continuationToken: page1.continuationToken });
  assert.equal(page2.endOfResults, true);
  assert.equal(page1.objects.length + page2.objects.length, 4);
  assert.equal((await client.getSeries('getting-started')).id, 'ser_1');
  await assert.rejects(client.getLesson('les_missing'), (e) => e.status === 404 && e.code === 'NotFound');
  assert.deepEqual((await client.followSeries('ser_1')), { isFollowed: true });
  assert.deepEqual((await client.enumerateSeries({ filters: { followedOnly: true } })).objects.map((s) => s.id), ['ser_1']);
  assert.equal(makeLessonProgress('les_x').lessonId, 'les_x');
  const levels = await client.enumerateLibraryLevels();
  assert.deepEqual(levels.objects, [{ key: 'a', label: 'Alpha', color: null }]);
});

test('library fake: progress merge follows section 5.5', async () => {
  const fake = libraryWorld();
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  let p = await client.putLessonProgress('les_1', { positionSeconds: 30, playedSeconds: 30, clientUpdatedUtc: '2026-11-11T10:00:00Z' });
  assert.equal(p.positionSeconds, 30);
  assert.equal(p.watchedSeconds, 30);
  p = await client.putLessonProgress('les_1', { positionSeconds: 99, playedSeconds: 500, clientUpdatedUtc: '2026-11-11T10:01:00Z' });
  assert.equal(p.watchedSeconds, 60, 'played seconds clamp to 2x interval');
  assert.equal(p.maxPositionSeconds, 99);
  assert.equal(p.percent, 60, 'seeking to the end does not complete');
  assert.equal(p.completed, false);
  p = await client.putLessonProgress('les_1', { positionSeconds: 10, playedSeconds: 30, clientUpdatedUtc: '2026-11-11T09:00:00Z' });
  assert.equal(p.positionSeconds, 99, 'stale update keeps the resume point');
  assert.equal(p.watchedSeconds, 90, 'watched seconds still merge');
  p = await client.putLessonProgress('les_1', { positionSeconds: 99, playedSeconds: 30, clientUpdatedUtc: '2026-11-11T10:02:00Z' });
  assert.equal(p.completed, true);
  p = await client.putLessonProgress('les_1', { positionSeconds: 5, playedSeconds: 0, clientUpdatedUtc: '2026-11-11T10:03:00Z' });
  assert.equal(p.completed, true, 'completion is sticky');
  p = await client.uncompleteLesson('les_1');
  assert.equal(p.completed, false);
  await assert.rejects(client.putLessonProgress('les_3', { positionSeconds: 1, playedSeconds: 1 }), (e) => e.status === 402 && e.body.required === 'Membership');
  const article = await client.completeLesson('les_2');
  assert.equal(article.percent, 100);
  const cont = await client.enumerateContinueWatching();
  assert.deepEqual(cont.objects.map((x) => x.lesson.id), ['les_1']);
  const series = await client.getSeries('ser_1');
  assert.equal(series.myProgress.completedLessons, 1);
  await client.resetLessonProgress('les_1');
  assert.equal(fake.progressOf('les_1'), null);
});

test('library fake: admin lifecycle, viewing history audit, downloads, kill switch', async () => {
  const fake = libraryWorld();
  fake.state.viewers.push({ user: { id: 'usr_9', displayName: 'Pat' }, progress: makeProgress('les_1', { percent: 50 }) });
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });
  const created = await client.adminCreateSeries({ title: 'New series' });
  assert.equal(created.status, 'Draft');
  await assert.rejects(client.adminCreateSeries({ title: 'New series' }), (e) => e.code === 'SlugInUse');
  const lesson = await client.adminCreateLesson(created.id, { title: 'First', kind: 'Video' });
  await assert.rejects(client.adminPublishLesson(lesson.id), (e) => e.code === 'LessonVideoRequired');
  await client.adminUpdateLesson(lesson.id, { videoMediaId: 'med_9' });
  assert.equal((await client.adminPublishLesson(lesson.id)).status, 'Published');
  await assert.rejects(client.adminDeleteSeries(created.id), (e) => e.code === 'SeriesNotEmpty');
  const admin = await client.adminEnumerateLessons({ filters: { status: ['Published'], seriesId: created.id } });
  assert.deepEqual(admin.objects.map((l) => l.id), [lesson.id]);
  const viewers = await client.adminEnumerateLessonViewers('les_1');
  assert.equal(viewers.objects[0].user.id, 'usr_9');
  const history = await client.adminEnumerateMemberLibraryProgress('usr_9');
  assert.equal(history.objects[0].lesson.id, 'les_1');
  assert.deepEqual(fake.state.audits.map((a) => a.targetType), ['Lesson', 'User']);
  await assert.rejects(client.createLessonDownload('les_1', { deviceId: 'd', platform: 'Ios' }), (e) => e.code === 'DownloadsNotAllowed');
  await client.adminUpdateLibrarySettings({ downloadsEnabled: true });
  const d = await client.createLessonDownload('les_1', { deviceId: 'd', platform: 'Ios' });
  assert.match(d.id, /^ldl_/);
  assert.equal((await client.enumerateMyDownloads()).objects.length, 1);
  await client.deleteLessonDownload(d.id);
  assert.equal((await client.enumerateMyDownloads()).objects.length, 0);
  const pub = await client.enumeratePublicLessons();
  assert.deepEqual(pub.objects.map((l) => l.id), ['les_4']);
  assert.equal(pub.objects[0].viewer, undefined);
  fake.state.killed = true;
  await assert.rejects(client.enumerateSeries(), (e) => e.status === 503 && e.code === 'FeatureDisabled');
  assert.ok((await client.adminEnumerateSeries()).objects.length >= 3, 'admin routes stay open');
  const combined = new ApiClient({ fetchImpl: combineFakes(fake, () => undefined) });
  assert.equal((await combined.adminGetSeries('ser_1')).id, 'ser_1');
  await assert.rejects(combined.get('/nothing'), (e) => e.status === 404);
});
