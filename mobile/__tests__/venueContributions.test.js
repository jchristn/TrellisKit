/**
 * Venue reviews and member media on mobile (docs/PHASE4_CONTRACT.md sections 3.12 and 3.13): the
 * star input, the review form, own review actions, ordering and load more, replies, media add,
 * remove, and report gated by the viewer flags, and stars on venue rows. The API client is a
 * local mock (no fake server).
 */
import { fireEvent, render as baseRender, screen, waitFor } from '@testing-library/react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { resolveNotificationPath } from '../src/notifications/deepLinks';
import { directoryStrings } from '../src/content/phase4';
import { getBundledProperty } from '../src/property/property';
import VenueCard from '../src/components/places/VenueCard';
import { StarInput, starIcon } from '../src/components/places/StarRating';
import VenueMedia, { galleryEntries } from '../src/components/places/VenueMedia';
import VenueReviews, { ReviewForm } from '../src/components/places/VenueReviews';

const mockAuth = { api: null, user: null };
jest.mock('../src/auth/AuthProvider', () => ({ useAuth: () => mockAuth }));

const mockLayout = { width: 390, height: 800, isWide: false };
jest.mock('../src/hooks/useLayout', () => ({ ...jest.requireActual('../src/hooks/useLayout'), useLayout: () => mockLayout }));

const mockPropertyState = { property: null };
jest.mock('../src/property', () => {
  const actual = jest.requireActual('../src/property');
  return { ...actual, useProperty: () => mockPropertyState.property || actual.useProperty() };
});

const mockUploads = { items: [], busy: false, failed: false, mediaIds: [], add: jest.fn(), remove: jest.fn(), clear: jest.fn(), setAltText: jest.fn(), setMetadata: jest.fn(), resumeInterrupted: jest.fn() };
jest.mock('../src/media/useUploads', () => ({ ...jest.requireActual('../src/media/useUploads'), useUploads: () => mockUploads }));

const metrics = { frame: { x: 0, y: 0, width: 390, height: 800 }, insets: { top: 0, left: 0, right: 0, bottom: 0 } };
function render(ui) {
  return baseRender(<SafeAreaProvider initialMetrics={metrics}>{ui}</SafeAreaProvider>);
}

const t = directoryStrings(getBundledProperty());
const me = { id: 'usr_1', displayName: 'Test Member', handle: 'tester', roles: [], flags: {} };
const other = { id: 'usr_2', displayName: 'Robin Vale', handle: 'robin' };

function page(objects, continuationToken = null) {
  return { objects, endOfResults: !continuationToken, continuationToken };
}

function makeApi(overrides = {}) {
  return {
    baseUrl: 'https://api.example.test',
    accessToken: 'token',
    enumerateVenueReviews: jest.fn(async () => page([])),
    getVenueReview: jest.fn(async () => null),
    createVenueReview: jest.fn(async (venueId, body) => review('vrv_new', { ...body, author: me, viewer: { isAuthor: true, canEdit: true, canDelete: true } })),
    updateVenueReview: jest.fn(async (venueId, id, body) => review(id, { ...body, author: me, viewer: { isAuthor: true, canEdit: true, canDelete: true } })),
    deleteVenueReview: jest.fn(async () => null),
    replyToVenueReview: jest.fn(async (venueId, id, body) => review(id, { reply: { body, instructorName: 'Coach Example', createdUtc: '2026-11-13T00:00:00Z' }, viewer: { canReply: true, canDeleteReply: true, canReport: false } })),
    deleteVenueReviewReply: jest.fn(async (venueId, id) => review(id, { reply: null, viewer: { canReply: true } })),
    enumerateVenueMedia: jest.fn(async () => page([])),
    addVenueMedia: jest.fn(async () => ({ objects: [] })),
    removeVenueMedia: jest.fn(async () => null),
    post: jest.fn(async () => ({ id: 'rpt_1' })),
    ...overrides
  };
}

function review(id, overrides = {}) {
  return { id, venueId: 'ven_1', author: other, rating: 4, body: 'Friendly and well organized.', editedUtc: null, reply: null, moderationState: 'None', labels: [], tags: {}, viewer: { isAuthor: false, canEdit: false, canDelete: false, canReply: false, canDeleteReply: false, canReport: true, canModerate: false }, createdUtc: '2026-11-10T00:00:00Z', updatedUtc: '2026-11-10T00:00:00Z', ...overrides };
}

