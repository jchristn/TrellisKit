import { Link } from 'react-router-dom';
import {
  cardFields, formatProfileValue, formatRankValue, historyEntryParts, optionColor, profilePageFields, urlHost
} from '@trelliskit/shared';
import strings from '../../strings.js';
import { memberPath } from '../../utils/engagement.js';
import { LOCALE } from '../../utils/format.js';
import VerificationBadge from '../../views/ranks/VerificationBadge.jsx';

const s = strings.profileFields;
const words = { yes: s.yes, no: s.no, locale: LOCALE };

/** A small color dot (nothing without a color). */
export function RankSwatch({ color, label = '' }) {
  if (!color) return null;
  return <span className="rank-swatch" style={{ backgroundColor: color }} role={label ? 'img' : undefined} aria-label={label || undefined} aria-hidden={label ? undefined : 'true'} />;
}

/**
 * A rank value with its color swatch and configured text, for example "Gold, 2 stars".
 * `verification` ('verified' | 'selfReported' | null) adds the verification label when ranks are on.
 */
export function RankBadge({ field, value, compact = false, verification = null }) {
  const text = formatRankValue(field, value);
  if (!text) return null;
  const color = optionColor(field, value && value.key);
  const badge = verification ? <> <VerificationBadge badge={verification} fieldLabel={field.label} /></> : null;
  if (compact) {
    return (
      <span className="rank-chip" title={field.label + ': ' + text}>
        {color && <span className="rank-bar" style={{ backgroundColor: color }} aria-hidden="true" />}
        <span>{text}</span>
        {badge}
      </span>
    );
  }
  return <span className="rank-text"><RankSwatch color={color} /><span>{text}</span>{badge}</span>;
}

function HistoryList({ field, value, allFields }) {
  const entries = Array.isArray(value) ? value : [];
  return (
    <ol className="history-view">
      {entries.map((entry, index) => {
        const parts = historyEntryParts(field, entry, { fields: allFields, ...words });
        return (
          <li key={entry.id || index} className="history-entry">
            {parts.map((part, i) => {
              let node = part.text;
              if (part.type === 'RankValue') node = <span className="rank-text"><RankSwatch color={part.color} /><strong>{part.text}</strong></span>;
              else if (part.type === 'Member' && part.userId) node = <Link to={memberPath(part.userId)}>{part.text}</Link>;
              else if (part.type === 'Date') node = <span className="muted small">{part.text}</span>;
              return (
                <span key={part.key} className={'history-part history-part-' + part.type.toLowerCase()}>
                  {i > 0 && part.type === 'Member' && part.label ? <span className="muted">{' ' + part.label + ' '}</span> : i > 0 ? ' ' : ''}
                  {node}
                </span>
              );
            })}
          </li>
        );
      })}
    </ol>
  );
}

/** One field's value rendered per its type (section 9.11 `display`). */
export function ProfileFieldValue({ field, value, allFields = [], verification = null }) {
  switch (field.type) {
    case 'Rank': return <RankBadge field={field} value={value} verification={verification} />;
    case 'History': return <HistoryList field={field} value={value} allFields={allFields} />;
    case 'Url':
      return <a href={value} rel="nofollow ugc noopener noreferrer" target="_blank">{urlHost(value)}</a>;
    case 'Email': return <a href={'mailto:' + value}>{value}</a>;
    case 'Phone': return <a href={'tel:' + value}>{value}</a>;
    case 'LongText': return <span className="preserve-lines">{value}</span>;
    default: return <>{formatProfileValue(field, value, { fields: allFields, ...words })}</>;
  }
}

/**
 * The profile page body: every field with `showOnProfile` and a value, in profile order, as
 * labeled rows. History fields take the full width under the grid.
 * @param {{ fields: object[], profile: object, allFields?: object[], emptyText?: string, showEmpty?: object[], rankBadges?: object }} props
 *   `showEmpty` lists fields to show even without a value (with `emptyText`), for staff views.
 *   `rankBadges` maps Rank field keys to 'verified' or 'selfReported' (from `rankBadgeMap`).
 */
export function ProfileFieldList({ fields, profile, allFields, emptyText = null, showEmpty = null, rankBadges = null }) {
  const every = allFields || fields;
  const shown = showEmpty || profilePageFields(fields, profile);
  const flat = shown.filter((f) => f.type !== 'History');
  const lists = shown.filter((f) => f.type === 'History');
  const value = (f) => (profile ? profile[f.key] : null);
  const present = (f) => value(f) !== null && value(f) !== undefined && !(Array.isArray(value(f)) && value(f).length === 0);
  if (!shown.length) return emptyText ? <p className="muted">{emptyText}</p> : null;
  return (
    <div className="profile-field-list">
      {flat.length > 0 && (
        <dl className="kv-grid">
          {flat.map((f) => (
            <div className="kv-item" key={f.key} data-field={f.key}>
              <dt>{f.label}</dt>
              <dd>{present(f) ? <ProfileFieldValue field={f} value={value(f)} allFields={every} verification={rankBadges ? rankBadges[f.key] || null : null} /> : <span className="muted">{emptyText || s.notSet}</span>}</dd>
            </div>
          ))}
        </dl>
      )}
      {lists.map((f) => (
        <div key={f.key} className="profile-history" data-field={f.key}>
          <h2 className="label-text">{f.label}</h2>
          {present(f) ? <ProfileFieldValue field={f} value={value(f)} allFields={every} /> : <p className="muted">{emptyText || s.notSet}</p>}
        </div>
      ))}
    </div>
  );
}

/**
 * Compact card fields of a `UserSummary` (`showOnCard` with a value, at most `max`), inline:
 * a rank as a color bar plus its short text, other fields as text chips. `rankBadges` maps Rank
 * field keys to 'verified' or 'selfReported' (from `rankBadgeMap`) when ranks are on.
 */
export function CardFields({ fields, profile, max, allFields, rankBadges = null }) {
  const list = cardFields(fields, profile, max);
  if (!list.length) return null;
  return (
    <span className="card-fields">
      {list.map((f, i) => (
        <span key={f.key} className="card-field" data-field={f.key}>
          {i > 0 && <span aria-hidden="true" className="card-field-sep">·</span>}
          {f.type === 'Rank'
            ? <RankBadge field={f} value={profile[f.key]} compact verification={rankBadges ? rankBadges[f.key] || null : null} />
            : <span title={f.label}>{formatProfileValue(f, profile[f.key], { fields: allFields || fields, ...words })}</span>}
        </span>
      ))}
    </span>
  );
}

export default ProfileFieldList;
