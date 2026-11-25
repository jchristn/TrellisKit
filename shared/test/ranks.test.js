import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, AttendanceMethod, RankSource, RankStatus, StaffRoles, Role, canAwardRank, effectiveRank, eligibilityDisplay, groupCurriculumItems, isSelfCompletable,
  nextRank, optionMaxLevel, parseCheckInPayload, checkInLink, checkInAppPath, ranksFeatureEnabled, rankVerificationLabelsEnabled, rankBadgeMap, verifiedRankFieldKeys, secondsUntil, localIsoDate, isRankStaff, promotersFor, rankIndex, rankLevelChoices, rankText, ranksMethods, venueSessionTargetId, verificationBadge
} from '../src/index.js';

const field = {
  key: 'rank',
  label: 'Level',
  type: 'Rank',
  options: [
    { key: 'Beginner', label: 'Beginner', color: '#999999', maxLevel: 1 },
    { key: 'Intermediate', label: 'Intermediate', color: '#1F5FBF', maxLevel: null },
    { key: 'Advanced', label: 'Advanced', color: '#111111' }
  ],
  rank: { levelLabel: 'Stars', maxLevel: 3, awarding: null }
};

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('ranks ApiClient methods exist and map to their routes', async () => {
  const names = ['getMyRank', 'getMemberRank', 'putMyLineage', 'verifyLineage', 'awardRank', 'revokeRankAward', 'enumerateRankAwards', 'enumerateRankEligibility',
    'enumerateRankRequirements', 'putRankRequirements', 'grantRankAwarder', 'revokeRankAwarder', 'enumerateCurricula', 'getCurriculum', 'completeCurriculumItem',
    'uncompleteCurriculumItem', 'adminEnumerateCurricula', 'adminCreateCurriculum', 'adminGetCurriculum', 'adminUpdateCurriculum', 'adminReplaceCurriculumItems',
    'adminPublishCurriculum', 'adminArchiveCurriculum', 'adminGetCurriculumProgress', 'verifyCurriculumItem', 'checkIn', 'getCheckInCode', 'enumerateAttendance',
    'deleteAttendance', 'enumerateMyAttendance'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(ranksMethods).length, names.length);

  const { calls, client } = recorder();
  await client.getMyRank();
  await client.getMemberRank('usr_1');
  await client.awardRank({ userId: 'usr_1', optionKey: 'Beginner', level: 1 });
  await client.revokeRankAward('rkp_1', 'Entered in error');
  await client.enumerateRankAwards({ maxResults: 900, filters: { status: 'Revoked' } });
  await client.enumerateRankEligibility({ filters: { eligibleOnly: false } });
  await client.putRankRequirements({ fieldKey: 'rank', requirements: [] });
  await client.putMyLineage({ entries: [] });
  await client.verifyLineage('lin_1');
  await client.enumerateCurricula({ maxResults: 500 });
  await client.completeCurriculumItem('cur_1', 'cui_1');
  await client.uncompleteCurriculumItem('cur_1', 'cui_1');
  await client.adminReplaceCurriculumItems('cur_1', [{ itemType: 'Custom', customText: 'Drill' }]);
  await client.verifyCurriculumItem('cur_1', 'usr_1', 'cui_1');
  await client.checkIn({ targetType: 'VenueSession', targetId: 'ven_1:0', occurrenceDate: '2026-11-25', code: '123456' });
  await client.getCheckInCode({ targetType: 'VenueSession', targetId: 'ven_1:0', occurrenceDate: '2026-11-25' });
  await client.enumerateMyAttendance();
  await client.grantRankAwarder('usr_5');
  await client.revokeRankAwarder('usr_5');
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'GET /me/rank', 'GET /members/usr_1/rank', 'POST /admin/ranks/awards', 'POST /admin/ranks/awards/rkp_1/revoke', 'POST /admin/ranks/awards/enumerate',
    'POST /admin/ranks/eligibility/enumerate', 'PUT /admin/ranks/requirements', 'PUT /me/lineage', 'POST /admin/lineage/lin_1/verify', 'POST /curricula/enumerate',
    'PUT /me/curricula/cur_1/items/cui_1/complete', 'DELETE /me/curricula/cur_1/items/cui_1/complete', 'PUT /admin/curricula/cur_1/items',
    'POST /admin/curricula/cur_1/progress/usr_1/items/cui_1/verify', 'POST /attendance/check-in',
    'GET /attendance/check-in-code?targetType=VenueSession&targetId=ven_1%3A0&occurrenceDate=2026-11-25', 'POST /me/attendance/enumerate', 'POST /admin/moderators',
    'DELETE /admin/moderators/usr_5?resourceType=Ranks&resourceId=*'
  ]);
  assert.deepEqual(calls[3].body, { reason: 'Entered in error' });
  assert.equal(calls[4].body.maxResults, 500);
  assert.deepEqual(calls[4].body.filters, { status: 'Revoked' });
  assert.equal(calls[9].body.maxResults, 100);
  assert.deepEqual(calls[12].body, { items: [{ itemType: 'Custom', customText: 'Drill' }] });
  assert.deepEqual(calls[17].body, { userId: 'usr_5', resourceType: 'Ranks', resourceId: '*' });
});