function image(id) {
  return { id, kind: 'Image', status: 'Ready', image: { variants: { thumb: { webpUrl: 'https://cdn.example.test/' + id + '-thumb.webp' }, large: { webpUrl: 'https://cdn.example.test/' + id + '-large.webp' } } } };
}

function mediaItem(id, viewer = {}, overrides = {}) {
  return { id, venueId: 'ven_1', media: image('med_' + id), uploader: other, moderationState: 'None', labels: [], tags: {}, viewer: { isUploader: false, canRemove: false, canReport: true, ...viewer }, ...overrides };
}

function venue(overrides = {}) {
  return {
    id: 'ven_1',
    name: 'Downtown Hall',
    photos: [],
    mediaCount: 0,
    ratingAverage: 4.5,
    ratingCount: 2,
    reviews: { enabled: true, averageRating: 4.5, count: 2, distribution: { 1: 0, 2: 0, 3: 0, 4: 1, 5: 1 }, mine: null, enumerate: null },
    viewer: { canReview: true, canAddMedia: true, canManageMedia: false, canReplyToReviews: false },
    ...overrides
  };
}

beforeEach(() => {
  mockAuth.api = makeApi();
  mockAuth.user = me;
  mockPropertyState.property = null;
  mockLayout.isWide = false;
  mockLayout.width = 390;
  Object.assign(mockUploads, { items: [], busy: false, failed: false, mediaIds: [] });
  mockUploads.clear.mockClear();
  mockUploads.remove.mockClear();
});

describe('star input', () => {
  test('is a radio group of five labeled stars with the selection', async () => {
    const onChange = jest.fn();
    await render(<StarInput value={4} onChange={onChange} label="Your rating" testID="stars" />);
    expect(screen.getByTestId('stars').props.accessibilityRole).toBe('radiogroup');
    const four = screen.getByTestId('stars-4');
    expect(four.props.accessibilityRole).toBe('radio');
    expect(four.props.accessibilityLabel).toBe('4 stars');
    expect(four.props.accessibilityState).toMatchObject({ selected: true });
    expect(screen.getByTestId('stars-1').props.accessibilityLabel).toBe('1 star');
    expect(screen.getByTestId('stars-2').props.accessibilityState).toMatchObject({ selected: false });
    await fireEvent.press(screen.getByTestId('stars-2'));
    expect(onChange).toHaveBeenCalledWith(2);
  });

  test('display stars round to halves', () => {
    expect([1, 2, 3, 4, 5].map((n) => starIcon(n, 3.6))).toEqual(['star', 'star', 'star', 'star-half', 'star-outline']);
  });
});

describe('review form', () => {
  const limits = { enabled: true, minLength: 10, maxLength: 2000, replyMaxLength: 2000 };

  test('validates the rating and the length before sending', async () => {
    const onSaved = jest.fn();
    await render(<ReviewForm venueId="ven_1" limits={limits} onSaved={onSaved} testID="form" />);
    await fireEvent.press(screen.getByTestId('form-submit'));
    expect(screen.getByTestId('form-rating-error')).toHaveTextContent('Choose a rating from 1 to 5 stars.');
    expect(screen.getByText('Write at least 10 characters.')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('form-rating-5'));
    await fireEvent.changeText(screen.getByTestId('form-body'), 'Too short');
    await fireEvent.press(screen.getByTestId('form-submit'));
    expect(mockAuth.api.createVenueReview).not.toHaveBeenCalled();
    await fireEvent.changeText(screen.getByTestId('form-body'), '  Great open mat and friendly people.  ');
    await fireEvent.press(screen.getByTestId('form-submit'));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(mockAuth.api.createVenueReview).toHaveBeenCalledWith('ven_1', { rating: 5, body: 'Great open mat and friendly people.' });
  });

  test('shows the server error code text', async () => {
    mockAuth.api.createVenueReview.mockRejectedValueOnce(Object.assign(new Error('x'), { status: 409, code: 'AlreadyReviewed' }));
    await render(<ReviewForm venueId="ven_1" limits={limits} onSaved={jest.fn()} testID="form" />);
    await fireEvent.press(screen.getByTestId('form-rating-3'));
    await fireEvent.changeText(screen.getByTestId('form-body'), 'A perfectly fine place to train.');
    await fireEvent.press(screen.getByTestId('form-submit'));
    expect(await screen.findByText(t.errors.AlreadyReviewed)).toBeOnTheScreen();
  });
});

