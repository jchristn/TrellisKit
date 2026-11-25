import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import { formatEventShort } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useCursorList from '../../hooks/useCursorList.js';
import { EmptyState, ErrorBanner, Loading } from '../../components/States.jsx';
import { formatDate } from '../../utils/format.js';
import { appPaths } from '../../utils/phase4.js';
import { featureEnabled } from '../member/memberRoutes.js';
import { ranksEnabled } from './trainingModel.js';

const t = strings.training.schedule;

function More({ list }) {
  return (
    <>
      <ErrorBanner message={list.error ? t.loadError : ''} onRetry={list.items.length ? list.loadMore : list.refresh} />
      {!list.loaded && list.loading && <Loading inline />}
      {list.loaded && !list.done && !list.loading && !list.error && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{t.loadMore}</button>}
    </>
  );
}

/** Upcoming events the member is going to (`POST /me/events/enumerate`, rsvpStatus Going, upcoming). */
function Upcoming({ client }) {
  const fetchPage = useCallback((cursor, signal) => client.enumerateMyEvents({ maxResults: 25, continuationToken: cursor, filters: { rsvpStatus: 'Going', upcoming: true } }, signal), [client]);
  const list = useCursorList(fetchPage);
  return (
    <section className="panel stack" aria-labelledby="schedule-upcoming">
      <h2 id="schedule-upcoming" className="section-title">{t.upcomingTitle}</h2>
      {list.loaded && !list.items.length && !list.error && (
        <EmptyState icon="calendar" title={t.upcomingEmpty} action={<Link to={appPaths.events} className="button button-secondary button-sm">{t.browseEvents}</Link>} />
      )}
      {list.items.length > 0 && (
        <ul className="training-list" data-testid="schedule-upcoming-list">
          {list.items.map((event) => (
            <li key={event.id}>
              <Link to={appPaths.event(event.slug || event.id)} className="training-list-link">
                <span className="training-list-title">{event.title}</span>
                <span className="muted small">{[formatEventShort(event), event.locationName].filter(Boolean).join(' · ')}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
      <More list={list} />
    </section>
  );
}

/** The member's check-ins (`POST /me/attendance/enumerate`), when ranks are on. */
function Attendance({ client }) {
  const fetchPage = useCallback((cursor, signal) => client.enumerateMyAttendance({ maxResults: 25, continuationToken: cursor }, signal), [client]);
  const list = useCursorList(fetchPage);
  return (
    <section className="panel stack" aria-labelledby="schedule-attendance">
      <h2 id="schedule-attendance" className="section-title">{t.attendanceTitle}</h2>
      {list.loaded && !list.items.length && !list.error && <p className="muted small">{t.attendanceEmpty}</p>}
      {list.items.length > 0 && (
        <ul className="training-list" data-testid="schedule-attendance-list">
          {list.items.map((row) => {
            const text = t.attendanceRow(formatDate(row.checkedInUtc || row.createdUtc));
            const kind = t.attendanceTarget[row.targetType] || row.targetType;
            return (
              <li key={row.id} className="training-list-link">
                {row.targetType === 'Event'
                  ? <Link to={appPaths.event(row.targetId)} className="training-list-title">{kind}: {text}</Link>
                  : <span className="training-list-title">{kind}: {text}</span>}
              </li>
            );
          })}
        </ul>
      )}
      <More list={list} />
    </section>
  );
}

/**
 * Training Schedule (`/app/training/schedule`): the events the member is going to, and, when ranks
 * are on, their check-ins. Shown only when the overview reports the Schedule section available.
 */
function ScheduleView() {
  const { client } = useAuth();
  const { property } = useProperty();
  return (
    <div className="stack">
      <p className="muted">{t.intro}</p>
      {featureEnabled(property, 'events') && <Upcoming client={client} />}
      {ranksEnabled(property) && <Attendance client={client} />}
    </div>
  );
}

export default ScheduleView;
