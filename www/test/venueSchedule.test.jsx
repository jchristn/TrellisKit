import { useState } from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import VenueScheduleEditor from '../src/components/directory/VenueScheduleEditor.jsx';
import { VenueSchedule } from '../src/components/directory/VenueSchedule.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { siteWith } from './phase4Helpers.jsx';

const schedule = { timeZone: 'Pacific/Honolulu', openingHours: [{ dayOfWeek: 0, startTime: '22:00', endTime: '02:00', endsNextDay: true }], sessions: [{ dayOfWeek: 1, startTime: '18:00', endTime: '19:00', endsNextDay: false, name: 'Fundamentals', instructor: 'Alex' }], notes: 'Holiday hours vary.' };
describe('structured schedules', () => {
  it('renders hours, overnight context, session names, instructors and the time zone', () => {
    render(<VenueSchedule schedule={schedule} text="Old hours" />);
    expect(screen.getByText('Pacific/Honolulu')).toBeInTheDocument();
    expect(screen.getByText('10 PM to 2 AM (next day)')).toBeInTheDocument();
    expect(screen.getByText('Fundamentals')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Sessions' })).toBeInTheDocument();
    expect(screen.getByText(/Alex/)).toBeInTheDocument();
    expect(screen.queryByText('Old hours')).toBeNull();
  });
  it('lets an editor add and remove opening hours and sessions', async () => {
    function Harness() { const [value, set] = useState(null); return <VenueScheduleEditor value={value} onChange={set} />; }
    render(<Harness />);
    await userEvent.click(screen.getByRole('button', { name: 'Add schedule' }));
    await userEvent.type(screen.getByLabelText('Time zone'), 'Pacific/Honolulu');
    await userEvent.click(screen.getByRole('button', { name: 'Add opening hours' }));
    await userEvent.click(screen.getByRole('button', { name: 'Add a session' }));
    await userEvent.type(screen.getByLabelText('Session name'), 'Fundamentals');
    expect(screen.getByLabelText('Session name')).toHaveValue('Fundamentals');
    expect(screen.getAllByLabelText('Start time')).toHaveLength(2);
    await userEvent.click(screen.getAllByRole('button', { name: 'Remove entry' })[0]);
    expect(screen.getAllByLabelText('Start time')).toHaveLength(1);
  });
  it('labels sessions and instructors with the property terms', async () => {
    const site = siteWith({ directory: { sessionLabel: 'Meeting', sessionLabelPlural: 'Meetings', instructorLabel: 'Host', instructorLabelPlural: 'Hosts' } });
    function Harness() { const [value, set] = useState(null); return <VenueScheduleEditor value={value} onChange={set} />; }
    render(<PropertyProvider site={site}><VenueSchedule schedule={schedule} /><Harness /></PropertyProvider>);
    expect(screen.getByRole('heading', { name: 'Meetings' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Add schedule' }));
    await userEvent.click(screen.getByRole('button', { name: 'Add a meeting' }));
    expect(screen.getByLabelText('Meeting name')).toBeInTheDocument();
    expect(screen.getByLabelText('Host (optional)')).toBeInTheDocument();
  });
});
