/**
 * A hand-rolled SVG progress bar.
 * Props: `value` (number done), `max` (total; 0 draws an empty bar), `label` (accessible name),
 * `height` (pixels, default 8).
 */
export function ProgressBar({ value = 0, max = 0, label, height = 8 }) {
  const total = Math.max(0, Number(max) || 0);
  const done = Math.min(Math.max(0, Number(value) || 0), total);
  const percent = total ? Math.round((done / total) * 100) : 0;
  return (
    <svg className="rank-progress" width="100%" height={height} viewBox={'0 0 100 ' + height} preserveAspectRatio="none" role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={total} aria-valuenow={done} aria-valuetext={percent + '%'}>
      <rect x="0" y="0" width="100" height={height} rx={height / 2} fill="var(--color-surface-alt)" stroke="var(--color-border)" strokeWidth="0.5" vectorEffect="non-scaling-stroke" />
      {percent > 0 && <rect x="0" y="0" width={percent} height={height} rx={height / 2} fill="var(--color-accent)" />}
    </svg>
  );
}

export default ProgressBar;
