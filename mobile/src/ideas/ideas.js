import { IdeaOrdering, IdeaSubmissions } from '@trelliskit/shared';
import strings from '../strings';

/**
 * Ideas board helpers (docs/PHASE5_CONTRACT.md section 7).
 */

/** Board orderings offered in the filter panel (Relevance applies only with a search). */
export const IdeaOrderings = Object.freeze([IdeaOrdering.Top, IdeaOrdering.Trending, IdeaOrdering.CreatedDescending, IdeaOrdering.UpdatedDescending]);

/** Ideas per page. */
export const IdeaPageSize = 20;

/** Engine default statuses when neither the server nor the property lists any (contract section 2.4). */
const DEFAULT_STATUSES = Object.freeze(
  ['UnderReview', 'Planned', 'InProgress', 'Shipped', 'Declined'].map((key) => ({ key, label: strings.ideas.defaultStatuses[key], color: null, isClosed: key === 'Shipped' || key === 'Declined' }))
);

/** The property's configured idea statuses (`property.json` `ideas.statuses`), else the engine defaults. */
export function configuredStatuses(property) {
  const list = property && property.ideas && Array.isArray(property.ideas.statuses) ? property.ideas.statuses : null;
  const usable = (list || []).filter((s) => s && typeof s.key === 'string').map((s) => ({ key: s.key, label: s.label || s.key, color: s.color || null, isClosed: Boolean(s.isClosed) }));
  return usable.length ? usable : DEFAULT_STATUSES.slice();
}

/** Whether members may post ideas themselves (`ideas.memberSubmissions: Direct`); the default is staff only. */
export function membersSubmitIdeas(property) {
  return Boolean(property && property.ideas && property.ideas.memberSubmissions === IdeaSubmissions.Direct);
}

/**
 * The board's collection filters: `status` keys, `open` (not a closed status), `votedByMe`.
 * @param {{ statuses?: string[], openOnly?: boolean, votedByMe?: boolean }} board
 */
export function ideaCollectionFilters({ statuses = [], openOnly = false, votedByMe = false } = {}) {
  const out = {};
  if (statuses.length) out.status = statuses.slice();
  if (openOnly) out.open = true;
  if (votedByMe) out.votedByMe = true;
  return Object.keys(out).length ? out : null;
}

/** Apply a vote locally (optimistic): `hasVoted` and `voteCount`. */
export function withVote(idea, voted) {
  if (!idea || Boolean(idea.hasVoted) === voted) return idea;
  return { ...idea, hasVoted: voted, voteCount: Math.max(0, (Number(idea.voteCount) || 0) + (voted ? 1 : -1)) };
}
