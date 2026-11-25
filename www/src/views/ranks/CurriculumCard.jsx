/**
 * A curriculum summary card with an SVG progress bar (Training Overview and Progress, Curricula).
 * Props:
 * - `curriculum` CurriculumView (`id`, `name`, `description`, `itemCount`, `progress { completed, total, percent }`).
 * - `to` link target (default `/app/curricula/{id}`).
 */
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import ProgressBar from './ProgressBar.jsx';

const t = strings.ranks;

export function curriculumPath(id) {
  return '/app/curricula/' + encodeURIComponent(id);
}

export function CurriculumCard({ curriculum, to = null }) {
  if (!curriculum) return null;
  const progress = curriculum.progress || {};
  const total = progress.total ?? curriculum.itemCount ?? 0;
  const done = progress.completed || 0;
  const text = t.curriculumProgress(done, total);
  return (
    <section className="panel curriculum-card" aria-label={curriculum.name}>
      <h2 className="section-title"><Link to={to || curriculumPath(curriculum.id)}>{curriculum.name}</Link></h2>
      {curriculum.description && <p className="muted small">{curriculum.description}</p>}
      <p className="small">{text}</p>
      <ProgressBar value={done} max={total} label={curriculum.name + ': ' + text} />
    </section>
  );
}

export default CurriculumCard;
