/**
 * Poll helpers (docs/PHASE3_CONTRACT.md section 6, with the resolved decisions of 2026-11-11:
 * the poll author chooses results visibility and whether voters are visible). Display state is
 * computed only from server fields: when the server says results are hidden, nothing is derived.
 */

/** Results visibility choices (section 6.1); `AfterVote` is the initial form value. */
export const PollResultsVisibilities = Object.freeze(['Always', 'AfterVote', 'AfterClose']);

/**
 * Voter visibility choices (resolved decision 4: the poll author chooses anonymous counts or
 * visible voters). `Anonymous` is the default and is not sent.
 */
export const PollVoterVisibilities = Object.freeze(['Anonymous', 'Visible']);

/** Poll limits from the contract defaults (`polls.maxOptions`, `polls.maxDurationDays`). */
export const POLL_LIMITS = Object.freeze({ minOptions: 2, maxOptions: 20, optionMax: 140, minLeadMinutes: 5, maxDurationDays: 365 });

function round1(n) {
  return Math.round(n * 10) / 10;
}

/**
 * Display state of a poll (section 6.2). `resultsVisible`, counts, and percentages come from the
 * server; when `percent` is missing but `voteCount` and `totalVoters` are present (and results
 * are visible) the percentage is `voteCount / totalVoters` to one decimal place, the server's own
 * rule. `barPercent` is clamped to 0..100 for drawing; `isLeading` marks the highest count.
 * @param {object} poll Poll or the reduced ThreadSummary poll.
 */
export function pollDisplay(poll) {
  if (!poll) return null;
  const myVote = Array.isArray(poll.myVote) ? poll.myVote : [];
  const resultsVisible = poll.resultsVisible === true;
  const total = Number.isFinite(poll.totalVoters) ? poll.totalVoters : null;
  const options = (Array.isArray(poll.options) ? poll.options : []).slice().sort((a, b) => (a.position || 0) - (b.position || 0)).map((o) => {
    let percent = null;
    let voteCount = null;
    if (resultsVisible) {
      voteCount = Number.isFinite(o.voteCount) ? o.voteCount : null;
      if (Number.isFinite(o.percent)) percent = o.percent;
      else if (voteCount !== null && total) percent = round1((voteCount / total) * 100);
      else if (voteCount !== null && total === 0) percent = 0;
    }
    return { id: o.id, label: o.label, position: o.position, voteCount, percent, barPercent: percent === null ? 0 : Math.max(0, Math.min(100, percent)), isMine: myVote.includes(o.id), isLeading: false };
  });
  if (resultsVisible) {
    const max = Math.max(0, ...options.map((o) => o.voteCount || 0));
    if (max > 0) for (const o of options) o.isLeading = o.voteCount === max;
  }
  const isClosed = Boolean(poll.isClosed || poll.closedUtc);
  const hasVoted = myVote.length > 0 || poll.hasVoted === true;
  return {
    id: poll.id,
    resultsVisible,
    isClosed,
    hasVoted,
    totalVoters: total,
    multipleChoice: Boolean(poll.multipleChoice),
    maxChoices: poll.multipleChoice ? (Number.isFinite(poll.maxChoices) ? poll.maxChoices : null) : 1,
    allowVoteChange: poll.allowVoteChange !== false,
    canChangeVote: !isClosed && (!hasVoted || poll.allowVoteChange !== false),
    resultsVisibility: poll.resultsVisibility || 'AfterVote',
    voterVisibility: poll.voterVisibility === 'Visible' ? 'Visible' : 'Anonymous',
    closesUtc: poll.closesUtc || null,
    closedUtc: poll.closedUtc || null,
    myVote,
    options
  };
}

/**
 * Problems with a vote selection before sending (section 6.3): `selectOne`, `tooMany`, `unknown`.
 * @param {object} poll
 * @param {string[]} optionIds
 * @returns {string|null}
 */
