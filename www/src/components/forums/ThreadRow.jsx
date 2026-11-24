import { Link } from 'react-router-dom';
import { formatRelativeTime } from '@trelliskit/shared';
import strings from '../../strings.js';
import Icon from '../Icon.jsx';
import { authorName } from '../../utils/engagement.js';
import { appForumPath, appThreadPath } from '../../utils/forums.js';
import { formatDateTime } from '../../utils/format.js';
import ThreadBadges from './ThreadBadges.jsx';
import SnippetText from './SnippetText.jsx';

const t = strings.forums;

/**
 * One ThreadSummary in a list (PHASE3_CONTRACT.md section 2.3): badges, the title linking to the
 * thread (or to the matching comment for a comment search hit), the excerpt or the search snippet
 * with highlights, author, forum (in cross-forum lists), counts, last activity, and an unread dot.
 * @param {{ thread: object, showForum?: boolean, headingLevel?: 2|3 }} props
 */
function ThreadRow({ thread, showForum = false, headingLevel = 3 }) {
  const Heading = headingLevel === 2 ? 'h2' : 'h3';
  const snippet = thread.searchSnippet || null;
  const to = appThreadPath(thread.id, snippet && snippet.commentId ? snippet.commentId : undefined);
  return (
    <article className={'thread-row' + (thread.isUnread ? ' thread-unread' : '') + (thread.isPinned ? ' thread-pinned' : '')} aria-labelledby={'thread-' + thread.id + '-title'}>
      <div className="thread-row-main">
        <ThreadBadges thread={thread} />
        <Heading id={'thread-' + thread.id + '-title'} className="thread-row-title">
          {thread.isUnread && <span className="unread-dot" title={t.unreadDot}><span className="visually-hidden">{t.unreadDot}: </span></span>}
          <Link to={to}>{thread.title}</Link>
        </Heading>
        {snippet ? <SnippetText snippet={snippet} /> : thread.excerpt && <p className="muted small line-clamp">{thread.excerpt}</p>}
        <p className="thread-row-meta muted small">
          <span>{t.by(authorName(thread.author))}</span>
          {showForum && thread.forum && <><span aria-hidden="true"> · </span><Link to={appForumPath(thread.forum)} className="muted">{t.inForum(thread.forum.name)}</Link></>}
          {thread.lastActivityUtc && <><span aria-hidden="true"> · </span><time dateTime={thread.lastActivityUtc} title={formatDateTime(thread.lastActivityUtc)}>{t.lastActivity(formatRelativeTime(thread.lastActivityUtc))}</time></>}
        </p>
      </div>
      <div className="thread-row-stats small muted">
        <span title={t.commentCount(thread.commentCount || 0)}><Icon name="message" size={14} /> <span className="visually-hidden">{t.commentCount(thread.commentCount || 0)}</span><span aria-hidden="true">{thread.commentCount || 0}</span></span>
        {thread.reactionTotal > 0 && <span title={strings.reactions.total(thread.reactionTotal)}><Icon name="smile" size={14} /> <span aria-hidden="true">{thread.reactionTotal}</span><span className="visually-hidden">{strings.reactions.total(thread.reactionTotal)}</span></span>}
      </div>
    </article>
  );
}

export default ThreadRow;