test('RankAwarder is a staff role', () => {
  assert.equal(Role.RankAwarder, 'RankAwarder');
  assert.ok(StaffRoles.includes('RankAwarder'));
});

test('per-option level maximums, labels, and level choices', () => {
  assert.equal(rankIndex(field, 'intermediate'), 1);
  assert.equal(rankIndex(field, 'Expert'), -1);
  assert.equal(optionMaxLevel(field, 'Beginner'), 1);
  assert.equal(optionMaxLevel(field, 'Intermediate'), 3);
  assert.equal(optionMaxLevel(field, 'Advanced'), 3);
  assert.equal(rankText(field, 'Intermediate', 2), 'Intermediate, 2 Stars');
  assert.equal(rankText(field, 'Intermediate', 0), 'Intermediate');
  assert.equal(rankText({ ...field, rank: { maxLevel: 3 } }, 'Intermediate', 2), 'Intermediate');
  assert.deepEqual(rankLevelChoices(field, 'Beginner'), [{ level: 0, label: 'Beginner' }, { level: 1, label: 'Beginner, 1 Stars' }]);
  assert.equal(rankLevelChoices(field, 'Advanced').length, 4);
  assert.deepEqual(rankLevelChoices(field, 'Expert'), []);
});

test('next rank walks levels, then options, skipping retired ones', () => {
  assert.deepEqual(nextRank(field, 'Beginner', 0), { optionKey: 'Beginner', level: 1, label: 'Beginner, 1 Stars' });
  assert.deepEqual(nextRank(field, 'Beginner', 1), { optionKey: 'Intermediate', level: 0, label: 'Intermediate' });
  assert.equal(nextRank(field, 'Advanced', 3), null);
  const retired = { ...field, options: [field.options[0], field.options[1], { ...field.options[2], retired: true }] };
  assert.equal(nextRank(retired, 'Intermediate', 3), null);
  assert.equal(nextRank(field, 'Expert', 0), null);
});

test('effective rank prefers a verified row over the self-reported value', () => {
  const self = effectiveRank(field, null, { key: 'Beginner', level: 1 });
  assert.equal(self.source, RankSource.SelfReported);
  assert.equal(self.verified, false);
  assert.equal(self.label, 'Beginner, 1 Stars');
  const verified = effectiveRank(field, { optionKey: 'Intermediate', level: 2, status: RankStatus.Verified, awardedBy: 'usr_9' }, { key: 'Beginner', level: 1 });
  assert.equal(verified.source, RankSource.Verified);
  assert.equal(verified.optionKey, 'Intermediate');
  assert.equal(verified.awardedBy, 'usr_9');
  const revoked = effectiveRank(field, { optionKey: 'Advanced', level: 1, status: RankStatus.Revoked }, { key: 'Beginner', level: 5 });
  assert.equal(revoked.source, RankSource.SelfReported);
  assert.equal(revoked.level, 1, 'Clamped to the option maxLevel');
  assert.equal(effectiveRank(field, null, null).source, RankSource.None);
});

test('verification badges honor showVerificationLabels', () => {
  assert.equal(verificationBadge({ verified: true, source: 'Verified', optionKey: 'Beginner' }), 'verified');
  assert.equal(verificationBadge({ verified: false, source: 'SelfReported', optionKey: 'Beginner' }), 'selfReported');
  assert.equal(verificationBadge({ verified: false, source: 'None', optionKey: null }), null);
  assert.equal(verificationBadge({ verified: true, source: 'Verified', optionKey: 'Beginner' }, false), null);
  assert.equal(verificationBadge(null), null);
});

