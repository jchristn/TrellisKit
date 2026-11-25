/**
 * `/admin/attendance`: rank staff pick an event or a venue schedule session and date, show its
 * rotating check-in code and QR, review and remove check-ins, and check members in without a
 * code. Shown only when `features.ranks` is on.
 */
import { useCallback, useState } from 'react';
import { localIsoDate, venueSessionTargetId } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useProperty } from '../../../context/PropertyContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import PageHeader from '../../../components/PageHeader.jsx';
import EntityPicker from '../../../components/admin/EntityPicker.jsx';
import { ErrorBanner } from '../../../components/States.jsx';
import { CheckInRoster, StaffCheckInPanel, checkInErrorText } from '../../ranks/CheckIn.jsx';
import { useMemberSearch } from './rankAdminParts.jsx';

const t = strings.ranks.attendanceAdmin;
const ci = strings.ranks.checkIn;

function StaffAddMember({ client, target, search, onAdded }) {
  const [member, setMember] = useState([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const add = async () => {
    if (!member.length) return;
    setBusy(true);
    setMessage('');
    try {
      await client.checkIn({ ...target, userId: member[0].id });
      setMember([]);
      setMessage(ci.added);
      onAdded();
    } catch (err) {
      setMessage(checkInErrorText(err));
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="stack" aria-label={ci.addMember}>
      <h3 className="section-title">{ci.addMember}</h3>
      <p className="muted small">{ci.addMemberHint}</p>
      <EntityPicker id="attendance-add-member" label={strings.ranks.admin.member} value={member} onChange={(items) => setMember(items.slice(0, 1))} search={search} placeholder={strings.ranks.admin.memberPlaceholder} disabled={busy} />
      <div className="form-actions"><button type="button" className="button button-primary" onClick={add} disabled={busy || !member.length}>{ci.button}</button></div>
      {message && <p className="small" role="status">{message}</p>}
    </section>
  );
}

function AttendanceAdminView() {
  const { client, isSystemAdmin, staffAreas = [] } = useAdminAuth();
  const { property } = useProperty();
  const search = useMemberSearch(client, isSystemAdmin);
  const [targetType, setTargetType] = useState('Event');
  const [event, setEvent] = useState([]);
  const [venue, setVenue] = useState([]);
  const [sessionIndex, setSessionIndex] = useState('');
  const [date, setDate] = useState(() => localIsoDate());
  const [showCode, setShowCode] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const directoryStaff = isSystemAdmin || staffAreas.includes('directory');
  const eventSearch = useCallback(async (term, signal) => ((await (isSystemAdmin || staffAreas.includes('events') ? client.adminEnumerateEvents : client.enumerateEvents).call(client, { search: term, maxResults: 10 }, signal)).objects || []).map((x) => ({ id: x.id, label: x.title })), [client, isSystemAdmin, staffAreas]);
  const venueSearch = useCallback(async (term, signal) => ((await (directoryStaff ? client.adminEnumerateVenues : client.enumerateVenues).call(client, { search: term, maxResults: 10 }, signal)).objects || []).map((x) => ({ id: x.id, label: x.name })), [client, directoryStaff]);
  const venueId = venue.length ? venue[0].id : null;
  const venueQuery = useApiQuery(useCallback((signal) => (directoryStaff ? client.adminGetVenue(venueId, signal) : client.getVenue(venueId, signal)), [client, venueId, directoryStaff]), { enabled: Boolean(venueId) });
  const sessions = (venueQuery.data && venueQuery.data.schedule && venueQuery.data.schedule.sessions) || [];
  let target = null;
  if (targetType === 'Event' && event.length) target = { targetType: 'Event', targetId: event[0].id };
  if (targetType === 'VenueSession' && venueId && sessionIndex !== '' && date) target = { targetType: 'VenueSession', targetId: venueSessionTargetId(venueId, Number(sessionIndex)), occurrenceDate: date };
  const days = strings.venues.scheduleDays;
  return (
    <div className="view stack">
      <PageHeader title={t.title} summary={strings.ranks.admin.attendanceSummary} />
      <div className="form-grid">
        <label className="field" htmlFor="attendance-type"><span className="label-text">{t.targetType}</span>
          <select id="attendance-type" value={targetType} onChange={(e) => { setTargetType(e.target.value); setShowCode(false); }}>
            {Object.keys(t.types).map((k) => <option key={k} value={k}>{t.types[k]}</option>)}
          </select>
        </label>
      </div>
      {targetType === 'Event' ? (
        <EntityPicker id="attendance-event" label={t.event} value={event} onChange={(items) => { setEvent(items.slice(0, 1)); setShowCode(false); }} search={eventSearch} placeholder={t.eventPlaceholder} />
      ) : (
        <>
          <EntityPicker id="attendance-venue" label={t.venue} value={venue} onChange={(items) => { setVenue(items.slice(0, 1)); setSessionIndex(''); setShowCode(false); }} search={venueSearch} placeholder={t.venuePlaceholder} />
          <ErrorBanner message={venueQuery.error ? checkInErrorText(venueQuery.error) : ''} onRetry={venueQuery.reload} />
          {venueId && venueQuery.data && (sessions.length ? (
            <div className="form-grid">
              <label className="field" htmlFor="attendance-session"><span className="label-text">{t.session}</span>
                <select id="attendance-session" value={sessionIndex} onChange={(e) => { setSessionIndex(e.target.value); setShowCode(false); }}>
                  <option value="" />
                  {sessions.map((s, i) => <option key={i} value={String(i)}>{s.name}{' · '}{days[s.dayOfWeek] || ''} {s.startTime || ''}</option>)}
                </select>
              </label>
              <label className="field" htmlFor="attendance-date"><span className="label-text">{t.date}</span><input id="attendance-date" type="date" value={date} onChange={(e) => { setDate(e.target.value); setShowCode(false); }} /></label>
            </div>
          ) : <p className="muted">{t.noSessions}</p>)}
        </>
      )}
      {!target ? <p className="muted">{t.chooseTarget}</p> : (
        <section className="panel stack" key={JSON.stringify(target)}>
          <div className="row-actions"><button type="button" className="button button-secondary" onClick={() => setShowCode((v) => !v)} aria-expanded={showCode}>{showCode ? t.hideCode : t.showCode}</button></div>
          {showCode ? <StaffCheckInPanel client={client} target={target} property={property} canRemove /> : <CheckInRoster client={client} target={target} refreshKey={refresh} canRemove />}
          <StaffAddMember client={client} target={target} search={search} onAdded={() => setRefresh((n) => n + 1)} />
        </section>
      )}
    </div>
  );
}

export default AttendanceAdminView;
