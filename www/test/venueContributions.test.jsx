import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect, useState } from 'react';
import { createDirectoryFake, makeFakeMedia, makeInstructor, makeVenue, makeVenueMediaItem, makeVenueReview } from '@trelliskit/shared/testing';
import { VenueView } from '../src/views/directory/VenueViews.jsx';
import { VenueRow } from '../src/views/directory/DirectoryView.jsx';
import { VenueMediaSection } from '../src/components/directory/VenueMedia.jsx';
import { StarInput } from '../src/components/directory/Stars.jsx';
import { notificationHref } from '../src/views/member/NotificationsView.jsx';
import { useAuth } from '../src/context/AuthContext.jsx';
import { json, makeMe } from './helpers.jsx';
import { phase4Fetch, renderP4, siteWith } from './phase4Helpers.jsx';

// jsdom has no media playback; the lightbox autoplays videos.
const realPlay = HTMLMediaElement.prototype.play;
beforeAll(() => { HTMLMediaElement.prototype.play = () => Promise.resolve(); });
afterAll(() => { HTMLMediaElement.prototype.play = realPlay; });

const TERMS = { venueLabel: 'Club', venueLabelPlural: 'Clubs', instructorLabel: 'Coach', instructorLabelPlural: 'Coaches', submissions: true, claims: true, map: { provider: 'None' } };
const site = (extra = {}) => siteWith({ directory: { ...TERMS, ...extra } });
const kim = { id: 'usr_2', displayName: 'Kim Park', handle: 'kimpark', avatarUrl: null, isStaff: false, isDeleted: false };
const alex = { id: 'usr_3', displayName: 'Alex Chen', handle: 'alexchen', avatarUrl: null, isStaff: false, isDeleted: false };
const sam = { id: 'usr_1', displayName: 'Sam Rivera', handle: 'sam', avatarUrl: null, isStaff: false, isDeleted: false };

const noEvents = (call) => (call.path === '/api/v1/events/enumerate' ? json({ objects: [], endOfResults: true }) : undefined);

function venueFake(extra = {}) {
  return createDirectoryFake({
    me: sam,
    venues: [makeVenue('ven_1', { name: 'Example Club Downtown', photos: extra.photos || [] })],
    instructors: [makeInstructor('ins_1', { displayName: 'Pat Example', linkedUser: extra.linked ? sam : null })],
    links: [{ venueId: 'ven_1', instructorId: 'ins_1' }],
    ...extra
  });
}

async function openVenue(fake, { path = '/app/venues/ven_1', siteExtra = {} } = {}) {
  const { fetchImpl, calls } = phase4Fetch([fake], makeMe(), noEvents);
  const view = await renderP4(<VenueView />, { fetchImpl, site: site(siteExtra), path, routePath: '/app/venues/:id' });
  await screen.findByRole('heading', { name: 'Example Club Downtown', level: 1 });
  return { view, calls };
}

describe('star input', () => {
  function Harness({ initial = 0 }) {
    const [value, setValue] = useState(initial);
    return <><StarInput value={value} onChange={setValue} label="Your rating" placeholder="Choose a rating" /><output data-testid="value">{value}</output></>;
  }

  it('is a radio group of 1 to 5 with one tab stop, visible labels, and arrow keys that move and select', async () => {
    render(<Harness />);
    const group = screen.getByRole('radiogroup', { name: 'Your rating' });
    const radios = within(group).getAllByRole('radio');
    expect(radios.map((r) => r.getAttribute('aria-label'))).toEqual(['1 star', '2 stars', '3 stars', '4 stars', '5 stars']);
    expect(radios.filter((r) => r.tabIndex === 0)).toHaveLength(1);
    expect(radios.every((r) => r.getAttribute('aria-checked') === 'false')).toBe(true);
    expect(screen.getByText('Choose a rating')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: '3 stars' }));
    expect(screen.getByTestId('value')).toHaveTextContent('3');
    expect(screen.getByRole('radio', { name: '3 stars' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByText('3 stars')).toBeInTheDocument();
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByTestId('value')).toHaveTextContent('4');
    expect(screen.getByRole('radio', { name: '4 stars' })).toHaveFocus();
    expect(screen.getByRole('radio', { name: '4 stars' }).tabIndex).toBe(0);
    await userEvent.keyboard('{ArrowLeft}{ArrowLeft}');
    expect(screen.getByTestId('value')).toHaveTextContent('2');
    await userEvent.keyboard('{End}');
    expect(screen.getByTestId('value')).toHaveTextContent('5');
    await userEvent.keyboard('{ArrowUp}');
    expect(screen.getByTestId('value')).toHaveTextContent('5');
    await userEvent.keyboard('{Home}');
    expect(screen.getByTestId('value')).toHaveTextContent('1');
  });
});

