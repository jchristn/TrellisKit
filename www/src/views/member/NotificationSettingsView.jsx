import { collectEnumeration } from '@trelliskit/shared';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { DEFAULT_NOTIFICATION_HOURS, NotificationCategoryOrder, applyPreferenceChange, isDigestEmailCategory, isTimeOfDay } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import { deviceTimeZone, errorText, timeZoneOptions } from '../../utils/engagement.js';

const ns = strings.notificationSettings;
const CHANNELS = ['inApp', 'push', 'email'];
const TZ_SENT_KEY = 'tk.notificationTimeZone.';

/** "07:00" as "7:00 AM". */
export function formatTimeOfDay(value) {
  if (!isTimeOfDay(value)) return value || '';
  const [h, m] = value.split(':').map(Number);
  return new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit', timeZone: 'UTC' }).format(new Date(Date.UTC(2026, 0, 1, h, m)));
}

/** Summary sentence for a notification hours setting. */
export function hoursSummary(hours) {
  if (!hours || hours.enabled === false) return ns.hoursAllDay;
  if (hours.start === hours.end) return ns.hoursSameAllDay;
  const text = ns.hoursWindow(formatTimeOfDay(hours.start), formatTimeOfDay(hours.end), hours.timeZone || deviceTimeZone());
  return hours.start > hours.end ? text + ' ' + ns.hoursOvernight : text;
}

function readSent(userId) {
  try {
    return window.localStorage.getItem(TZ_SENT_KEY + userId);
  } catch {
    return null;
  }
}

function writeSent(userId, zone) {
  try {
    window.localStorage.setItem(TZ_SENT_KEY + userId, zone);
  } catch {
    // Storage can be unavailable; the zone is sent again next time.
  }
}

function Cell({ row, channel, onToggle, busy }) {
  const editable = row.editable ? row.editable[channel] !== false : true;
  const label = ns.toggle((ns.categories[row.category] || { label: row.category }).label, ns.columns[channel]);
  const pushBlocked = channel === 'push' && row.inApp === false;
  // Following and Forums email is an opt-in digest (PHASE3_CONTRACT.md section 9.3).
  const digest = channel === 'email' && isDigestEmailCategory(row.category);
  if (!editable) {
    const on = Boolean(row[channel]);
    return (
      <td className="pref-cell" data-label={ns.columns[channel]}>
        <span className="pref-fixed muted small" title={on ? ns.notEditableTitle : ns.reservedTitle}>{on ? ns.notEditable : ns.reserved}</span>
      </td>
    );
  }
  return (
    <td className="pref-cell" data-label={ns.columns[channel]}>
      <label className="toggle" title={pushBlocked ? ns.pushNeedsInApp : digest ? ns.digestTitle : undefined}>
        <input type="checkbox" checked={Boolean(row[channel]) && !pushBlocked} onChange={(e) => onToggle(row.category, channel, e.target.checked)} disabled={busy || pushBlocked} aria-label={digest ? label + ' (' + ns.digest + ')' : label} />
        <span className="toggle-track"><span className="toggle-thumb" /></span>
      </label>
      {digest && <span className="muted small pref-digest" title={ns.digestTitle}>{ns.digest}</span>}
    </td>
  );
}

