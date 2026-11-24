// Directory: venues with pins and programs, instructors linked to venues (and to members),
// member reviews (with an optional instructor reply) and member photos or videos on venues, and a member
// submission left pending in the review queue.

import { pick } from '../util.mjs';

export async function seedDirectory(ctx) {
  if (ctx.property.features?.directory === false) return;
  const venues = ctx.seed.venues ?? [];
  const instructors = ctx.seed.instructors ?? [];
  const submissions = ctx.seed.directorySubmissions ?? [];
  const reviews = ctx.seed.venueReviews ?? [];
  const media = ctx.seed.venueMedia ?? [];
  if (!venues.length && !instructors.length && !submissions.length && !reviews.length && !media.length) return;
  ctx.log.info(`Directory (${venues.length} venues, ${instructors.length} instructors, ${reviews.length} reviews, ${media.length} member media, ${submissions.length} submissions)`);
  const staff = ctx.adminSession;

  for (const spec of venues) {
    const found = await ctx.findByTag('/api/v1/admin/venues/enumerate', spec.key, { auth: staff, filters: { includeClosed: true } });
    let venue = found[0];
    if (!venue) {
      const photoMediaIds = [];
      for (const [i, asset] of (spec.photos ?? []).entries()) {
        const id = await ctx.uploadAsset(staff, asset, { tags: ctx.tags(`${spec.key}/photo/${i}`), waitReady: true });
        if (id) photoMediaIds.push(id);
      }
      const body = {
        ...pick(spec, ['name', 'slug', 'affiliation', 'summary', 'description', 'address', 'addressVisibility', 'geo', 'timeZone', 'phone', 'email', 'websiteUrl', 'socialLinks', 'scheduleText', 'programs', 'visitorPolicy', 'visitorPolicyNote', 'audience']),
        photoMediaIds, labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
      };
      const res = await ctx.api.request('POST', '/api/v1/admin/venues', { auth: staff, body, ok: ['SlugInUse'] });
      venue = res.error ? await ctx.api.post('/api/v1/admin/venues', { ...body, slug: undefined }, { auth: staff }) : res.data;
      ctx.count('venues');
      if (spec.verified) await ctx.api.post(`/api/v1/admin/venues/${venue.id}/verify`, { note: 'Verified for the demo' }, { auth: staff });
    } else ctx.count('venues', 'existing');
    ctx.setId('venue', spec.key, venue.id);
  }

  for (const spec of instructors) {
    const found = await ctx.findByTag('/api/v1/admin/instructors/enumerate', spec.key, { auth: staff });
    let instructor = found[0];
    const venueLinks = (spec.venues ?? []).map((v) => ({ id: ctx.id('venue', v.venue), role: v.role ?? 'Regular' })).filter((v) => v.id);
    if (!instructor) {
      const body = {
        ...pick(spec, ['displayName', 'slug', 'bio', 'rank', 'rankLevel', 'affiliation', 'websiteUrl', 'socialLinks', 'audience']),
        venueIds: venueLinks.map((v) => v.id), labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
      };
      instructor = await ctx.api.post('/api/v1/admin/instructors', body, { auth: staff });
      ctx.count('instructors');
      for (const link of venueLinks) {
        if (link.role !== 'Regular') await ctx.api.put(`/api/v1/admin/venues/${link.id}/instructors/${instructor.id}`, { role: link.role }, { auth: staff });
      }
      await linkInstructor(ctx, staff, instructor, spec);
    } else {
      ctx.count('instructors', 'existing');
      // A linkMember added to the seed file after the instructor was created still links it on the next run.
      if (spec.linkMember && !instructor.linkedUser) await linkInstructor(ctx, staff, instructor, spec);
    }
    ctx.setId('instructor', spec.key, instructor.id);
  }

  await seedVenueReviews(ctx);
  await seedVenueMedia(ctx);

  for (const spec of submissions) {
    const submitter = ctx.member(spec.submitter);
    const mine = (await ctx.findByTag('/api/v1/me/directory/submissions/enumerate', spec.key, { auth: submitter.session }))
      .filter((x) => x.status !== 'Withdrawn');
    if (mine.length) {
      ctx.count('directory submissions', 'existing');
      continue;
    }
    const body = {
      type: spec.type, targetId: spec.target ? ctx.id('venue', spec.target) ?? ctx.id('instructor', spec.target) : undefined,
      fields: spec.fields, note: spec.note, confirmNotDuplicate: spec.confirmNotDuplicate ?? true,
      labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
    };
    const res = await ctx.api.request('POST', '/api/v1/directory/submissions', { auth: submitter.session, body, ok: [403, 409] });
    if (res.error) ctx.warn(`directory submission ${spec.key}: ${res.error}`);
    else ctx.count('directory submissions');
  }
}

