import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import { isLockedItem } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { featureEnabled } from '../member/memberRoutes.js';
import { formatClock, libraryPathsFor } from './trainingModel.js';

const t = strings.training;

/**
 * "Techniques in this lesson" (`POST /lessons/{id}/catalog-entries/enumerate`): the linked catalog
 * entries in order, each linking to its entry. A start time seeks the lesson's player when `onSeek`
 * is given (the player supports seeking), and shows as text otherwise. Hidden while
 * `features.catalog` is off, when the client has no such route, and when nothing is linked.
 * @param {{ client: object, lessonId: string, onSeek?: ((seconds: number) => boolean)|null }} props
 */
export function LessonTechniques({ client, lessonId, onSeek = null }) {
  const { property } = useProperty();
  const on = featureEnabled(property, 'catalog') && Boolean(client && typeof client.listLessonCatalogEntries === 'function');
  const fetchPage = useCallback((cursor, signal) => client.listLessonCatalogEntries(lessonId, { maxResults: 50, continuationToken: cursor }, signal), [client, lessonId]);
  const list = useCursorList(fetchPage, { enabled: on });
  if (!on || (list.loaded && !list.items.length && !list.error)) return null;
  const paths = libraryPathsFor(property);
  return (
    <section className="panel" aria-labelledby="lesson-techniques">
      <h2 id="lesson-techniques" className="section-title">{t.lessonTechniques.title}</h2>
      {!list.loaded && list.loading && <Loading inline />}
      <ErrorBanner message={list.error ? t.lessonTechniques.error : ''} onRetry={list.refresh} />
      <ul className="training-list" data-testid="lesson-techniques">
        {list.items.map((entry) => {
          const seconds = Number.isFinite(entry.startSeconds) ? entry.startSeconds : null;
          const clock = seconds !== null ? formatClock(seconds) : null;
          return (
            <li key={entry.id} className="training-technique-row">
              <Link to={paths.catalog(entry.slug || entry.id)} className="training-list-title">{entry.name}</Link>
              {clock && (onSeek
                ? <button type="button" className="link-button small" aria-label={t.lessonTechniques.seek(entry.name, clock)} onClick={() => onSeek(seconds)}>{clock}</button>
                : <span className="muted small">{t.lessonTechniques.at(clock)}</span>)}
            </li>
          );
        })}
      </ul>
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{t.search.more}</button>}
    </section>
  );
}

/**
 * "Lessons that teach this" on a catalog entry: `POST /catalog/entries/{id}/lessons/enumerate` for
 * members (locked stubs marked), `GET /public/catalog/entries/{id}/lessons` signed out.
 * @param {{ client: object, entryId: string, publicPage?: boolean }} props
 */
export function TechniqueLessons({ client, entryId, publicPage = false }) {
  const { property } = useProperty();
  const method = publicPage ? 'listPublicCatalogEntryLessons' : 'listCatalogEntryLessons';
  const on = Boolean(client && typeof client[method] === 'function');
  const fetcher = useCallback((signal) => (publicPage ? client.listPublicCatalogEntryLessons(entryId, signal) : client.listCatalogEntryLessons(entryId, { maxResults: 25 }, signal)), [client, entryId, publicPage]);
  const query = useApiQuery(fetcher, { enabled: on });
  if (!on) return null;
  const rows = ((query.data && query.data.objects) || []).filter(Boolean);
  const paths = libraryPathsFor(property);
  return (
    <section className="panel" aria-labelledby="technique-lessons">
      <h2 id="technique-lessons" className="section-title">{t.techniqueLessons.title}</h2>
      {query.loading && !query.data && <Loading inline />}
      <ErrorBanner message={query.error ? t.techniqueLessons.error : ''} onRetry={query.reload} />
      {query.data && !rows.length && <p className="muted small">{t.techniqueLessons.empty}</p>}
      {rows.length > 0 && (
        <ul className="training-list" data-testid="technique-lessons">
          {rows.map((lesson) => (
            <li key={lesson.id}>
              <Link to={paths.lesson(lesson.slug || lesson.id)} className="training-list-link">
                <span className="training-list-title">{lesson.title}</span>
                <span className="muted small">{[lesson.series && lesson.series.title, isLockedItem(lesson) ? t.techniqueLessons.locked : null].filter(Boolean).join(' · ')}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
