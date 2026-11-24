import { pollPayload, validatePollDraft } from '@trelliskit/shared';
import { closeAfterDays } from '../components/forums/PollBuilder';

/**
 * Poll editing (docs/PHASE3_CONTRACT.md section 6.4). Before the first vote the author may replace
 * the options and every poll setting; after the first vote only the closing time may change.
 * Edits go through `PUT /threads/{id}` with `{ poll }`.
 */

/** Whether the poll has received a vote (the server's `totalVoters` is shown even when results are hidden). */
export function pollHasVotes(poll) {
  if (!poll) return false;
  if (Number(poll.totalVoters) > 0) return true;
  return Array.isArray(poll.myVote) && poll.myVote.length > 0;
}

/** Whether `user` may edit the poll at all: the thread author, while the poll is open. */
export function canEditPoll(poll, thread, user) {
  if (!poll || !thread || !user || !thread.author) return false;
  if (poll.isClosed || poll.closedUtc) return false;
  return thread.author.id === user.id;
}

/** A builder draft from the current poll (closeInDays blank keeps the current closing time). */
export function pollEditDraft(poll) {
  return {
    options: (poll.options || []).map((o) => ({ label: o.label })),
    multipleChoice: Boolean(poll.multipleChoice),
    maxChoices: poll.maxChoices || null,
    resultsVisibility: poll.resultsVisibility || 'AfterVote',
    voterVisibility: poll.voterVisibility || 'Anonymous',
    allowVoteChange: poll.allowVoteChange !== false,
    closeInDays: ''
  };
}

/**
 * Validate an edit draft and build the `PUT /threads/{id}` body.
 * @param {object} poll Current poll.
 * @param {object} draft Builder draft (`pollEditDraft` shape).
 * @param {{ now?: number }} [options]
 * @returns {{ problems: object, body: object|null }}
 */
export function pollEditRequest(poll, draft, { now = Date.now() } = {}) {
  const typed = String(draft.closeInDays || '').trim();
  const closesUtc = typed ? closeAfterDays(typed, now) || 'invalid' : poll.closesUtc || null;
  const locked = pollHasVotes(poll);
  const check = locked ? { options: [{ label: 'a' }, { label: 'b' }] } : { ...draft };
  if (typed) check.closesUtc = closesUtc;
  const problems = validatePollDraft(check, { now: new Date(now) });
  if (Object.keys(problems).length) return { problems, body: null };
  if (locked) return { problems, body: { poll: { closesUtc } } };
  const { closeInDays: _days, ...rest } = draft;
  const payload = pollPayload({ ...rest, closesUtc });
  payload.voterVisibility = draft.voterVisibility === 'Visible' ? 'Visible' : 'Anonymous';
  return { problems, body: { poll: payload } };
}