export function pollSelectionProblem(poll, optionIds) {
  const ids = Array.isArray(optionIds) ? optionIds : [];
  if (!ids.length) return 'selectOne';
  const known = new Set((poll && poll.options ? poll.options : []).map((o) => o.id));
  if (ids.some((id) => !known.has(id))) return 'unknown';
  if (!poll.multipleChoice && ids.length > 1) return 'tooMany';
  if (poll.multipleChoice && Number.isFinite(poll.maxChoices) && ids.length > poll.maxChoices) return 'tooMany';
  return null;
}

/**
 * Toggle an option in a selection, honoring single choice (replace) and `maxChoices` (ignore
 * additions past the limit).
 * @param {object} poll
 * @param {string[]} selection
 * @param {string} optionId
 */
export function togglePollOption(poll, selection, optionId) {
  const current = Array.isArray(selection) ? selection : [];
  if (!poll || !poll.multipleChoice) return current.includes(optionId) ? [] : [optionId];
  if (current.includes(optionId)) return current.filter((id) => id !== optionId);
  if (Number.isFinite(poll.maxChoices) && current.length >= poll.maxChoices) return current;
  return current.concat(optionId);
}

/** A new poll builder state (section 6.1 defaults; results after voting, anonymous voters). */
export function emptyPollDraft() {
  return { options: [{ label: '' }, { label: '' }], multipleChoice: false, maxChoices: null, closesUtc: null, resultsVisibility: 'AfterVote', allowVoteChange: true, voterVisibility: 'Anonymous' };
}

function normalizeLabel(label) {
  return String(label || '').trim().replace(/\s+/g, ' ').toLowerCase();
}

/**
 * Validate a poll draft (section 6.1). Returns `{ field: code }`: `options` (`tooFewOptions`,
 * `tooManyOptions`, `optionRequired`, `optionTooLong`, `duplicateOption`), `maxChoices`
 * (`maxChoicesInvalid`), `closesUtc` (`closeTooSoon`, `closeTooLate`, `closeInvalid`).
 * @param {object} draft
 * @param {{ now?: Date, maxOptions?: number, maxDurationDays?: number }} [options]
 */
export function validatePollDraft(draft, { now = new Date(), maxOptions = POLL_LIMITS.maxOptions, maxDurationDays = POLL_LIMITS.maxDurationDays } = {}) {
  const problems = {};
  const labels = (draft && Array.isArray(draft.options) ? draft.options : []).map((o) => String((o && o.label) || '').trim());
  if (labels.length < POLL_LIMITS.minOptions) problems.options = 'tooFewOptions';
  else if (labels.length > maxOptions) problems.options = 'tooManyOptions';
  else if (labels.some((l) => !l)) problems.options = 'optionRequired';
  else if (labels.some((l) => Array.from(l).length > POLL_LIMITS.optionMax)) problems.options = 'optionTooLong';
  else if (new Set(labels.map(normalizeLabel)).size !== labels.length) problems.options = 'duplicateOption';
  if (draft && draft.multipleChoice && draft.maxChoices !== null && draft.maxChoices !== undefined && draft.maxChoices !== '') {
    const n = Number(draft.maxChoices);
    if (!Number.isInteger(n) || n < 2 || n > labels.length) problems.maxChoices = 'maxChoicesInvalid';
  }
  if (draft && draft.closesUtc) {
    const at = new Date(draft.closesUtc).getTime();
    if (!Number.isFinite(at)) problems.closesUtc = 'closeInvalid';
    else if (at - now.getTime() < POLL_LIMITS.minLeadMinutes * 60000) problems.closesUtc = 'closeTooSoon';
    else if (at - now.getTime() > maxDurationDays * 86400000) problems.closesUtc = 'closeTooLate';
  }
  return problems;
}

/**
 * The `poll` object of a create or edit body (section 6.1). `voterVisibility` is sent only when
 * the author chose visible voters.
 * @param {object} draft
 */