test('eligibility display lists only configured requirements', () => {
  assert.deepEqual(eligibilityDisplay(null), { state: 'highest', checks: [] });
  assert.deepEqual(eligibilityDisplay({ requirement: null, eligible: false }), { state: 'noRequirements', checks: [] });
  const shown = eligibilityDisplay({
    requirement: { minimumDaysInGrade: 30, minimumAttendance: 0 }, daysInGrade: 12, daysInGradeMet: false, attendance: 4, attendanceMet: true,
    curriculum: { required: 5, completedRequired: 5 }, curriculumMet: true, eligible: false
  });
  assert.equal(shown.state, 'inProgress');
  assert.deepEqual(shown.checks.map((c) => c.key), ['daysInGrade', 'curriculum']);
  assert.deepEqual(shown.checks[0], { key: 'daysInGrade', met: false, current: 12, required: 30 });
  assert.equal(eligibilityDisplay({ requirement: { minimumDaysInGrade: 0, minimumAttendance: 2 }, attendance: 2, attendanceMet: true, eligible: true }).state, 'eligible');
});

test('awarding rules matrix mirrors the server', () => {
  const awarder = { rank: { optionKey: 'Intermediate', level: 2, status: 'Verified' } };
  const holder = { optionKey: 'Intermediate', level: 0, status: 'Verified' };
  assert.equal(canAwardRank(field, awarder, null, 'Beginner', 1), true, 'strictly lower option');
  assert.equal(canAwardRank(field, awarder, null, 'Intermediate', 0), false, 'own option refused by default');
  assert.equal(canAwardRank(field, awarder, holder, 'Intermediate', 1), true, 'own-option levels for a holder');
  assert.equal(canAwardRank(field, awarder, holder, 'Intermediate', 3), false, 'never above own level');
  assert.equal(canAwardRank(field, awarder, null, 'Advanced', 0), false, 'higher option');
  assert.equal(canAwardRank(field, { rank: null }, null, 'Beginner', 0), false, 'no verified rank');
  assert.equal(canAwardRank(field, { isSystemAdmin: true }, null, 'Advanced', 3), true, 'SystemAdmin exempt');
  const equal = { ...field, rank: { ...field.rank, awarding: { allowEqualOption: true } } };
  assert.equal(canAwardRank(equal, awarder, null, 'Intermediate', 0), true, 'allowEqualOption');
  const noLevels = { ...field, rank: { ...field.rank, awarding: { allowEqualOptionLevels: false } } };
  assert.equal(canAwardRank(noLevels, awarder, holder, 'Intermediate', 1), false, 'allowEqualOptionLevels false');
  const minimum = { ...field, rank: { ...field.rank, awarding: { optionAwarderMinimum: 'Advanced' } } };
  assert.equal(canAwardRank(minimum, awarder, null, 'Beginner', 0), false, 'optionAwarderMinimum');
  assert.equal(canAwardRank(minimum, { rank: { optionKey: 'Advanced', level: 0, status: 'Verified' } }, null, 'Intermediate', 3), true, 'at the minimum');
});

test('check-in payloads and venue session ids', () => {
  assert.equal(venueSessionTargetId('ven_1', 2), 'ven_1:2');
  assert.deepEqual(parseCheckInPayload('checkin?targetType=VenueSession&targetId=ven_1%3A2&occurrenceDate=2026-11-25&code=012345'),
    { targetType: 'VenueSession', targetId: 'ven_1:2', code: '012345', method: AttendanceMethod.QR, occurrenceDate: '2026-11-25' });
  assert.deepEqual(parseCheckInPayload('https://example.test/app/checkin?targetType=Event&targetId=evt_1&code=999999'),
    { targetType: 'Event', targetId: 'evt_1', code: '999999', method: 'QR' });
  assert.equal(parseCheckInPayload('checkin?targetType=Class&targetId=x&code=123456'), null);
  assert.equal(parseCheckInPayload('checkin?targetType=Event&targetId=evt_1&code=12'), null);
  assert.equal(parseCheckInPayload('hello'), null);
  assert.equal(parseCheckInPayload('https://example.test/app/feed?t=Event&id=evt_1'), null);
});

