// Feed posts with media, reactions, comment threads (with mentions), and reports.
// Posts are created first (oldest first, so the newest seed entry is on top), then the
// step waits once for media processing, then adds reactions and comments.

import { setTimeout as sleep } from 'node:timers/promises';
import { commentTree, react } from '../engagement.mjs';
import { expandText } from '../util.mjs';

export async function seedFeed(ctx) {
  const specs = ctx.seed.feed ?? [];
  if (!specs.length) return;
  ctx.log.info(`Feed posts (${specs.length})`);
  const posts = [];
  for (const spec of specs) {
    const author = ctx.member(spec.author);
    const found = await ctx.findByTag('/api/v1/admin/feed/enumerate', spec.key);
    let post = found[0];
    if (!post) {
      const mediaIds = [];
      for (const [i, asset] of (spec.media ?? []).entries()) {
        const id = await ctx.uploadAsset(author.session, asset, { tags: ctx.tags(`${spec.key}/media/${i}`) });
        if (id) mediaIds.push(id);
      }
      const body = {
        title: expandText(spec.title, ctx), body: expandText(spec.body, ctx), mediaIds,
        audience: spec.audience, visibility: spec.audience === 'Public' ? 'Public' : undefined,
        commentsEnabled: spec.commentsEnabled ?? true, pin: spec.pin ?? false, showLinkPreview: spec.showLinkPreview ?? false,
        labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
      };
      const res = await ctx.api.request('POST', '/api/v1/feed', { auth: author.session, body, ok: [403] });
      if (res.error) {
        ctx.warn(`feed post ${spec.key} by ${spec.author}: ${res.error} ${res.data?.message ?? ''} (does the author have flags.canPostToFeed?)`);
        continue;
      }
      post = res.data;
      ctx.count('feed posts');
    } else ctx.count('feed posts', 'existing');
    ctx.setId('post', spec.key, post.id);
    posts.push({ spec, post, author });
  }

  await waitPublished(ctx, posts.filter((p) => p.post.state !== 'Published'));

  for (const { spec, post } of posts) {
    await react(ctx, { path: `/api/v1/feed/${post.id}`, objectKey: spec.key, spec, authorKey: spec.author, currentTotal: post.reactionTotal ?? 0 });
    await commentTree(ctx, { base: `/api/v1/feed/${post.id}`, objectKey: spec.key, comments: spec.comments });
  }
}

// Posts with media stay Processing until every attachment is Ready; comments need Published.
async function waitPublished(ctx, pending, timeoutMs = 240000) {
  const deadline = Date.now() + timeoutMs;
  let left = pending;
  while (left.length && Date.now() < deadline) {
    const still = [];
    for (const p of left) {
      const res = await ctx.api.request('GET', `/api/v1/feed/${p.post.id}`, { auth: p.author.session, ok: [404] });
      if (res.error || res.data.state !== 'Published') still.push(p);
    }
    left = still;
    if (left.length) {
      ctx.log.debug(`waiting for ${left.length} post(s) with media to publish`);
      await sleep(3000);
    }
  }
  if (left.length) ctx.warn(`${left.length} feed post(s) not Published after ${timeoutMs / 1000}s`);
}
