import { useCallback } from 'react';
import strings from '../../strings.js';
import Modal from '../Modal.jsx';
import { ErrorBanner, Loading } from '../States.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import { authorName } from '../../utils/engagement.js';
import { formatDateTime } from '../../utils/format.js';
import MarkdownBody from './MarkdownBody.jsx';

const f = strings.feed;

/**
 * Edit history of a post, comment, or forum thread (Phase 2 sections 5.5 and 7.6, Phase 3
 * section 4.1), visible to everyone who can
 * see the item: the current version first, then stored revisions, newest first.
 * @param {{ kind: 'post'|'comment'|'thread', item: object, client: object, onClose: () => void }} props
 */
function EditHistoryDialog({ kind, item, client, onClose }) {
  const fetchPage = useCallback(
    (cursor, signal) => {
      const query = { continuationToken: cursor, maxResults: 20 };
      if (kind === 'thread') return client.enumerateThreadRevisions(item.id, query, signal);
      return kind === 'post' ? client.listFeedPostRevisions(item.id, query, signal) : client.listCommentRevisions(item.id, query, signal);
    },
    [client, kind, item.id]
  );
  const list = useCursorList(fetchPage);
  const allowHeadings = kind === 'post' || kind === 'thread';
  return (
    <Modal open size="lg" onClose={onClose} title={f.historyTitle}>
      <ol className="history-list">
        <li className="history-item history-current">
          <div className="history-head"><strong>{f.historyCurrent}</strong>{item.editedUtc && <span className="muted small">{formatDateTime(item.editedUtc)}</span>}</div>
          {item.title && <h3 className="history-title">{item.title}</h3>}
          <MarkdownBody body={item.body} mentions={item.mentions} allowHeadings={allowHeadings} />
        </li>
        {list.items.map((rev) => (
          <li key={rev.id} className="history-item">
            <div className="history-head">
              <strong>{f.historyRevision(rev.revision)}</strong>
              <span className="muted small">{formatDateTime(rev.createdUtc)}</span>
              {rev.editedBy && <span className="muted small">{f.historyEditedBy(authorName(rev.editedBy))}</span>}
              {rev.editedByRole === 'Moderator' && <span className="badge badge-info">{strings.forums.historyByModerator}</span>}
            </div>
            {rev.title && <h3 className="history-title">{rev.title}</h3>}
            <MarkdownBody body={rev.body} allowHeadings={allowHeadings} linkMentions={false} />
            {rev.attachmentSummary && <p className="muted small">{rev.attachmentSummary}</p>}
          </li>
        ))}
      </ol>
      <ErrorBanner message={list.error ? f.historyError : ''} onRetry={list.refresh} />
      {list.loading && <Loading inline />}
      {list.loaded && !list.loading && !list.items.length && !list.error && <p className="muted small">{f.historyEmpty}</p>}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{f.historyLoadMore}</button>}
    </Modal>
  );
}

export default EditHistoryDialog;
