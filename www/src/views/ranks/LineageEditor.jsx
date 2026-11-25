/**
 * Lineage display and editor for one Rank field.
 * - `LineageList` props: `entries` (LineageEntryView[]), `field` (Rank field), `me` (caller, for
 *   Verify on entries naming them), `client`, `onVerified(entry)`.
 * - `LineageEditor` props: `client`, `field`, `entries` (current LineageEntryView[] of the field),
 *   `onSaved(response)` with the PUT /me/lineage response.
 */
import { useId, useState } from 'react';
import { RANK_LIMITS, rankText } from '@trelliskit/shared';
import strings from '../../strings.js';
import { Badge } from '../../components/Badges.jsx';
import EntityPicker, { memberItem } from '../../components/admin/EntityPicker.jsx';
import { formatDate } from '../../utils/format.js';
import { authorName, errorText } from '../../utils/engagement.js';

const t = strings.ranks.lineage;
const e = strings.ranks.errors;

export function LineageList({ entries = [], field, me = null, client = null, onVerified = null, canVerifyAll = false }) {
  const [busy, setBusy] = useState(null);
  const [error, setError] = useState('');
  if (!entries.length) return <p className="muted small">{t.empty}</p>;
  const verify = async (entry) => {
    setBusy(entry.id);
    setError('');
    try {
      const updated = await client.verifyLineage(entry.id);
      if (onVerified) onVerified(updated && updated.id ? updated : { ...entry, verified: true });
    } catch (err) {
      setError(errorText(err, { ...e, ...strings.ranks.admin.errors }, e.generic));
    } finally {
      setBusy(null);
    }
  };
  return (
    <>
      {error && <p className="field-error" role="alert">{error}</p>}
      <ol className="rank-lineage">
        {entries.map((entry) => {
          const promoter = entry.promotedBy ? authorName(entry.promotedBy) : entry.promotedByName || '';
          const mine = Boolean(me && entry.promotedByUserId && entry.promotedByUserId === me.id);
          return (
            <li key={entry.id} data-entry={entry.id}>
              <strong>{entry.optionLabel || rankText(field, entry.optionKey, 0)}</strong>
              {promoter && <span> · {t.promoter}: {promoter}</span>}
              {entry.promotedUtc && <span className="muted small"> · {formatDate(entry.promotedUtc)}</span>}{' '}
              <Badge tone={entry.verified ? 'success' : 'neutral'}>{entry.verified ? t.verified : t.unverified}</Badge>
              {!entry.verified && client && (mine || canVerifyAll) && (
                <>
                  {mine && <span className="muted small"> {t.pendingForYou}</span>}{' '}
                  <button type="button" className="button button-secondary button-sm" onClick={() => verify(entry)} disabled={busy === entry.id}>{t.verify}</button>
                </>
              )}
            </li>
          );
        })}
      </ol>
    </>
  );
}

let rowKey = 0;
const toRow = (entry) => ({
  key: 'l' + (rowKey += 1),
  optionKey: entry.optionKey || '',
  member: entry.promotedByUserId ? [{ id: entry.promotedByUserId, label: entry.promotedBy ? authorName(entry.promotedBy) : entry.promotedByUserId }] : [],
  name: entry.promotedByName || '',
  date: entry.promotedUtc ? String(entry.promotedUtc).slice(0, 10) : ''
});

/** PUT /me/lineage body from editor rows. */
export function lineageBody(fieldKey, rows) {
  return {
    fieldKey,
    entries: rows.map((row) => {
      const entry = { optionKey: row.optionKey };
      if (row.member.length) entry.promotedByUserId = row.member[0].id;
      else if (row.name.trim()) entry.promotedByName = row.name.trim();
      if (row.date) entry.promotedUtc = row.date + 'T00:00:00Z';
      return entry;
    })
  };
}

export function LineageEditor({ client, field, entries = [], onSaved, onCancel }) {
  const id = useId();
  const [rows, setRows] = useState(() => entries.map(toRow));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const options = (field.options || []).filter(Boolean);
  const update = (key, patch) => setRows((list) => list.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  const search = async (term, signal) => ((await client.searchMembers({ search: term, maxResults: 10 }, signal)).objects || []).map(memberItem).filter(Boolean);
  const save = async (event) => {
    event.preventDefault();
    if (rows.some((row) => !row.optionKey || (!row.member.length && !row.name.trim()))) { setError(t.promoterRequired); return; }
    if (rows.some((row) => row.name.trim().length > RANK_LIMITS.lineageNameMax)) { setError(t.nameTooLong(RANK_LIMITS.lineageNameMax)); return; }
    setBusy(true);
    setError('');
    try {
      onSaved(await client.putMyLineage(lineageBody(field.key, rows)));
    } catch (err) {
      setError(errorText(err, e, e.generic));
    } finally {
      setBusy(false);
    }
  };
  return (
    <form className="stack" onSubmit={save} noValidate aria-label={t.edit}>
      <p className="muted small">{t.promoterHint} {t.limit(RANK_LIMITS.maxLineageEntries)}</p>
      {error && <p className="field-error" role="alert">{error}</p>}
      {rows.map((row, index) => (
        <fieldset key={row.key} className="fieldset">
          <legend className="label-text">{t.rank} {index + 1}</legend>
          <div className="form-grid">
            <label className="field"><span className="label-text">{t.rank}</span>
              <select value={row.optionKey} onChange={(ev) => update(row.key, { optionKey: ev.target.value })} disabled={busy}>
                <option value="" />
                {options.map((o) => <option key={o.key} value={o.key}>{o.label || o.key}</option>)}
              </select>
            </label>
            <label className="field"><span className="label-text">{t.date}</span>
              <input type="date" value={row.date} onChange={(ev) => update(row.key, { date: ev.target.value })} disabled={busy} />
            </label>
          </div>
          <EntityPicker id={id + '-m-' + row.key} label={t.promoterMember} value={row.member} onChange={(items) => update(row.key, { member: items.slice(0, 1), name: items.length ? '' : row.name })} search={search} placeholder={t.searchMember} disabled={busy || Boolean(row.name.trim())} />
          <label className="field"><span className="label-text">{t.promoterName}</span>
            <input type="text" maxLength={RANK_LIMITS.lineageNameMax} value={row.name} onChange={(ev) => update(row.key, { name: ev.target.value })} disabled={busy || row.member.length > 0} />
          </label>
          <button type="button" className="button button-danger-outline button-sm" onClick={() => setRows((list) => list.filter((r) => r.key !== row.key))} disabled={busy}>{t.remove}</button>
        </fieldset>
      ))}
      <div className="form-actions">
        <button type="button" className="button button-secondary" onClick={() => setRows((list) => [...list, toRow({})])} disabled={busy || rows.length >= RANK_LIMITS.maxLineageEntries}>{t.add}</button>
        {onCancel && <button type="button" className="button button-ghost" onClick={onCancel} disabled={busy}>{t.cancel}</button>}
        <button type="submit" className="button button-primary" disabled={busy}>{busy ? t.saving : t.save}</button>
      </div>
    </form>
  );
}

export default LineageEditor;
