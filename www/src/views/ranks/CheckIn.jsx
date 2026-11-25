/**
 * Attendance check-in for events and venue schedule sessions.
 *
 * - `CheckInControls` props: `targetType` ('Event' | 'VenueSession'), `targetId` (event id, or
 *   `{venueId}:{sessionIndex}`), `occurrenceDate` (yyyy-MM-dd; required for sessions), `title`
 *   (what the member checks in to). Renders nothing unless `features.ranks` is on and a member is
 *   signed in; members get "Check in" (code entry), rank staff also get "Show check-in code".
 * - `CheckInDialog` props: `open`, `target` ({ targetType, targetId, occurrenceDate }), `title`, `onClose`.
 * - `StaffCheckInPanel` props: `client`, `target`, `property` (for the QR link's web domain), `canRemove`.
 * - `CheckInRoster` props: `client`, `target`, `refreshKey` (changing it reloads), `canRemove`.
 */
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { checkInLink, isRankStaff, localIsoDate, ranksFeatureEnabled, secondsUntil, venueSessionTargetId } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import Modal from '../../components/Modal.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { formatDateTime, formatShortDateTime } from '../../utils/format.js';
import { authorName, errorText } from '../../utils/engagement.js';
import QrCodeSvg from './QrCodeSvg.jsx';

const t = strings.ranks.checkIn;

/** The error text of a failed check-in. */
export function checkInErrorText(err) {
  const map = { ...t.errors };
  if (err && err.status === 404 && err.code !== 'NotAvailable') return t.errors.NotFound;
  return errorText(err, map, strings.ranks.errors.generic);
}

/** Code entry form; `onDone(attendance)` after a successful check-in. */
export function CheckInForm({ client, target, onDone, autoFocus = true }) {
  const id = useId();
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const submit = async (event) => {
    event.preventDefault();
    if (busy) return;
    if (!/^\d{6}$/.test(code)) { setError(t.codeInvalid); return; }
    setBusy(true);
    setError('');
    try {
      const body = { targetType: target.targetType, targetId: target.targetId, code, method: 'SelfWithCode' };
      if (target.occurrenceDate) body.occurrenceDate = target.occurrenceDate;
      onDone(await client.checkIn(body));
    } catch (err) {
      setError(err && err.code === 'AlreadyCheckedIn' ? '' : checkInErrorText(err));
      if (err && err.code === 'AlreadyCheckedIn') onDone(null, true);
    } finally {
      setBusy(false);
    }
  };
  return (
    <form className="stack" onSubmit={submit} noValidate>
      <p className="muted small">{t.intro}</p>
      <div className="field">
        <label className="label-text" htmlFor={id + '-code'}>{t.code}</label>
        <input
          id={id + '-code'} type="text" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]*" maxLength={6} value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} autoFocus={autoFocus} disabled={busy}
          aria-invalid={Boolean(error) || undefined} aria-describedby={error ? id + '-error' : undefined}
        />
        {error && <span className="field-error" id={id + '-error'} role="alert">{error}</span>}
      </div>
      <div className="form-actions"><button type="submit" className="button button-primary" disabled={busy}>{busy ? t.submitting : t.submit}</button></div>
    </form>
  );
}

export function CheckInDialog({ open, target, title, onClose }) {
  const { client } = useAuth();
  const [done, setDone] = useState(null);
  if (!open) return null;
  return (
    <Modal open onClose={onClose} title={t.title} subtitle={title} footer={<button type="button" className="button button-secondary" onClick={onClose}>{t.cancel}</button>}>
      {done ? <p role="status" className="success-text">{done}</p> : (
        <CheckInForm client={client} target={target} onDone={(_a, already) => setDone(already ? t.errors.AlreadyCheckedIn : title ? t.doneFor(title) : t.done)} />
      )}
    </Modal>
  );
}

