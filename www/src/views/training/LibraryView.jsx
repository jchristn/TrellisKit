import { useCallback, useEffect, useState } from 'react';
import { Link, Navigate, Outlet, useLocation } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useEnumerationUrlState from '../../hooks/useEnumerationUrlState.js';
import MetadataFilterBar from '../../components/MetadataFilterBar.jsx';
import SectionTabs from '../../components/training/SectionTabs.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { LessonRow, SeriesCard } from '../../components/library/LibraryCards.jsx';
import { useTraining } from './TrainingLayout.jsx';
import { librarySegmentsFor, trainingPaths } from './trainingModel.js';

const t = strings.training;
const SEARCH_MIN = 2;
const SEARCH_MAX = 100;
const PER_KIND = 10;

/** The kinds the unified search asks for: techniques only while the Techniques segment shows. */
export function searchKinds(segments) {
  return ['Series', 'Lesson', ...(segments.includes('techniques') ? ['CatalogEntry'] : [])];
}

function searchError(error) {
  if (!error) return '';
  if (error.status === 429) return t.search.rateLimited;
  if (error.status === 400) return t.search.tooShort;
  return t.search.error;
}

const KINDS = Object.freeze([
  { key: 'series', kind: 'Series', title: () => t.search.courses, more: 'enumerateSeries' },
  { key: 'lessons', kind: 'Lesson', title: () => t.search.lessons, more: 'enumerateLessons' },
  { key: 'techniques', kind: 'CatalogEntry', title: () => t.search.techniques, more: 'enumerateCatalogEntries' }
]);

/**
 * Unified Library search (`POST /library/search`): courses, lessons, and techniques for one query,
 * with the label and tag filters. "Show more" continues each kind through its own enumeration route
 * with the same query.
 */
function LibrarySearchResults({ client, meta, kinds }) {
  const query = meta.search.trim();
  const [state, setState] = useState({ key: '', loading: true, error: null, data: null });
  const key = JSON.stringify({ query, kinds, labels: meta.labels, tags: meta.tags });

  useEffect(() => {
    const controller = new AbortController();
    const body = JSON.parse(key);
    const request = { query: body.query, kinds: body.kinds, maxResultsPerKind: PER_KIND };
    if (body.labels.length) request.labels = body.labels;
    if (Object.keys(body.tags).length) request.tags = body.tags;
    client.searchLibrary(request, controller.signal)
      .then((data) => setState({ key, loading: false, error: null, data: { series: data && data.series, lessons: data && data.lessons, techniques: data && data.techniques } }))
      .catch((error) => { if (!controller.signal.aborted) setState({ key, loading: false, error, data: null }); });
    return () => controller.abort();
  }, [client, key]);

  const more = async (kind) => {
    const page = state.data[kind.key];
    if (!page || !page.continuationToken) return;
    try {
      const next = await client[kind.more]({ search: query, labels: meta.labels, tags: meta.tags, maxResults: PER_KIND, continuationToken: page.continuationToken });
      setState((current) => ({ ...current, data: { ...current.data, [kind.key]: { ...next, objects: [...page.objects, ...((next && next.objects) || [])] } } }));
    } catch (error) {
      setState((current) => ({ ...current, error }));
    }
  };

  const current = state.key === key ? state : { loading: true, error: null, data: null };
  const groups = KINDS.filter((kind) => kinds.includes(kind.kind)).map((kind) => ({ kind, page: current.data && current.data[kind.key] })).filter((group) => group.page && Array.isArray(group.page.objects) && group.page.objects.length);
  return (
    <section className="stack" aria-labelledby="library-search-title" data-testid="library-search-results">
      <h2 id="library-search-title" className="section-title">{t.search.resultsFor(query)}</h2>
      {current.loading && <Loading label={t.search.resultsTitle} />}
      <ErrorBanner message={searchError(current.error)} />
      {current.data && !groups.length && <EmptyState icon="search" title={t.search.empty} />}
      {groups.map(({ kind, page }) => (
        <section key={kind.key} className="p4-section" aria-labelledby={'library-search-' + kind.key}>
          <h3 id={'library-search-' + kind.key}>{kind.title()}</h3>
          {kind.key === 'series' && <div className="p4-grid">{page.objects.map((series) => <SeriesCard key={series.id} series={series} />)}</div>}
          {kind.key === 'lessons' && <div className="p4-list">{page.objects.map((lesson) => <LessonRow key={lesson.id} lesson={lesson} showSeries />)}</div>}
          {kind.key === 'techniques' && (
            <ul className="training-list">
              {page.objects.map((entry) => (
                <li key={entry.id}>
                  <Link to={trainingPaths.catalog(entry.slug || entry.id)} className="training-list-link">
                    <span className="training-list-title">{entry.name}</span>
                    {entry.summary && <span className="muted small">{entry.summary}</span>}
                  </Link>
                </li>
              ))}
            </ul>
          )}
          {page.continuationToken && !page.endOfResults && typeof client[kind.more] === 'function' && (
            <button type="button" className="button button-secondary button-sm" onClick={() => more(kind)}>{t.search.more}</button>
          )}
        </section>
      ))}
    </section>
  );
}

/**
 * The one Library (`/app/training/library/:segment`): Courses (series and lessons), Techniques (the
 * reviewed catalog, hidden while the catalog is off or the overview does not list it), and Saved
 * (signed-in members with saves on), as a tablist under one unified search box with label and tag
 * filters. A search of at least two characters shows results across the kinds instead of the
 * segment; labels and tags alone filter the segment's own lists.
 */
function LibraryView() {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const { overview } = useTraining();
  const location = useLocation();
  const [meta, setMeta] = useEnumerationUrlState();
  const segments = librarySegmentsFor({ property, me, overview });
  const current = segments.find((segment) => location.pathname === trainingPaths.segment(segment));
  const search = (meta.search || '').trim();
  const onChange = useCallback((next) => setMeta({ ...next, search: (next.search || '').slice(0, SEARCH_MAX) }), [setMeta]);
  if (!current) return <Navigate to={{ pathname: trainingPaths.segment('courses'), search: location.search }} replace />;
  const tabs = segments.map((key) => ({ key, label: t.segments[key], to: trainingPaths.segment(key) }));
  const searching = search.length >= SEARCH_MIN && current !== 'saved';
  return (
    <div className="stack">
      {current !== 'saved' && (
        <div className="training-search">
          <MetadataFilterBar value={meta} onChange={onChange} client={client} targetType="Lesson" showSearch searchLabel={t.search.placeholder} />
          {search.length > 0 && search.length < SEARCH_MIN && <p className="muted small" role="status">{t.search.tooShort}</p>}
          {searching && <button type="button" className="link-button small" onClick={() => setMeta({ ...meta, search: '' })}>{t.search.clear}</button>}
        </div>
      )}
      {tabs.length > 1 && <SectionTabs label={t.segmentsLabel} tabs={tabs} current={current} controls="training-library-panel" id="training-segment" keepSearch />}
      <div id="training-library-panel" role={tabs.length > 1 ? 'tabpanel' : undefined} aria-labelledby={tabs.length > 1 ? 'training-segment-' + current : undefined}>
        {searching ? <LibrarySearchResults client={client} meta={meta} kinds={searchKinds(segments)} /> : <Outlet />}
      </div>
    </div>
  );
}

export default LibraryView;
