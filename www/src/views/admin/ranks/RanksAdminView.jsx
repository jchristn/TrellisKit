/**
 * `/admin/ranks`: verified rank administration for rank staff (SystemAdmin and RankAwarder):
 * awards (filtered list, award form with per-option level choices, revoke), eligibility,
 * requirements (SystemAdmin edits), and the lineage verification lookup. Shown only when
 * `features.ranks` is on.
 */
import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { RANK_LIMITS, canAwardRank, eligibilityDisplay, rankLevelChoices, rankText } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import useCursorList from '../../../hooks/useCursorList.js';
import PageHeader from '../../../components/PageHeader.jsx';
import Modal from '../../../components/Modal.jsx';
import EntityPicker from '../../../components/admin/EntityPicker.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../../components/States.jsx';
import { authorName } from '../../../utils/engagement.js';
import { formatShortDate } from '../../../utils/format.js';
import { LineageList } from '../../ranks/LineageEditor.jsx';
import { FieldSelect, Tabs, rankAdminError, useMemberSearch, useRankFields } from './rankAdminParts.jsx';

const a = strings.ranks.admin;
const r = strings.ranks;

function OptionSelect({ id, label, field, value, onChange, anyLabel = null, disabled = false }) {
  return (
    <label className="field" htmlFor={id}>
      <span className="label-text">{label}</span>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled}>
        {anyLabel !== null && <option value="">{anyLabel}</option>}
        {(field.options || []).filter(Boolean).map((o) => <option key={o.key} value={o.key}>{o.label || o.key}</option>)}
      </select>
    </label>
  );
}

/** Award form: member, option, level choices from the option's own maximum, note, optional event. */
export function AwardForm({ client, field, initial = {}, awarder, onDone, onCancel, search }) {
  const id = useId();
  const [member, setMember] = useState(initial.member ? [initial.member] : []);
  const [optionKey, setOptionKey] = useState(initial.optionKey || ((field.options || [])[0] || {}).key || '');
  const [level, setLevel] = useState(initial.level || 0);
  const [note, setNote] = useState('');
  const [eventId, setEventId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const choices = rankLevelChoices(field, optionKey);
  const allowed = canAwardRank(field, awarder, null, optionKey, level);
  const submit = async (event) => {
    event.preventDefault();
    if (!member.length || !optionKey) { setError(a.errors.ValidationFailed); return; }
    setBusy(true);
    setError('');
    try {
      const body = { userId: member[0].id, fieldKey: field.key, optionKey, level: Number(level) };
      if (note.trim()) body.note = note.trim();
      if (eventId.trim()) body.eventId = eventId.trim();
      onDone(await client.awardRank(body));
    } catch (err) {
      setError(rankAdminError(err));
    } finally {
      setBusy(false);
    }
  };
  return (
    <form className="stack" onSubmit={submit} noValidate aria-label={a.awardTitle}>
      {error && <p className="field-error" role="alert">{error}</p>}
      <EntityPicker id={id + '-member'} label={a.member} value={member} onChange={(items) => setMember(items.slice(0, 1))} search={search} placeholder={a.memberPlaceholder} disabled={busy} />
      <div className="form-grid">
        <OptionSelect id={id + '-option'} label={a.option} field={field} value={optionKey} onChange={(key) => { setOptionKey(key); setLevel(0); }} disabled={busy} />
        <label className="field" htmlFor={id + '-level'}>
          <span className="label-text">{a.level}</span>
          <select id={id + '-level'} value={level} onChange={(e) => setLevel(Number(e.target.value))} disabled={busy || choices.length <= 1}>
            {choices.map((c) => <option key={c.level} value={c.level}>{c.label}</option>)}
          </select>
        </label>
      </div>
      {!allowed && <p className="muted small" role="note">{a.awardBlocked}</p>}
      <div className="field">
        <label className="label-text" htmlFor={id + '-note'}>{a.note}</label>
        <textarea id={id + '-note'} rows={2} maxLength={RANK_LIMITS.noteMax} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} aria-describedby={id + '-note-hint'} />
        <span className="muted small" id={id + '-note-hint'}>{a.noteHint}</span>
      </div>
      <label className="field" htmlFor={id + '-event'}>
        <span className="label-text">{a.eventId}</span>
        <input id={id + '-event'} type="text" value={eventId} onChange={(e) => setEventId(e.target.value)} disabled={busy} />
      </label>
      <div className="form-actions">
        {onCancel && <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>{strings.ranks.lineage.cancel}</button>}
        <button type="submit" className="button button-primary" disabled={busy}>{busy ? a.awarding : a.award}</button>
      </div>
    </form>
  );
}

