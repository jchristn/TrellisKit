import { useState } from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import VenueScheduleEditor from '../src/components/directory/VenueScheduleEditor.jsx';
import { VenueSchedule } from '../src/components/directory/VenueSchedule.jsx';

const schedule = { timeZone: 'Pacific/Honolulu', openingHours: [{ dayOfWeek: 0, startTime: '22:00', endTime: '02:00', endsNextDay: true }], classes: [{ dayOfWeek: 1, startTime: '18:00', endTime: '19:00', endsNextDay: false, name: 'Fundamentals', instructor: 'Alex' }], notes: 'Holiday hours vary.' };
describe('structured schedules', () => {
  it('renders hours, overnight context, class names, instructors and the time zone', () => {
    render(<VenueSchedule schedule={schedule} text="Old hours" />);
    expect(screen.getByText('Pacific/Honolulu')).toBeInTheDocument();
    expect(screen.getByText('10 PM to 2 AM (next day)')).toBeInTheDocument();
    expect(screen.getByText('Fundamentals')).toBeInTheDocument();
    expect(screen.getByText(/Alex/)).toBeInTheDocument();
    expect(screen.queryByText('Old hours')).toBeNull();
  });
  it('lets an editor add and remove opening hours and classes', async () => {
    function Harness() { const [value, set] = useState(null); return <VenueScheduleEditor value={value} onChange={set} />; }
    render(<Harness />);
    await userEvent.click(screen.getByRole('button', { name: 'Add a structured schedule' }));
    await userEvent.type(screen.getByLabelText('Time zone'), 'Pacific/Honolulu');
    await userEvent.click(screen.getByRole('button', { name: 'Add opening hours' }));
    await userEvent.click(screen.getByRole('button', { name: 'Add class' }));
    await userEvent.type(screen.getByLabelText('Class name'), 'Fundamentals');
    expect(screen.getByLabelText('Class name')).toHaveValue('Fundamentals');
    expect(screen.getAllByLabelText('Start time')).toHaveLength(2);
    await userEvent.click(screen.getAllByRole('button', { name: 'Remove entry' })[0]);
    expect(screen.getAllByLabelText('Start time')).toHaveLength(1);
  });
});
