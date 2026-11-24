import { Link } from 'react-router-dom';
import { formatDuration, formatTotalDuration, isLockedItem, progressPercent } from '@trelliskit/shared';
import strings from '../../strings.js';
import { appPaths, mediaThumbUrl } from '../../utils/phase4.js';
import Icon from '../Icon.jsx';
import { Badge } from '../Badges.jsx';
import { LockBadge } from '../AccessPrompt.jsx';

const t = strings.library;

/** A horizontal progress bar (0 to 100). */
export function ProgressBar({ percent, label = t.progressLabel }) {
  const value = Math.max(0, Math.min(100, Math.round(Number(percent) || 0)));
  return (
    <div className="p4-progress" role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={value}>
      <span style={{ width: value + '%' }} />
    </div>
  );
}

/** Level chips for a series or a lesson (`levels` carry `{ key, label, color }`). */
export function LevelChips({ levels }) {
  if (!levels || !levels.length) return null;
  return (
    <span className="badge-row">
      {levels.map((level) => (
        <span key={level.key} className="chip"><span className="p4-chip-dot" style={level.color ? { background: level.color } : undefined} aria-hidden="true" />{level.label || level.key}</span>
      ))}
    </span>
  );
}

/** A series card (SeriesSummary) for grids and rails; locked stubs show the lock badge. */
export function SeriesCard({ series }) {
  const locked = isLockedItem(series);
  const cover = mediaThumbUrl(series.cover);
  const progress = series.myProgress || null;
  return (
    <Link to={appPaths.series(series.slug || series.id)} className={'p4-card' + (locked ? ' p4-card-locked' : '')} data-testid={'series-card-' + series.id}>
      {cover ? <img className="p4-card-cover" src={cover} alt="" loading="lazy" /> : <div className="p4-card-cover" aria-hidden="true" />}
      <span className="p4-card-title">{series.title}</span>
      {series.summary && <span className="muted small">{series.summary}</span>}
      <span className="p4-card-meta">
        <span>{t.lessonCount(series.lessonCount || 0)}</span>
        {series.totalDurationSeconds > 0 && <span>{formatTotalDuration(series.totalDurationSeconds)}</span>}
        {locked && <LockBadge feature="library" />}
        {series.isFollowed && <Badge tone="info">{t.following}</Badge>}
      </span>
      <LevelChips levels={series.levels} />
      {progress && progress.percent > 0 && (
        <>
          <ProgressBar percent={progress.percent} />
          <span className="muted small">{t.percentDone(progress.percent)}</span>
        </>
      )}
    </Link>
  );
}

/** A lesson row (LessonSummary) with position, duration, progress, and lock or sample badges. */
export function LessonRow({ lesson, progress = null, showSeries = false }) {
  const locked = isLockedItem(lesson);
  const p = progress || lesson.myProgress || null;
  const duration = (lesson.video && lesson.video.durationSeconds) || lesson.durationSeconds;
  return (
    <Link to={appPaths.lesson(lesson.id)} className={'p4-row' + (locked ? ' p4-card-locked' : '')} data-testid={'lesson-row-' + lesson.id}>
      {lesson.position ? <span className="p4-row-index" aria-hidden="true">{lesson.position}</span> : <Icon name={lesson.kind === 'Article' ? 'book' : 'video'} size={18} />}
      <span className="p4-row-main">
        <span className="p4-row-title">{lesson.title}</span>
        <span className="p4-card-meta">
          {showSeries && lesson.series && <span>{lesson.series.title}</span>}
          <span>{t.kinds[lesson.kind] || lesson.kind}</span>
          {duration > 0 && <span>{formatDuration(duration)}</span>}
          {lesson.audience === 'Public' && !locked && <Badge tone="success">{t.sample}</Badge>}
          {locked && <LockBadge feature="library" />}
          {p && p.completed && <Badge tone="success">{t.completed}</Badge>}
        </span>
        {p && !p.completed && progressPercent(p) > 0 && <ProgressBar percent={progressPercent(p)} />}
      </span>
      <Icon name="chevronRight" size={16} />
    </Link>
  );
}
