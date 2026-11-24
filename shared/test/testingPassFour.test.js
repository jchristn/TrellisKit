import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createConversationListStore, hasUpdatedDocuments, headlineParts, isUpdatedDocument, searchEventInfo } from '../src/index.js';

test('search results describe events: the next date of a collapsed series, the rule, and canceled marks', () => {
  const series = searchEventInfo({ type: 'Event', id: 'evt_2', event: { startUtc: '2027-03-04T23:00:00Z', timeZone: 'UTC', isCanceled: false, isPast: false, recurrenceId: 'erc_1', recurrenceSummary: 'Every week on Thursday', occurrenceCount: 6 } });
  assert.equal(series.recurring, true);
  assert.equal(series.occurrenceCount, 6);
  assert.equal(series.recurrenceSummary, 'Every week on Thursday');
  assert.match(series.when, /Mar 4/);
  const canceled = searchEventInfo({ type: 'Event', id: 'evt_3', event: { startUtc: '2027-03-04T23:00:00Z', timeZone: 'UTC', isCanceled: true } });
  assert.equal(canceled.canceled, true);
  assert.equal(canceled.recurring, false);
  assert.equal(searchEventInfo({ type: 'Thread', id: 'thr_1' }), null);
});

test('pending documents are "updated" only when they replace a version the member accepted', () => {
  assert.equal(isUpdatedDocument({ key: 'terms', previouslyAccepted: true }), true);
  assert.equal(isUpdatedDocument({ key: 'terms', previouslyAccepted: false }), false);
  assert.equal(isUpdatedDocument({ key: 'terms' }), false);
  assert.equal(hasUpdatedDocuments({ pendingDocuments: [{ key: 'a', previouslyAccepted: false }] }), false);
  assert.equal(hasUpdatedDocuments({ pendingDocuments: [{ key: 'a', previouslyAccepted: false }, { key: 'b', previouslyAccepted: true }] }), true);
  assert.equal(hasUpdatedDocuments(null), false);
});

test('an accepted message request clears the requester waiting state live', () => {
  const list = createConversationListStore({ currentUserId: 'usr_a' });
  list.upsertConversation({ id: 'cnv_1', requestState: 'AwaitingAcceptance', viewer: { canSend: true }, updatedUtc: '2026-11-13T10:00:00Z' });
  assert.equal(list.getConversation('cnv_1').requestState, 'AwaitingAcceptance');
  // The server publishes conversation.updated to the requester when the recipient accepts (requestState null).
  list.applyEvent('conversation.updated', { conversation: { id: 'cnv_1', requestState: null, viewer: { canSend: true, cannotSendReason: null }, updatedUtc: '2026-11-13T10:00:00Z' } });
  assert.equal(list.getConversation('cnv_1').requestState, null);
});

test('message search headlines split into highlighted parts', () => {
  assert.deepEqual(headlineParts('see you at **practice** tonight'), [{ text: 'see you at ', match: false }, { text: 'practice', match: true }, { text: ' tonight', match: false }]);
});