/** The rotating check-in code of a target; fetches the next code when the current one expires. */
export function useCheckInCode(client, target, enabled = true) {
  const [state, setState] = useState({ key: '', data: null, error: null });
  const [now, setNow] = useState(() => Date.now());
  const [nonce, setNonce] = useState(0);
  const key = target ? [target.targetType, target.targetId, target.occurrenceDate || ''].join('|') : '';
  const targetRef = useRef(target);
  useEffect(() => { targetRef.current = target; });
  useEffect(() => {
    if (!enabled || !key) return undefined;
    let live = true;
    let timer = null;
    const load = () => {
      client.getCheckInCode(targetRef.current).then(
        (data) => {
          if (!live) return;
          setState({ key, data, error: null });
          const wait = Date.parse((data && data.expiresUtc) || '') - Date.now();
          timer = setTimeout(load, Number.isFinite(wait) ? Math.max(1000, wait + 250) : 60000);
        },
        (error) => { if (live) setState((s) => ({ ...s, key, error })); }
      );
    };
    load();
    return () => { live = false; clearTimeout(timer); };
  }, [client, key, enabled, nonce]);
  useEffect(() => {
    if (!enabled) return undefined;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [enabled]);
  const current = state.key === key;
  const data = current ? state.data : null;
  const error = current ? state.error : null;
  const reload = useCallback(() => setNonce((n) => n + 1), []);
  return { data, error, loading: enabled && !data && !error, remaining: data ? secondsUntil(data.expiresUtc, now) : 0, reload };
}

export function CheckInRoster({ client, target, refreshKey = 0, canRemove = false }) {
  const filterKey = JSON.stringify({ targetType: target.targetType, targetId: target.targetId, ...(target.occurrenceDate ? { occurrenceDate: target.occurrenceDate } : {}) });
  const fetchPage = useCallback((cursor, signal) => client.enumerateAttendance({ filters: JSON.parse(filterKey), maxResults: 50, continuationToken: cursor }, signal), [client, filterKey]);
  const list = useCursorList(fetchPage);
  const { refresh } = list;
  const first = useRef(true);
  useEffect(() => {
    if (first.current) { first.current = false; return; }
    refresh();
  }, [refreshKey, refresh]);
  const [removing, setRemoving] = useState(null);
  const [status, setStatus] = useState('');
  const remove = async (row) => {
    if (typeof window !== 'undefined' && window.confirm && !window.confirm(t.removeConfirm)) return;
    setRemoving(row.id);
    try {
      await client.deleteAttendance(row.id);
      list.update((items) => items.filter((item) => item.id !== row.id));
      setStatus(t.removed);
    } catch (err) {
      setStatus(checkInErrorText(err));
    } finally {
      setRemoving(null);
    }
  };
  const total = Number.isInteger(list.totalRecords) ? list.totalRecords : null;
  return (
    <section className="stack" aria-label={t.rosterTitle}>
      <h3 className="section-title">{t.rosterTitle}{total !== null && <span className="muted small"> {t.rosterCount(total)}</span>}</h3>
      <ErrorBanner message={list.error ? checkInErrorText(list.error) : ''} onRetry={list.refresh} />
      {status && <p className="small" role="status">{status}</p>}
      {list.loaded && !list.items.length && !list.loading && !list.error && <p className="muted small">{t.rosterEmpty}</p>}
      <ul className="rank-roster">
        {list.items.map((row) => (
          <li key={row.id} className="row-between">
            <span>{row.user ? authorName(row.user) : row.userId} <span className="muted small">{formatShortDateTime(row.checkedInUtc)} · {t.methods[row.method] || row.method}</span></span>
            {canRemove && <button type="button" className="button button-danger-outline button-sm" onClick={() => remove(row)} disabled={removing === row.id}>{t.remove}</button>}
          </li>
        ))}
      </ul>
      {list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{t.rosterMore}</button>}
    </section>
  );
}

export function StaffCheckInPanel({ client, target, property, canRemove = false }) {
  const code = useCheckInCode(client, target);
  const data = code.data;
  const link = data ? checkInLink(property, { ...target, ...data }) : null;
  const value = link || (data && data.qrPayload) || '';
  return (
    <div className="stack rank-staff-check-in">
      <p className="muted small">{t.staffIntro}</p>
      <ErrorBanner message={code.error ? checkInErrorText(code.error) || t.codeError : ''} onRetry={code.reload} />
      {!data && code.loading && <Loading label={t.refreshing} />}
      {data && (
        <div className="rank-code-display">
          <QrCodeSvg value={value} label={t.qrLabel} />
          <p className="rank-code mono" aria-label={t.code}>{data.code}</p>
          <p className="muted small" aria-live="polite">{code.remaining > 0 ? t.countdown(code.remaining) : t.refreshing}</p>
          {data.windowOpensUtc && data.windowClosesUtc && <p className="muted small">{t.window(formatDateTime(data.windowOpensUtc), formatDateTime(data.windowClosesUtc))}</p>}
        </div>
      )}
      <CheckInRoster client={client} target={target} refreshKey={data ? data.code : ''} canRemove={canRemove} />
    </div>
  );
}

export function CheckInControls({ targetType, targetId, occurrenceDate = null, title = '' }) {
  const { client, me } = useAuth();
  const { property } = useProperty();
  const [open, setOpen] = useState(null);
  if (!ranksFeatureEnabled(property) || !me || !targetId) return null;
  const target = { targetType, targetId, ...(occurrenceDate ? { occurrenceDate } : {}) };
  const staff = isRankStaff(me);
  return (
    <span className="rank-check-in-controls">
      <button type="button" className="button button-secondary button-sm" onClick={() => setOpen('member')}>{t.button}</button>
      {staff && <button type="button" className="button button-ghost button-sm" onClick={() => setOpen('staff')}>{t.staffButton}</button>}
      <CheckInDialog open={open === 'member'} target={target} title={title} onClose={() => setOpen(null)} />
      {open === 'staff' && (
        <Modal open onClose={() => setOpen(null)} title={t.staffTitle} subtitle={title} size="lg" footer={<button type="button" className="button button-secondary" onClick={() => setOpen(null)}>{t.cancel}</button>}>
          <StaffCheckInPanel client={client} target={target} property={property} canRemove />
        </Modal>
      )}
    </span>
  );
}

/**
 * `CheckInControls` in its own panel with a heading (event detail side column). Same props;
 * renders nothing when ranks are off or nobody is signed in.
 */
export function CheckInPanel(props) {
  const { me } = useAuth();
  const { property } = useProperty();
  if (!ranksFeatureEnabled(property) || !me || !props.targetId) return null;
  return (
    <section className="panel" aria-labelledby="check-in-title">
      <h2 id="check-in-title" className="section-title">{t.title}</h2>
      <CheckInControls {...props} />
    </section>
  );
}

/** Monday-first weekday index (0 = Monday) of a date, matching venue schedule `dayOfWeek`. */
export function mondayFirstDay(date = new Date()) {
  return (date.getDay() + 6) % 7;
}

/**
 * A `VenueSchedule` `sessionAction` that adds check-in controls to today's sessions (the device's
 * date; the server checks the venue's time zone and the check-in window).
 */
export function venueSessionCheckIn(venue, now = new Date()) {
  if (!venue || !venue.id) return null;
  const today = mondayFirstDay(now);
  const date = localIsoDate(now);
  return function SessionCheckIn(entry, index) {
    if (index < 0 || entry.dayOfWeek !== today) return null;
    return <CheckInControls targetType="VenueSession" targetId={venueSessionTargetId(venue.id, index)} occurrenceDate={date} title={entry.name} />;
  };
}

export default CheckInControls;
