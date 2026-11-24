// Direct message conversations. A message is skipped when the conversation already shows the
// same text from the same author, so re-running never duplicates messages (and a conversation
// that was deleted by a content reset gets its messages again). A conversation whose recipient
// never replies stays a pending message request.

import { randomUUID } from 'node:crypto';
import { expandText } from '../util.mjs';

export async function seedMessages(ctx) {
  const specs = ctx.seed.conversations ?? [];
  if (!specs.length || ctx.property.features?.messaging === false) return;
  ctx.log.info(`Conversations (${specs.length})`);
  for (const spec of specs) {
    const from = ctx.member(spec.from);
    const to = ctx.member(spec.to);
    const existing = await ctx.api.post('/api/v1/conversations/enumerate', { maxResults: 5, filters: { folder: 'All', withUserId: to.userId } }, { auth: from.session });
    let conversationId = existing.objects?.[0]?.id ?? null;
    const seen = new Set();
    if (conversationId) {
      const history = await ctx.api.post(`/api/v1/conversations/${conversationId}/messages/enumerate`, { maxResults: 100 }, { auth: from.session });
      for (const m of history.objects ?? []) if (m.state === 'Visible') seen.add(`${m.author?.id}|${m.body}`);
    }
    let created = 0;
    for (const [i, msg] of spec.messages.entries()) {
      const sender = ctx.member(msg.from);
      const text = expandText(msg.body, ctx);
      if (seen.has(`${sender.userId}|${text}`)) continue;
      const body = { clientMessageId: randomUUID(), body: text, showLinkPreview: false };
      let res;
      if (!conversationId) {
        if (sender !== from) throw new Error(`conversation ${spec.key}: the first message must be from ${spec.from}`);
        res = await ctx.api.request('POST', '/api/v1/conversations', { auth: from.session, body: { recipientUserId: to.userId, message: body }, ok: [403] });
        if (res.error) {
          ctx.warn(`conversation ${spec.key}: ${res.error} ${res.data?.message ?? ''}`);
          break;
        }
        conversationId = res.data.conversation.id;
        ctx.count('conversations');
      } else {
        res = await ctx.api.request('POST', `/api/v1/conversations/${conversationId}/messages`, { auth: sender.session, body, ok: [403] });
        if (res.error) {
          ctx.warn(`message ${i} in ${spec.key}: ${res.error} ${res.data?.message ?? ''}`);
          continue;
        }
      }
      created += 1;
    }
    if (created) ctx.count('direct messages', 'created', created);
    else ctx.count('conversations', 'existing');
    if (conversationId) ctx.setId('conversation', spec.key, conversationId);
  }
}
