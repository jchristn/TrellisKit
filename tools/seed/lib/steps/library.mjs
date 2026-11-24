// Library: series and lessons (uploaded and published by library staff), member progress,
// lesson comments, and a public sample lesson.

import { randomUUID } from 'node:crypto';
import { setTimeout as sleep } from 'node:timers/promises';
import { commentTree } from '../engagement.mjs';
import { expandText } from '../util.mjs';

export async function seedLibrary(ctx) {
  const lib = ctx.seed.library;
  if (!lib || ctx.property.features?.library === false) return;
  const editor = ctx.member(lib.editor ?? ctx.seed.accounts?.adminMember);
  const series = lib.series ?? [];
  ctx.log.info(`Library (${series.length} series, ${series.reduce((n, s) => n + (s.lessons?.length ?? 0), 0)} lessons)`);
  const lessonDurations = new Map();

  for (const spec of series) {
    const found = await ctx.findByTag('/api/v1/admin/series/enumerate', spec.key, { auth: editor.session });
    let s = found[0];
    if (!s) {
      const body = {
        title: spec.title, slug: spec.slug, summary: spec.summary, description: expandText(spec.description, ctx),
        levels: spec.levels ?? [], audience: spec.audience, labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
      };
      if (spec.cover) body.coverMediaId = await ctx.uploadAsset(editor.session, spec.cover, { tags: ctx.tags(`${spec.key}/cover`), waitReady: true });
      const res = await ctx.api.request('POST', '/api/v1/admin/series', { auth: editor.session, body, ok: ['SlugInUse'] });
      s = res.error ? await ctx.api.post('/api/v1/admin/series', { ...body, slug: undefined }, { auth: editor.session }) : res.data;
      ctx.count('series');
    } else ctx.count('series', 'existing');
    ctx.setId('series', spec.key, s.id);

    for (const lspec of spec.lessons ?? []) {
      const lfound = await ctx.findByTag('/api/v1/admin/lessons/enumerate', lspec.key, { auth: editor.session });
      let lesson = lfound[0];
      if (!lesson) {
        const kind = lspec.kind ?? (lspec.video ? 'Video' : 'Article');
        const body = {
          title: lspec.title, slug: lspec.slug, summary: lspec.summary, description: expandText(lspec.description, ctx), kind,
          levels: lspec.levels ?? [], audience: lspec.audience === 'Public' ? undefined : lspec.audience, commentsEnabled: lspec.commentsEnabled ?? true,
          markers: lspec.markers ?? [], labels: lspec.labels ?? [], tags: ctx.tags(lspec.key, lspec.tags),
        };
        if (lspec.video) body.videoMediaId = await ctx.uploadAsset(editor.session, lspec.video, { tags: ctx.tags(`${lspec.key}/video`) });
        const res = await ctx.api.request('POST', `/api/v1/admin/series/${s.id}/lessons`, { auth: editor.session, body, ok: [400] });
        if (res.error) {
          ctx.warn(`lesson ${lspec.key}: ${res.error} ${res.data?.message ?? ''} ${JSON.stringify(res.data?.fieldErrors ?? {})}`);
          continue;
        }
        lesson = res.data;
        ctx.count(kind === 'Video' ? 'lessons (video)' : 'lessons (article)');
      } else ctx.count('lessons', 'existing');
      ctx.setId('lesson', lspec.key, lesson.id);
      if (lspec.audience === 'Public' && lesson.audience !== 'Public') {
        // Only a SystemAdmin may make a lesson Public (a public sample).
        const res = await ctx.api.request('PUT', `/api/v1/admin/lessons/${lesson.id}`, { auth: ctx.adminSession, body: { audience: 'Public' }, ok: [400] });
        if (res.error) ctx.warn(`make lesson ${lspec.key} Public: ${res.error} ${JSON.stringify(res.data?.fieldErrors ?? {})}`);
        else ctx.count('lessons', 'made public sample');
      }
      if (lesson.status === 'Draft') {
        const res = await ctx.api.request('POST', `/api/v1/admin/lessons/${lesson.id}/publish`, { auth: editor.session, ok: [400] });
        if (res.error) ctx.warn(`publish lesson ${lspec.key}: ${res.error} ${res.data?.message ?? ''}`);
      }
      const asset = (ctx.seed.assets ?? []).find((a) => a.key === lspec.video);
      lessonDurations.set(lspec.key, asset?.durationSeconds ?? 60);
    }
    const wantedOrder = (spec.lessons ?? []).map((l) => ctx.id('lesson', l.key)).filter(Boolean);
    const listed = await ctx.api.post(`/api/v1/series/${s.id}/lessons/enumerate`, { maxResults: 100, ordering: 'PositionAscending' }, { auth: editor.session });
    const actualOrder = (listed.objects ?? []).map((l) => l.id).filter((id) => wantedOrder.includes(id));
    if (wantedOrder.length > 1 && actualOrder.length === wantedOrder.length && actualOrder.join() !== wantedOrder.join()) {
      const all = (listed.objects ?? []).map((l) => l.id);
      const lessonIds = [...wantedOrder, ...all.filter((id) => !wantedOrder.includes(id))];
      const res = await ctx.api.request('PUT', `/api/v1/admin/series/${s.id}/lessons/order`, { auth: editor.session, body: { lessonIds }, ok: [400] });
      if (res.error) ctx.warn(`reorder lessons of ${spec.key}: ${res.error} ${res.data?.message ?? ''}`);
      else ctx.count('series', 'lessons reordered');
    }
    const current = await ctx.api.get(`/api/v1/admin/series/${s.id}`, { auth: editor.session });
    if (current.status === 'Draft') await ctx.api.post(`/api/v1/admin/series/${s.id}/publish`, {}, { auth: editor.session });
  }

  // Progress and comments need Published lessons (video lessons publish once their video is Ready).
  const needed = new Set([...(lib.progress ?? []).map((p) => p.lesson), ...(lib.comments ?? []).map((c) => c.lesson)]);
  await waitPublished(ctx, editor.session, [...needed].map((k) => ctx.id('lesson', k)).filter(Boolean));

  for (const p of lib.progress ?? []) {
    const lessonId = ctx.id('lesson', p.lesson);
    if (!lessonId) continue;
    const m = ctx.member(p.member);
    const mine = await ctx.api.post('/api/v1/me/library/progress/enumerate', { maxResults: 100 }, { auth: m.session });
    if ((mine.objects ?? []).some((x) => x.lessonId === lessonId)) {
      ctx.count('lesson progress', 'existing');
      continue;
    }
    if (p.complete) {
      const res = await ctx.api.request('POST', `/api/v1/lessons/${lessonId}/complete`, { auth: m.session, ok: [400, 402, 404] });
      if (res.error) ctx.warn(`complete ${p.lesson} for ${p.member}: ${res.error}`);
      else ctx.count('lesson progress', 'completed');
    } else {
      const duration = lessonDurations.get(p.lesson) ?? 60;
      const position = Math.max(1, Math.round((duration * (p.percent ?? 50)) / 100));
      const res = await ctx.api.request('PUT', `/api/v1/lessons/${lessonId}/progress`, {
        auth: m.session, ok: [400, 402, 404],
        body: { positionSeconds: position, playedSeconds: Math.min(position, 30), clientUpdatedUtc: new Date().toISOString(), sessionId: randomUUID() },
      });
      if (res.error) ctx.warn(`progress ${p.lesson} for ${p.member}: ${res.error} ${res.data?.message ?? ''}`);
      else ctx.count('lesson progress', 'in progress');
    }
  }

  for (const c of lib.comments ?? []) {
    const lessonId = ctx.id('lesson', c.lesson);
    if (!lessonId) continue;
    await commentTree(ctx, { base: `/api/v1/lessons/${lessonId}`, objectKey: c.lesson, comments: [c] });
  }
}

async function waitPublished(ctx, session, ids, timeoutMs = 300000) {
  const deadline = Date.now() + timeoutMs;
  let pending = ids;
  while (pending.length && Date.now() < deadline) {
    const still = [];
    for (const id of pending) {
      const l = await ctx.api.get(`/api/v1/admin/lessons/${id}`, { auth: session });
      if (l.status !== 'Published') still.push(id);
    }
    pending = still;
    if (pending.length) {
      ctx.log.debug(`waiting for ${pending.length} lesson(s) to publish (video processing)`);
      await sleep(5000);
    }
  }
  if (pending.length) ctx.warn(`${pending.length} lesson(s) not Published after ${timeoutMs / 1000}s; progress on them may fail`);
}
