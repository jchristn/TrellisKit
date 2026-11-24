import { forumPath, threadPath } from '@trelliskit/shared';
import strings from '../strings.js';
import { isSystemAdmin } from './onboarding.js';

/** Website route of a forum: `/app/forums/{slug}` (the slug is resolved through `getForum`, which also accepts ids). */
export function appForumPath(forum) {
  if (!forum) return '/app/forums';
  return '/app' + forumPath(typeof forum === 'string' ? forum : forum.slug || forum.id);
}

/** Website route of a thread, optionally focused on a comment. */
export function appThreadPath(id, commentId) {
  return '/app' + threadPath(id, commentId);
}

/** Error text map for forum writes (PHASE3_CONTRACT.md section 0.3) on top of the composer map. */
export function forumErrors() {
  return { ...strings.composer.errors, ...strings.forums.errors };
}

/** Whether `Me` may promote threads and open feed posts for discussion (SystemAdmin or `flags.canPromoteToFeed`). */
export function canPromote(me) {
  return isSystemAdmin(me) || Boolean(me && me.flags && me.flags.canPromoteToFeed);
}
