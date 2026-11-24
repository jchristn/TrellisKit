import { useState } from 'react';
import { Link } from 'react-router-dom';
import { categoryOf, dateKeyInZone, formatAddress, formatEventShort, formatEventTime, isLockedItem, monthGrid, rsvpState } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import { deviceTimeZone, errorText } from '../../utils/engagement.js';
import { appPaths, phase4Errors } from '../../utils/phase4.js';
import Icon from '../Icon.jsx';
import { Badge } from '../Badges.jsx';
import { LockBadge, useAccessPrompt } from '../AccessPrompt.jsx';

const t = strings.events;

/** A category chip with its color. */
export function CategoryChip({ event }) {
  const { property } = useProperty();
  const category = categoryOf(property, event);
  if (!category) return null;
  return <span className="chip"><span className="p4-chip-dot" style={category.color ? { background: category.color } : undefined} aria-hidden="true" />{category.label}</span>;
}

function dateBadge(event) {
  const zone = event.timeZone || 'UTC';
  try {
    const d = new Date(event.startUtc);
    return {
      month: new Intl.DateTimeFormat('en-US', { timeZone: zone, month: 'short' }).format(d),
      day: new Intl.DateTimeFormat('en-US', { timeZone: zone, day: 'numeric' }).format(d)
    };
  } catch {
    return { month: '', day: '' };
  }
}

/** One event in a list (EventSummary): date badge, title, time, place, category, RSVP status, and lock. */
export function EventRow({ event }) {
  const badge = dateBadge(event);
  const locked = isLockedItem(event);
  const canceled = event.status === 'Canceled';
  const loc = event.location || {};
  const place = loc.venue ? loc.venue.name : loc.name || formatAddress(loc.address, loc.addressVisibility, { includeCountry: false });
  const rsvp = event.myRsvp;
  return (
    <Link to={appPaths.event(event.id)} className="p4-row" data-testid={'event-row-' + event.id}>
      <span className="p4-event-date" aria-hidden="true"><small>{badge.month}</small>{badge.day}</span>
      <span className="p4-row-main">
        <span className="p4-row-title" style={canceled ? { textDecoration: 'line-through' } : undefined}>{event.title}</span>
        <span className="p4-card-meta">
          <span>{formatEventShort(event)}</span>
          {event.format !== 'InPerson' && <span>{t.formats[event.format]}</span>}
          {place && event.format !== 'Online' && <span>{place}</span>}
          <CategoryChip event={event} />
          {canceled && <Badge tone="danger">{t.canceled}</Badge>}
          {rsvp && <Badge tone={rsvp.status === 'Going' ? 'success' : 'info'}>{rsvp.status === 'Going' ? t.going : rsvp.status === 'Waitlisted' ? t.joinWaitlist : t.interested}</Badge>}
          {event.recurrence && <span title={event.recurrence.summary}><Icon name="refresh" size={12} /></span>}
          {locked && <LockBadge feature="events" />}
        </span>
      </span>
      <Icon name="chevronRight" size={16} />
    </Link>
  );
}

/** The event's time in its zone, with the viewer's zone as a second line when they differ. */
export function EventTime({ event }) {
  const time = formatEventTime(event, deviceTimeZone());
  return (
    <div>
      <div><Icon name="clock" size={14} /> {time.primary}{time.isAllDay ? ' (' + t.allDay + ')' : ''}</div>
      {time.secondary && <div className="muted small">{t.yourTime(time.secondary)}</div>}
    </div>
  );
}

/**
 * The RSVP control (section 4.3): Going (or Join the waitlist when full), Interested, guests,
 * hide me from attendee lists, and Not going. Every change returns the updated Event.
 * @param {{ event: object, client: object, me: object|null, onChange: (event: object) => void }} props
 */
