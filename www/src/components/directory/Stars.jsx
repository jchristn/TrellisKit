import { useId, useRef } from 'react';
import { REVIEW_LIMITS, formatRating } from '@trelliskit/shared';
import strings from '../../strings.js';

const r = strings.venues.rating;
const SCALE = [1, 2, 3, 4, 5];

function StarGlyphs() {
  return SCALE.map((n) => (
    <svg key={n} viewBox="0 0 24 24" width="1em" height="1em" focusable="false" aria-hidden="true">
      <path d="M12 2l3.1 6.3 6.9 1-5 4.9 1.2 6.8L12 17.8 5.8 21l1.2-6.8-5-4.9 6.9-1z" />
    </svg>
  ));
}

/**
 * Read-only stars for a rating (0 to 5, partial stars for averages). Announced as "Rated 4.5 out
 * of 5" unless `decorative` (when the surrounding text already says it).
 * @param {{ value: number|null, label?: string, decorative?: boolean, size?: 'sm'|'md'|'lg' }} props
 */
export function StarRating({ value, label, decorative = false, size = 'md' }) {
  const n = Math.max(0, Math.min(REVIEW_LIMITS.maxRating, Number(value) || 0));
  const percent = (n / REVIEW_LIMITS.maxRating) * 100;
  const a11y = decorative ? { 'aria-hidden': true } : { role: 'img', 'aria-label': label || r.outOf(formatRating(n) || '0') };
  return (
    <span className={'stars stars-' + size} {...a11y}>
      <span className="stars-base"><StarGlyphs /></span>
      <span className="stars-fill" style={{ width: percent + '%' }}><StarGlyphs /></span>
    </span>
  );
}

/**
 * Compact average and count for list rows ("4.5 (12)"), with an accessible label; nothing when
 * there are no ratings.
 * @param {{ average: number|null, count: number }} props
 */
export function RatingBadge({ average, count }) {
  const text = formatRating(average, count);
  if (!text || !(count > 0)) return null;
  return (
    <span className="rating-badge" role="img" aria-label={r.label(text, count)}>
      <StarRating value={average} decorative size="sm" />
      <span aria-hidden="true">{r.short(text, count)}</span>
    </span>
  );
}

/**
 * Star input as a radio group of 1 to 5: one tab stop, arrow keys (and Home/End) move and select,
 * each option is named "N stars", and the chosen value shows as visible text.
 * @param {{ value: number, onChange: (n: number) => void, label: string, error?: string, disabled?: boolean, placeholder?: string }} props
 */
export function StarInput({ value, onChange, label, error = '', disabled = false, placeholder = '' }) {
  const id = useId();
  const refs = useRef([]);
  const current = Number(value) || 0;
  const tabStop = current || 1;
  const choose = (n) => {
    onChange(n);
    const node = refs.current[n - 1];
    if (node) node.focus();
  };
  const onKeyDown = (e, n) => {
    const keys = {
      ArrowRight: Math.min(5, n + 1), ArrowUp: Math.min(5, n + 1),
      ArrowLeft: Math.max(1, n - 1), ArrowDown: Math.max(1, n - 1),
      Home: 1, End: 5
    };
    if (keys[e.key] === undefined) return;
    e.preventDefault();
    choose(keys[e.key]);
  };
  return (
    <div className="field star-input-field">
      <span className="label-text" id={id + '-label'}>{label}</span>
      <div className="star-input-row">
        <div className="star-input" role="radiogroup" aria-labelledby={id + '-label'} aria-invalid={Boolean(error)} aria-describedby={error ? id + '-error' : undefined}>
          {SCALE.map((n) => (
            <button
              key={n}
              ref={(node) => { refs.current[n - 1] = node; }}
              type="button"
              role="radio"
              aria-checked={current === n}
              aria-label={r.stars(n)}
              title={r.stars(n)}
              tabIndex={n === tabStop ? 0 : -1}
              className={'star-option' + (n <= current ? ' is-on' : '')}
              onClick={() => choose(n)}
              onKeyDown={(e) => onKeyDown(e, n)}
              disabled={disabled}
            >
              <svg viewBox="0 0 24 24" width="1em" height="1em" focusable="false" aria-hidden="true"><path d="M12 2l3.1 6.3 6.9 1-5 4.9 1.2 6.8L12 17.8 5.8 21l1.2-6.8-5-4.9 6.9-1z" /></svg>
            </button>
          ))}
        </div>
        <span className="star-input-value small" aria-hidden="true">{current ? r.stars(current) : placeholder}</span>
      </div>
      {error && <span className="field-error" id={id + '-error'} role="alert">{error}</span>}
    </div>
  );
}

export default StarRating;
