import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { groupByLevel, libraryLevels } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { LessonRow, SeriesCard } from '../../components/library/LibraryCards.jsx';
import { errorText } from '../../utils/engagement.js';
import { phase4Errors } from '../../utils/phase4.js';

const t = strings.library;
const PAGE_SIZE = 24;
const DURATIONS = Object.freeze({ short: { maxDurationSeconds: 599 }, medium: { minDurationSeconds: 600, maxDurationSeconds: 1800 }, long: { minDurationSeconds: 1801 } });

/** Lesson filters from the URL (`level`, `kind`, `progress`, `duration`, `resources`). */
export function lessonFiltersFrom(params) {
  const filters = {};
  const levels = params.getAll('level').filter(Boolean);
  if (levels.length) filters.levels = levels;
  if (params.get('kind')) filters.kind = params.get('kind');
  if (params.get('progress')) filters.progress = params.get('progress');
  if (params.get('resources') === 'true') filters.hasResources = true;
  Object.assign(filters, DURATIONS[params.get('duration')] || {});
  return filters;
}

function ListFooter({ list, empty }) {
  return (
    <>
      <ErrorBanner message={list.error ? t.loadError + ' ' + errorText(list.error, phase4Errors(), '') : ''} onRetry={list.items.length ? list.loadMore : list.refresh} />
      {!list.loaded && list.loading && <Loading />}
      {list.loaded && !list.items.length && !list.loading && !list.error && <EmptyState icon="book" title={empty} />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary feed-more" onClick={list.loadMore}>{t.loadMore}</button>}
    </>
  );
}

function LessonResults({ client, meta, filters }) {
  const key = JSON.stringify(filters);
  const fetchPage = useCallback((cursor, signal) => client.enumerateLessons({ ...meta, ordering: meta.search ? 'Relevance' : meta.ordering || undefined, continuationToken: cursor, maxResults: PAGE_SIZE, filters: JSON.parse(key) }, signal), [client, meta, key]);
  const list = useCursorList(fetchPage);
  return (
    <section className="p4-section" aria-labelledby="library-results">
      <h2 id="library-results">{t.searchResults}</h2>
      <div className="p4-list">{list.items.map((lesson) => <LessonRow key={lesson.id} lesson={lesson} showSeries />)}</div>
      <ListFooter list={list} empty={t.searchEmpty} />
    </section>
  );
}

function ContinueWatching({ client }) {
  const fetcher = useCallback((signal) => client.enumerateContinueWatching({ maxResults: 10 }, signal), [client]);
  const query = useApiQuery(fetcher);
  // Completed lessons never show here (the server excludes them too).
  const rows = ((query.data && query.data.objects) || []).filter((row) => row && row.lesson && !(row.progress && row.progress.completed));
  return (
    <section className="p4-section" aria-labelledby="library-continue">
      <h2 id="library-continue">{t.continueTitle}</h2>
      {query.loading && !query.data && <Loading inline />}
      {query.data && !rows.length && <p className="muted small">{t.continueEmpty}</p>}
      {rows.length > 0 && <div className="p4-list">{rows.map((row) => <LessonRow key={row.lesson.id} lesson={row.lesson} progress={row.progress} showSeries />)}</div>}
    </section>
  );
}

function FollowedSeries({ client }) {
  const fetcher = useCallback((signal) => client.enumerateSeries({ maxResults: 20, filters: { followedOnly: true } }, signal), [client]);
  const query = useApiQuery(fetcher);
  const rows = (query.data && query.data.objects) || [];
  if (!rows.length) return null;
  return (
    <section className="p4-section" aria-labelledby="library-followed">
      <h2 id="library-followed">{t.followedTitle}</h2>
      <div className="p4-rail">{rows.map((s) => <SeriesCard key={s.id} series={s} />)}</div>
    </section>
  );
}

function SeriesGrid({ client, property, meta }) {
  const fetchPage = useCallback((cursor, signal) => client.enumerateSeries({ labels: meta.labels, labelMatch: meta.labelMatch, tags: meta.tags, continuationToken: cursor, maxResults: PAGE_SIZE }, signal), [client, meta]);
  const list = useCursorList(fetchPage);
  const groups = groupByLevel(property, list.items);
  const hasLevels = libraryLevels(property).length > 0;
  const filtered = Boolean(meta.labels.length || Object.keys(meta.tags).length);
  return (
    <>
      {hasLevels ? groups.map((group) => (
        <section key={group.level ? group.level.key : 'other'} className="p4-section" aria-label={group.level ? group.level.label : t.otherLevel}>
          <h2>{group.level ? group.level.label : t.otherLevel}</h2>
          <div className="p4-grid">{group.items.map((s) => <SeriesCard key={s.id} series={s} />)}</div>
        </section>
      )) : list.items.length > 0 && (
        <section className="p4-section" aria-labelledby="library-all">
          <h2 id="library-all">{t.allSeries}</h2>
          <div className="p4-grid">{list.items.map((s) => <SeriesCard key={s.id} series={s} />)}</div>
        </section>
      )}
      <ListFooter list={list} empty={filtered ? t.seriesEmptyFiltered : t.seriesEmpty} />
    </>
  );
}

/**
 * /app/library (docs/PHASE4_CONTRACT.md sections 5.6 and 16): "Continue watching", followed series,
 * and series grouped by level. Search, level chips, type, length, progress, and label and tag filters
 * live in the URL and switch the page to matching lessons. Locked stubs carry a lock badge; the
 * footer states that staff can see viewing history (resolved decision 17).
 *
 * `embedded` is the Training hub's Courses segment: the hub supplies the page header and the one
 * unified search box (with label and tag filters), so this view drops its own and ignores `search`.
 * @param {{ embedded?: boolean }} props
 */
function LibraryHomeView({ embedded = false }) {
  const { client, me } = useAuth();
  const { property, fill } = useProperty();
  const [searchParams, setSearchParams] = useSearchParams();
  const [urlMeta, setMeta] = useEnumerationUrlState();
  const meta = useMemo(() => (embedded && urlMeta.search ? { ...urlMeta, search: '' } : urlMeta), [embedded, urlMeta]);
  useDocumentMeta(embedded ? null : strings.member.metaTitle(t.title));
  const levels = libraryLevels(property);
  const filters = useMemo(() => lessonFiltersFrom(searchParams), [searchParams]);
  const selectedLevels = filters.levels || [];
  const lessonMode = Boolean(meta.search || Object.keys(filters).length);

  const setParam = (key, value) => {
    const next = new URLSearchParams(searchParams);
    if (value) next.set(key, value);
    else next.delete(key);
    setSearchParams(next);
  };
  const toggleLevel = (key) => {
    const next = new URLSearchParams(searchParams);
    const current = next.getAll('level');
    next.delete('level');
    for (const value of current.includes(key) ? current.filter((x) => x !== key) : current.concat(key)) next.append('level', value);
    setSearchParams(next);
  };

  return (
    <div className={embedded ? 'stack library-view' : 'member-view library-view'}>
      {!embedded && <PageHeader title={t.title} summary={fill(t.summary)} />}
      {!embedded && <MetadataFilterBar value={meta} onChange={setMeta} client={client} targetType="Lesson" showSearch searchLabel={t.searchLabel} />}
      {levels.length > 0 && (
        <div className="chip-row" role="group" aria-label={t.levelChips}>
          {levels.map((level) => (
            <button key={level.key} type="button" className={'chip-toggle' + (selectedLevels.includes(level.key) ? ' active' : '')} aria-pressed={selectedLevels.includes(level.key)} onClick={() => toggleLevel(level.key)}>
              <span className="p4-chip-dot" style={level.color ? { background: level.color } : undefined} aria-hidden="true" />{level.label}
            </button>
          ))}
        </div>
      )}
      <div className="p4-filters">
        <label className="field field-compact">
          <span className="label-text">{t.kindFilter}</span>
          <select className="select-compact" value={searchParams.get('kind') || ''} onChange={(e) => setParam('kind', e.target.value)}>
            <option value="">{t.kindAny}</option>
            {Object.entries(t.kinds).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </label>
        <label className="field field-compact">
          <span className="label-text">{t.durationFilter}</span>
          <select className="select-compact" value={searchParams.get('duration') || ''} onChange={(e) => setParam('duration', e.target.value)}>
            <option value="">{t.durations.any}</option>
            {['short', 'medium', 'long'].map((value) => <option key={value} value={value}>{t.durations[value]}</option>)}
          </select>
        </label>
        {me && (
          <label className="field field-compact">
            <span className="label-text">{t.progressFilter}</span>
            <select className="select-compact" value={searchParams.get('progress') || ''} onChange={(e) => setParam('progress', e.target.value)}>
              <option value="">{t.progressOptions.any}</option>
              {['NotStarted', 'InProgress', 'Completed'].map((value) => <option key={value} value={value}>{t.progressOptions[value]}</option>)}
            </select>
          </label>
        )}
        <label className="check-field">
          <input type="checkbox" checked={searchParams.get('resources') === 'true'} onChange={(e) => setParam('resources', e.target.checked ? 'true' : '')} />
          <span>{t.withResources}</span>
        </label>
      </div>
      {lessonMode ? <LessonResults client={client} meta={meta} filters={filters} /> : (
        <>
          {me && <ContinueWatching client={client} />}
          {me && <FollowedSeries client={client} />}
          <SeriesGrid client={client} property={property} meta={meta} />
        </>
      )}
      {me && (
        <aside className="panel p4-section" aria-labelledby="library-history">
          <h2 id="library-history" className="section-title">{t.historyTitle}</h2>
          <p className="muted small">{t.historyNote}</p>
        </aside>
      )}
    </div>
  );
}

export default LibraryHomeView;