function HoursForm({ hours, onSave }) {
  const device = deviceTimeZone();
  const [draft, setDraft] = useState(() => ({ ...DEFAULT_NOTIFICATION_HOURS, timeZone: device, ...(hours || {}) }));
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const zones = timeZoneOptions(draft.timeZone);
  const dirty = JSON.stringify(draft) !== JSON.stringify({ ...DEFAULT_NOTIFICATION_HOURS, timeZone: device, ...(hours || {}) });

  const save = async (e) => {
    e.preventDefault();
    if (!isTimeOfDay(draft.start) || !isTimeOfDay(draft.end)) {
      setError(ns.hoursInvalid);
      return;
    }
    setError('');
    setBusy(true);
    try {
      await onSave({ enabled: draft.enabled, start: draft.start, end: draft.end, timeZone: draft.timeZone });
    } catch (err) {
      setError(errorText(err, {}, ns.saveError));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="hours-form" onSubmit={save} noValidate>
      <ErrorBanner message={error} />
      <label className="toggle">
        <input type="checkbox" checked={draft.enabled !== false} onChange={(e) => setDraft((d) => ({ ...d, enabled: e.target.checked }))} disabled={busy} />
        <span className="toggle-track"><span className="toggle-thumb" /></span>
        <span>{ns.hoursEnabled}</span>
      </label>
      <div className="hours-fields">
        <div className="field field-compact">
          <label className="label-text" htmlFor="hours-start">{ns.hoursStart}</label>
          <input id="hours-start" type="time" value={draft.start} onChange={(e) => setDraft((d) => ({ ...d, start: e.target.value }))} disabled={busy || draft.enabled === false} />
        </div>
        <div className="field field-compact">
          <label className="label-text" htmlFor="hours-end">{ns.hoursEnd}</label>
          <input id="hours-end" type="time" value={draft.end} onChange={(e) => setDraft((d) => ({ ...d, end: e.target.value }))} disabled={busy || draft.enabled === false} />
        </div>
        <div className="field field-compact field-grow">
          <label className="label-text" htmlFor="hours-zone">{ns.hoursZone}</label>
          <select id="hours-zone" value={draft.timeZone} onChange={(e) => setDraft((d) => ({ ...d, timeZone: e.target.value }))} disabled={busy}>
            {zones.map((z) => <option key={z} value={z}>{z.replace(/_/g, ' ')}</option>)}
          </select>
          <span className="muted small">
            {ns.zoneDetected(device)}{' '}
            {draft.timeZone !== device && <button type="button" className="link-button" onClick={() => setDraft((d) => ({ ...d, timeZone: device }))}>{ns.useDeviceZone}</button>}
          </span>
        </div>
      </div>
      <p className="muted small" aria-live="polite">{hoursSummary(draft)}</p>
      <div className="form-actions">
        <button type="submit" className="button button-primary button-sm" disabled={busy || !dirty}>{ns.hoursSave}</button>
      </div>
    </form>
  );
}

/**
 * /app/settings/notifications (contract sections 8.5 and 8.6): one row per category with in-app,
 * push, and email toggles (every one on by default; fixed cells explained), pause all pushes,
 * and notification hours in the member's time zone. The device time zone is sent the first time
 * the screen is shown when it differs from the saved one.
 */
function NotificationSettingsView() {
  const { client, me } = useAuth();
  const toast = useToast();
  useDocumentMeta(ns.meta, ns.summary);
  const fetcher = useCallback(async (signal) => { const page = await collectEnumeration((q, sig) => client.getNotificationPreferences(q, sig), {}, { signal }); return { ...page, categories: page.objects }; }, [client]);
  const query = useApiQuery(fetcher);
  const [doc, setDoc] = useState(null);
  const [busy, setBusy] = useState(false);
  const zoneChecked = useRef(false);
  const current = doc || query.data;

  useEffect(() => {
    if (!query.data || zoneChecked.current || !me) return;
    zoneChecked.current = true;
    const device = deviceTimeZone();
    const saved = query.data.notificationHours && query.data.notificationHours.timeZone;
    if (saved === device || readSent(me.id) === device) return;
    client.updateNotificationPreferences({ notificationHours: { timeZone: device } }).then(
      (next) => { writeSent(me.id, device); if (next && next.categories) setDoc(next); },
      () => {}
    );
  }, [query.data, client, me]);

  const apply = async (change) => {
    const before = current;
    setDoc(applyPreferenceChange(before, change));
    setBusy(true);
    try {
      const next = await client.updateNotificationPreferences(change);
      if (next && next.categories) setDoc(next);
      return next;
    } catch (err) {
      setDoc(before);
      toast.error(errorText(err, {}, ns.saveError));
      throw err;
    } finally {
      setBusy(false);
    }
  };

  const toggle = (category, channel, value) => apply({ categories: [{ category, [channel]: value }] }).catch(() => {});
  const saveHours = async (hours) => {
    await apply({ notificationHours: hours });
    toast.success(ns.hoursSaved);
  };

  const rows = current ? NotificationCategoryOrder.map((key) => current.categories.find((r) => r.category === key)).filter(Boolean)
    .concat(current.categories.filter((r) => !NotificationCategoryOrder.includes(r.category))) : [];

  return (
    <div className="member-view settings-view">
      <Link to="/app/profile" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{ns.back}</span></Link>
      <header className="page-header">
        <div className="page-header-text">
          <h1>{ns.title}</h1>
          <p className="page-summary">{ns.summary}</p>
        </div>
      </header>
      {query.loading && !current && <Loading />}
      {query.error && !current && <ErrorBanner message={ns.loadError + ' ' + errorText(query.error, {}, '')} onRetry={query.reload} />}
      {current && (
        <>
          <section className="panel" aria-labelledby="prefs-title">
            <h2 id="prefs-title">{ns.categoriesTitle}</h2>
            <Banner tone="info" icon="info">{ns.pushNote}</Banner>
            <div className="table-scroll">
              <table className="data-table pref-table">
                <caption className="visually-hidden">{ns.categoriesTitle}</caption>
                <thead>
                  <tr>
                    <th scope="col">{ns.columns.category}</th>
                    {CHANNELS.map((ch) => <th key={ch} scope="col" className="pref-cell">{ns.columns[ch]}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => {
                    const meta = ns.categories[row.category] || { label: row.category, description: '' };
                    return (
                      <tr key={row.category}>
                        <th scope="row" className="pref-category">
                          <span className="pref-label">{meta.label}</span>
                          <span className="muted small">{meta.description}</span>
                        </th>
                        {CHANNELS.map((ch) => <Cell key={ch} row={row} channel={ch} onToggle={toggle} busy={busy} />)}
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            <p className="muted small">{ns.emailNote}</p>
          </section>

          <section className="panel" aria-labelledby="pause-title">
            <div className="row-between">
              <div>
                <h2 id="pause-title">{ns.pauseTitle}</h2>
                <p className="muted small">{ns.pauseBody}</p>
              </div>
              <label className="toggle">
                <input type="checkbox" checked={Boolean(current.pushPaused)} onChange={(e) => apply({ pushPaused: e.target.checked }).catch(() => {})} disabled={busy} aria-label={ns.pauseTitle} />
                <span className="toggle-track"><span className="toggle-thumb" /></span>
              </label>
            </div>
          </section>

          {typeof current.autoFollowOnComment === 'boolean' && (
            <section className="panel" aria-labelledby="autofollow-title">
              <div className="row-between">
                <div>
                  <h2 id="autofollow-title">{ns.autoFollowTitle}</h2>
                  <p className="muted small">{ns.autoFollowBody}</p>
                </div>
                <label className="toggle">
                  <input type="checkbox" checked={current.autoFollowOnComment} onChange={(e) => apply({ autoFollowOnComment: e.target.checked }).catch(() => {})} disabled={busy} aria-label={ns.autoFollowTitle} />
                  <span className="toggle-track"><span className="toggle-thumb" /></span>
                </label>
              </div>
            </section>
          )}

          <section className="panel" aria-labelledby="hours-title">
            <h2 id="hours-title">{ns.hoursTitle}</h2>
            <p className="muted small">{ns.hoursBody}</p>
            <HoursForm key={JSON.stringify(current.notificationHours || {})} hours={current.notificationHours} onSave={saveHours} />
          </section>

          <section className="panel" aria-labelledby="safety-link-title">
            <h2 id="safety-link-title">{ns.blockedLink}</h2>
            <Link to="/app/settings/blocked" className="button button-secondary button-sm">{ns.blockedLink}</Link>
          </section>
        </>
      )}
    </div>
  );
}

export default NotificationSettingsView;
