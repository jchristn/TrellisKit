import { formatClockTime, venueScheduleDisplay, parseVenueDescription } from '@trelliskit/shared';
import strings from '../../strings.js';
import MarkdownBody from '../feed/MarkdownBody.jsx';

const v = strings.venues;

function dayLabel(row) {
  if (row.from === 0 && row.to === 6) return v.scheduleEveryDay;
  return row.from === row.to ? v.scheduleDays[row.from] : v.scheduleDayRange(v.scheduleDays[row.from], v.scheduleDays[row.to]);
}

function timeLabel(row) {
  return row.allDay ? v.scheduleOpen24 : v.scheduleTimeRange(formatClockTime(row.open), formatClockTime(row.close)) + (row.endsNextDay ? v.scheduleEditor.nextDaySuffix : '');
}

/**
 * A venue's schedule panel: opening hours as a two-column table (days that share hours on one
 * row), then the class schedule notes as paragraphs. Schedule text in any other shape is shown
 * as paragraphs. `hoursOnly` shows just the hours table, without the panel (the map preview).
 */
export function VenueSchedule({ text, schedule, hoursOnly = false }) {
  const parsed = venueScheduleDisplay(schedule, text);
  const hours = parsed.hours;
  const notes = hoursOnly ? [] : parsed.notes;
  if (!hours.length && !notes.length && (hoursOnly || !parsed.classes.length)) return null;
  return (
    <section className={hoursOnly ? 'venue-preview-hours' : 'panel'}>
      {parsed.timeZone && <p className="muted small">{parsed.timeZone}</p>}
      {hours.length > 0 && (
        <>
          <h2 className="section-title">{v.hours}</h2>
          <table className="venue-hours">
            <tbody>
              {hours.map((row, index) => (
                <tr key={index}>
                  <th scope="row">{dayLabel(row)}</th>
                  <td>{timeLabel(row)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      {!hoursOnly && parsed.classes.length > 0 && <>
        <h2 className="section-title">{v.scheduleEditor.classes}</h2>
        {parsed.classes.map((entry, i) => <p key={i}>
          <strong>{entry.name}</strong>{' · '}{v.scheduleDays[entry.dayOfWeek]}{' · '}
          {timeLabel({ open: entry.startTime, close: entry.endTime, endsNextDay: entry.endsNextDay })}
          {entry.instructor ? ' · ' + entry.instructor : ''}
        </p>)}
      </>}
      {notes.length > 0 && (
        <>
          <h2 className="section-title">{v.schedule}</h2>
          {notes.map((note, i) => <p key={i}>{note}</p>)}
        </>
      )}
    </section>
  );
}

/** A venue's description panel: any free text as Markdown, pricing as its own paragraph, and amenities as a list of tags. */
export function VenueAbout({ text }) {
  const { pricing, amenities, body } = parseVenueDescription(text);
  if (!pricing && !amenities.length && !body) return null;
  return (
    <section className="panel">
      {body && (
        <>
          <h2 className="section-title">{v.about}</h2>
          <MarkdownBody body={body} allowHeadings={false} linkMentions={false} />
        </>
      )}
      {pricing && (
        <>
          <h2 className="section-title">{v.pricing}</h2>
          <p>{pricing}</p>
        </>
      )}
      {amenities.length > 0 && (
        <>
          <h2 className="section-title">{v.amenities}</h2>
          <ul className="venue-amenities">{amenities.map((item, i) => <li key={i}>{item}</li>)}</ul>
        </>
      )}
    </section>
  );
}
