import { useState } from 'react';
import { render, fireEvent, screen } from '@testing-library/react-native';
import { directoryStrings } from '../src/content/phase4';
import { getBundledProperty } from '../src/property/property';
import VenueScheduleEditor from '../src/components/places/VenueScheduleEditor';
import { VenueSchedule } from '../src/components/places/VenueSchedule';
import { initialValues, submissionFieldsFor } from '../src/components/places/SubmissionForm';
const t = directoryStrings(getBundledProperty());
const schedule = { timeZone: 'Pacific/Honolulu', openingHours: [{ dayOfWeek: 0, startTime: '22:00', endTime: '02:00', endsNextDay: true }], classes: [{ dayOfWeek: 1, startTime: '18:00', endTime: '19:00', endsNextDay: false, name: 'Fundamentals', instructor: 'Alex' }], notes: 'Holiday hours vary.' };
test('native display shows structured hours and classes', async () => {
  await render(<VenueSchedule schedule={schedule} text="Old hours" t={t} />);
  expect(screen.getByText('Pacific/Honolulu')).toBeTruthy();
  expect(screen.getByText('10 PM to 2 AM (next day)')).toBeTruthy();
  expect(screen.getByText(/Fundamentals/)).toBeTruthy();
  expect(screen.queryByText('Old hours')).toBeNull();
});
test('native editor adds opening hours and class sessions', async () => {
  function Harness() { const [value, set] = useState(null); return <VenueScheduleEditor value={value} onChange={set} t={t} />; }
  await render(<Harness />);
  await fireEvent.press(screen.getByText('Add a structured schedule'));
  await fireEvent.press(screen.getByText('Add opening hours'));
  await fireEvent.press(screen.getByText('Add class'));
  expect(screen.getAllByLabelText('Start time')).toHaveLength(2);
  await fireEvent.changeText(screen.getByLabelText('Class name'), 'Fundamentals');
  expect(screen.getByDisplayValue('Fundamentals')).toBeTruthy();
});
test('submissions preserve schedule objects and send only changed fields, including clearing', async () => {
  const target = { name: 'Example Club', schedule };
  const form = initialValues('EditVenue', target);
  expect(submissionFieldsFor('EditVenue', form, target)).toEqual({});
  expect(submissionFieldsFor('EditVenue', { ...form, schedule: { ...schedule, notes: 'Changed' } }, target)).toEqual({ schedule: { ...schedule, notes: 'Changed' } });
  expect(submissionFieldsFor('EditVenue', { ...form, schedule: null }, target)).toEqual({ schedule: null });
});
