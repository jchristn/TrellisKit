/**
 * "Your rank" card for one Rank field (Training Overview and Progress, My rank).
 * Props:
 * - `rank` EffectiveRankView from GET /me/rank `fields[].rank` (`fieldLabel`, `rankLabel`, `color`,
 *   `verified`, `source`, `awarder`, `awardedUtc`).
 * - `next` RankNextView or null (`fields[].next`); shows the next rank and its requirement checks.
 * - `showVerificationLabels` boolean (GET /me/rank `showVerificationLabels`; default true).
 * - `to` optional link target for the card title (for example '/app/rank').
 * - `compact` boolean: hide the requirement checks (Overview).
 */
import { Link } from 'react-router-dom';
import { eligibilityDisplay, verificationBadge } from '@trelliskit/shared';
import strings from '../../strings.js';
import { RankSwatch } from '../../components/profile/ProfileFieldDisplay.jsx';
import { formatDate } from '../../utils/format.js';
import { authorName } from '../../utils/engagement.js';
import VerificationBadge from './VerificationBadge.jsx';
import ProgressBar from './ProgressBar.jsx';

const t = strings.ranks;

export function EligibilityChecks({ next }) {
  const display = eligibilityDisplay(next);
  if (display.state === 'highest') return <p className="muted small">{t.highest}</p>;
  if (display.state === 'noRequirements') return <p className="muted small">{t.noRequirements}</p>;
  return (
    <div className="stack rank-checks">
      <p className="small">{display.state === 'eligible' ? t.eligible : t.inProgress}</p>
      <ul className="rank-check-list">
        {display.checks.map((check) => {
          const text = t.checks[check.key](check.current, check.required);
          return (
            <li key={check.key} data-check={check.key}>
              <span className="small">{text}</span>{' '}
              <span className={'small ' + (check.met ? 'success-text' : 'muted')}>{check.met ? t.met : t.notMet}</span>
              <ProgressBar value={check.current || 0} max={check.required} label={text} />
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export function RankCard({ rank, next = null, showVerificationLabels = true, to = null, compact = false }) {
  if (!rank) return null;
  const badge = verificationBadge(rank, showVerificationLabels);
  const heading = rank.fieldLabel || t.title;
  return (
    <section className="panel rank-card" aria-label={heading} data-field={rank.fieldKey}>
      <h2 className="section-title">{to ? <Link to={to}>{heading}</Link> : heading}</h2>
      {rank.optionKey ? (
        <p className="rank-card-current">
          <span className="rank-text"><RankSwatch color={rank.color} /><strong>{rank.rankLabel || rank.optionLabel}</strong></span>{' '}
          <VerificationBadge badge={badge} fieldLabel={heading} />
        </p>
      ) : (
        <p className="muted">{t.noRank} <Link to="/app/profile">{t.setRank}</Link></p>
      )}
      {rank.verified && rank.awardedUtc && (
        <p className="muted small">{rank.awarder ? t.awardedBy(authorName(rank.awarder), formatDate(rank.awardedUtc)) : t.awardedOn(formatDate(rank.awardedUtc))}</p>
      )}
      {next && <p className="small">{t.next}: <strong>{next.rankLabel || next.optionLabel}</strong></p>}
      {!compact && <EligibilityChecks next={next} />}
    </section>
  );
}

export default RankCard;
