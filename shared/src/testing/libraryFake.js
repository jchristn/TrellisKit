import { dailyMetricPage } from './metricFake.js';

/**
 * An in-memory fake of the Phase 4 library routes (docs/PHASE4_CONTRACT.md section 5 plus the
 * viewing history routes of resolved decision 17), for client tests and for building screens
 * before the backend exists. It follows the contract shapes, the progress merge rules of section
 * 5.5 (anti-skip clamp, monotonic merge, stale updates ignored, sticky completion), locked stubs,
 * and the main error codes; it is not a second backend.
 *
 * Use `fake.handle({ method, path, query, body })` (path relative to `/api/v1`) or
 * `fake.fetchImpl` as an `ApiClient` fetch. Unknown routes return undefined from `handle`.
 */

import { fakeUser } from './forumsFake.js';
import { asList, clone, error, falsy, fetchOver, matchesMeta, matchesSearch, noContent, ok, page, splitPath, truthy } from './fakeUtils.js';

function slugify(text) {
  return String(text).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

const SERIES_VIEWER = Object.freeze({ canFollow: true, canEdit: false, canOpen: true, requires: null });
const LESSON_VIEWER = Object.freeze({ canPlay: true, canDownload: false, canComment: true, canReact: true, canReport: true, canEdit: false, canPromote: false, canOpen: true, requires: null });

/** A Series (section 2.6). */
export function makeSeries(id, extra = {}) {
  const title = extra.title || 'Series ' + id;
  return {
    id, slug: extra.slug || slugify(extra.title || id.replace(/^ser_/, '')), title, summary: 'About ' + title + '.', description: 'Description of ' + title + '.',
    cover: null, levels: [], status: 'Published', sortOrder: 10, lessonCount: 0, totalDurationSeconds: 0,
    myProgress: { completedLessons: 0, percent: 0, lastLessonId: null, lastWatchedUtc: null }, isFollowed: false, audience: 'Members', locked: false,
    labels: [], tags: {}, viewer: { ...SERIES_VIEWER }, publishedUtc: '2026-11-01T00:00:00Z', createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z',
    ...extra
  };
}

/** A Lesson (section 2.6) in `series`. */
export function makeLesson(id, series, extra = {}) {
  const title = extra.title || 'Lesson ' + id;
  const kind = extra.kind || 'Video';
  return {
    id, series: { id: series.id, slug: series.slug, title: series.title }, position: 1, slug: extra.slug || slugify(extra.title || id.replace(/^les_/, '')), title,
    summary: 'Summary of ' + title + '.', description: 'Notes for ' + title + '.', kind,
    video: kind === 'Video' ? { id: 'med_' + id, kind: 'Video', status: 'Ready', hlsUrl: '/media/' + id + '/hls/master.m3u8', posterUrl: null, posterThumbUrl: null, blurhash: null, durationSeconds: 300 } : null,
    durationSeconds: kind === 'Video' ? 300 : 0, markers: [], resources: [], captions: [], levels: [], status: 'Published', publishAtUtc: null, audience: series.audience || 'Members',
    downloadable: true, commentsEnabled: true, commentTarget: { type: 'Lesson', id }, commentCount: 0, reactionCounts: {}, reactionTotal: 0, myReactions: [],
    myProgress: null, previous: null, next: null, promotion: null, locked: false, labels: [], tags: {}, viewer: { ...LESSON_VIEWER },
    publishedUtc: '2026-11-02T00:00:00Z', createdUtc: '2026-11-02T00:00:00Z', updatedUtc: '2026-11-02T00:00:00Z',
    ...extra
  };
}

/** A LessonProgress (section 2.6). */
export function makeProgress(lessonId, extra = {}) {
  return {
    id: 'lpr_' + lessonId, lessonId, positionSeconds: 0, maxPositionSeconds: 0, watchedSeconds: 0, percent: 0, completed: false, completedUtc: null,
    lastWatchedUtc: '2026-11-10T18:00:00Z', clientUpdatedUtc: null, labels: [], tags: {}, ...extra
  };
}

/** Alias of `makeProgress`. */
export const makeLessonProgress = makeProgress;

/** Viewer capabilities of library staff (merge into `viewer`). */
export const libraryStaffViewer = Object.freeze({ canEdit: true, canPromote: true });

/**
 * Create a fake.
 * @param {{ me?: object, series?: object[], lessons?: object[], progress?: object[], comments?: object[], levels?: object[], viewers?: object[],
 *   settings?: object, now?: () => string, intervalSeconds?: number, completionThresholdPercent?: number, killed?: boolean, downloadsKilled?: boolean,
 *   members?: object[] }} [seed]
 */
export function createLibraryFake(seed = {}) {
  let n = 0;
  const nextId = (prefix) => prefix + 'fake' + (++n);
  const now = seed.now || (() => '2026-11-11T12:00:00Z');
  const interval = seed.intervalSeconds || 15;
  const threshold = seed.completionThresholdPercent || 90;
  const state = {
    me: seed.me || fakeUser('usr_1', 'Sam Rivera'),
    series: (seed.series || []).map(clone),
    lessons: (seed.lessons || []).map(clone),
    progress: (seed.progress || []).map(clone),
    comments: (seed.comments || []).map(clone),
    levels: (seed.levels || []).map(clone),
    downloads: (seed.downloads || []).map(clone),
    viewers: (seed.viewers || []).map(clone),
    members: (seed.members || []).map(clone),
    settings: { levelSource: 'Custom', levelFieldKey: null, downloadsEnabled: false, completionThresholdPercent: threshold, ...(seed.settings || {}) },
    killed: Boolean(seed.killed),
    downloadsKilled: Boolean(seed.downloadsKilled),
    audits: []
  };
  const calls = [];

  const findSeries = (idOrSlug) => state.series.find((s) => (s.id === idOrSlug || s.slug === idOrSlug) && !s.deletedUtc);
  const findLesson = (idOrSlug) => state.lessons.find((l) => (l.id === idOrSlug || l.slug === idOrSlug) && !l.deletedUtc);
  const progressOf = (lessonId) => state.progress.find((p) => p.lessonId === lessonId) || null;
  const lessonsOf = (seriesId) => state.lessons.filter((l) => l.series.id === seriesId && !l.deletedUtc).sort((a, b) => a.position - b.position);
  const canOpen = (item) => !item.locked && !(item.viewer && item.viewer.canOpen === false);

  function recount(series) {
    const list = lessonsOf(series.id).filter((l) => l.status === 'Published');
    series.lessonCount = list.length;
    series.totalDurationSeconds = list.reduce((sum, l) => sum + (l.durationSeconds || 0), 0);
    const done = list.filter((l) => (progressOf(l.id) || {}).completed).length;
    const last = state.progress.filter((p) => list.some((l) => l.id === p.lessonId)).sort((a, b) => String(b.lastWatchedUtc).localeCompare(String(a.lastWatchedUtc)))[0];
    series.myProgress = { completedLessons: done, percent: list.length ? Math.round((100 * done) / list.length) : 0, lastLessonId: last ? last.lessonId : null, lastWatchedUtc: last ? last.lastWatchedUtc : null };
  }

  function stubLesson(l) {
    if (canOpen(l)) return l;
    return { ...l, description: null, video: null, markers: [], resources: [], captions: [], myProgress: null };
  }

  function presentLesson(l) {
    const siblings = lessonsOf(l.series.id).filter((x) => x.status === 'Published');
    const at = siblings.findIndex((x) => x.id === l.id);
    const neighbor = (x) => (x ? { id: x.id, title: x.title, canOpen: canOpen(x) } : null);
    const out = { ...stubLesson(l), myProgress: canOpen(l) ? progressOf(l.id) : null, previous: neighbor(siblings[at - 1]), next: neighbor(siblings[at + 1]) };
    delete out.deletedUtc;
    return out;
  }

  function lessonSummary(l) {
    const full = presentLesson(l);
    const { description: _d, markers: _m, resources, captions: _c, previous: _p, next: _n, video, ...rest } = full;
    return { ...rest, hasResources: (resources || []).length > 0, video: video ? { durationSeconds: video.durationSeconds || l.durationSeconds, posterThumbUrl: video.posterThumbUrl || null, blurhash: video.blurhash || null } : null };
  }

  function presentSeries(s) {
    recount(s);
    const out = { ...s };
    delete out.deletedUtc;
    return out;
  }

  function seriesSummary(s) {
    const { description: _d, ...rest } = presentSeries(s);
    return rest;
  }

  function levelKeys(item) {
    return (item.levels || []).map((l) => (l && typeof l === 'object' ? l.key : l));
  }

  function progressMatches(lessonIds, value) {
    if (!value) return true;
    const states = lessonIds.map((id) => {
      const p = progressOf(id);
      if (!p) return 'NotStarted';
      return p.completed ? 'Completed' : 'InProgress';
    });
    if (value === 'Completed') return states.length > 0 && states.every((x) => x === 'Completed');
    if (value === 'NotStarted') return states.every((x) => x === 'NotStarted');
    return states.some((x) => x !== 'NotStarted') && !states.every((x) => x === 'Completed');
  }

  function filterSeries(list, body, { admin = false } = {}) {
    const f = body.filters || {};
    let out = list.filter((s) => !s.deletedUtc && (admin || s.status === 'Published'));
    if (admin && f.status) out = out.filter((s) => asList(f.status).includes(s.status));
    const levels = asList(f.levels);
    if (levels.length) out = out.filter((s) => levelKeys(s).some((k) => levels.includes(k)));
    if (truthy(f.followedOnly)) out = out.filter((s) => s.isFollowed);
    if (f.audience) out = out.filter((s) => asList(f.audience).includes(s.audience));
    if (f.progress) out = out.filter((s) => progressMatches(lessonsOf(s.id).filter((l) => l.status === 'Published').map((l) => l.id), f.progress));
    out = out.filter((s) => matchesMeta(s, body) && matchesSearch(s, body.search, ['title', 'summary', 'description']));
    const by = {
      SortOrderAscending: (a, b) => a.sortOrder - b.sortOrder || a.id.localeCompare(b.id),
      TitleAscending: (a, b) => a.title.localeCompare(b.title),
      PublishedDescending: (a, b) => String(b.publishedUtc).localeCompare(String(a.publishedUtc)),
      Relevance: () => 0
    };
    return out.sort(by[body.ordering] || by.SortOrderAscending);
  }

  function filterLessons(list, body, { admin = false } = {}) {
    const f = body.filters || {};
    let out = list.filter((l) => !l.deletedUtc && (admin || l.status === 'Published'));
    if (admin && f.status) out = out.filter((l) => asList(f.status).includes(l.status));
    if (f.seriesId) out = out.filter((l) => l.series.id === f.seriesId);
    const seriesIds = asList(f.seriesIds);
    if (seriesIds.length) out = out.filter((l) => seriesIds.includes(l.series.id));
    const levels = asList(f.levels);
    if (levels.length) out = out.filter((l) => levelKeys(l).some((k) => levels.includes(k)));
    if (f.kind) out = out.filter((l) => asList(f.kind).includes(l.kind));
    if (truthy(f.hasResources)) out = out.filter((l) => (l.resources || []).length > 0);
    if (truthy(f.hasVideo)) out = out.filter((l) => Boolean(l.video));
    if (falsy(f.hasVideo)) out = out.filter((l) => !l.video);
    if (f.minDurationSeconds) out = out.filter((l) => (l.durationSeconds || 0) >= Number(f.minDurationSeconds));
    if (f.maxDurationSeconds) out = out.filter((l) => (l.durationSeconds || 0) <= Number(f.maxDurationSeconds));
    if (f.audience) out = out.filter((l) => asList(f.audience).includes(l.audience));
    if (f.progress) out = out.filter((l) => progressMatches([l.id], f.progress));
    out = out.filter((l) => matchesMeta(l, body) && matchesSearch(l, body.search, ['title', 'summary', 'description']));
    const by = {
      PublishedDescending: (a, b) => String(b.publishedUtc).localeCompare(String(a.publishedUtc)) || b.id.localeCompare(a.id),
      PositionAscending: (a, b) => a.position - b.position,
      TitleAscending: (a, b) => a.title.localeCompare(b.title),
      DurationAscending: (a, b) => (a.durationSeconds || 0) - (b.durationSeconds || 0),
      Relevance: () => 0
    };
    return out.sort(by[body.ordering] || (admin ? by.PositionAscending : by.PublishedDescending));
  }

  /** The section 5.5 merge, applied as the server would. */
  function mergeProgress(lesson, body) {
    const duration = lesson.durationSeconds || (lesson.video && lesson.video.durationSeconds) || 0;
    const existing = progressOf(lesson.id);
    const p = existing || makeProgress(lesson.id, { id: nextId('lpr_'), lastWatchedUtc: now() });
    if (!existing) state.progress.push(p);
    const position = Math.max(0, Math.min(duration || Number(body.positionSeconds) || 0, Number(body.positionSeconds) || 0));
    const played = Math.max(0, Math.min(2 * interval, Number(body.playedSeconds) || 0));
    const stale = p.clientUpdatedUtc && body.clientUpdatedUtc && String(body.clientUpdatedUtc) < String(p.clientUpdatedUtc);
    p.maxPositionSeconds = Math.max(p.maxPositionSeconds || 0, position);
    p.watchedSeconds = Math.min(duration ? 3 * duration : Infinity, (p.watchedSeconds || 0) + played);
    if (!stale) {
      p.positionSeconds = position;
      p.clientUpdatedUtc = body.clientUpdatedUtc || now();
      p.lastWatchedUtc = now();
    }
    p.percent = duration ? Math.min(100, Math.round((100 * Math.min(p.maxPositionSeconds, p.watchedSeconds)) / duration)) : p.percent;
    if (!p.completed && p.percent >= state.settings.completionThresholdPercent) {
      p.completed = true;
      p.completedUtc = now();
    }
    return p;
  }

  function memberGuard() {
    return state.killed ? error(503, 'FeatureDisabled', 'The library is temporarily unavailable.') : null;
  }

  function handleSeries(method, parts, body) {
    if (parts[1] === 'enumerate' && method === 'POST') {
      const result = page(filterSeries(state.series, body), body);
      result.objects = result.objects.map((o) => seriesSummary(findSeries(o.id)));
      return ok(result);
    }
    const s = findSeries(parts[1]);
    if (!s || s.status !== 'Published') return error(404, 'NotFound', 'Not found.');
    if (!parts[2] && method === 'GET') return ok(presentSeries(s));
    if (parts[2] === 'lessons' && parts[3] === 'enumerate') {
      const list = filterLessons(lessonsOf(s.id), { ...body, ordering: body.ordering || 'PositionAscending' });
      const result = page(list, body);
      result.objects = result.objects.map((o) => lessonSummary(findLesson(o.id)));
      return ok(result);
    }
    if (parts[2] === 'follow') {
      s.isFollowed = method === 'PUT';
      return ok({ isFollowed: s.isFollowed });
    }
    return undefined;
  }

  function handleLessons(method, parts, body) {
    if (parts[1] === 'enumerate' && method === 'POST') {
      const result = page(filterLessons(state.lessons, body), body);
      result.objects = result.objects.map((o) => lessonSummary(findLesson(o.id)));
      return ok(result);
    }
    const l = findLesson(parts[1]);
    if (!l || (l.status !== 'Published' && l.status !== 'Archived')) return error(404, 'NotFound', 'Not found.');
    if (!parts[2] && method === 'GET') return ok(presentLesson(l));
    const writeDenied = () => (canOpen(l) ? null : error(402, 'MembershipRequired', 'A membership is required.', undefined));
    if (parts[2] === 'progress') {
      const denied = writeDenied();
      if (denied) return { ...denied, body: { ...denied.body, required: 'Membership', feature: 'library', targetType: 'Lesson', targetId: l.id } };
      if (method === 'DELETE') {
        state.progress = state.progress.filter((p) => p.lessonId !== l.id);
        return noContent();
      }
      return ok(mergeProgress(l, body));
    }
    if (parts[2] === 'complete' || parts[2] === 'uncomplete') {
      const p = progressOf(l.id) || mergeProgress(l, { positionSeconds: 0, playedSeconds: 0 });
      p.completed = parts[2] === 'complete';
      p.completedUtc = p.completed ? now() : null;
      if (p.completed && l.kind === 'Article') p.percent = 100;
      return ok(p);
    }
    if (parts[2] === 'comments') {
      if (parts[3] === 'enumerate') return ok(page(state.comments.filter((c) => c.targetId === l.id), body));
      if (method === 'POST') {
        if (!l.commentsEnabled) return error(403, 'CommentsDisabled', 'Comments are off for this lesson.');
        const c = { id: nextId('cmt_'), targetType: 'Lesson', targetId: l.id, parentCommentId: body.parentCommentId || null, depth: body.parentCommentId ? 1 : 0, state: 'Visible', author: state.me, body: body.body, mentions: [], attachments: [], createdUtc: now(), childCount: 0, descendantCount: 0, reactionCounts: {}, reactionTotal: 0, myReactions: [], labels: [], tags: {}, viewer: { canEdit: true, canDelete: true, canReply: true, canReact: true, canReport: false, canModerate: false }, children: [] };
        state.comments.push(c);
        l.commentCount += 1;
        return ok(c, 201);
      }
    }
    if (parts[2] === 'reactions') {
      if (parts[3] === 'enumerate') return ok(page([], body));
      const key = parts[3];
      const had = l.myReactions.includes(key);
      if (method === 'PUT' && !had) {
        l.myReactions.push(key);
        l.reactionCounts[key] = (l.reactionCounts[key] || 0) + 1;
        l.reactionTotal += 1;
      }
      if (method === 'DELETE' && had) {
        l.myReactions = l.myReactions.filter((k) => k !== key);
        l.reactionCounts[key] = Math.max(0, (l.reactionCounts[key] || 1) - 1);
        l.reactionTotal = Math.max(0, l.reactionTotal - 1);
      }
      return ok({ reactionCounts: l.reactionCounts, reactionTotal: l.reactionTotal, myReactions: l.myReactions });
    }
    if (parts[2] === 'downloads' && method === 'POST') {
      if (state.downloadsKilled) return error(503, 'FeatureDisabled', 'Downloads are temporarily unavailable.');
      if (!state.settings.downloadsEnabled || !l.downloadable || !canOpen(l)) return error(403, 'DownloadsNotAllowed', 'Downloads are not available for this lesson.');
      const d = { id: nextId('ldl_'), lessonId: l.id, url: 'https://media.example.test/' + l.id + '/download_720p.mp4?sig=fake', urlExpiresUtc: '2026-11-11T12:30:00Z', licenseExpiresUtc: '2026-12-11T12:00:00Z', sizeBytes: 1048576, sha256: 'fake', deviceId: body.deviceId, platform: body.platform, labels: [], tags: {} };
      state.downloads.push(d);
      return ok(d, 201);
    }
    if (parts[2] === 'promote' && method === 'POST') {
      l.promotion = { feedPostId: nextId('pst_'), promotedUtc: now() };
      return ok({ id: l.promotion.feedPostId, kind: 'PromotedLesson', source: { type: 'Lesson', id: l.id, title: l.title }, commentTarget: { type: 'Lesson', id: l.id } }, 201);
    }
    if (parts[2] === 'unpromote' && method === 'POST') {
      l.promotion = null;
      return ok(presentLesson(l));
    }
    return undefined;
  }

  function handleMe(method, parts, body) {
    if (parts[1] !== 'library') return undefined;
    if (parts[2] === 'continue' && parts[3] === 'enumerate') {
      const rows = state.progress
        .filter((p) => !p.completed)
        .map((p) => ({ p, l: findLesson(p.lessonId) }))
        .filter((x) => x.l && canOpen(x.l))
        .sort((a, b) => String(b.p.lastWatchedUtc).localeCompare(String(a.p.lastWatchedUtc)))
        .map((x) => ({ lesson: lessonSummary(x.l), progress: x.p }));
      return ok(page(rows, { maxResults: 10, ...body }));
    }
    if (parts[2] === 'progress' && parts[3] === 'enumerate') {
      const f = body.filters || {};
      let rows = state.progress.slice();
      if (truthy(f.completed)) rows = rows.filter((p) => p.completed);
      if (falsy(f.completed)) rows = rows.filter((p) => !p.completed);
      if (f.seriesId) rows = rows.filter((p) => (findLesson(p.lessonId) || { series: {} }).series.id === f.seriesId);
      return ok(page(rows, body));
    }
    if (parts[2] === 'downloads') {
      if (parts[3] === 'enumerate') return ok(page(state.downloads.filter((d) => !d.revokedUtc), body));
      const d = state.downloads.find((x) => x.id === parts[3] && !x.revokedUtc);
      if (!d) return error(404, 'NotFound', 'Not found.');
      if (parts[4] === 'renew' && method === 'POST') {
        if (state.downloadsKilled) return error(503, 'FeatureDisabled', 'Downloads are temporarily unavailable.');
        d.licenseExpiresUtc = '2027-01-10T12:00:00Z';
        return ok(d);
      }
      if (!parts[4] && method === 'DELETE') {
        d.revokedUtc = now();
        return noContent();
      }
    }
    return undefined;
  }

  function adminSeries(s) {
    return { ...presentSeries(s), viewer: { ...s.viewer, canEdit: true } };
  }

  function adminLesson(l) {
    return { ...presentLesson(l), locked: false, viewer: { ...l.viewer, ...libraryStaffViewer }, openReportCount: 0, mediaStatus: l.video ? l.video.status : null };
  }

  function handleAdmin(method, parts, query, body) {
    if (parts[1] === 'series') {
      if (parts[2] === 'enumerate') {
        const result = page(filterSeries(state.series, body, { admin: true }), body, { max: 500 });
        result.objects = result.objects.map((o) => adminSeries(findSeries(o.id)));
        return ok(result);
      }
      if (parts[2] === 'order' && method === 'PUT') {
        (body.seriesIds || []).forEach((id, i) => {
          const s = findSeries(id);
          if (s) s.sortOrder = (i + 1) * 10;
        });
        return noContent();
      }
      if (!parts[2] && method === 'POST') {
        if (!body.title || String(body.title).trim().length < 2) return error(400, 'ValidationFailed', 'A title is required.', { title: 'Required' });
        const slug = body.slug || String(body.title).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
        if (state.series.some((s) => s.slug === slug && !s.deletedUtc)) return error(409, 'SlugInUse', 'That slug is in use.');
        const s = makeSeries(nextId('ser_'), { ...body, slug, status: 'Draft', sortOrder: (state.series.length + 1) * 10, createdUtc: now(), updatedUtc: now() });
        state.series.push(s);
        return ok(adminSeries(s), 201);
      }
      const s = findSeries(parts[2]);
      if (!s) return error(404, 'NotFound', 'Not found.');
      if (!parts[3]) {
        if (method === 'GET') return ok(adminSeries(s));
        if (method === 'PUT') {
          Object.assign(s, body, { updatedUtc: now() });
          return ok(adminSeries(s));
        }
        if (method === 'DELETE') {
          if (lessonsOf(s.id).length) return error(409, 'SeriesNotEmpty', 'Archive the series or move its lessons first.');
          s.deletedUtc = now();
          return noContent();
        }
      }
      if (parts[3] === 'publish' || parts[3] === 'archive' || parts[3] === 'unarchive') {
        s.status = parts[3] === 'publish' ? 'Published' : parts[3] === 'archive' ? 'Archived' : 'Draft';
        if (parts[3] === 'publish') s.publishedUtc = now();
        return ok(adminSeries(s));
      }
      if (parts[3] === 'lessons') {
        if (parts[4] === 'order' && method === 'PUT') {
          (body.lessonIds || []).forEach((id, i) => {
            const l = findLesson(id);
            if (l) l.position = i + 1;
          });
          return noContent();
        }
        if (!parts[4] && method === 'POST') {
          if (!body.title) return error(400, 'ValidationFailed', 'A title is required.', { title: 'Required' });
          const l = makeLesson(nextId('les_'), s, { ...body, position: lessonsOf(s.id).length + 1, status: 'Draft', video: null, durationSeconds: 0, createdUtc: now(), updatedUtc: now() });
          state.lessons.push(l);
          return ok(adminLesson(l), 201);
        }
      }
      return undefined;
    }
    if (parts[1] === 'lessons') {
      if (parts[2] === 'enumerate') {
        const result = page(filterLessons(state.lessons, body, { admin: true }), body, { max: 500 });
        result.objects = result.objects.map((o) => adminLesson(findLesson(o.id)));
        return ok(result);
      }
      const l = findLesson(parts[2]);
      if (!l) return error(404, 'NotFound', 'Not found.');
      if (!parts[3]) {
        if (method === 'GET') return ok(adminLesson(l));
        if (method === 'PUT') {
          const { videoMediaId, ...rest } = body;
          Object.assign(l, rest, { updatedUtc: now() });
          if (videoMediaId !== undefined) l.video = videoMediaId ? { id: videoMediaId, kind: 'Video', status: 'Ready', durationSeconds: l.durationSeconds || 300 } : null;
          if (l.video && !l.durationSeconds) l.durationSeconds = l.video.durationSeconds;
          return ok(adminLesson(l));
        }
        if (method === 'DELETE') {
          l.deletedUtc = now();
          return noContent();
        }
      }
      if (parts[3] === 'publish') {
        if (l.kind === 'Video' && !(l.video && l.video.status === 'Ready')) return error(400, 'LessonVideoRequired', 'Attach a ready video first.');
        l.status = 'Published';
        l.publishedUtc = now();
        return ok(adminLesson(l));
      }
      if (parts[3] === 'schedule') {
        l.status = 'Scheduled';
        l.publishAtUtc = body.publishAtUtc;
        return ok(adminLesson(l));
      }
      if (parts[3] === 'archive' || parts[3] === 'unarchive') {
        l.status = parts[3] === 'archive' ? 'Archived' : 'Draft';
        return ok(adminLesson(l));
      }
      if (parts[3] === 'move') {
        const target = findSeries(body.seriesId);
        if (!target) return error(404, 'NotFound', 'Not found.');
        l.series = { id: target.id, slug: target.slug, title: target.title };
        l.position = Number(body.position) || lessonsOf(target.id).length;
        return ok(adminLesson(l));
      }
      if (parts[3] === 'viewers' && parts[4] === 'enumerate') {
        state.audits.push({ action: 'library.viewingHistory.read', targetType: 'Lesson', targetId: l.id });
        return ok(page(state.viewers.filter((v) => v.progress && v.progress.lessonId === l.id), body, { max: 500 }));
      }
      return undefined;
    }
    if (parts[1] === 'library') {
      if (parts[2] === 'members' && parts[4] === 'progress' && parts[5] === 'enumerate') {
        state.audits.push({ action: 'library.viewingHistory.read', targetType: 'User', targetId: parts[3] });
        const rows = state.viewers.filter((v) => v.user && v.user.id === parts[3]).map((v) => {
          const l = findLesson(v.progress.lessonId);
          return { lesson: l ? lessonSummary(l) : null, progress: v.progress };
        }).filter((x) => x.lesson);
        return ok(page(rows, body, { max: 500 }));
      }
      if (parts[2] === 'settings') {
        if (method === 'PUT') Object.assign(state.settings, body);
        return ok(state.settings);
      }
      if (parts[2] === 'levels') {
        if (parts[3] === 'enumerate') return ok(page(state.levels, body, { max: 500 }));
        if (parts[3] === 'order' && method === 'PUT') {
          state.levels.sort((a, b) => (body.levelIds || []).indexOf(a.id) - (body.levelIds || []).indexOf(b.id));
          return noContent();
        }
        if (!parts[3] && method === 'POST') {
          if (state.levels.some((x) => x.key.toLowerCase() === String(body.key || '').toLowerCase())) return error(409, 'Conflict', 'That key is in use.');
          const level = { id: nextId('llv_'), key: body.key, label: body.label, color: body.color || null, sortOrder: state.levels.length + 1, isRetired: false, labels: [], tags: {} };
          state.levels.push(level);
          return ok(level, 201);
        }
        const level = state.levels.find((x) => x.id === parts[3]);
        if (!level) return error(404, 'NotFound', 'Not found.');
        if (method === 'PUT') {
          Object.assign(level, body);
          return ok(level);
        }
        if (method === 'DELETE') {
          state.levels = state.levels.filter((x) => x !== level);
          return noContent();
        }
      }
      return undefined;
    }
    if (parts[1] === 'metrics' && parts[2] === 'library') {
      const days = Number((body.filters || query).days) || 30;
      const top = state.lessons.slice(0, 20).map((l) => ({ lessonId: l.id, title: l.title, plays: 0, completions: 0 }));
      return ok(dailyMetricPage({ days, playsByDay: [], completionsByDay: [], activeLearners: state.progress.length ? 1 : 0, topLessons: { objects: top, totalRecords: state.lessons.length, enumerate: null }, completionRateBySeries: { objects: [], totalRecords: 0, enumerate: null } }, body));
    }
    return undefined;
  }

  function handlePublic(method, parts, body) {
    if (parts[1] !== 'lessons') return undefined;
    const list = state.lessons.filter((l) => l.audience === 'Public' && l.status === 'Published' && !l.deletedUtc);
    const strip = (l) => {
      const { viewer: _v, myProgress: _p, myReactions: _r, ...rest } = lessonSummary(l);
      return { ...rest, description: l.description, markers: l.markers };
    };
    if (parts[2] === 'enumerate') {
      const result = page(list, { ...body, maxResults: Math.min(20, body.maxResults || 20) }, { max: 20 });
      result.objects = result.objects.map((o) => strip(list.find((l) => l.id === o.id)));
      return ok(result);
    }
    const l = list.find((x) => x.id === parts[2]);
    return l ? ok(strip(l)) : error(404, 'NotFound', 'Not found.');
  }

  /**
   * Answer one request.
   * @param {{ method: string, path: string, query?: object, body?: any }} call
   */
  function handle({ method, path, query = {}, body }) {
    const parts = splitPath(path);
    const m = String(method || 'GET').toUpperCase();
    const b = body && typeof body === 'object' ? body : {};
    const known = ['series', 'lessons'].includes(parts[0]) || (parts[0] === 'library') || (parts[0] === 'me' && parts[1] === 'library')
      || (parts[0] === 'public' && parts[1] === 'lessons') || (parts[0] === 'admin' && (['series', 'lessons', 'library'].includes(parts[1]) || (parts[1] === 'metrics' && parts[2] === 'library')));
    if (!known) return undefined;
    calls.push({ method: m, path: '/' + parts.join('/'), query, body: b });
    if (parts[0] !== 'admin' && parts[0] !== 'public') {
      const killed = memberGuard();
      if (killed) return killed;
    }
    switch (parts[0]) {
      case 'series': return handleSeries(m, parts, b);
      case 'lessons': return handleLessons(m, parts, b);
      case 'library': return parts[1] === 'levels' && parts[2] === 'enumerate' ? ok(page(state.levels.filter((x) => !x.isRetired).map((x) => ({ key: x.key, label: x.label, color: x.color || null })), b)) : undefined;
      case 'me': return handleMe(m, parts, b);
      case 'public': return handlePublic(m, parts, b);
      case 'admin': return handleAdmin(m, parts, query, b);
      default: return undefined;
    }
  }

  return { state, calls, handle, fetchImpl: fetchOver(handle), series: findSeries, lesson: findLesson, progressOf };
}
