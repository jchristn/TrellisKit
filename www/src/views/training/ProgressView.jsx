import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { LessonRow, ProgressBar } from '../../components/library/LibraryCards.jsx';
import { RankProgressPanel } from '../ranks/RankProgressPanel.jsx';
import { useMyRank } from '../ranks/rankHooks.js';
import { ranksEnabled, trainingPaths } from './trainingModel.js';

const t = strings.training.progress;

/** Completed lessons: the count (`POST /me/library/progress/enumerate`, completed) and the lessons themselves. */
function CompletedLessons({ client }) {
  const count = useApiQuery(useCallback((signal) => client.enumerateMyLessonProgress({ maxResults: 1, filters: { completed: true } }, signal), [client]));
  const fetchPage = useCallback((cursor, signal) => client.enumerateLessons({ maxResults: 25, continuationToken: cursor, filters: { progress: 'Completed' } }, signal), [client]);
  const list = useCursorList(fetchPage);
  const total = count.data && Number.isInteger(count.data.totalRecords) ? count.data.totalRecords : null;
  return (
    <section className="panel stack" aria-labelledby="progress-lessons">
      <h2 id="progress-lessons" className="section-title">{t.completedLessons}</h2>
      {total !== null && <p data-testid="progress-completed-count">{t.completedCount(total)}</p>}
      {list.loaded && !list.items.length && !list.error && <p className="muted small">{t.completedEmpty}</p>}
      {list.items.length > 0 && <div className="p4-list">{list.items.map((lesson) => <LessonRow key={lesson.id} lesson={lesson} showSeries />)}</div>}
      <ErrorBanner message={list.error || count.error ? t.loadError : ''} onRetry={() => { list.refresh(); count.reload(); }} />
      {!list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{t.loadMore}</button>}
    </section>
  );
}

/** Followed courses with the member's progress in each. */
function FollowedCourses({ client }) {
  const query = useApiQuery(useCallback((signal) => client.enumerateSeries({ maxResults: 50, filters: { followedOnly: true } }, signal), [client]));
  const rows = ((query.data && query.data.objects) || []).filter(Boolean);
  return (
    <section className="panel stack" aria-labelledby="progress-series">
      <h2 id="progress-series" className="section-title">{t.seriesTitle}</h2>
      {query.loading && !query.data && <Loading inline />}
      <ErrorBanner message={query.error ? t.loadError : ''} onRetry={query.reload} />
      {query.data && !rows.length && <p className="muted small">{t.seriesEmpty}</p>}
      {rows.length > 0 && (
        <ul className="training-list">
          {rows.map((series) => {
            const progress = series.myProgress || {};
            const done = progress.completedLessons || 0;
            const total = series.lessonCount || 0;
            return (
              <li key={series.id} className="training-progress-row">
                <Link to={trainingPaths.series(series.slug || series.id)} className="training-list-title">{series.title}</Link>
                <span className="muted small">{total > 0 && done >= total ? t.seriesComplete : t.seriesProgress(done, total)}</span>
                <ProgressBar percent={progress.percent || 0} label={series.title + ': ' + t.seriesProgress(done, total)} />
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** Rank history, attendance count, and curriculum completion (ranks on). */
function RankProgress({ client }) {
  const rank = useMyRank(client, true);
  const attendance = useApiQuery(useCallback((signal) => client.enumerateMyAttendance({ maxResults: 1 }, signal), [client]));
  const curricula = useApiQuery(useCallback((signal) => client.enumerateCurricula({ maxResults: 25 }, signal), [client]));
  const attendanceTotal = attendance.data && Number.isInteger(attendance.data.totalRecords) ? attendance.data.totalRecords : null;
  const curriculaRows = curricula.data ? (curricula.data.objects || []).filter(Boolean) : null;
  return (
    <>
      <ErrorBanner message={rank.error ? t.loadError : ''} onRetry={rank.reload} />
      <RankProgressPanel rank={rank.data || null} attendanceTotal={attendanceTotal} curricula={curriculaRows} />
    </>
  );
}

/**
 * Training Progress (`/app/training/progress`): completed lessons and followed courses from the
 * library progress routes, and, when ranks are on, rank history, attendance count, and curriculum
 * completion. Shown only when the overview reports the Progress section available.
 */
function ProgressView() {
  const { client } = useAuth();
  const { property } = useProperty();
  return (
    <div className="stack">
      <p className="muted">{t.intro}</p>
      <CompletedLessons client={client} />
      <FollowedCourses client={client} />
      {ranksEnabled(property) && <RankProgress client={client} />}
    </div>
  );
}

export default ProgressView;
