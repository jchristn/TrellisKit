// Forum threads: discussions, questions with an accepted answer, polls with votes, and
// feedback (video review) threads with a moderator review.

import { commentTree, react } from '../engagement.mjs';
import { expandText } from '../util.mjs';

export async function seedThreads(ctx) {
  const specs = ctx.seed.threads ?? [];
  if (!specs.length || ctx.property.features?.forums === false) return;
  ctx.log.info(`Threads (${specs.length})`);
  for (const spec of specs) {
    const forumId = ctx.id('forum', spec.forum) ?? (await ctx.findByTag('/api/v1/admin/forums/enumerate', spec.forum))[0]?.id;
    if (!forumId) {
      ctx.warn(`thread ${spec.key}: forum ${spec.forum} not found`);
      continue;
    }
    const author = ctx.member(spec.author);
    const found = await ctx.findByTag('/api/v1/admin/threads/enumerate', spec.key);
    let threadId = found[0]?.id;
    if (!threadId) {
      const mediaIds = [];
      for (const [i, asset] of (spec.media ?? []).entries()) {
        const id = await ctx.uploadAsset(author.session, asset, { tags: ctx.tags(`${spec.key}/media/${i}`) });
        if (id) mediaIds.push(id);
      }
      const body = {
        type: spec.type, title: expandText(spec.title, ctx), body: expandText(spec.body, ctx), mediaIds,
        showLinkPreview: false, labels: spec.labels ?? [], tags: ctx.tags(spec.key, spec.tags),
      };
      if (spec.poll) {
        body.poll = {
          options: spec.poll.options.map((label) => ({ label })),
          multipleChoice: spec.poll.multipleChoice ?? false, maxChoices: spec.poll.multipleChoice ? spec.poll.maxChoices : undefined,
          allowVoteChange: spec.poll.allowVoteChange ?? true, resultsVisibility: spec.poll.resultsVisibility ?? 'Always',
          voterVisibility: spec.poll.voterVisibility ?? 'Anonymous',
          closesUtc: spec.poll.closesInDays ? new Date(Date.now() + spec.poll.closesInDays * 86400_000).toISOString() : undefined,
        };
      }
      if (spec.feedback) body.feedback = spec.feedback;
      const res = await ctx.api.request('POST', `/api/v1/forums/${forumId}/threads`, { auth: author.session, body, ok: [403, 409] });
      if (res.error) {
        ctx.warn(`thread ${spec.key}: ${res.error} ${res.data?.message ?? ''}`);
        continue;
      }
      threadId = res.data.id;
      ctx.count('threads');
      if (mediaIds.length) await waitThreadPublished(ctx, author.session, threadId);
    } else ctx.count('threads', 'existing');
    ctx.setId('thread', spec.key, threadId);
    const thread = await ctx.api.get(`/api/v1/threads/${threadId}`, { auth: author.session });

    if (spec.votes && thread.poll) {
      const byVoter = new Map();
      for (const [index, voters] of Object.entries(spec.votes)) {
        for (const v of voters) {
          if (!byVoter.has(v)) byVoter.set(v, []);
          byVoter.get(v).push(thread.poll.options[Number(index)].id);
        }
      }
      if ((thread.poll.totalVoters ?? 0) < byVoter.size) {
        for (const [voter, optionIds] of byVoter) {
          const res = await ctx.api.request('PUT', `/api/v1/polls/${thread.poll.id}/votes`, { auth: ctx.member(voter).session, body: { optionIds }, ok: [400, 402, 403, 409] });
          if (res.error) ctx.warn(`poll vote by ${voter} on ${spec.key}: ${res.error} ${res.data?.message ?? ''}`);
          else ctx.count('poll votes');
        }
      }
    }
    await react(ctx, { path: `/api/v1/threads/${threadId}`, objectKey: spec.key, spec, authorKey: spec.author, currentTotal: thread.reactionTotal ?? 0 });
    const ids = await commentTree(ctx, { base: `/api/v1/threads/${threadId}`, objectKey: spec.key, comments: spec.comments });

    if (spec.acceptAnswer && !thread.acceptedAnswer && ids.get(spec.acceptAnswer)) {
      await ctx.api.post(`/api/v1/threads/${threadId}/accept-answer`, { commentId: ids.get(spec.acceptAnswer) }, { auth: author.session });
      ctx.count('accepted answers');
    }
    if (spec.review && ids.get(spec.review.comment) && thread.feedback?.status !== spec.review.status) {
      const reviewer = ctx.member(spec.review.by);
      if (spec.review.status === 'Reviewed' && thread.feedback?.status === 'Open') {
        await ctx.api.request('PUT', `/api/v1/threads/${threadId}/feedback`, { auth: reviewer.session, body: { status: 'InReview' }, ok: [400, 409] });
      }
      await ctx.api.put(`/api/v1/threads/${threadId}/feedback`, { status: spec.review.status, reviewCommentId: ids.get(spec.review.comment) }, { auth: reviewer.session });
      ctx.count('feedback reviews');
    }
  }
}

async function waitThreadPublished(ctx, session, id, timeoutMs = 180000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const t = await ctx.api.get(`/api/v1/threads/${id}`, { auth: session });
    if (t.state === 'Published') return;
    await new Promise((r) => setTimeout(r, 3000));
  }
  ctx.warn(`thread ${id} not Published after ${timeoutMs / 1000}s`);
}
