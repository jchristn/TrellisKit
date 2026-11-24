import { useCallback, useState } from 'react';
import { FeedbackStatuses } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import { authorName, errorText } from '../../utils/engagement.js';
import { appThreadPath, forumErrors } from '../../utils/forums.js';
import { formatShortDateTime } from '../../utils/format.js';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.admin.reviewRequests;
const col = s.columns;
const fo = strings.forums;

/**
 * Review requests (PHASE3_CONTRACT.md sections 7 and 12): Feedback threads by status, oldest
 * first so reviewers work a queue, with "Start review" (InReview) and "Mark reviewed" (opens the
 * thread, where the reviewer chooses the review comment). Moderators see their scope.
 */
function ReviewRequestsTab() {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [status, setStatus] = useState('Open');
  const fetchPage = useCallback((cursor, signal) => client.adminEnumerateThreads({ continuationToken: cursor, maxResults: 50, ordering: 'CreatedAscending', filters: { type: ['Feedback'], feedbackStatus: status ? [status] : undefined } }, signal), [client, status]);
  const list = useCursorList(fetchPage);
  const start = async (row) => {
    try {
      await client.setFeedbackStatus(row.id, 'InReview');
      toast.success(s.started);
      list.refresh();
    } catch (err) {
      toast.error(errorText(err, forumErrors()));
    }
  };
  return (
    <section className="panel" aria-labelledby="review-requests-title">
      <div className="row-between">
        <h2 id="review-requests-title">{strings.admin.moderation.tabs.reviewRequests}</h2>
        <label className="inline-field">
          <span className="label-text">{s.status}</span>
          <select className="select-compact" value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">{strings.admin.common.any}</option>
            {FeedbackStatuses.map((x) => <option key={x} value={x}>{fo.feedbackStatus[x]}</option>)}
          </select>
        </label>
      </div>
      <p className="muted small">{s.reviewHint}</p>
      <ErrorBanner message={list.error ? errorText(list.error) : ''} onRetry={list.refresh} />
      {list.loading && <Loading inline />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <p className="muted small">{s.empty}</p>}
      {list.items.length > 0 && (
        <div className="table-scroll">
          <TableColumns id="tk.admin.ReviewRequestsTab"><table className="data-table">
            <caption className="visually-hidden">{strings.admin.moderation.tabs.reviewRequests}</caption>
            <thead><tr>{['thread', 'forum', 'author', 'status', 'reviewer', 'created', 'actions'].map((k) => <th key={k} scope="col">{k === 'actions' ? <span className="visually-hidden">{col.actions}</span> : col[k]}</th>)}</tr></thead>
            <tbody>
              {list.items.map((row) => {
                const current = (row.feedback && row.feedback.status) || 'Open';
                return (
                  <tr key={row.id}>
                    <td><a href={appThreadPath(row.id)} target="_blank" rel="noreferrer">{row.title}</a></td>
                    <td>{row.forum ? row.forum.name : '-'}</td>
                    <td>{authorName(row.author)}</td>
                    <td><Badge tone={current === 'Open' ? 'warning' : current === 'Reviewed' ? 'success' : 'info'}>{fo.feedbackStatus[current]}</Badge></td>
                    <td>{row.feedback && row.feedback.reviewer ? authorName(row.feedback.reviewer) : '-'}</td>
                    <td className="nowrap">{formatShortDateTime(row.createdUtc)}</td>
                    <td className="cell-actions nowrap">
                      {current === 'Open' && <button type="button" className="button button-secondary button-sm" onClick={() => start(row)}>{s.startReview}</button>}
                      {(current === 'Open' || current === 'InReview') && <a className="button button-primary button-sm" href={appThreadPath(row.id)} target="_blank" rel="noreferrer">{s.markReviewed}</a>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table></TableColumns>
        </div>
      )}
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{fo.loadMore}</button>}
    </section>
  );
}

export default ReviewRequestsTab;
