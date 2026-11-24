import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pollDraftFromPoll, pollEditPayload, pollOptionsLocked, validatePollEdit } from '../src/index.js';

const poll = {
  id: 'pol_1', multipleChoice: false, maxChoices: null, resultsVisibility: 'AfterVote', allowVoteChange: true, voterVisibility: 'Anonymous',
  closesUtc: '2030-01-01T00:00:00.000Z', totalVoters: 0, options: [{ id: 'pop_a', label: 'Tuesday' }, { id: 'pop_b', label: 'Thursday' }]
};

test('a poll locks after the first vote (section 6.4)', () => {
  assert.equal(pollOptionsLocked(poll), false);
  assert.equal(pollOptionsLocked({ ...poll, totalVoters: 1 }), true);
  assert.equal(pollOptionsLocked({ ...poll, firstVoteUtc: '2026-01-01T00:00:00Z' }), true);
  assert.equal(pollOptionsLocked(null), false);
});

test('the edit draft mirrors the poll and an unchanged draft sends nothing', () => {
  const draft = pollDraftFromPoll(poll);
  assert.deepEqual(draft.options, [{ label: 'Tuesday' }, { label: 'Thursday' }]);
  assert.equal(pollEditPayload(draft, poll), null);
});

test('before the first vote the whole poll is replaced', () => {
  const draft = { ...pollDraftFromPoll(poll), options: [{ label: 'Tuesday' }, { label: 'Thursday' }, { label: 'Saturday' }], multipleChoice: true, maxChoices: 2, voterVisibility: 'Visible' };
  assert.deepEqual(pollEditPayload(draft, poll), {
    options: [{ label: 'Tuesday' }, { label: 'Thursday' }, { label: 'Saturday' }], multipleChoice: true, maxChoices: 2, closesUtc: poll.closesUtc,
    resultsVisibility: 'AfterVote', allowVoteChange: true, voterVisibility: 'Visible'
  });
});

test('after the first vote only closesUtc is sent, and it may move earlier but not into the past', () => {
  const voted = { ...poll, totalVoters: 3 };
  const draft = { ...pollDraftFromPoll(voted), options: [{ label: 'x' }, { label: 'y' }], closesUtc: '2029-06-01T00:00:00.000Z' };
  assert.deepEqual(pollEditPayload(draft, voted), { closesUtc: '2029-06-01T00:00:00.000Z' });
  assert.equal(pollEditPayload(pollDraftFromPoll(voted), voted), null);
  const now = new Date('2029-01-01T00:00:00Z');
  assert.deepEqual(validatePollEdit({ closesUtc: '2029-01-01T00:01:00Z' }, { locked: true, now }), {});
  assert.deepEqual(validatePollEdit({ closesUtc: '2028-12-31T00:00:00Z' }, { locked: true, now }), { closesUtc: 'closeInPast' });
  assert.deepEqual(validatePollEdit({ options: [{ label: 'a' }] }, { locked: false, now }), { options: 'tooFewOptions' });
});
