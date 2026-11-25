/**
 * Member curricula: `/app/curricula` (published curricula with the member's progress) and
 * `/app/curricula/:id` (items grouped by rank with completion). Hidden unless `features.ranks`.
 * `CurriculumItems` props: `curriculum` (CurriculumView with items), `field` (the Rank field the
 * curriculum groups by, or null), `onChange(item)` after a completion toggle, `client`.
 */
import { useCallback, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { catalogFeatureEnabled, groupCurriculumItems, profileFieldsOf } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import PageHeader from '../../components/PageHeader.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { Badge } from '../../components/Badges.jsx';
import { errorText } from '../../utils/engagement.js';
import CurriculumCard from './CurriculumCard.jsx';
import ProgressBar from './ProgressBar.jsx';

const t = strings.ranks.curricula;
const e = strings.ranks.errors;

export function curriculumItemPath(item, property) {
  if (!item || !item.targetId || item.targetAvailable === false) return null;
  const id = encodeURIComponent(item.targetId);
  if (item.itemType === 'Lesson') return '/app/lessons/' + id;
  if (item.itemType === 'Series') return '/app/series/' + id;
  if (item.itemType === 'CatalogEntry' && catalogFeatureEnabled(property)) return '/app/catalog/' + id;
  return null;
}

export function CurriculaListView() {
  const { client } = useAuth();
  const fetchPage = useCallback((cursor, signal) => client.enumerateCurricula({ maxResults: 25, continuationToken: cursor }, signal), [client]);
  const list = useCursorList(fetchPage);
  useDocumentMeta(t.title);
  return (
    <div className="member-view stack">
      <Link className="back-link" to="/app/rank">{strings.ranks.title}</Link>
      <PageHeader title={t.title} summary={t.summary} />
      <ErrorBanner message={list.error ? errorText(list.error, e, t.error) : ''} onRetry={list.refresh} />
      {list.loading && !list.loaded && <Loading label={t.loading} />}
      {list.loaded && !list.items.length && !list.error && <p className="muted">{t.empty}</p>}
      <div className="stack">{list.items.map((c) => <CurriculumCard key={c.id} curriculum={c} />)}</div>
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary" onClick={list.loadMore}>{t.more}</button>}
    </div>
  );
}

function ItemRow({ item, client, curriculumId, property, onChange }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const title = item.title || item.customText || item.targetId || '';
  const path = curriculumItemPath(item, property);
  const done = item.state === 'SelfComplete' || item.state === 'Completed' || item.state === 'Verified';
  const canToggle = item.selfCompletable && item.state !== 'Verified';
  const toggle = async () => {
    setBusy(true);
    setError('');
    try {
      const next = item.state === 'SelfComplete' ? await client.uncompleteCurriculumItem(curriculumId, item.id) : await client.completeCurriculumItem(curriculumId, item.id);
      onChange(next && next.id ? next : { ...item, state: item.state === 'SelfComplete' ? 'Incomplete' : 'SelfComplete' });
    } catch (err) {
      setError(errorText(err, e, e.generic));
    } finally {
      setBusy(false);
    }
  };
  return (
    <li className="curriculum-item" data-item={item.id} data-state={item.state}>
      <div className="row-between">
        <span>
          {path ? <Link to={path}>{title}</Link> : <span>{title}</span>}{' '}
          <span className="muted small">{t.types[item.itemType] || item.itemType}</span>{' '}
          <Badge tone={item.required ? 'info' : 'neutral'}>{item.required ? t.required : t.optional}</Badge>{' '}
          <Badge tone={item.satisfied ? 'success' : done ? 'warning' : 'neutral'}>{t.states[item.state] || item.state}</Badge>
        </span>
        {canToggle && (
          <button type="button" className="button button-secondary button-sm" onClick={toggle} disabled={busy} aria-pressed={item.state === 'SelfComplete'} aria-label={item.state === 'SelfComplete' ? t.undoLabel(title) : t.completeLabel(title)}>
            {item.state === 'SelfComplete' ? t.undo : t.complete}
          </button>
        )}
      </div>
      {item.targetAvailable === false && <p className="muted small">{t.unavailable}</p>}
      {!item.selfCompletable && !done && <p className="muted small">{t.autoComplete}</p>}
      {item.selfCompletable && item.required && item.state === 'SelfComplete' && <p className="muted small">{t.waitingVerification}</p>}
      {error && <p className="field-error" role="alert">{error}</p>}
    </li>
  );
}

export function CurriculumItems({ curriculum, field, client, onChange }) {
  const { property } = useProperty();
  const groups = useMemo(() => groupCurriculumItems(field, curriculum.items || []), [field, curriculum.items]);
  return (
    <div className="stack">
      {groups.map((group) => {
        const heading = group.label || t.ungrouped;
        return (
          <section key={group.optionKey || 'none'} className="panel" aria-label={heading}>
            <h2 className="section-title">{heading} <span className="muted small">{strings.ranks.curriculumProgress(group.completed, group.total)}</span></h2>
            <ProgressBar value={group.completed} max={group.total} label={heading + ': ' + strings.ranks.curriculumProgress(group.completed, group.total)} />
            <ul className="curriculum-items">
              {group.items.map((item) => <ItemRow key={item.id} item={item} client={client} curriculumId={curriculum.id} property={property} onChange={onChange} />)}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

export function CurriculumDetailView() {
  const { id } = useParams();
  return <CurriculumDetail key={id} id={id} />;
}

function CurriculumDetail({ id }) {
  const { client } = useAuth();
  const { property } = useProperty();
  const query = useApiQuery(useCallback((signal) => client.getCurriculum(id, signal), [client, id]));
  const [override, setOverride] = useState(null);
  const curriculum = override || query.data;
  useDocumentMeta(curriculum ? curriculum.name : t.title);
  const field = useMemo(() => {
    const rankFields = profileFieldsOf(property).filter((f) => f.type === 'Rank');
    return (curriculum && rankFields.find((f) => f.key === curriculum.fieldKey)) || rankFields[0] || null;
  }, [property, curriculum]);
  const replaceItem = (item) => setOverride((current) => {
    const base = current || query.data;
    const items = (base.items || []).map((i) => (i.id === item.id ? { ...i, ...item, satisfied: item.satisfied ?? (item.state === 'Verified' || (!i.required && item.state === 'SelfComplete')) } : i));
    return { ...base, items };
  });
  const total = curriculum ? (curriculum.items || []).length : 0;
  const done = curriculum ? (curriculum.items || []).filter((i) => i.satisfied).length : 0;
  return (
    <div className="member-view stack">
      <Link className="back-link" to="/app/curricula">{t.back}</Link>
      <ErrorBanner message={query.error ? errorText(query.error, e, t.error) : ''} onRetry={query.reload} />
      {query.loading && !curriculum && <Loading label={t.loading} />}
      {curriculum && (
        <>
          <PageHeader title={curriculum.name} summary={curriculum.description} />
          <p className="small">{strings.ranks.curriculumProgress(done, total)}</p>
          <ProgressBar value={done} max={total} label={curriculum.name + ': ' + strings.ranks.curriculumProgress(done, total)} />
          <CurriculumItems curriculum={curriculum} field={field} client={client} onChange={replaceItem} />
        </>
      )}
    </div>
  );
}
