/**
 * `/app/rank`: the member's effective rank per Rank field with the next rank's requirements, recent
 * history, attendance count, and the lineage editor. Hidden unless `features.ranks`.
 */
import { useCallback, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import PageHeader from '../../components/PageHeader.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { errorText } from '../../utils/engagement.js';
import { useRanksFeature, useMyRank } from './rankHooks.js';
import RankCard from './RankCard.jsx';
import RankProgressPanel from './RankProgressPanel.jsx';
import { LineageEditor, LineageList } from './LineageEditor.jsx';

const t = strings.ranks;

function LineageSection({ client, field, entries, me, onChange }) {
  const [editing, setEditing] = useState(false);
  const [status, setStatus] = useState('');
  const titleId = 'lineage-' + field.key;
  return (
    <section className="panel stack" aria-labelledby={titleId}>
      <div className="row-between">
        <h2 id={titleId} className="section-title">{t.lineage.title}{field.label ? ' · ' + field.label : ''}</h2>
        {!editing && <button type="button" className="button button-secondary button-sm" onClick={() => { setEditing(true); setStatus(''); }}>{t.lineage.edit}</button>}
      </div>
      <p className="muted small">{t.lineage.intro}</p>
      {status && <p className="success-text small" role="status">{status}</p>}
      {editing
        ? <LineageEditor client={client} field={field} entries={entries} onCancel={() => setEditing(false)} onSaved={(response) => { setEditing(false); setStatus(t.lineage.saved); onChange(field.key, (response && response.entries) || []); }} />
        : <LineageList entries={entries} field={field} me={me} />}
    </section>
  );
}

function MyRankView() {
  const { client, me } = useAuth();
  const { enabled, rankFields } = useRanksFeature();
  const query = useMyRank(client, enabled);
  const attendance = useApiQuery(useCallback((signal) => client.enumerateMyAttendance({ maxResults: 1 }, signal), [client]), { enabled });
  const [lineage, setLineage] = useState(null);
  useDocumentMeta(t.title);
  const data = query.data;
  const entries = lineage || (data && data.lineage) || [];
  const replaceLineage = (fieldKey, next) => setLineage([...entries.filter((entry) => entry.fieldKey !== fieldKey), ...next]);
  const fieldsByKey = useMemo(() => new Map(rankFields.map((f) => [f.key, f])), [rankFields]);
  const attendanceTotal = attendance.data && Number.isInteger(attendance.data.totalRecords) ? attendance.data.totalRecords : null;
  if (!enabled) return <div className="member-view"><p className="muted">{t.notAvailable}</p></div>;
  return (
    <div className="member-view stack">
      <PageHeader title={t.title} summary={t.summary} actions={<Link className="button button-secondary button-sm" to="/app/curricula">{t.curriculaLink}</Link>} />
      <ErrorBanner message={query.error ? errorText(query.error, t.errors, t.error) : ''} onRetry={query.reload} />
      {query.loading && !data && <Loading label={t.loading} />}
      {data && (
        <>
          {(data.fields || []).map((entry) => <RankCard key={entry.rank.fieldKey} rank={entry.rank} next={entry.next} showVerificationLabels={data.showVerificationLabels !== false} />)}
          <RankProgressPanel rank={data} attendanceTotal={attendanceTotal} />
          {(data.fields || []).map((entry) => fieldsByKey.get(entry.rank.fieldKey)).filter(Boolean).map((field) => (
            <LineageSection key={field.key} client={client} field={field} me={me} entries={entries.filter((entry) => entry.fieldKey === field.key)} onChange={replaceLineage} />
          ))}
        </>
      )}
    </div>
  );
}

export default MyRankView;