function RevokeDialog({ client, award, onClose, onDone }) {
  const id = useId();
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const submit = async () => {
    if (!reason.trim()) { setError(a.revokeReasonHint); return; }
    setBusy(true);
    try {
      await client.revokeRankAward(award.id, reason.trim());
      onDone();
    } catch (err) {
      setError(rankAdminError(err));
      setBusy(false);
    }
  };
  return (
    <Modal open onClose={onClose} title={a.revokeTitle} subtitle={award.rankLabel} footer={<>
      <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.ranks.lineage.cancel}</button>
      <button type="button" className="button button-danger" onClick={submit} disabled={busy}>{a.revoke}</button>
    </>}>
      {error && <p className="field-error" role="alert">{error}</p>}
      <div className="field">
        <label className="label-text" htmlFor={id}>{a.revokeReason}</label>
        <textarea id={id} rows={3} maxLength={RANK_LIMITS.noteMax} value={reason} onChange={(e) => setReason(e.target.value)} disabled={busy} aria-describedby={id + '-hint'} />
        <span className="muted small" id={id + '-hint'}>{a.revokeReasonHint}</span>
      </div>
    </Modal>
  );
}

function AwardsTab({ client, field, awarder, search, prefill, onPrefillUsed }) {
  const [params, setParams] = useSearchParams();
  const [member, setMember] = useState(() => (params.get('userId') ? [{ id: params.get('userId'), label: params.get('userId') }] : []));
  const [filters, setFilters] = useState({ optionKey: '', action: '', status: '', fromUtc: '', toUtc: '' });
  const [awarding, setAwarding] = useState(null);
  const [revoking, setRevoking] = useState(null);
  const [status, setStatus] = useState('');
  useEffect(() => {
    if (prefill) { setAwarding(prefill); onPrefillUsed(); }
  }, [prefill, onPrefillUsed]);
  const query = { fieldKey: field.key, ...(member.length ? { userId: member[0].id } : {}) };
  for (const key of Object.keys(filters)) if (filters[key]) query[key] = key.endsWith('Utc') ? filters[key] + (key === 'toUtc' ? 'T23:59:59Z' : 'T00:00:00Z') : filters[key];
  const filterKey = JSON.stringify(query);
  const fetchPage = useCallback((cursor, signal) => client.enumerateRankAwards({ filters: JSON.parse(filterKey), maxResults: 25, continuationToken: cursor }, signal), [client, filterKey]);
  const list = useCursorList(fetchPage);
  const set = (key) => (e) => setFilters((f) => ({ ...f, [key]: e.target.value }));
  return (
    <div className="stack">
      <div className="row-between">
        <span />
        <button type="button" className="button button-primary" onClick={() => setAwarding({})}>{a.award}</button>
      </div>
      {status && <p className="success-text small" role="status">{status}</p>}
      <div className="p4-filters">
        <EntityPicker id="awards-member" label={a.member} value={member} onChange={(items) => { setMember(items.slice(0, 1)); if (!items.length && params.get('userId')) setParams({}); }} search={search} placeholder={a.memberPlaceholder} />
        <OptionSelect id="awards-option" label={a.option} field={field} value={filters.optionKey} onChange={(v) => setFilters((f) => ({ ...f, optionKey: v }))} anyLabel={a.anyOption} />
        <label className="field" htmlFor="awards-action"><span className="label-text">{a.action}</span>
          <select id="awards-action" value={filters.action} onChange={set('action')}><option value="">{a.anyAction}</option>{Object.keys(r.actions).map((k) => <option key={k} value={k}>{r.actions[k]}</option>)}</select>
        </label>
        <label className="field" htmlFor="awards-status"><span className="label-text">{a.status}</span>
          <select id="awards-status" value={filters.status} onChange={set('status')}><option value="">{a.anyStatus}</option>{Object.keys(a.statuses).map((k) => <option key={k} value={k}>{a.statuses[k]}</option>)}</select>
        </label>
        <label className="field" htmlFor="awards-from"><span className="label-text">{a.from}</span><input id="awards-from" type="date" value={filters.fromUtc} onChange={set('fromUtc')} /></label>
        <label className="field" htmlFor="awards-to"><span className="label-text">{a.to}</span><input id="awards-to" type="date" value={filters.toUtc} onChange={set('toUtc')} /></label>
      </div>
      <ErrorBanner message={list.error ? rankAdminError(list.error) : ''} onRetry={list.refresh} />
      {list.loaded && !list.items.length && !list.error && <p className="muted">{a.awardsEmpty}</p>}
      <table className="data-table">
        <thead><tr><th scope="col">{a.member}</th><th scope="col">{a.action}</th><th scope="col">{a.option}</th><th scope="col">{a.awardedBy}</th><th scope="col">{a.date}</th><th scope="col"><span className="visually-hidden">{a.revoke}</span></th></tr></thead>
        <tbody>
          {list.items.map((row) => (
            <tr key={row.id} data-award={row.id}>
              <td>{row.user ? authorName(row.user) : row.userId}</td>
              <td>{r.actions[row.action] || row.action}{row.revokedUtc && <> <Badge tone="warning">{a.revokedOn(formatShortDate(row.revokedUtc))}</Badge></>}</td>
              <td>{row.rankLabel || rankText(field, row.optionKey, row.level)}{(row.note || row.reason) && <p className="muted small preserve-lines">{row.note || row.reason}</p>}</td>
              <td>{row.actor ? authorName(row.actor) : row.actorId}</td>
              <td className="nowrap">{formatShortDate(row.createdUtc)}</td>
              <td>{row.action === 'Award' && !row.revokedUtc && <button type="button" className="button button-danger-outline button-sm" onClick={() => setRevoking(row)}>{a.revoke}</button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary" onClick={list.loadMore}>{a.more}</button>}
      {awarding && (
        <Modal open onClose={() => setAwarding(null)} title={a.awardTitle} size="lg">
          <AwardForm client={client} field={field} awarder={awarder} initial={awarding} search={search} onCancel={() => setAwarding(null)} onDone={() => { setAwarding(null); setStatus(a.awarded); list.refresh(); }} />
        </Modal>
      )}
      {revoking && <RevokeDialog client={client} award={revoking} onClose={() => setRevoking(null)} onDone={() => { setRevoking(null); setStatus(a.revoked); list.refresh(); }} />}
    </div>
  );
}

function EligibilityTab({ client, field, onAward }) {
  const [optionKey, setOptionKey] = useState('');
  const [eligibleOnly, setEligibleOnly] = useState(true);
  const filterKey = JSON.stringify({ fieldKey: field.key, eligibleOnly, ...(optionKey ? { optionKey } : {}) });
  const fetchPage = useCallback((cursor, signal) => client.enumerateRankEligibility({ filters: JSON.parse(filterKey), maxResults: 25, continuationToken: cursor }, signal), [client, filterKey]);
  const list = useCursorList(fetchPage);
  return (
    <div className="stack">
      <div className="p4-filters">
        <OptionSelect id="elig-option" label={a.current} field={field} value={optionKey} onChange={setOptionKey} anyLabel={a.anyOption} />
        <label className="check-field"><input type="checkbox" checked={eligibleOnly} onChange={(e) => setEligibleOnly(e.target.checked)} /><span>{a.eligibleOnly}</span></label>
      </div>
      <ErrorBanner message={list.error ? rankAdminError(list.error) : ''} onRetry={list.refresh} />
      {list.loaded && !list.items.length && !list.error && !list.loading && <p className="muted">{a.eligibilityEmpty}</p>}
      <ul className="p4-list">
        {list.items.map((row) => {
          const display = eligibilityDisplay(row.next);
          return (
            <li key={row.userId} className="panel" data-member={row.userId}>
              <div className="row-between">
                <strong>{row.user ? authorName(row.user) : row.userId}</strong>
                <Badge tone={row.eligible ? 'success' : 'neutral'}>{row.eligible ? a.eligibleBadge : a.notEligible}</Badge>
              </div>
              <p className="small">{a.current}: {(row.current && (row.current.rankLabel || row.current.optionLabel)) || r.noRank}{row.next && <> · {a.next}: {row.next.rankLabel}</>}</p>
              {display.checks.length > 0 && <p className="muted small">{display.checks.map((c) => r.checks[c.key](c.current, c.required)).join(' · ')}</p>}
              {row.next && <button type="button" className="button button-secondary button-sm" onClick={() => onAward({ member: { id: row.userId, label: row.user ? authorName(row.user) : row.userId }, optionKey: row.next.optionKey, level: row.next.level || 0 })}>{a.award}</button>}
            </li>
          );
        })}
      </ul>
      {list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary" onClick={list.loadMore}>{a.more}</button>}
    </div>
  );
}

let reqKey = 0;
const reqRow = (row = {}) => ({ key: 'q' + (reqKey += 1), optionKey: row.optionKey || '', level: row.level === null || row.level === undefined ? '' : String(row.level), minimumDaysInGrade: row.minimumDaysInGrade || 0, minimumAttendance: row.minimumAttendance || 0, notes: row.notes || '', curriculumId: row.curriculumId || '', labels: row.labels, tags: row.tags });

/** PUT /admin/ranks/requirements body from editor rows. */
export function requirementsBody(fieldKey, rows) {
  return {
    fieldKey,
    requirements: rows.map((row) => {
      const out = { optionKey: row.optionKey, level: row.level === '' ? null : Number(row.level), minimumDaysInGrade: Number(row.minimumDaysInGrade) || 0, minimumAttendance: Number(row.minimumAttendance) || 0 };
      if (row.notes.trim()) out.notes = row.notes.trim();
      if (row.curriculumId) out.curriculumId = row.curriculumId;
      if (row.labels) out.labels = row.labels;
      if (row.tags) out.tags = row.tags;
      return out;
    })
  };
}

function RequirementsTab({ client, field, isSystemAdmin }) {
  const query = useApiQuery(useCallback((signal) => client.enumerateRankRequirements({ filters: { fieldKey: field.key }, maxResults: RANK_LIMITS.maxRequirements }, signal), [client, field.key]));
  const curricula = useApiQuery(useCallback((signal) => client.adminEnumerateCurricula({ filters: { fieldKey: field.key }, maxResults: 100 }, signal), [client, field.key]));
  const [rows, setRows] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');
  const current = rows || ((query.data && query.data.objects) || []).map(reqRow);
  const update = (key, patch) => setRows(current.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  const save = async () => {
    const seen = new Set();
    for (const row of current) {
      const k = row.optionKey + '|' + row.level;
      if (!row.optionKey || seen.has(k)) { setError(a.duplicateRequirement); return; }
      seen.add(k);
    }
    setBusy(true);
    setError('');
    try {
      const saved = await client.putRankRequirements(requirementsBody(field.key, current));
      setRows(((saved && saved.requirements) || []).map(reqRow));
      setStatus(a.requirementsSaved);
    } catch (err) {
      setError(rankAdminError(err));
    } finally {
      setBusy(false);
    }
  };
  const readOnly = !isSystemAdmin || busy;
  return (
    <div className="stack">
      <p className="muted small">{a.requirementsIntro}</p>
      {!isSystemAdmin && <p className="muted small">{a.requirementsAdminOnly}</p>}
      <ErrorBanner message={query.error ? rankAdminError(query.error) : error} onRetry={query.error ? query.reload : undefined} />
      {status && <p className="success-text small" role="status">{status}</p>}
      {query.loading && !query.data && <Loading />}
      {query.data && !current.length && <p className="muted">{a.requirementsEmpty}</p>}
      {current.map((row, index) => {
        const id = 'req-' + row.key;
        return (
          <fieldset key={row.key} className="fieldset" data-requirement={index}>
            <legend className="label-text">{row.optionKey ? rankText(field, row.optionKey, row.level === '' ? 0 : Number(row.level)) : a.option}</legend>
            <div className="form-grid">
              <OptionSelect id={id + '-option'} label={a.option} field={field} value={row.optionKey} onChange={(v) => update(row.key, { optionKey: v, level: '' })} anyLabel="" disabled={readOnly} />
              <label className="field" htmlFor={id + '-level'}><span className="label-text">{a.level}</span>
                <select id={id + '-level'} value={row.level} onChange={(e) => update(row.key, { level: e.target.value })} disabled={readOnly}>
                  <option value="">{a.allLevels}</option>
                  {rankLevelChoices(field, row.optionKey).map((c) => <option key={c.level} value={String(c.level)}>{c.label}</option>)}
                </select>
              </label>
              <label className="field" htmlFor={id + '-days'}><span className="label-text">{a.minimumDays}</span><input id={id + '-days'} type="number" min={0} max={3650} value={row.minimumDaysInGrade} onChange={(e) => update(row.key, { minimumDaysInGrade: e.target.value })} disabled={readOnly} /></label>
              <label className="field" htmlFor={id + '-att'}><span className="label-text">{a.minimumAttendance}</span><input id={id + '-att'} type="number" min={0} max={10000} value={row.minimumAttendance} onChange={(e) => update(row.key, { minimumAttendance: e.target.value })} disabled={readOnly} /></label>
              <label className="field" htmlFor={id + '-cur'}><span className="label-text">{a.requirementCurriculum}</span>
                <select id={id + '-cur'} value={row.curriculumId} onChange={(e) => update(row.key, { curriculumId: e.target.value })} disabled={readOnly}>
                  <option value="">{a.noCurriculum}</option>
                  {((curricula.data && curricula.data.objects) || []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              </label>
            </div>
            <label className="field" htmlFor={id + '-notes'}><span className="label-text">{a.requirementNotes}</span><textarea id={id + '-notes'} rows={2} maxLength={1000} value={row.notes} onChange={(e) => update(row.key, { notes: e.target.value })} disabled={readOnly} /></label>
            {isSystemAdmin && <button type="button" className="button button-danger-outline button-sm" onClick={() => setRows(current.filter((x) => x.key !== row.key))} disabled={busy}>{a.removeRow}</button>}
          </fieldset>
        );
      })}
      {isSystemAdmin && (
        <div className="form-actions">
          <button type="button" className="button button-secondary" onClick={() => setRows([...current, reqRow()])} disabled={busy || current.length >= RANK_LIMITS.maxRequirements}>{a.addRequirement}</button>
          <button type="button" className="button button-primary" onClick={save} disabled={busy || !query.data}>{a.saveRequirements}</button>
        </div>
      )}
    </div>
  );
}

function LineageTab({ client, field, me, isSystemAdmin, mode, search }) {
  const [member, setMember] = useState([]);
  const userId = member.length ? member[0].id : null;
  const query = useApiQuery(useCallback((signal) => client.getMemberRank(userId, signal), [client, userId]), { enabled: Boolean(userId) });
  const [verified, setVerified] = useState({});
  const entries = ((query.data && query.data.lineage) || []).filter((e) => e.fieldKey === field.key).map((e) => (verified[e.id] ? { ...e, verified: true } : e));
  if (mode === 'token') return <p className="muted">{a.lineageSessionOnly}</p>;
  return (
    <div className="stack">
      <p className="muted small">{a.lineageIntro}</p>
      <EntityPicker id="lineage-member" label={a.member} value={member} onChange={(items) => { setMember(items.slice(0, 1)); setVerified({}); }} search={search} placeholder={a.memberPlaceholder} />
      <ErrorBanner message={query.error ? rankAdminError(query.error) : ''} onRetry={query.reload} />
      {userId && query.loading && <Loading inline />}
      {userId && query.data && (entries.length ? <LineageList entries={entries} field={field} me={me} client={client} canVerifyAll={isSystemAdmin} onVerified={(entry) => setVerified((v) => ({ ...v, [entry.id]: true }))} /> : <p className="muted">{a.lineageEmpty}</p>)}
    </div>
  );
}

function RanksAdminView() {
  const { client, isSystemAdmin, me, mode } = useAdminAuth();
  const fields = useRankFields();
  const [fieldKey, setFieldKey] = useState('');
  const field = fields.find((f) => f.key === fieldKey) || fields[0] || null;
  const [tab, setTab] = useState('awards');
  const [prefill, setPrefill] = useState(null);
  const search = useMemberSearch(client, isSystemAdmin);
  const myRank = useApiQuery(useCallback((signal) => client.getMyRank(signal), [client]), { enabled: !isSystemAdmin && mode === 'session' });
  const awarder = useMemo(() => {
    if (isSystemAdmin) return { isSystemAdmin: true };
    const own = ((myRank.data && myRank.data.fields) || []).map((f) => f.rank).find((rk) => field && rk && rk.fieldKey === field.key);
    return { isSystemAdmin: false, rank: own && own.verified ? { optionKey: own.optionKey, level: own.level, status: 'Verified' } : null };
  }, [isSystemAdmin, myRank.data, field]);
  const clearPrefill = useCallback(() => setPrefill(null), []);
  const tabs = ['awards', 'eligibility', 'requirements', 'lineage'].map((id) => ({ id, label: a.tabs[id] }));
  return (
    <div className="view stack">
      <PageHeader title={a.nav} summary={a.summary} />
      {!field ? <p className="muted">{a.noFields}</p> : (
        <>
          <FieldSelect fields={fields} value={field.key} onChange={setFieldKey} />
          <Tabs tabs={tabs} value={tab} onChange={setTab} label={a.nav} />
          <div role="tabpanel" id={'panel-' + tab} aria-labelledby={'tab-' + tab} className="tab-panel">
            {tab === 'awards' && <AwardsTab key={field.key} client={client} field={field} awarder={awarder} search={search} prefill={prefill} onPrefillUsed={clearPrefill} />}
            {tab === 'eligibility' && <EligibilityTab key={field.key} client={client} field={field} onAward={(initial) => { setPrefill(initial); setTab('awards'); }} />}
            {tab === 'requirements' && <RequirementsTab key={field.key} client={client} field={field} isSystemAdmin={isSystemAdmin} />}
            {tab === 'lineage' && <LineageTab key={field.key} client={client} field={field} me={me} isSystemAdmin={isSystemAdmin} mode={mode} search={search} />}
          </div>
        </>
      )}
    </div>
  );
}

export default RanksAdminView;