describe('venue rows', () => {
  it('show stars and count with an accessible label only when rated', async () => {
    const { fetchImpl } = phase4Fetch([]);
    await renderP4(<><VenueRow venue={makeVenue('ven_1', { ratingAverage: 4.5, ratingCount: 12 })} /><VenueRow venue={makeVenue('ven_2')} /></>, { fetchImpl, site: site() });
    const rated = screen.getByTestId('venue-row-ven_1');
    expect(within(rated).getByRole('img', { name: 'Rated 4.5 out of 5 from 12 reviews' })).toBeInTheDocument();
    expect(rated).toHaveTextContent('4.5 (12)');
    expect(within(screen.getByTestId('venue-row-ven_2')).queryByRole('img', { name: /Rated/ })).toBeNull();
  });
});

describe('venue reviews', () => {
  it('summarizes the average and distribution', async () => {
    const fake = venueFake({ reviews: [makeVenueReview('vrv_1', { rating: 5 }), makeVenueReview('vrv_2', { rating: 4, author: alex }), makeVenueReview('vrv_3', { rating: 4, author: { ...alex, id: 'usr_4' } })] });
    await openVenue(fake);
    const summary = await screen.findByRole('group', { name: 'Rating summary' });
    expect(within(summary).getByRole('img', { name: 'Rated 4.3 out of 5 from 3 reviews' })).toBeInTheDocument();
    const rows = within(within(summary).getByRole('list', { name: 'Rating distribution' })).getAllByRole('listitem');
    expect(rows.map((r) => r.querySelector('.visually-hidden').textContent)).toEqual([
      '5 stars: 1 review (33%)', '4 stars: 2 reviews (67%)', '3 stars: 0 reviews (0%)', '2 stars: 0 reviews (0%)', '1 star: 0 reviews (0%)'
    ]);
  });

  it('validates with the property limits, posts, then edits and deletes the own review', async () => {
    const fake = venueFake();
    await openVenue(fake, { siteExtra: { reviews: { minLength: 20 } } });
    const form = await screen.findByRole('form', { name: 'Write a review' });
    await userEvent.click(within(form).getByRole('button', { name: 'Post review' }));
    expect(within(form).getByText('Choose a rating from 1 to 5 stars.')).toBeInTheDocument();
    expect(within(form).getByText('Write your review.')).toBeInTheDocument();
    expect(fake.calls.some((c) => c.method === 'POST' && c.path === '/venues/ven_1/reviews')).toBe(false);
    await userEvent.click(within(form).getByRole('radio', { name: '4 stars' }));
    await userEvent.type(within(form).getByLabelText('Review'), 'Nice place');
    await userEvent.click(within(form).getByRole('button', { name: 'Post review' }));
    expect(within(form).getByText('Write at least 20 characters.')).toBeInTheDocument();
    await userEvent.type(within(form).getByLabelText('Review'), ' with friendly people.');
    await userEvent.click(within(form).getByRole('button', { name: 'Post review' }));
    await waitFor(() => expect(fake.state.reviews).toHaveLength(1));
    expect(fake.state.reviews[0]).toMatchObject({ rating: 4, body: 'Nice place with friendly people.' });
    expect(await screen.findByRole('heading', { name: 'Your review' })).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Write a review' })).toBeNull();
    const mine = screen.getByTestId('review-' + fake.state.reviews[0].id);
    expect(within(mine).queryByRole('button', { name: 'Report review' })).toBeNull();

    await userEvent.click(within(mine).getByRole('button', { name: 'Edit your review' }));
    const edit = screen.getByRole('form', { name: 'Edit your review' });
    expect(within(edit).getByRole('radio', { name: '4 stars' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(edit).getByRole('radio', { name: '2 stars' }));
    await userEvent.click(within(edit).getByRole('button', { name: 'Save review' }));
    await waitFor(() => expect(fake.state.reviews[0].rating).toBe(2));
    expect(await screen.findByRole('img', { name: 'Rated 2 out of 5' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Delete your review' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(fake.state.reviews[0].deletedUtc).toBeTruthy());
    expect(await screen.findByRole('form', { name: 'Write a review' })).toBeInTheDocument();
  });

  it('shows the AlreadyReviewed error from the server', async () => {
    const fake = venueFake();
    await openVenue(fake);
    fake.state.reviews.push(makeVenueReview('vrv_9', { author: sam }));
    const form = await screen.findByRole('form', { name: 'Write a review' });
    await userEvent.click(within(form).getByRole('radio', { name: '5 stars' }));
    await userEvent.type(within(form).getByLabelText('Review'), 'Great coaching every week.');
    await userEvent.click(within(form).getByRole('button', { name: 'Post review' }));
    expect(await within(form).findByText('You already reviewed this. Edit your review instead.')).toBeInTheDocument();
  });

  it('orders the list and loads more with the continuation token', async () => {
    const reviews = Array.from({ length: 12 }, (_, i) => makeVenueReview('vrv_' + String(i + 1).padStart(2, '0'), {
      rating: (i % 5) + 1, body: 'Review number ' + (i + 1) + ' text.', author: { ...kim, id: 'usr_r' + i, displayName: 'Member ' + (i + 1) },
      createdUtc: '2026-11-' + String(i + 1).padStart(2, '0') + 'T00:00:00Z'
    }));
    const fake = venueFake({ reviews });
    await openVenue(fake);
    await screen.findByTestId('review-vrv_12');
    expect(document.querySelectorAll('.review-list .review-item')).toHaveLength(10);
    expect(document.querySelector('.review-list .review-item').dataset.testid).toBe('review-vrv_12');
    await userEvent.click(screen.getByRole('button', { name: 'Load more reviews' }));
    await screen.findByTestId('review-vrv_01');
    expect(document.querySelectorAll('.review-list .review-item')).toHaveLength(12);
    const more = fake.calls.filter((c) => c.path === '/venues/ven_1/reviews/enumerate').pop();
    expect(more.body.continuationToken).toBe('o:10');
    expect(screen.queryByRole('button', { name: 'Load more reviews' })).toBeNull();

    const select = screen.getByLabelText('Sort reviews');
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['Newest', 'Oldest', 'Highest rated', 'Lowest rated']);
    await userEvent.selectOptions(select, 'RatingAscending');
    await waitFor(() => expect(fake.calls.filter((c) => c.path === '/venues/ven_1/reviews/enumerate').pop().body.ordering).toBe('RatingAscending'));
    await waitFor(() => expect(document.querySelector('.review-list .review-item').dataset.testid).toBe('review-vrv_11'));
  });

  it('lets a linked instructor reply and delete the reply, and members report others', async () => {
    const fake = venueFake({ linked: true, reviews: [makeVenueReview('vrv_1', { body: 'Friendly and well organized.' })] });
    await openVenue(fake);
    const review = await screen.findByTestId('review-vrv_1');
    await userEvent.click(within(review).getByRole('button', { name: 'Reply' }));
    await userEvent.click(within(review).getByRole('button', { name: 'Post reply' }));
    expect(within(review).getByText('Write a reply.')).toBeInTheDocument();
    await userEvent.type(within(review).getByLabelText('Your public reply'), 'Thank you for training with us!');
    await userEvent.click(within(review).getByRole('button', { name: 'Post reply' }));
    const reply = await within(review).findByRole('group', { name: 'Reply from Pat Example' });
    expect(reply).toHaveTextContent('Thank you for training with us!');
    expect(fake.state.reviews[0].reply).toMatchObject({ instructorId: 'ins_1' });
    await userEvent.click(within(review).getByRole('button', { name: 'Delete reply' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete reply' }));
    await waitFor(() => expect(within(review).queryByRole('group', { name: 'Reply from Pat Example' })).toBeNull());

    await userEvent.click(within(review).getByRole('button', { name: 'Report review' }));
    expect(within(screen.getByRole('dialog')).getByText('Why are you reporting this?')).toBeInTheDocument();
  });

  it('shows an existing instructor reply to members without reply actions', async () => {
    const reply = { body: 'See you on the mat.', author: { ...kim, id: 'usr_9' }, instructorId: 'ins_1', instructorName: 'Pat Example', createdUtc: '2026-11-06T00:00:00Z', editedUtc: null };
    const fake = venueFake({ reviews: [makeVenueReview('vrv_1', { reply })] });
    await openVenue(fake);
    const review = await screen.findByTestId('review-vrv_1');
    expect(within(review).getByRole('group', { name: 'Reply from Pat Example' })).toHaveTextContent('See you on the mat.');
    expect(within(review).queryByRole('button', { name: 'Reply' })).toBeNull();
    expect(within(review).queryByRole('button', { name: 'Delete reply' })).toBeNull();
  });

  it('is hidden when the property or the venue turns reviews off', async () => {
    const off = await openVenue(venueFake(), { siteExtra: { reviews: { enabled: false } } });
    expect(screen.queryByRole('heading', { name: 'Reviews' })).toBeNull();
    off.view.unmount();
    await openVenue(venueFake({ reviewsEnabled: false }));
    expect(screen.queryByRole('heading', { name: 'Reviews' })).toBeNull();
  });

  it('opens a review from a notification link even past the first page', async () => {
    expect(notificationHref({ type: 'VenueReview', path: '/venues/ven_1?review=vrv_1' })).toBe('/app/venues/ven_1?review=vrv_1');
    expect(notificationHref({ type: 'VenueReviewReply', path: '/venues/ven_1?review=vrv_1' })).toBe('/app/venues/ven_1?review=vrv_1');
    const reviews = Array.from({ length: 11 }, (_, i) => makeVenueReview('vrv_' + String(i + 1).padStart(2, '0'), {
      author: { ...kim, id: 'usr_r' + i }, createdUtc: '2026-11-' + String(i + 1).padStart(2, '0') + 'T00:00:00Z'
    }));
    await openVenue(venueFake({ reviews }), { path: '/app/venues/ven_1?review=vrv_01' });
    const linked = await screen.findByTestId('review-vrv_01');
    expect(linked).toHaveClass('is-highlighted');
    expect(linked).toHaveTextContent('Linked review');
  });
});

describe('venue media', () => {
  const staffPhoto = makeFakeMedia('med_staff', { altText: 'Front door' });

  it('shows staff photos first, then member items, with remove and report by viewer flags and a keyboard lightbox', async () => {
    const fake = venueFake({
      photos: [staffPhoto],
      mediaItems: [
        makeVenueMediaItem('vmd_1', { uploader: kim, createdUtc: '2026-11-04T00:00:00Z' }),
        makeVenueMediaItem('vmd_2', { uploader: sam, createdUtc: '2026-11-05T00:00:00Z' }),
        makeVenueMediaItem('vmd_3', { uploader: alex, media: makeFakeMedia('med_v', { kind: 'Video' }), createdUtc: '2026-11-03T00:00:00Z' })
      ]
    });
    await openVenue(fake);
    const gallery = await screen.findByRole('list', { name: '4 photos and videos' });
    const tiles = within(gallery).getAllByRole('listitem');
    expect(tiles.map((t) => t.dataset.testid)).toEqual(['venue-media-med_staff', 'venue-media-vmd_2', 'venue-media-vmd_1', 'venue-media-vmd_3']);
    expect(within(tiles[0]).queryByRole('button', { name: /Remove|Report/ })).toBeNull();
    expect(within(tiles[1]).getByRole('button', { name: 'Remove photo 2' })).toBeInTheDocument();
    expect(within(tiles[1]).queryByRole('button', { name: 'Report photo 2' })).toBeNull();
    expect(within(tiles[2]).queryByRole('button', { name: 'Remove photo 3' })).toBeNull();
    expect(within(tiles[2]).getByRole('button', { name: 'Report photo 3' })).toBeInTheDocument();
    expect(within(tiles[3]).getByRole('button', { name: 'Open video 4 of 4' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Add photos/ })).toBeInTheDocument();

    await userEvent.click(within(tiles[0]).getByRole('button', { name: 'Open photo 1 of 4: Front door' }));
    expect(screen.getByRole('dialog', { name: 'Photo 1 of 4' })).toHaveTextContent('Listing photo');
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('dialog', { name: 'Photo 2 of 4' })).toHaveTextContent('Added by Sam Rivera');
    await userEvent.keyboard('{ArrowLeft}{ArrowLeft}');
    expect(screen.getByRole('dialog', { name: 'Video 4 of 4' })).toBeInTheDocument();
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('dialog', { name: 'Photo 1 of 4' })).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();

    await userEvent.click(within(tiles[2]).getByRole('button', { name: 'Report photo 3' }));
    expect(within(screen.getByRole('dialog')).getByText('Why are you reporting this?')).toBeInTheDocument();
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    await userEvent.click(within(tiles[1]).getByRole('button', { name: 'Remove photo 2' }));
    const confirm = screen.getByRole('dialog', { name: 'Delete your photo or video?' });
    expect(within(confirm).queryByLabelText('Reason (optional)')).toBeNull();
    await userEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(screen.queryByTestId('venue-media-vmd_2')).toBeNull());
    expect(fake.state.mediaItems.find((x) => x.id === 'vmd_2').deletedUtc).toBeTruthy();
  });

  it('asks a linked instructor for an optional reason when removing a member item', async () => {
    const fake = venueFake({ linked: true, mediaItems: [makeVenueMediaItem('vmd_1', { uploader: kim })] });
    await openVenue(fake);
    await userEvent.click(await screen.findByRole('button', { name: 'Remove photo 1' }));
    const dialog = screen.getByRole('dialog', { name: 'Remove this photo or video?' });
    await userEvent.type(within(dialog).getByLabelText('Reason (optional)'), 'Not this club');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(fake.state.mediaItems[0]).toMatchObject({ moderationState: 'Removed', removedReason: 'Not this club' }));
    expect(screen.queryByTestId('venue-media-vmd_1')).toBeNull();
  });

  it('loads more member media and hides adding when member media is off', async () => {
    const items = Array.from({ length: 26 }, (_, i) => makeVenueMediaItem('vmd_' + String(i + 1).padStart(2, '0'), { uploader: kim, createdUtc: '2026-11-' + String(i + 1).padStart(2, '0') + 'T00:00:00Z' }));
    const first = await openVenue(venueFake({ mediaItems: items }));
    await screen.findByTestId('venue-media-vmd_26');
    expect(screen.queryByTestId('venue-media-vmd_01')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Load more' }));
    expect(await screen.findByTestId('venue-media-vmd_01')).toBeInTheDocument();
    first.view.unmount();

    await openVenue(venueFake({ mediaItems: items }), { siteExtra: { memberMedia: false } });
    expect(screen.queryByRole('button', { name: /^Add photos/ })).toBeNull();
    expect(screen.getByRole('heading', { name: 'Multimedia' })).toBeInTheDocument();
  });

  it('hides adding when the viewer may not add media', async () => {
    const fake = venueFake({ memberMediaEnabled: false, mediaItems: [makeVenueMediaItem('vmd_1')] });
    await openVenue(fake);
    await screen.findByTestId('venue-media-vmd_1');
    expect(screen.queryByRole('button', { name: /^Add photos/ })).toBeNull();
  });

  it('uploads through the media flow and attaches the uploaded ids', async () => {
    const fake = venueFake();
    const media = { id: 'med_up', kind: 'Image', status: 'Uploading', altText: null, image: null };
    const ready = { ...media, status: 'Ready', image: { variants: { thumb: { width: 320, height: 240, webpUrl: '/m/t.webp', jpegUrl: '/m/t.jpg' } } } };
    const uploads = (call) => {
      if (call.path === '/api/v1/media/uploads') return json({ media, upload: { mode: 'Single', single: { method: 'PUT', url: 'https://store.test/1', headers: {} }, multipart: null, uploadedParts: [] } }, 201);
      if (call.path === '/api/v1/media/med_up/complete') return json({ ...media, status: 'Processing' }, 202);
      if (call.path === '/api/v1/media/enumerate') return json({ objects: [ready], endOfResults: true });
      return undefined;
    };
    const { fetchImpl } = phase4Fetch([fake], makeMe(), uploads);
    const put = async ({ onProgress, body }) => { if (onProgress) onProgress(body.size || 0); return { etag: '"e1"' }; };
    const Harness = () => {
      const { client } = useAuth();
      const [venue, setVenue] = useState(null);
      useEffect(() => { client.getVenue('ven_1').then(setVenue); }, [client]);
      if (!venue) return null;
      return <VenueMediaSection venue={venue} uploadOptions={{ put, sleep: async () => {} }} />;
    };
    await renderP4(<Harness />, { fetchImpl, site: site() });
    await screen.findByRole('heading', { name: 'Multimedia' });
    expect(await screen.findByText('No photos or videos yet.')).toBeInTheDocument();
    await userEvent.upload(screen.getByLabelText(/^Add photos/), new File([new Uint8Array(10)], 'mat.png', { type: 'image/png' }));
    expect(await screen.findByText('Ready')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Add to this club' }));
    await waitFor(() => expect(fake.state.mediaItems).toHaveLength(1));
    expect(fake.state.mediaItems[0].media.id).toBe('med_up');
    expect(fake.calls.find((c) => c.method === 'POST' && c.path === '/venues/ven_1/media').body.mediaIds).toEqual(['med_up']);
    expect(await screen.findByRole('list', { name: '1 photo or video' })).toBeInTheDocument();
  });
});

describe('profile multimedia', () => {
  it('uploads photos to the profile endpoint and deletes the owned item', async () => {
    const ready = makeFakeMedia('med_profile', { kind: 'Image', status: 'Ready' });
    const items = [];
    const { fetchImpl, calls } = phase4Fetch([], makeMe(), (call) => {
      if (call.path === '/api/v1/media/uploads') return json({ media: { ...ready, status: 'Uploading' }, upload: { mode: 'Single', single: { method: 'PUT', url: 'https://store.test/profile', headers: {} }, multipart: null, uploadedParts: [] } }, 201);
      if (call.path.endsWith('/med_profile/complete')) return json(ready, 202);
      if (call.path === '/api/v1/media/enumerate') return json({ objects: [ready], endOfResults: true });
      if (call.path.endsWith('/users/usr_1/media/enumerate')) return json({ objects: items, endOfResults: true });
      if (call.path.endsWith('/users/usr_1/media') && call.method === 'POST') {
        items.push({ id: 'pmd_1', media: ready, uploader: sam, viewer: { isUploader: true, canRemove: true } });
        return json({ objects: items }, 201);
      }
      if (call.path.endsWith('/users/usr_1/media/pmd_1') && call.method === 'DELETE') return new Response(null, { status: 204 });
      return undefined;
    });
    await renderP4(<VenueMediaSection profile venue={{ id: 'usr_1', viewer: { canAddMedia: true } }} uploadOptions={{ put: async () => ({ etag: 'e1' }), sleep: async () => {} }} />, { fetchImpl, site: site() });
    await userEvent.upload(screen.getByLabelText(/^Add photos/), new File([new Uint8Array(10)], 'photo.png', { type: 'image/png' }));
    await screen.findByText('Ready');
    await userEvent.click(screen.getByRole('button', { name: 'Add to profile' }));
    await screen.findByTestId('venue-media-pmd_1');
    expect(calls.some((c) => c.path.endsWith('/users/usr_1/media') && c.body.mediaIds[0] === 'med_profile')).toBe(true);
    await userEvent.click(screen.getByRole('button', { name: 'Remove photo 1' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(screen.queryByTestId('venue-media-pmd_1')).toBeNull());
  });

  it('opens a public-page report link for the exact item and submits ProfileMedia', async () => {
    const { fetchImpl, calls } = phase4Fetch([], makeMe(), (call) => {
      if (call.path.endsWith('/media/enumerate')) return json({ objects: [], endOfResults: true });
      if (call.path === '/api/v1/reports') return json({ id: 'rpt_profile' }, 201);
      return undefined;
    });
    await renderP4(<VenueMediaSection profile venue={{ id: 'usr_2', viewer: { canAddMedia: false } }} />, { fetchImpl, site: site(), path: '/test?reportMedia=pmd_old' });
    expect(screen.queryByRole('button', { name: 'Add to profile' })).toBeNull();
    const dialog = screen.getByRole('dialog');
    await userEvent.click(within(dialog).getByLabelText(/Spam/));
    await userEvent.click(within(dialog).getByRole('button', { name: /Send report/ }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/reports')?.body).toMatchObject({ targetType: 'ProfileMedia', targetId: 'pmd_old', reason: 'Spam' }));
  });
});
