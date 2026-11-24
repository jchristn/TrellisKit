import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createDirectoryFake, makeInstructor, makeVenue, makeVenueMediaItem, makeVenueReview } from '../src/testing/index.js';
import { fakeUser } from '../src/testing/forumsFake.js';

const me = fakeUser('usr_1', 'Sam Rivera');

function seeded(extra = {}) {
  return createDirectoryFake({
    me,
    venues: [makeVenue('ven_1'), makeVenue('ven_2')],
    instructors: [makeInstructor('ins_1', { displayName: 'Coach One', linkedUser: fakeUser('usr_9', 'Coach One') })],
    links: [{ venueId: 'ven_1', instructorId: 'ins_1' }],
    reviews: [
      makeVenueReview('vrv_1', { rating: 5, createdUtc: '2026-11-01T00:00:00Z' }),
      makeVenueReview('vrv_2', { rating: 2, author: fakeUser('usr_3', 'Alex Chen'), createdUtc: '2026-11-03T00:00:00Z' }),
      makeVenueReview('vrv_3', { rating: 5, author: fakeUser('usr_4', 'Jo Lee'), createdUtc: '2026-11-04T00:00:00Z' })
    ],
    mediaItems: [makeVenueMediaItem('vmd_1'), makeVenueMediaItem('vmd_2', { uploader: me, createdUtc: '2026-11-06T00:00:00Z' })],
    ...extra
  });
}

const call = (fake, method, path, body) => fake.handle({ method, path: '/api/v1' + path, body });

test('venues carry rating and media aggregates, the reviews block, and the new viewer flags', () => {
  const fake = seeded();
  const venue = call(fake, 'GET', '/venues/ven_1').body;
  assert.equal(venue.ratingAverage, 4);
  assert.equal(venue.ratingCount, 3);
  assert.equal(venue.mediaCount, 2);
  assert.deepEqual(venue.reviews.distribution, { 1: 0, 2: 1, 3: 0, 4: 0, 5: 2 });
  assert.equal(venue.reviews.enabled, true);
  assert.equal(venue.reviews.mine, null);
  assert.deepEqual({ ...venue.viewer }, { ...venue.viewer, canReview: true, canAddMedia: true, canManageMedia: false, canReplyToReviews: false });
  const row = call(fake, 'POST', '/venues/enumerate', {}).body.objects.find((v) => v.id === 'ven_2');
  assert.equal(row.ratingAverage, null);
  assert.equal(row.ratingCount, 0);
  assert.equal(row.reviews, undefined);
});

test('review enumeration honours the four orderings and pages', () => {
  const fake = seeded();
  const ids = (ordering) => call(fake, 'POST', '/venues/ven_1/reviews/enumerate', { ordering }).body.objects.map((r) => r.id);
  assert.deepEqual(ids(undefined), ['vrv_3', 'vrv_2', 'vrv_1']);
  assert.deepEqual(ids('CreatedAscending'), ['vrv_1', 'vrv_2', 'vrv_3']);
  assert.deepEqual(ids('RatingDescending'), ['vrv_3', 'vrv_1', 'vrv_2']);
  assert.deepEqual(ids('RatingAscending'), ['vrv_2', 'vrv_3', 'vrv_1']);
  const first = call(fake, 'POST', '/venues/ven_1/reviews/enumerate', { maxResults: 2 }).body;
  assert.equal(first.objects.length, 2);
  const second = call(fake, 'POST', '/venues/ven_1/reviews/enumerate', { maxResults: 2, continuationToken: first.continuationToken }).body;
  assert.deepEqual(second.objects.map((r) => r.id), ['vrv_1']);
  assert.equal(second.endOfResults, true);
  assert.deepEqual(call(fake, 'POST', '/venues/ven_1/reviews/enumerate', { filters: { rating: 5 } }).body.objects.map((r) => r.id), ['vrv_3', 'vrv_1']);
});

