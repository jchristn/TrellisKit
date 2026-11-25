/**
 * Rank history panel on the admin member detail drawer: the member's ten most recent awards and
 * revokes with a link to the Ranks page filtered to them. Renders nothing unless `features.ranks`.
 * Props: `userId`.
 */
import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import { ranksFeatureEnabled } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { RankHistoryList } from '../../ranks/RankProgressPanel.jsx';
import { rankAdminError } from './rankAdminParts.jsx';

const a = strings.ranks.admin;

function MemberRankPanel({ userId }) {
  const { client } = useAdminAuth();
  const { property } = useProperty();
  const enabled = ranksFeatureEnabled(property);
  const query = useApiQuery(useCallback((signal) => client.enumerateRankAwards({ filters: { userId }, maxResults: 10 }, signal), [client, userId]), { enabled });
  if (!enabled) return null;
  const rows = (query.data && query.data.objects) || [];
  const total = query.data && Number.isInteger(query.data.totalRecords) ? query.data.totalRecords : null;
  return (
    <section className="detail-block" aria-labelledby="member-rank-history-title">
      <div className="row-between">
        <h3 id="member-rank-history-title">{a.historyTitle}</h3>
        <Link className="button button-secondary button-sm" to={'/admin/ranks?userId=' + encodeURIComponent(userId)}>{a.historyAll}</Link>
      </div>
      <ErrorBanner message={query.error ? rankAdminError(query.error) : ''} onRetry={query.reload} />
      {query.loading && !query.data && <Loading inline />}
      {query.data && (rows.length ? <RankHistoryList history={rows} total={total} /> : <p className="muted small">{a.historyEmpty}</p>)}
    </section>
  );
}

export default MemberRankPanel;