export function pollPayload(draft) {
  const out = {
    options: (draft.options || []).map((o) => ({ label: String(o.label || '').trim() })),
    multipleChoice: Boolean(draft.multipleChoice),
    maxChoices: draft.multipleChoice && draft.maxChoices ? Number(draft.maxChoices) : null,
    closesUtc: draft.closesUtc || null,
    resultsVisibility: PollResultsVisibilities.includes(draft.resultsVisibility) ? draft.resultsVisibility : 'AfterVote',
    allowVoteChange: draft.allowVoteChange !== false
  };
  if (draft.voterVisibility === 'Visible') out.voterVisibility = 'Visible';
  return out;
}

/**
 * Whether a poll's options and settings are locked (section 6.4): after the first vote only
 * `closesUtc` may change. A closed poll cannot be edited at all.
 * @param {object} poll
 */
export function pollOptionsLocked(poll) {
  if (!poll) return false;
  return Number(poll.totalVoters) > 0 || Boolean(poll.firstVoteUtc);
}

/**
 * The builder state for editing an existing poll (section 6.4).
 * @param {object} poll Poll (section 6.2).
 */
export function pollDraftFromPoll(poll) {
  if (!poll) return emptyPollDraft();
  return {
    options: (Array.isArray(poll.options) ? poll.options : []).map((o) => ({ label: String((o && o.label) || '') })),
    multipleChoice: Boolean(poll.multipleChoice),
    maxChoices: poll.multipleChoice && poll.maxChoices ? poll.maxChoices : null,
    closesUtc: poll.closesUtc || null,
    resultsVisibility: PollResultsVisibilities.includes(poll.resultsVisibility) ? poll.resultsVisibility : 'AfterVote',
    allowVoteChange: poll.allowVoteChange !== false,
    voterVisibility: poll.voterVisibility === 'Visible' ? 'Visible' : 'Anonymous'
  };
}

/**
 * Validate a poll edit (section 6.4). Before the first vote it is the create validation; after it
 * only `closesUtc` is checked: it may move later or earlier, but not into the past and not beyond
 * `maxDurationDays`.
 * @param {object} draft
 * @param {{ locked?: boolean, now?: Date, maxOptions?: number, maxDurationDays?: number }} [options]
 */
export function validatePollEdit(draft, { locked = false, now = new Date(), maxOptions = POLL_LIMITS.maxOptions, maxDurationDays = POLL_LIMITS.maxDurationDays } = {}) {
  if (!locked) return validatePollDraft(draft, { now, maxOptions, maxDurationDays });
  const problems = {};
  if (draft && draft.closesUtc) {
    const at = new Date(draft.closesUtc).getTime();
    if (!Number.isFinite(at)) problems.closesUtc = 'closeInvalid';
    else if (at <= now.getTime()) problems.closesUtc = 'closeInPast';
    else if (at - now.getTime() > maxDurationDays * 86400000) problems.closesUtc = 'closeTooLate';
  }
  return problems;
}

/**
 * The `poll` object of a thread edit (`PUT /threads/{id}`, section 6.4), or null when nothing
 * changed. Before the first vote it is the full replacement; after it only `closesUtc` is sent
 * (the server answers `409 PollOptionsLocked` to anything else).
 * @param {object} draft Builder state.
 * @param {object} original The poll being edited.
 */
export function pollEditPayload(draft, original) {
  const locked = pollOptionsLocked(original);
  const before = pollPayload(pollDraftFromPoll(original));
  const after = pollPayload(draft);
  if (locked) {
    const was = before.closesUtc ? new Date(before.closesUtc).getTime() : null;
    const now = after.closesUtc ? new Date(after.closesUtc).getTime() : null;
    return was === now ? null : { closesUtc: after.closesUtc };
  }
  const visibilityChanged = (original && original.voterVisibility === 'Visible' ? 'Visible' : 'Anonymous') !== (draft.voterVisibility === 'Visible' ? 'Visible' : 'Anonymous');
  if (!visibilityChanged && JSON.stringify(before) === JSON.stringify(after)) return null;
  return { ...after, voterVisibility: draft.voterVisibility === 'Visible' ? 'Visible' : 'Anonymous' };
}
