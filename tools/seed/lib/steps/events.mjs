// Events: single and recurring events (created and published by event staff), RSVPs
// (including a waitlist when Going exceeds capacity), comments, and member proposals.

import { commentTree, react } from '../engagement.mjs';
import { expandText, pick, resolveLocalTime } from '../util.mjs';

function eventBody(ctx, spec) {
  const tz = spec.timeZone ?? 'UTC';
  const body = {
    ...pick(spec, ['title', 'slug', 'summary', 'category', 'format', 'capacity', 'maxGuestsPerRsvp', 'waitlistEnabled', 'rsvpAudience', 'attendeeVisibility', 'externalUrl', 'reminderOffsetsMinutes', 'commentsEnabled', 'audience', 'online']),
    description: expandText(spec.description, ctx),
    timeZone: tz,
    startLocal: resolveLocalTime(spec.start, tz),
    endLocal: resolveLocalTime(spec.start, tz, spec.durationMinutes ?? 60),
    labels: spec.labels ?? [],
    tags: ctx.tags(spec.key, spec.tags),
  };
  const categories = (ctx.property.events?.categories ?? []).map((c) => c.key);
  if (body.category && !categories.includes(body.category)) {
    ctx.warn(`event ${spec.key}: category ${body.category} is not configured for this property; leaving it empty`);
    delete body.category;
  }
  if (spec.venue) {
    const venueId = ctx.id('venue', spec.venue);
    if (venueId) body.location = { venueId };
    else ctx.warn(`event ${spec.key}: venue ${spec.venue} not seeded; using no location`);
  } else if (spec.location || spec.locationName) {
    body.location = spec.location ?? { name: spec.locationName };
  }
  if (spec.hosts) body.hostUserIds = spec.hosts.map((k) => ctx.member(k).userId);
  if (spec.instructors) body.instructorIds = spec.instructors.map((k) => ctx.id('instructor', k)).filter(Boolean);
  if (spec.recurrence) body.recurrence = spec.recurrence;
  return body;
}

export async function seedEvents(ctx) {
  if (ctx.property.features?.events === false) return;
  const specs = ctx.seed.events ?? [];
  const proposals = ctx.seed.eventProposals ?? [];
  if (!specs.length && !proposals.length) return;
  ctx.log.info(`Events (${specs.length} events, ${proposals.length} proposals)`);

  for (const spec of specs) {
    const creator = ctx.member(spec.creator ?? ctx.seed.accounts?.adminMember);
    const found = await ctx.api.post('/api/v1/admin/events/enumerate', {
      maxResults: 5, ordering: 'StartAscending', tags: { [ctx.namespace]: spec.key }, filters: { when: 'Upcoming' },
    }, { auth: creator.session });
    let event = found.objects?.[0];
    if (!event) {
      const body = eventBody(ctx, spec);
      if (spec.cover) body.coverMediaId = await ctx.uploadAsset(creator.session, spec.cover, { tags: ctx.tags(`${spec.key}/cover`), waitReady: true });
      const res = await ctx.api.request('POST', '/api/v1/admin/events', { auth: creator.session, body, ok: ['SlugInUse'] });
      event = res.error ? await ctx.api.post('/api/v1/admin/events', { ...body, slug: undefined }, { auth: creator.session }) : res.data;
      ctx.count(spec.recurrence ? 'events (recurring series)' : 'events');
    } else ctx.count('events', 'existing');
    if (event.status === 'Draft') {
      event = await ctx.api.post(`/api/v1/admin/events/${event.id}/publish`, { notifyMembers: false }, { auth: creator.session });
    }
    ctx.setId('event', spec.key, event.id);

    let goingOrder = 0;
    for (const [status, list] of [['Going', spec.rsvps?.going ?? []], ['Interested', spec.rsvps?.interested ?? []]]) {
      for (const who of list) {
        const m = ctx.member(who);
        const current = await ctx.api.request('GET', `/api/v1/events/${event.id}`, { auth: m.session, ok: [402, 404] });
        if (current.error || current.data.myRsvp) continue;
        const res = await ctx.api.request('PUT', `/api/v1/events/${event.id}/rsvp`, { auth: m.session, body: { status, guestCount: 0 }, ok: [402, 403, 409] });
        if (res.error) ctx.warn(`rsvp ${status} by ${who} on ${spec.key}: ${res.error}`);
        else ctx.count(res.data.myRsvp?.status === 'Waitlisted' ? 'rsvps (waitlisted)' : `rsvps (${status.toLowerCase()})`);
        goingOrder += 1;
      }
    }
    await react(ctx, { path: `/api/v1/events/${event.id}`, objectKey: spec.key, spec, authorKey: spec.creator });
    await commentTree(ctx, { base: `/api/v1/events/${event.id}`, objectKey: spec.key, comments: spec.comments });
  }

  for (const spec of proposals) {
    const proposer = ctx.member(spec.proposer);
    const mine = (await ctx.findByTag('/api/v1/me/events/proposals/enumerate', spec.key, { auth: proposer.session }))
      .filter((x) => x.status !== 'Withdrawn');
    if (mine.length) {
      ctx.count('event proposals', 'existing');
      continue;
    }
    const body = { ...eventBody(ctx, spec), proposalNote: spec.proposalNote };
    delete body.hostUserIds;
    delete body.recurrence;
    const res = await ctx.api.request('POST', '/api/v1/events/proposals', { auth: proposer.session, body, ok: [400, 403, 409] });
    if (res.error) ctx.warn(`event proposal ${spec.key}: ${res.error} ${res.data?.message ?? ''} ${JSON.stringify(res.data?.fieldErrors ?? {})}`);
    else ctx.count('event proposals');
  }
}
