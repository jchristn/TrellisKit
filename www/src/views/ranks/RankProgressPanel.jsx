/**
 * Rank progress for the Training Progress section: rank history, attendance count, and curriculum
 * completion bars.
 * Props:
 * - `rank` GET /me/rank response (`history`, `historyTotal`), or null.
 * - `attendanceTotal` number of check-ins (for example `totalRecords` of POST /me/attendance/enumerate), or null to hide.
 * - `curricula` CurriculumView[] with `progress` (from POST /curricula/enumerate), or null to hide.
 */
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { formatDate } from '../../utils/format.js';
import { authorName } from '../../utils/engagement.js';
import ProgressBar from './ProgressBar.jsx';
import { curriculumPath } from './CurriculumCard.jsx';

const t = strings.ranks;

export function RankHistoryList({ history = [], total = null }) {
  if (!history.length) return <p className="muted small">{t.historyEmpty}</p>;
  return (
    <>
      <ol className="rank-history">
        {history.map((row) => (
          <li key={row.id} data-action={row.action}>
            <span>{t.historyRow(t.actions[row.action] || row.action, row.rankLabel || row.optionLabel || row.optionKey, formatDate(row.createdUtc))}</span>
            {row.actor && <span className="muted small"> {t.historyBy(authorName(row.actor))}</span>}
            {row.note && <p className="muted small preserve-lines">{row.note}</p>}
            {row.reason && <p className="muted small preserve-lines">{row.reason}</p>}
          </li>
        ))}
      </ol>
      {total !== null && total > history.length && <p className="muted small">{t.historyMore(history.length, total)}</p>}
    </>
  );
}

export function RankProgressPanel({ rank = null, attendanceTotal = null, curricula = null }) {
  return (
    <div className="stack rank-progress-panel">
      {rank && (
        <section className="panel" aria-labelledby="rank-history-title">
          <h2 id="rank-history-title" className="section-title">{t.historyTitle}</h2>
          <RankHistoryList history={rank.history || []} total={rank.historyTotal ?? null} />
        </section>
      )}
      {attendanceTotal !== null && attendanceTotal !== undefined && (
        <section className="panel" aria-labelledby="rank-attendance-title">
          <h2 id="rank-attendance-title" className="section-title">{t.attendanceTitle}</h2>
          <p>{t.attendanceCount(attendanceTotal)}</p>
        </section>
      )}
      {Array.isArray(curricula) && (
        <section className="panel" aria-labelledby="rank-curricula-title">
          <h2 id="rank-curricula-title" className="section-title">{t.curriculaTitle}</h2>
          {curricula.length === 0 ? <p className="muted small">{t.curriculaEmpty}</p> : (
            <ul className="rank-curricula">
              {curricula.map((c) => {
                const done = (c.progress && c.progress.completed) || 0;
                const total = (c.progress && c.progress.total) ?? c.itemCount ?? 0;
                const text = t.curriculumProgress(done, total);
                return (
                  <li key={c.id}>
                    <Link to={curriculumPath(c.id)}>{c.name}</Link> <span className="muted small">{text}</span>
                    <ProgressBar value={done} max={total} label={c.name + ': ' + text} />
                  </li>
                );
              })}
            </ul>
          )}
        </section>
      )}
    </div>
  );
}

export default RankProgressPanel;