describe('venue reviews section', () => {
  test('shows the summary and distribution, writes a review, then reloads the list and the venue', async () => {
    const onChanged = jest.fn();
    await render(<VenueReviews venue={venue()} onChanged={onChanged} />);
    expect(screen.getByTestId('reviews-summary-average', { includeHiddenElements: true })).toHaveTextContent('4.5');
    expect(screen.getByTestId('reviews-summary-stars').props.accessibilityLabel).toBe('Rated 4.5 out of 5, 2 reviews');
    expect(screen.getByTestId('reviews-summary-row-5').props.accessibilityLabel).toBe('5 stars, 1 review, 50 percent');
    await waitFor(() => expect(mockAuth.api.enumerateVenueReviews).toHaveBeenCalledTimes(1));
    await fireEvent.press(screen.getByTestId('reviews-form-rating-4'));
    await fireEvent.changeText(screen.getByTestId('reviews-form-body'), 'Clean, friendly, and well run.');
    await fireEvent.press(screen.getByTestId('reviews-form-submit'));
    expect(await screen.findByTestId('reviews-mine')).toBeOnTheScreen();
    expect(screen.getByTestId('reviews-mine-body')).toHaveTextContent('Clean, friendly, and well run.');
    expect(onChanged).toHaveBeenCalled();
    expect(mockAuth.api.enumerateVenueReviews).toHaveBeenCalledTimes(2);
  });

  test('without canReview there is no form; disabled reviews hide the section', async () => {
    const view = await render(<VenueReviews venue={venue({ viewer: { canReview: false } })} />);
    expect(screen.queryByTestId('reviews-form')).toBeNull();
    await view.unmount();
    await render(<VenueReviews venue={venue({ reviews: { enabled: false, count: 0 } })} />);
    expect(screen.queryByTestId('reviews')).toBeNull();
  });

  test('own review edit and delete follow its viewer flags', async () => {
    const mine = review('vrv_me', { author: me, rating: 3, body: 'It was fine for a visit.', viewer: { isAuthor: true, canEdit: true, canDelete: true } });
    await render(<VenueReviews venue={venue({ reviews: { ...venue().reviews, mine } })} />);
    await fireEvent.press(screen.getByTestId('reviews-mine-edit'));
    expect(screen.getByTestId('reviews-form-rating-3').props.accessibilityState).toMatchObject({ selected: true });
    await fireEvent.press(screen.getByTestId('reviews-form-rating-5'));
    await fireEvent.press(screen.getByTestId('reviews-form-submit'));
    await waitFor(() => expect(mockAuth.api.updateVenueReview).toHaveBeenCalledWith('ven_1', 'vrv_me', { rating: 5, body: 'It was fine for a visit.' }));
    expect(await screen.findByTestId('reviews-mine')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('reviews-mine-delete'));
    await fireEvent.press(screen.getByTestId('reviews-delete-confirm-confirm'));
    await waitFor(() => expect(mockAuth.api.deleteVenueReview).toHaveBeenCalledWith('ven_1', 'vrv_me'));
    expect(await screen.findByTestId('reviews-form')).toBeOnTheScreen();
  });

  test('own review without edit or delete flags shows no actions', async () => {
    const mine = review('vrv_me', { author: me, viewer: { isAuthor: true, canEdit: false, canDelete: false } });
    await render(<VenueReviews venue={venue({ reviews: { ...venue().reviews, mine } })} />);
    expect(screen.getByTestId('reviews-mine')).toBeOnTheScreen();
    expect(screen.queryByTestId('reviews-mine-edit')).toBeNull();
    expect(screen.queryByTestId('reviews-mine-delete')).toBeNull();
  });

  test('orderings re-enumerate and load more pages with the token', async () => {
    mockAuth.api.enumerateVenueReviews.mockImplementation(async (venueId, query) => (query.continuationToken ? page([review('vrv_2', { body: 'Second page review here.' })]) : page([review('vrv_1')], 'tok-2')));
    await render(<VenueReviews venue={venue({ viewer: { canReview: false } })} />);
    expect(await screen.findByTestId('review-vrv_1')).toBeOnTheScreen();
    expect(mockAuth.api.enumerateVenueReviews.mock.calls[0][1]).toMatchObject({ ordering: 'CreatedDescending', maxResults: 10 });
    await fireEvent.press(screen.getByTestId('reviews-more'));
    expect(await screen.findByTestId('review-vrv_2')).toBeOnTheScreen();
    expect(mockAuth.api.enumerateVenueReviews.mock.calls[1][1]).toMatchObject({ continuationToken: 'tok-2' });
    for (const [key, label] of [['RatingAscending', 'Lowest'], ['RatingDescending', 'Highest'], ['CreatedAscending', 'Oldest']]) {
      expect(screen.getByTestId('reviews-order-' + key)).toHaveTextContent(label);
      await fireEvent.press(screen.getByTestId('reviews-order-' + key));
      await waitFor(() => expect(mockAuth.api.enumerateVenueReviews.mock.calls.at(-1)[1]).toMatchObject({ ordering: key }));
    }
  });

  test('a linked instructor replies and deletes the reply; report opens the report sheet', async () => {
    mockAuth.api.enumerateVenueReviews.mockResolvedValue(page([review('vrv_1', { viewer: { canReply: true, canDeleteReply: false, canReport: true } })]));
    await render(<VenueReviews venue={venue()} />);
    await fireEvent.press(await screen.findByTestId('review-vrv_1-reply-open'));
    await fireEvent.press(screen.getByTestId('review-vrv_1-reply-form-submit'));
    expect(screen.getByText('Write a reply first.')).toBeOnTheScreen();
    await fireEvent.changeText(screen.getByTestId('review-vrv_1-reply-form-body'), 'Thanks for visiting!');
    await fireEvent.press(screen.getByTestId('review-vrv_1-reply-form-submit'));
    await waitFor(() => expect(mockAuth.api.replyToVenueReview).toHaveBeenCalledWith('ven_1', 'vrv_1', 'Thanks for visiting!'));
    expect(await screen.findByTestId('review-vrv_1-reply')).toHaveTextContent(/Reply from Coach Example/);
    expect(screen.getByTestId('review-vrv_1-reply-open')).toHaveTextContent('Edit reply');
    await fireEvent.press(screen.getByTestId('review-vrv_1-reply-delete'));
    await fireEvent.press(screen.getByTestId('review-vrv_1-reply-confirm-confirm'));
    await waitFor(() => expect(mockAuth.api.deleteVenueReviewReply).toHaveBeenCalledWith('ven_1', 'vrv_1'));
    await waitFor(() => expect(screen.queryByTestId('review-vrv_1-reply')).toBeNull());
  });

  test('report on a review sends targetType VenueReview', async () => {
    mockAuth.api.enumerateVenueReviews.mockResolvedValue(page([review('vrv_1')]));
    await render(<VenueReviews venue={venue()} />);
    expect(screen.queryByTestId('review-vrv_1-reply-open')).toBeNull();
    await fireEvent.press(await screen.findByTestId('review-vrv_1-report'));
    await fireEvent.press(screen.getByTestId('report-reason-Inaccurate'));
    await fireEvent.press(screen.getByTestId('report-submit'));
    await waitFor(() => expect(mockAuth.api.post).toHaveBeenCalledWith('/reports', expect.objectContaining({ targetType: 'VenueReview', targetId: 'vrv_1', reason: 'Inaccurate' })));
  });

  test('a notification review shows first; signed-out callers see only the summary', async () => {
    mockAuth.api.getVenueReview.mockResolvedValue(review('vrv_9', { body: 'The one from the notification.' }));
    const view = await render(<VenueReviews venue={venue()} focusReviewId="vrv_9" />);
    expect(await screen.findByTestId('reviews-focused-item')).toBeOnTheScreen();
    expect(mockAuth.api.getVenueReview).toHaveBeenCalledWith('ven_1', 'vrv_9');
    await view.unmount();
    mockAuth.user = null;
    mockAuth.api.enumerateVenueReviews.mockClear();
    await render(<VenueReviews venue={venue()} />);
    expect(screen.getByTestId('reviews-signed-out')).toBeOnTheScreen();
    expect(screen.queryByTestId('reviews-form')).toBeNull();
    expect(mockAuth.api.enumerateVenueReviews).not.toHaveBeenCalled();
  });

  test('the summary sits beside the breakdown on wide windows', async () => {
    mockLayout.isWide = true;
    await render(<VenueReviews venue={venue()} />);
    expect(screen.getByTestId('reviews-summary')).toHaveStyle({ flexDirection: 'row' });
  });
});

