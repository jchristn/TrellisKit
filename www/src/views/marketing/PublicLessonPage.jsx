import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { formatDuration } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import LessonPlayer from '../../components/library/LessonPlayer.jsx';
import { LevelChips } from '../../components/library/LibraryCards.jsx';
import { appPaths } from '../../utils/phase4.js';

const t = strings.publicLessons;

/**
 * Public page of one sample lesson (docs/PHASE4_CONTRACT.md section 5.12, `GET /public/lessons/{id}`)
 * on the marketing site, for visitors who are not signed in. The video plays from the public media
 * paths (no account and no media grant), with its chapter markers and captions; progress is not
 * recorded. Comments and the rest of the library are for members, so the page invites the visitor
 * to join (or a signed-in member to open the lesson in the app). A lesson that is not public, or
 * does not exist, shows "Lesson not found".
 */
function PublicLessonPage() {
  const { id } = useParams();
  const { client, isSignedIn } = useAuth();
  const [state, setState] = useState({ id: null, lesson: null, error: null });
  const [attempt, setAttempt] = useState(0);
  const current = state.id === id ? state : { id, lesson: null, error: null };
  const lesson = current.lesson;
  useDocumentMeta(lesson ? t.meta.replace('{title}', lesson.title) : t.loading + ' | {name}', lesson ? lesson.summary || '' : '');

  useEffect(() => {
    if (!client || typeof client.getPublicLesson !== 'function') return undefined;
    const controller = new AbortController();
    client.getPublicLesson(id, controller.signal).then(
      (body) => { if (!controller.signal.aborted) setState({ id, lesson: body, error: null }); },
      (err) => { if (!controller.signal.aborted) setState({ id, lesson: null, error: err || new Error('failed') }); }
    );
    return () => controller.abort();
  }, [client, id, attempt]);

  if (current.error && current.error.status === 404) {
    return (
      <div className="site-container page">
        <EmptyState icon="info" title={t.notFoundTitle} action={<Link to="/" className="button button-primary">{t.backHome}</Link>}>{t.notFoundBody}</EmptyState>
      </div>
    );
  }

  const appPath = appPaths.lesson(lesson ? lesson.slug || lesson.id : id);
  return (
    <div className="site-container page public-lesson">
      {!lesson && !current.error && <Loading label={t.loading} />}
      {current.error && <ErrorBanner message={t.loadError} onRetry={() => setAttempt((n) => n + 1)} />}
      {lesson && (
        <article className="post-card public-sample" aria-labelledby="public-lesson-title">
          {lesson.series && lesson.series.title && <p className="eyebrow">{t.inSeries(lesson.series.title)}</p>}
          <h1 id="public-lesson-title" className="post-title">{lesson.title}</h1>
          {lesson.summary && <p className="muted">{lesson.summary}</p>}
          {lesson.durationSeconds > 0 && <p className="muted small">{formatDuration(lesson.durationSeconds)}</p>}
          <LevelChips levels={lesson.levels} />
          {lesson.kind === 'Video' && <LessonPlayer key={lesson.id} lesson={lesson} client={client} reportProgress={false} />}
          {lesson.description && <MarkdownBody body={lesson.description} allowHeadings linkMentions={false} />}
          <footer className="post-foot">
            <span className="muted small">{t.comments(lesson.commentCount || 0)}</span>
            {isSignedIn
              ? <Link to={appPath} className="button button-primary button-sm">{t.openApp}</Link>
              : (
                <span className="badge-row">
                  <Link to="/sign-up" className="button button-primary button-sm">{t.join}</Link>
                  <Link to={'/sign-in?next=' + encodeURIComponent(appPath)} className="button button-secondary button-sm">{t.signIn}</Link>
                </span>
              )}
          </footer>
        </article>
      )}
    </div>
  );
}

export default PublicLessonPage;
