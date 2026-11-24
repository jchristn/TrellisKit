import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { formatTotalDuration, isLockedItem, lockedRequirement } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useCursorList from '../../hooks/useCursorList.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import FollowButton from '../../components/forums/FollowButton.jsx';
import { AccessPromptCard } from '../../components/AccessPrompt.jsx';
import { LessonRow, LevelChips, ProgressBar } from '../../components/library/LibraryCards.jsx';
import { errorText } from '../../utils/engagement.js';
import { appPaths, mediaThumbUrl, phase4Errors } from '../../utils/phase4.js';

const t = strings.library;

/**
 * /app/series/:id (section 5.1): the series header (cover, levels, follow, progress), its
 * description, and the lesson list in position order (continuation paged; locked stubs list only
 * what the caller may open, each with a lock badge). Accepts a slug or an id.
 */
function SeriesView() {
  const { id } = useParams();
  const { client, me } = useAuth();
  const navigate = useNavigate();
  const fetcher = useCallback((signal) => client.getSeries(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const [followed, setFollowed] = useState(null);
  const series = query.data;
  useDocumentMeta(strings.member.metaTitle(series ? series.title : t.title));
  const fetchPage = useCallback((cursor, signal) => client.enumerateSeriesLessons(series.id, { ordering: 'PositionAscending', continuationToken: cursor, maxResults: 50 }, signal), [client, series]);
  const lessons = useCursorList(fetchPage, { enabled: Boolean(series) });

  useEffect(() => {
    if (series && series.slug && id !== series.slug && id === series.id) navigate(appPaths.series(series.slug), { replace: true });
  }, [series, id, navigate]);

  const back = <Link to={appPaths.library} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.back}</span></Link>;
  if (query.error && query.error.status === 404) {
    return <div className="member-view">{back}<EmptyState icon="book" title={t.seriesNotFound}>{t.notFoundBody}</EmptyState></div>;
  }
  if (!series) {
    return <div className="member-view">{back}{query.loading && <Loading />}<ErrorBanner message={query.error ? errorText(query.error, phase4Errors(), t.loadError) : ''} onRetry={query.reload} /></div>;
  }
  const locked = isLockedItem(series);
  const isFollowed = followed === null ? Boolean(series.isFollowed) : followed;
  const progress = series.myProgress;
  const cover = mediaThumbUrl(series.cover);

  return (
    <div className="member-view series-view">
      {back}
      <header className="page-header">
        {cover && <img className="p4-card-cover" style={{ maxWidth: 220 }} src={cover} alt="" />}
        <div className="page-header-text">
          <h1>{series.title}</h1>
          {series.summary && <p className="muted">{series.summary}</p>}
          <span className="p4-card-meta">
            <span>{t.lessonCount(series.lessonCount || 0)}</span>
            {series.totalDurationSeconds > 0 && <span>{formatTotalDuration(series.totalDurationSeconds)}</span>}
          </span>
          <LevelChips levels={series.levels} />
        </div>
        <div className="page-header-actions">
          {me && series.viewer && series.viewer.canFollow !== false && (
            <FollowButton
              followed={isFollowed}
              label={t.followLabel(series.title)}
              unfollowLabel={t.unfollowLabel(series.title)}
              onToggle={async (next) => {
                const result = next ? await client.followSeries(series.id) : await client.unfollowSeries(series.id);
                setFollowed(result && typeof result.isFollowed === 'boolean' ? result.isFollowed : next);
              }}
            />
          )}
        </div>
      </header>
      {me && progress && progress.percent > 0 && (
        <div className="panel">
          <span className="small">{t.progressOf(progress.completedLessons || 0, series.lessonCount || 0)}</span>
          <ProgressBar percent={progress.percent} />
        </div>
      )}
      {locked && <AccessPromptCard compact required={lockedRequirement(series)} feature="library" title={t.lockedSeries} />}
      {series.description && <section className="panel"><MarkdownBody body={series.description} allowHeadings linkMentions={false} /></section>}
      <section className="p4-section" aria-labelledby="series-lessons">
        <h2 id="series-lessons">{t.lessonsTitle}</h2>
        <div className="p4-list">{lessons.items.map((lesson) => <LessonRow key={lesson.id} lesson={lesson} />)}</div>
        <ErrorBanner message={lessons.error ? errorText(lessons.error, phase4Errors(), t.loadError) : ''} onRetry={lessons.refresh} />
        {!lessons.loaded && lessons.loading && <Loading />}
        {lessons.loaded && !lessons.items.length && !lessons.error && <EmptyState icon="book" title={t.lessonsEmpty} />}
        {lessons.loaded && !lessons.done && !lessons.loading && <button type="button" className="button button-secondary feed-more" onClick={lessons.loadMore}>{t.loadMore}</button>}
      </section>
    </div>
  );
}

export default SeriesView;