describe('venue media section', () => {
  test('staff photos come first, then member items', () => {
    const entries = galleryEntries([image('staff')], [mediaItem('vmd_1')]);
    expect(entries.map((e) => (e.item ? e.item.id : 'staff'))).toEqual(['staff', 'vmd_1']);
  });

  test('add is offered with canAddMedia and attaches the uploaded ids', async () => {
    const added = mediaItem('vmd_new', { isUploader: true, canRemove: true });
    mockAuth.api.addVenueMedia.mockResolvedValue({ objects: [added] });
    await render(<VenueMedia venue={venue({ photos: [image('staff')] })} />);
    await fireEvent.press(screen.getByTestId('venue-media-add'));
    await fireEvent.press(screen.getByTestId('venue-media-add-sheet-submit'));
    expect(screen.getByText(t.media.nothingPicked)).toBeOnTheScreen();
    Object.assign(mockUploads, { mediaIds: ['med_a', 'med_b'] });
    await fireEvent.press(screen.getByTestId('venue-media-add-sheet-submit'));
    await waitFor(() => expect(mockAuth.api.addVenueMedia).toHaveBeenCalledWith('ven_1', ['med_a', 'med_b']));
    expect(mockUploads.clear).toHaveBeenCalled();
    expect(await screen.findByTestId('venue-media-item-1')).toBeOnTheScreen();
    expect(screen.queryByTestId('venue-media-add-sheet')).toBeNull();
  });

  test('no add without canAddMedia, or when member media is off', async () => {
    const view = await render(<VenueMedia venue={venue({ photos: [image('staff')], viewer: { canAddMedia: false } })} />);
    expect(screen.getByTestId('venue-media-item-0')).toBeOnTheScreen();
    expect(screen.queryByTestId('venue-media-add')).toBeNull();
    await view.unmount();
    mockPropertyState.property = { ...getBundledProperty(), directory: { ...(getBundledProperty().directory || {}), memberMedia: false } };
    await render(<VenueMedia venue={venue({ photos: [image('staff')] })} />);
    expect(screen.getByTestId('venue-media-item-0')).toBeOnTheScreen();
    expect(screen.queryByTestId('venue-media-add')).toBeNull();
  });

  test('remove and report follow each item viewer flags; a moderator gives a reason', async () => {
    mockAuth.api.enumerateVenueMedia.mockResolvedValue(page([mediaItem('vmd_1', { canRemove: true, canReport: true }), mediaItem('vmd_2', { canRemove: false, canReport: false })]));
    await render(<VenueMedia venue={venue({ mediaCount: 2 })} />);
    await fireEvent.press(await screen.findByTestId('venue-media-item-1'));
    expect(screen.getByTestId('venue-media-viewer-position')).toHaveTextContent('2 of 2');
    expect(screen.queryByTestId('venue-media-viewer-remove')).toBeNull();
    expect(screen.queryByTestId('venue-media-viewer-report')).toBeNull();
    await fireEvent.press(screen.getByTestId('venue-media-viewer-previous'));
    expect(screen.getByTestId('venue-media-viewer-position')).toHaveTextContent('1 of 2');
    await fireEvent.press(screen.getByTestId('venue-media-viewer-remove'));
    await fireEvent.changeText(screen.getByTestId('venue-media-remove-reason'), 'Not this venue');
    await fireEvent.press(screen.getByTestId('venue-media-remove-confirm'));
    await waitFor(() => expect(mockAuth.api.removeVenueMedia).toHaveBeenCalledWith('ven_1', 'vmd_1', 'Not this venue'));
    await waitFor(() => expect(screen.queryByTestId('venue-media-item-1')).toBeNull());
  });

  test('the uploader removes without a reason; report sends targetType VenueMedia', async () => {
    mockAuth.api.enumerateVenueMedia.mockResolvedValue(page([mediaItem('vmd_1', { isUploader: true, canRemove: true, canReport: false }), mediaItem('vmd_2', { canReport: true })]));
    await render(<VenueMedia venue={venue({ mediaCount: 2 })} />);
    await fireEvent.press(await screen.findByTestId('venue-media-item-1'));
    await fireEvent.press(screen.getByTestId('venue-media-viewer-report'));
    await fireEvent.press(screen.getByTestId('report-reason-Spam'));
    await fireEvent.press(screen.getByTestId('report-submit'));
    await waitFor(() => expect(mockAuth.api.post).toHaveBeenCalledWith('/reports', expect.objectContaining({ targetType: 'VenueMedia', targetId: 'vmd_2', reason: 'Spam' })));
    await fireEvent.press(screen.getByTestId('report-done'));
    await fireEvent.press(screen.getByTestId('venue-media-item-0'));
    await fireEvent.press(screen.getByTestId('venue-media-viewer-remove'));
    expect(screen.queryByTestId('venue-media-remove-reason')).toBeNull();
    await fireEvent.press(screen.getByTestId('venue-media-remove-confirm'));
    await waitFor(() => expect(mockAuth.api.removeVenueMedia).toHaveBeenCalledWith('ven_1', 'vmd_1', undefined));
  });

  test('member media pages with load more', async () => {
    mockAuth.api.enumerateVenueMedia.mockImplementation(async (venueId, query) => (query.continuationToken ? page([mediaItem('vmd_2')]) : page([mediaItem('vmd_1')], 'm-2')));
    await render(<VenueMedia venue={venue({ mediaCount: 2 })} />);
    await fireEvent.press(await screen.findByTestId('venue-media-more'));
    expect(await screen.findByTestId('venue-media-item-1')).toBeOnTheScreen();
    expect(mockAuth.api.enumerateVenueMedia.mock.calls[1][1]).toMatchObject({ continuationToken: 'm-2' });
    expect(screen.queryByTestId('venue-media-more')).toBeNull();
  });
});

