import strings from '../../strings.js';
import Icon from '../Icon.jsx';
import { Badge } from '../Badges.jsx';

const t = strings.forums;
const STATE_TONES = { Processing: 'warning', Hidden: 'warning', Removed: 'danger' };
const FEEDBACK_TONES = { Open: 'warning', InReview: 'info', Reviewed: 'success', Closed: 'neutral' };

/**
 * Type and status badges for a thread or ThreadSummary: type, pinned, locked, answered, poll
 * state, feedback status, promoted, linked feed post, and non-Published states.
 * @param {{ thread: object, showType?: boolean }} props
 */
function ThreadBadges({ thread, showType = true }) {
  const answered = thread.type === 'Question' && (thread.hasAcceptedAnswer || thread.acceptedAnswer);
  const feedbackStatus = thread.feedback && thread.feedback.status;
  return (
    <span className="badge-row thread-badges">
      {showType && <Badge tone="neutral">{t.types[thread.type] || thread.type}</Badge>}
      {thread.isPinned && <Badge tone="info"><Icon name="thumbtack" size={12} /> {t.pinned}</Badge>}
      {thread.isLocked && <Badge tone="warning"><Icon name="lock" size={12} /> {t.locked}</Badge>}
      {answered && <Badge tone="success"><Icon name="check" size={12} /> {t.answered}</Badge>}
      {thread.poll && <Badge tone={thread.poll.isClosed ? 'neutral' : 'info'}>{thread.poll.isClosed ? t.pollClosed : t.pollOpen}</Badge>}
      {feedbackStatus && <Badge tone={FEEDBACK_TONES[feedbackStatus] || 'neutral'}>{t.feedbackStatus[feedbackStatus] || feedbackStatus}</Badge>}
      {thread.promotion && <Badge tone="success">{t.promotedBadge}</Badge>}
      {thread.kind === 'LinkedFeedPost' && <Badge tone="info">{t.fromFeedBadge}</Badge>}
      {thread.state && thread.state !== 'Published' && <Badge tone={STATE_TONES[thread.state] || 'neutral'}>{t.states[thread.state] || thread.state}</Badge>}
    </span>
  );
}

export default ThreadBadges;