// The member key of an instructor entry that is linked to a member and to the venue (the default reply author).
function linkedInstructorMember(ctx, venueKey) {
  for (const spec of ctx.seed.instructors ?? []) {
    if (spec.linkMember && (spec.venues ?? []).some((v) => v.venue === venueKey)) return spec.linkMember;
  }
  return null;
}

// Venue reviews: one per member per venue (the API enforces it), so an existing review of the author is kept.
async function seedVenueReviews(ctx) {
  for (const spec of ctx.seed.venueReviews ?? []) {
    const venueId = ctx.id('venue', spec.venue);
    if (!venueId) {
      ctx.warn(`venue review ${spec.key}: unknown venue ${spec.venue}`);
      continue;
    }
    const author = ctx.member(spec.author);
    const venue = await ctx.api.get(`/api/v1/venues/${venueId}`, { auth: author.session });
    let review = venue.reviews?.mine ?? null;
    if (review) ctx.count('venue reviews', 'existing');
    else {
      const body = { rating: spec.stars, body: spec.text, labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags) };
      const res = await ctx.api.request('POST', `/api/v1/venues/${venueId}/reviews`, { auth: author.session, body, ok: [403, 409] });
      if (res.error) {
        ctx.warn(`venue review ${spec.key}: ${res.error}`);
        continue;
      }
      review = res.data;
      ctx.count('venue reviews');
    }
    if (!spec.reply || review.reply) continue;
    const replier = spec.reply.author ?? linkedInstructorMember(ctx, spec.venue);
    if (!replier) {
      ctx.warn(`venue review ${spec.key}: the reply needs reply.author or an instructor with linkMember at ${spec.venue}`);
      continue;
    }
    const res = await ctx.api.request('PUT', `/api/v1/venues/${venueId}/reviews/${review.id}/reply`, { auth: ctx.session(replier), body: { body: spec.reply.text }, ok: [403, 404] });
    if (res.error) ctx.warn(`venue review ${spec.key} reply: ${res.error}`);
    else ctx.count('venue review replies');
  }
}

// Member photos and videos on venues: each item is uploaded by its member and attached to the venue.
async function seedVenueMedia(ctx) {
  for (const spec of ctx.seed.venueMedia ?? []) {
    const venueId = ctx.id('venue', spec.venue);
    if (!venueId) {
      ctx.warn(`venue media ${spec.key}: unknown venue ${spec.venue}`);
      continue;
    }
    const member = ctx.member(spec.member);
    const found = await ctx.findByTag(`/api/v1/venues/${venueId}/media/enumerate`, spec.key, { auth: member.session });
    if (found.length) {
      ctx.count('venue media', 'existing');
      continue;
    }
    const mediaId = await ctx.uploadAsset(member.session, spec.asset, { tags: ctx.tags(`${spec.key}/upload`) });
    if (!mediaId) continue;
    const res = await ctx.api.request('POST', `/api/v1/venues/${venueId}/media`, { auth: member.session, body: { mediaIds: [mediaId], labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags) }, ok: [403, 409] });
    if (res.error) ctx.warn(`venue media ${spec.key}: ${res.error}`);
    else ctx.count('venue media');
  }
}

async function linkInstructor(ctx, staff, instructor, spec) {
  if (!spec.linkMember) return;
  const res = await ctx.api.request('POST', `/api/v1/admin/instructors/${instructor.id}/link`, { auth: staff, body: { userId: ctx.member(spec.linkMember).userId }, ok: ['AlreadyClaimed'] });
  if (!res.error) ctx.count('instructor links');
}