describe('venue rows and notifications', () => {
  test('a rated row shows compact stars with an accessible label', async () => {
    const view = await render(<VenueCard venue={{ id: 'ven_1', name: 'Downtown Hall', ratingAverage: 4.25, ratingCount: 12 }} terms={t} onPress={jest.fn()} />);
    expect(screen.getByTestId('venue-ven_1-rating').props.accessibilityLabel).toBe('Rated 4.3 out of 5, 12 reviews');
    expect(screen.getByTestId('venue-ven_1').props.accessibilityLabel).toContain('Rated 4.3 out of 5, 12 reviews');
    await view.unmount();
    await render(<VenueCard venue={{ id: 'ven_2', name: 'Uptown Room', ratingAverage: null, ratingCount: 0 }} terms={t} onPress={jest.fn()} />);
    expect(screen.queryByTestId('venue-ven_2-rating')).toBeNull();
  });

  test('review notifications open the venue', () => {
    expect(resolveNotificationPath('/venues/ven_1?review=vrv_2')).toBe('/venues/ven_1?review=vrv_2');
  });
});

describe('profile multimedia', () => {
  test('uses profile upload and delete endpoints and labels public visibility', async () => {
    mockAuth.api.enumerateProfileMedia = jest.fn(async () => page([]));
    mockAuth.api.addProfileMedia = jest.fn(async () => ({ objects: [mediaItem('pmd_1', { isUploader: true, canRemove: true })] }));
    mockAuth.api.removeProfileMedia = jest.fn(async () => null);
    await render(<VenueMedia profile venue={{ id: me.id, viewer: { canAddMedia: true } }} />);
    await fireEvent.press(screen.getByTestId('venue-media-add'));
    expect(screen.getByText(/including its public page when enabled/)).toBeOnTheScreen();
    mockUploads.mediaIds = ['med_profile'];
    await fireEvent.press(screen.getByTestId('venue-media-add-sheet-submit'));
    await waitFor(() => expect(mockAuth.api.addProfileMedia).toHaveBeenCalledWith(me.id, ['med_profile']));
    await fireEvent.press(await screen.findByTestId('venue-media-item-0'));
    await fireEvent.press(screen.getByTestId('venue-media-viewer-remove'));
    await fireEvent.press(screen.getByTestId('venue-media-remove-confirm'));
    await waitFor(() => expect(mockAuth.api.removeProfileMedia).toHaveBeenCalledWith(me.id, 'pmd_1'));
  });

  test('reports the exact profile item linked from a public page', async () => {
    mockAuth.api.enumerateProfileMedia = jest.fn(async () => page([]));
    await render(<VenueMedia profile initialReportId="pmd_old" venue={{ id: other.id, viewer: {} }} />);
    await fireEvent.press(screen.getByTestId('report-reason-Spam'));
    await fireEvent.press(screen.getByTestId('report-submit'));
    await waitFor(() => expect(mockAuth.api.post).toHaveBeenCalledWith('/reports', expect.objectContaining({ targetType: 'ProfileMedia', targetId: 'pmd_old', reason: 'Spam' })));
  });
});