export function RsvpControl({ event, client, me, onChange }) {
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const [busy, setBusy] = useState(false);
  const rsvp = event.myRsvp;
  const [guests, setGuests] = useState(rsvp ? rsvp.guestCount || 0 : 0);
  const [hidden, setHidden] = useState(false);
  const state = rsvpState(event);
  const maxGuests = Math.max(0, Number(event.maxGuestsPerRsvp) || 0);

  if (!me) return <p className="muted small">{t.rsvpSignIn}</p>;
  const send = async (status) => {
    setBusy(true);
    try {
      const updated = await client.rsvpEvent(event.id, { status, guestCount: maxGuests ? guests : undefined, hideFromAttendeeLists: hidden });
      const mine = updated && updated.myRsvp;
      toast.success(mine && mine.status === 'Waitlisted' ? t.rsvpWaitlisted : t.rsvpSaved);
      onChange(updated);
    } catch (err) {
      if (!accessPrompt.handle(err)) toast.error(errorText(err, phase4Errors()));
    } finally {
      setBusy(false);
    }
  };
  const remove = async () => {
    setBusy(true);
    try {
      onChange(await client.cancelRsvp(event.id));
      toast.success(t.rsvpRemoved);
    } catch (err) {
      toast.error(errorText(err, phase4Errors()));
    } finally {
      setBusy(false);
    }
  };

  let status = null;
  if (state.status === 'Going') status = t.youAreGoing;
  if (state.status === 'Interested') status = t.youAreInterested;
  if (state.status === 'Waitlisted') status = t.youAreWaitlisted(state.position);

  return (
    <div className="p4-rsvp" data-testid="rsvp-control">
      {status && <p role="status"><strong>{status}</strong></p>}
      {state.closed && !state.canceled && <p className="muted small">{t.rsvpClosed}</p>}
      {!state.closed && (
        <>
          {maxGuests > 0 && (
            <label className="field field-compact">
              <span className="label-text">{t.guests}</span>
              <input className="input-compact input-number" type="number" min={0} max={maxGuests} value={guests} onChange={(e) => setGuests(Math.max(0, Math.min(maxGuests, Number(e.target.value) || 0)))} />
              <span className="muted small">{t.guestsHint(maxGuests)}</span>
            </label>
          )}
          <label className="check-field">
            <input type="checkbox" checked={hidden} onChange={(e) => setHidden(e.target.checked)} />
            <span>{t.hideMe}</span>
          </label>
          <span className="muted small">{t.hideMeHint}</span>
          <div className="p4-rsvp-buttons">
            {state.canGoing && state.status !== 'Going' && state.status !== 'Waitlisted' && (
              <button type="button" className="button button-primary button-sm" disabled={busy} onClick={() => send('Going')}>{state.joinsWaitlist ? t.joinWaitlist : t.going}</button>
            )}
            {state.status === 'Going' && maxGuests > 0 && <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={() => send('Going')}>{strings.common.save}</button>}
            {state.canInterested && state.status !== 'Interested' && <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={() => send('Interested')}>{t.interested}</button>}
            {state.canCancel && <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={remove}>{t.notGoing}</button>}
          </div>
        </>
      )}
    </div>
  );
}

/**
 * A month view (section 16): weeks of days with each day's events in the viewer's zone (all-day
 * events by their own date). Days link to the events; `onPrev`, `onNext`, `onToday` move months.
 */
export function MonthCalendar({ year, month, events, onPrev, onNext, onToday, truncated = false }) {
  const zone = deviceTimeZone();
  const weeks = monthGrid(year, month);
  const byDay = {};
  for (const e of events || []) {
    const key = dateKeyInZone(e.startUtc, e.isAllDay ? e.timeZone : zone);
    (byDay[key] = byDay[key] || []).push(e);
  }
  const todayKey = dateKeyInZone(new Date(), zone);
  const label = new Intl.DateTimeFormat('en-US', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, 1)));
  return (
    <section className="p4-section" aria-label={t.monthTitle(label)}>
      <div className="p4-player-bar">
        <h2>{label}</h2>
        <span className="badge-row">
          <button type="button" className="icon-button" onClick={onPrev} aria-label={t.prevMonth} title={t.prevMonth}><Icon name="chevronLeft" size={16} /></button>
          <button type="button" className="button button-secondary button-sm" onClick={onToday}>{t.today}</button>
          <button type="button" className="icon-button" onClick={onNext} aria-label={t.nextMonth} title={t.nextMonth}><Icon name="chevronRight" size={16} /></button>
        </span>
      </div>
      {truncated && <p className="muted small">{t.calendarTruncated}</p>}
      <div className="p4-month" role="grid">
        {t.weekdays.map((d) => <div key={d} className="p4-month-head" role="columnheader">{d}</div>)}
        {weeks.flat().map((cell) => {
          const list = (byDay[cell.key] || []).sort((a, b) => String(a.startUtc).localeCompare(String(b.startUtc)));
          return (
            <div key={cell.key} role="gridcell" className={'p4-day' + (cell.inMonth ? '' : ' out') + (cell.key === todayKey ? ' today' : '')} aria-label={list.length ? t.dayEvents(cell.key) : undefined} data-testid={'day-' + cell.key}>
              <span className="p4-day-num">{cell.day}</span>
              {list.slice(0, 3).map((e) => <Link key={e.id} to={appPaths.event(e.id)} className={'p4-day-event' + (e.status === 'Canceled' ? ' canceled' : '')} title={e.title}>{e.title}</Link>)}
              {list.length > 3 && <span className="muted small">{t.moreOnDay(list.length - 3)}</span>}
            </div>
          );
        })}
      </div>
    </section>
  );
}
