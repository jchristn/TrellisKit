import { useCallback } from 'react';
import { formatRelativeTime } from '@trelliskit/shared';
import strings from '../../strings.js';
import { errorMessage } from '../../utils/api.js';
import { formatDateTime } from '../../utils/format.js';
import { Badge } from '../Badges.jsx';
import { ErrorBanner, Loading } from '../States.jsx';
import { useTokenList } from './DocumentsManager.jsx';
import TableColumns from '../TableColumns.jsx';

const d = strings.documents;
const t = d.admin.drawer;
const a = d.admin.acceptances;

/** Acceptances fetched per "Load more". */
export const MEMBER_ACCEPTANCES_PAGE = 25;

/**
 * One member's document acceptance history in the member drawer:
 * `POST /admin/documents/acceptances/enumerate` with `filters.userId`, bounded, with "Load more".
 * @param {{ client: import('@trelliskit/shared').ApiClient, userId: string }} props
 */
function MemberDocumentsHistory({ client, userId }) {
  const fetchPage = useCallback((token, signal) => client.adminEnumerateDocumentAcceptances({
    maxResults: MEMBER_ACCEPTANCES_PAGE,
    filters: { userId },
    ...(token ? { continuationToken: token } : {})
  }, signal), [client, userId]);
  const list = useTokenList(fetchPage, 0);
  return (
    <div className="member-documents">
      <ErrorBanner message={list.error ? t.loadError + ' ' + errorMessage(list.error, '') : ''} onRetry={list.reload} />
      {list.rows.length === 0 && !list.loading && !list.error && <p className="muted small">{t.empty}</p>}
      {list.rows.length > 0 && (
        <div className="table-scroll">
          <TableColumns id="tk.documents.MemberDocumentsHistory"><table className="data-table">
            <caption className="visually-hidden">{t.title}</caption>
            <thead><tr>{['document', 'version', 'method', 'client', 'accepted'].map((k) => <th key={k} scope="col">{a.columns[k]}</th>)}</tr></thead>
            <tbody>
              {list.rows.map((row) => (
                <tr key={row.id}>
                  <td>{row.documentTitle || row.documentKey}</td>
                  <td className="mono">{row.version}</td>
                  <td><Badge tone={row.method === 'Reacceptance' ? 'info' : 'neutral'}>{d.methods[row.method] || row.method}</Badge></td>
                  <td>{d.clients[row.client] || row.client}</td>
                  <td className="nowrap"><span title={formatDateTime(row.acceptedUtc)}>{formatRelativeTime(row.acceptedUtc)}</span></td>
                </tr>
              ))}
            </tbody>
          </table></TableColumns>
        </div>
      )}
      {list.loading && <Loading inline />}
      {!list.done && !list.loading && list.rows.length > 0 && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{t.loadMore}</button>}
    </div>
  );
}

export default MemberDocumentsHistory;