test('one review per member: create, 409 AlreadyReviewed, validation, update, delete, and write again', () => {
  const fake = seeded();
  assert.equal(call(fake, 'POST', '/venues/ven_1/reviews', { rating: 6, body: 'Short' }).status, 400);
  const created = call(fake, 'POST', '/venues/ven_1/reviews', { rating: 4, body: 'A friendly place to train.' });
  assert.equal(created.status, 201);
  assert.equal(created.body.viewer.isAuthor, true);
  assert.equal(created.body.viewer.canReport, false);
  const again = call(fake, 'POST', '/venues/ven_1/reviews', { rating: 3, body: 'Writing a second review.' });
  assert.equal(again.status, 409);
  assert.equal(again.body.error, 'AlreadyReviewed');
  assert.equal(call(fake, 'GET', '/venues/ven_1').body.reviews.mine.id, created.body.id);
  const updated = call(fake, 'PUT', '/venues/ven_1/reviews/' + created.body.id, { rating: 5 });
  assert.equal(updated.body.rating, 5);
  assert.ok(updated.body.editedUtc);
  assert.equal(call(fake, 'PUT', '/venues/ven_1/reviews/vrv_1', { rating: 1 }).status, 403);
  assert.equal(call(fake, 'DELETE', '/venues/ven_1/reviews/' + created.body.id).status, 204);
  assert.equal(call(fake, 'GET', '/venues/ven_1').body.reviews.mine, null);
  assert.equal(call(fake, 'POST', '/venues/ven_1/reviews', { rating: 3, body: 'Back again with a new view.' }).status, 201);
  const off = seeded({ reviewsEnabled: false });
  assert.equal(call(off, 'POST', '/venues/ven_1/reviews', { rating: 3, body: 'Reviews are off here.' }).body.error, 'ReviewsDisabled');
  assert.equal(call(off, 'GET', '/venues/ven_1').body.viewer.canReview, false);
});

test('linked instructors reply and remove their reply; others may not', () => {
  const coach = fakeUser('usr_9', 'Coach One');
  const fake = seeded({ me: coach });
  const venue = call(fake, 'GET', '/venues/ven_1').body;
  assert.equal(venue.viewer.canReplyToReviews, true);
  assert.equal(venue.viewer.canManageMedia, true);
  const listed = call(fake, 'POST', '/venues/ven_1/reviews/enumerate', {}).body.objects[0];
  assert.equal(listed.viewer.canReply, true);
  const replied = call(fake, 'PUT', '/venues/ven_1/reviews/vrv_1/reply', { body: 'Thank you!' });
  assert.equal(replied.status, 200);
  assert.equal(replied.body.reply.instructorName, 'Coach One');
  assert.equal(replied.body.viewer.canDeleteReply, true);
  assert.equal(call(fake, 'DELETE', '/venues/ven_1/reviews/vrv_1/reply').body.reply, null);
  const member = seeded();
  assert.equal(call(member, 'PUT', '/venues/ven_1/reviews/vrv_1/reply', { body: 'Hi' }).status, 403);
});

test('member media: list, add, uploader delete, manager remove with a reason, and the off switch', () => {
  const fake = seeded();
  const list = call(fake, 'POST', '/venues/ven_1/media/enumerate', {}).body.objects;
  assert.deepEqual(list.map((x) => x.id), ['vmd_2', 'vmd_1']);
  assert.deepEqual(list[0].viewer, { isUploader: true, canRemove: true, canReport: false });
  assert.deepEqual(list[1].viewer, { isUploader: false, canRemove: false, canReport: true });
  const added = call(fake, 'POST', '/venues/ven_1/media', { mediaIds: ['med_new'] });
  assert.equal(added.status, 201);
  assert.equal(added.body.objects[0].media.id, 'med_new');
  assert.equal(call(fake, 'POST', '/venues/ven_1/media', { mediaIds: ['med_new'] }).status, 409);
  assert.equal(call(fake, 'POST', '/venues/ven_1/media', { mediaIds: [] }).status, 400);
  assert.equal(call(fake, 'DELETE', '/venues/ven_1/media/vmd_1').status, 403);
  assert.equal(call(fake, 'DELETE', '/venues/ven_1/media/vmd_2').status, 204);
  assert.equal(call(fake, 'GET', '/venues/ven_1').body.mediaCount, 2);
  const coach = seeded({ me: fakeUser('usr_9', 'Coach One') });
  assert.equal(call(coach, 'DELETE', '/venues/ven_1/media/vmd_1', { reason: 'Not this venue' }).status, 204);
  assert.equal(coach.state.mediaItems.find((x) => x.id === 'vmd_1').moderationState, 'Removed');
  assert.equal(call(coach, 'POST', '/venues/ven_1/media/enumerate', {}).body.objects.length, 1);
  const off = seeded({ memberMediaEnabled: false });
  assert.equal(call(off, 'POST', '/venues/ven_1/media', { mediaIds: ['med_x'] }).body.error, 'MemberMediaDisabled');
  assert.equal(call(off, 'GET', '/venues/ven_1').body.viewer.canAddMedia, false);
});