test('check-in universal links round-trip through the parser', () => {
  const property = { domains: { web: 'example.test' } };
  const code = { targetType: 'VenueSession', targetId: 'ven_1:2', occurrenceDate: '2026-11-25', code: '012345', expiresUtc: '2026-11-25T10:00:00Z' };
  const link = checkInLink(property, code);
  assert.equal(link, 'https://example.test/app/check-in?t=VenueSession&id=ven_1%3A2&d=2026-11-25&c=012345');
  assert.deepEqual(parseCheckInPayload(link), { targetType: 'VenueSession', targetId: 'ven_1:2', occurrenceDate: '2026-11-25', code: '012345', method: 'QR' });
  assert.equal(checkInAppPath(code), '/check-in?t=VenueSession&id=ven_1%3A2&d=2026-11-25&c=012345');
  assert.deepEqual(parseCheckInPayload(checkInAppPath(code)), parseCheckInPayload(link));
  assert.deepEqual(parseCheckInPayload({ t: 'Event', id: 'evt_1' }), { targetType: 'Event', targetId: 'evt_1' });
  assert.deepEqual(parseCheckInPayload({ t: ['Event'], id: 'evt_1', c: '123456' }), { targetType: 'Event', targetId: 'evt_1', code: '123456', method: 'QR' });
  assert.equal(parseCheckInPayload({ t: 'Event', id: 'evt_1', d: 'tomorrow' }), null);
  assert.equal(checkInLink({ domains: { web: 'https://example.test/' } }, code).startsWith('https://example.test/app/check-in?'), true);
  assert.equal(checkInLink({}, code), null);
  assert.equal(checkInLink(property, { ...code, code: '' }), null);
});

test('rank feature switches, badges, locks, and countdowns', () => {
  assert.equal(ranksFeatureEnabled({ features: {} }), false);
  assert.equal(ranksFeatureEnabled({ features: { ranks: true } }), true);
  assert.equal(rankVerificationLabelsEnabled({ features: { ranks: true } }), true);
  assert.equal(rankVerificationLabelsEnabled({ features: { ranks: true, rankVerificationLabels: false } }), false);
  assert.equal(rankVerificationLabelsEnabled({ features: { rankVerificationLabels: true } }), false);
  const me = { showVerificationLabels: true, fields: [{ rank: { fieldKey: 'rank', optionKey: 'Beginner', verified: true, source: 'Verified' } }, { rank: { fieldKey: 'other', optionKey: 'x', verified: false, source: 'SelfReported' } }, { rank: { fieldKey: 'none', optionKey: null, verified: false, source: 'None' } }] };
  assert.deepEqual(rankBadgeMap(me), { rank: 'verified', other: 'selfReported' });
  assert.deepEqual(rankBadgeMap({ ...me, showVerificationLabels: false }), {});
  assert.deepEqual(rankBadgeMap({ showVerificationLabels: true, ranks: [{ fieldKey: 'rank', optionKey: 'A', verified: false, source: 'SelfReported' }] }), { rank: 'selfReported' });
  assert.deepEqual(verifiedRankFieldKeys(me), ['rank']);
  assert.equal(secondsUntil('2026-11-25T10:00:30Z', Date.parse('2026-11-25T10:00:00Z')), 30);
  assert.equal(secondsUntil('2026-11-25T09:00:00Z', Date.parse('2026-11-25T10:00:00Z')), 0);
  assert.equal(secondsUntil(null), 0);
  assert.equal(localIsoDate(new Date(2026, 0, 5, 12)), '2026-01-05');
  assert.equal(isRankStaff({ roles: [{ role: 'RankAwarder' }] }), true);
  assert.equal(isRankStaff({ roles: ['User'] }), false);
  assert.deepEqual(promotersFor({ lineage: [{ id: 'a', fieldKey: 'rank' }, { id: 'b', fieldKey: 'other' }] }, 'rank').map((e) => e.id), ['a']);
  assert.deepEqual(promotersFor(null, 'rank'), []);
});

test('curriculum helpers', () => {
  assert.equal(isSelfCompletable({ itemType: 'Custom' }), true);
  assert.equal(isSelfCompletable({ itemType: 'CatalogEntry' }), true);
  assert.equal(isSelfCompletable({ itemType: 'Lesson' }), false);
  const groups = groupCurriculumItems(field, [
    { id: 'a', optionKey: 'Intermediate', satisfied: true },
    { id: 'b', optionKey: null, satisfied: false },
    { id: 'c', optionKey: 'Beginner', satisfied: false },
    { id: 'd', optionKey: 'Beginner', satisfied: true }
  ]);
  assert.deepEqual(groups.map((g) => [g.optionKey, g.completed, g.total]), [['Beginner', 1, 2], ['Intermediate', 1, 1], [null, 0, 1]]);
});
