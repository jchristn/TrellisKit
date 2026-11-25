import { useCallback, useRef, useState } from 'react';
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom';
import { formatDuration, isLockedItem, libraryConfig, lockedRequirement } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import ReactionBar from '../../components/feed/ReactionBar.jsx';
import CommentThread from '../../components/feed/CommentThread.jsx';
import ReportDialog from '../../components/feed/ReportDialog.jsx';
import { AccessPromptCard, useAccessPrompt } from '../../components/AccessPrompt.jsx';
import LessonPlayer from '../../components/library/LessonPlayer.jsx';
import { LevelChips, ProgressBar } from '../../components/library/LibraryCards.jsx';
import { errorText } from '../../utils/engagement.js';
import { appPaths, mediaFileUrl, phase4Errors } from '../../utils/phase4.js';
import SaveButton from '../../components/SaveButton.jsx';
import { LessonTechniques } from '../training/LessonTechniques.jsx';
import { trainingPaths } from '../training/trainingModel.js';

const t = strings.library;

function Resources({ resources }) {
  if (!resources || !resources.length) return null;
  return (
    <section className="panel" aria-labelledby="lesson-resources">
      <h2 id="lesson-resources" className="section-title">{t.resources}</h2>
      <ul className="bullet-list">
        {resources.map((r) => {
          const href = r.kind === 'Document' ? mediaFileUrl(r.media) : r.kind === 'Link' && /^https:\/\//i.test(r.url || '') ? r.url : null;
          return (
            <li key={r.id}>
              {href ? <a href={href} target="_blank" rel="noopener noreferrer" download={r.kind === 'Document' ? true : undefined}>{r.title}</a> : <strong>{r.title}</strong>}
              {' '}<span className="muted small">{t.resourceActions[r.kind] || r.kind}</span>
              {r.kind === 'Reference' && r.text && <div className="muted small">{r.text}</div>}
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function Neighbor({ item, label, icon }) {
  if (!item) return <span />;
  return (
    <Link to={appPaths.lesson(item.id)} className="button button-secondary button-sm">
      {icon === 'chevronLeft' && <Icon name="chevronLeft" size={14} />}
      <span>{label}: {item.title}{item.canOpen === false ? ' (' + t.lockedNeighbor + ')' : ''}</span>
      {icon === 'chevronRight' && <Icon name="chevronRight" size={14} />}
    </Link>
  );
}

/**
 * /app/lessons/:id (sections 5.1 to 5.5): the player (or the article body), progress with mark
 * complete and reset, chapters, resources, captions, previous and next lessons, reactions,
 * comments on the lesson's `commentTarget`, and report. Locked stubs show the access prompt.
 */
function LessonView() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const focusId = searchParams.get('comment');
  const { client, me } = useAuth();
  const { property } = useProperty();
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const config = libraryConfig(property);
  const fetcher = useCallback((signal) => client.getLesson(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  const [override, setOverride] = useState(null);
  const [reporting, setReporting] = useState(false);
  const [busy, setBusy] = useState(false);
  const seekRef = useRef(null);
  const inHub = useLocation().pathname.startsWith(trainingPaths.overview + '/');
  const base = query.data;
  const lesson = base && override && override.id === base.id ? { ...base, ...override } : base;
  useDocumentMeta(strings.member.metaTitle(lesson ? lesson.title : t.title));

  const back = lesson && lesson.series
    ? <Link to={(inHub ? trainingPaths.series : appPaths.series)(lesson.series.slug || lesson.series.id)} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.backToSeries(lesson.series.title)}</span></Link>
    : <Link to={inHub ? trainingPaths.segment('courses') : appPaths.library} className="back-link small"><Icon name="chevronLeft" size={14} /><span>{t.back}</span></Link>;

  if (query.error && query.error.status === 404) {
    return <div className="member-view">{back}<EmptyState icon="book" title={t.lessonNotFound}>{t.notFoundBody}</EmptyState></div>;
  }
  if (!lesson) {
    return <div className="member-view">{back}{query.loading && <Loading />}<ErrorBanner message={query.error ? errorText(query.error, phase4Errors(), t.loadError) : ''} onRetry={query.reload} /></div>;
  }

  const locked = isLockedItem(lesson);
  const viewer = lesson.viewer || {};
  const progress = lesson.myProgress;
  const setProgress = (p) => setOverride((o) => ({ ...(o || {}), id: lesson.id, myProgress: p }));
  const run = async (fn, message) => {
    setBusy(true);
    try {
      const result = await fn();
      if (message) toast.success(message);
      return result;
    } catch (err) {
      if (!accessPrompt.handle(err)) toast.error(errorText(err, phase4Errors()));
      return null;
    } finally {
      setBusy(false);
    }
  };
  const target = lesson.commentTarget || { type: 'Lesson', id: lesson.id };

  return (
    <div className="member-view lesson-view">
      {back}
      <header className="page-header">
        <div className="page-header-text">
          <h1>{lesson.title}</h1>
          {lesson.summary && <p className="muted">{lesson.summary}</p>}
          <span className="p4-card-meta">
            <span>{t.kinds[lesson.kind] || lesson.kind}</span>
            {lesson.durationSeconds > 0 && <span>{formatDuration(lesson.durationSeconds)}</span>}
            {lesson.audience === 'Public' && <Badge tone="success">{t.sample}</Badge>}
            {progress && progress.completed && <Badge tone="success">{t.completed}</Badge>}
          </span>
          <LevelChips levels={lesson.levels} />
        </div>
        <div className="page-header-actions"><SaveButton targetType="Lesson" targetId={lesson.id} title={lesson.title} /></div>
      </header>

      {locked ? (
        <AccessPromptCard required={lockedRequirement(lesson)} feature="library" title={t.lockedLesson} />
      ) : (
        <div className="p4-detail">
          <div className="p4-section">
            {lesson.kind === 'Video' && (
              <LessonPlayer
                key={lesson.id}
                lesson={base}
                client={client}
                intervalSeconds={config.progressIntervalSeconds}
                resumeTailSeconds={config.resumeTailSeconds}
                reportProgress={Boolean(me) && viewer.canPlay !== false}
                onProgress={setProgress}
                seekRef={seekRef}
              />
            )}
            {lesson.description && <section className="panel"><MarkdownBody body={lesson.description} allowHeadings linkMentions={false} /></section>}
            <div className="p4-player-bar">
              <Neighbor item={lesson.previous} label={t.previous} icon="chevronLeft" />
              <Neighbor item={lesson.next} label={t.next} icon="chevronRight" />
            </div>
          </div>
          <aside className="p4-detail-side">
            {me && (
              <section className="panel" aria-labelledby="lesson-progress">
                <h2 id="lesson-progress" className="section-title">{t.progressLabel}</h2>
                {progress && <ProgressBar percent={progress.percent} />}
                {progress && <p className="muted small">{t.percentDone(progress.percent || 0)}</p>}
                <div className="form-actions-start">
                  {progress && progress.completed ? (
                    <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={async () => { const p = await run(() => client.uncompleteLesson(lesson.id), t.uncompletedToast); if (p) setProgress(p); }}>{t.markIncomplete}</button>
                  ) : (
                    <button type="button" className="button button-primary button-sm" disabled={busy} onClick={async () => { const p = await run(() => client.completeLesson(lesson.id), t.completedToast); if (p) setProgress(p); }}><Icon name="check" size={14} /><span>{t.markComplete}</span></button>
                  )}
                  {progress && <button type="button" className="link-button small" disabled={busy} onClick={async () => { const ok = await run(() => client.resetLessonProgress(lesson.id), t.progressReset); if (ok !== null) setProgress(null); }}>{t.resetProgress}</button>}
                </div>
                <p className="muted small">{t.historyNote}</p>
              </section>
            )}
            <LessonTechniques client={client} lessonId={lesson.id} onSeek={lesson.kind === 'Video' ? (seconds) => { if (!seekRef.current) return false; seekRef.current(seconds); return true; } : null} />
            <Resources resources={lesson.resources} />
            {(lesson.captions || []).length > 0 && <p className="muted small">{t.captionsNote}</p>}
            {me && viewer.canReport !== false && <button type="button" className="link-button small" onClick={() => setReporting(true)}><Icon name="flag" size={12} /> {t.report}</button>}
          </aside>
        </div>
      )}

      {!locked && (
        <section className="p4-section" aria-labelledby={lesson.commentsEnabled === false ? 'lesson-comments' : 'comments-title'}>
          <footer className="post-foot">
            <ReactionBar target={target} value={lesson} canReact={Boolean(me) && viewer.canReact !== false} onChange={(fields) => setOverride((o) => ({ ...(o || {}), id: lesson.id, ...fields }))} />
          </footer>
          {lesson.commentsEnabled === false ? <><h2 id="lesson-comments">{t.comments}</h2><Banner tone="info">{t.commentsOff}</Banner></> : (
            <CommentThread key={lesson.id + ':' + (focusId || '')} post={lesson} focusId={focusId} canComment={Boolean(me) && viewer.canComment !== false} viewAllPath={appPaths.lesson(lesson.id)} accessFeature="library" />
          )}
        </section>
      )}
      {reporting && <ReportDialog target={{ type: 'Lesson', id: lesson.id }} onClose={() => setReporting(false)} />}
    </div>
  );
}

export default LessonView;
