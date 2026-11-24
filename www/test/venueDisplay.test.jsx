import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { VenueAbout, VenueSchedule } from '../src/components/directory/VenueSchedule.jsx';
import VideoPlayer from '../src/components/feed/VideoPlayer.jsx';

describe('reusable directory displays', () => {
  it('shows grouped hours with preserved schedule notes', () => {
    render(<VenueSchedule text={'Hours: Mon 09:00-17:00; Tue 09:00-17:00\nVisits by appointment.'} />);
    expect(screen.getByRole('rowheader', { name: 'Mon to Tue' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: '9 AM to 5 PM' })).toBeInTheDocument();
    expect(screen.getByText('Visits by appointment.')).toBeInTheDocument();
  });

  it('previews only hours, and safely renders pricing and amenities as text', () => {
    const { unmount } = render(<VenueSchedule text={'Hours: Mon 00:00-24:00\nPrivate notes'} hoursOnly />);
    expect(screen.getByText('Open 24 hours')).toBeInTheDocument();
    expect(screen.queryByText('Private notes')).toBeNull();
    unmount();
    render(<MemoryRouter><VenueAbout text={'Pricing: Contact us\nAmenities: Parking, <script>bad</script>'} /></MemoryRouter>);
    expect(screen.getByRole('heading', { name: 'Pricing' })).toBeInTheDocument();
    expect(screen.getByText('<script>bad</script>')).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
  });
});

describe('portrait video frame', () => {
  it('keeps the media ratio separate from the caption and defers playback', () => {
    const { container } = render(<VideoPlayer media={{ id: 'med_one', width: 720, height: 1280, altText: 'A portrait clip', video: { posterUrl: '/poster.png', mp4Url: '/movie.mp4' } }} />);
    const frame = container.querySelector('.media-video');
    expect(frame.style.getPropertyValue('--video-ratio')).toBe('720 / 1280');
    expect(frame.style.getPropertyValue('--video-ratio-number')).toBe('0.5625');
    expect(frame.querySelector('.media-video-box')).toBeInTheDocument();
    expect(frame.querySelector('.media-video-box figcaption')).toBeNull();
    expect(frame.querySelector('figcaption')).toHaveTextContent('A portrait clip');
    expect(container.querySelector('video')).toBeNull();
  });
});
