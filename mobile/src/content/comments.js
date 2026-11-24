/**
 * Comment tree helpers (docs/PHASE2_CONTRACT.md section 7). The server returns nested pages:
 * every Comment has `children` (already sorted) and, when replies were left out, `moreChildren`
 * { remainingCount, cursor }. These pure functions keep that tree in state and flatten it into
 * rows for a virtualized list.
 */

/** Placeholder states and their display text keys (contract 7.2). */
export const PlaceholderStates = Object.freeze(['Deleted', 'Removed', 'Hidden', 'BlockedAuthor']);

/** Whether a comment renders as a placeholder (no author, body, or actions). */
export function isPlaceholder(comment) {
  return Boolean(comment) && comment.state !== 'Visible' && !(comment.moderation && comment.body);
}

function mapTree(nodes, fn) {
  let changed = false;
  const next = [];
  for (const node of nodes || []) {
    const mapped = fn(node);
    if (mapped === null) {
      changed = true;
      continue;
    }
    let result = mapped;
    if (mapped.children && mapped.children.length) {
      const children = mapTree(mapped.children, fn);
      if (children !== mapped.children) result = { ...mapped, children };
    }
    if (result !== node) changed = true;
    next.push(result);
  }
  return changed ? next : nodes;
}

/** Find a comment anywhere in the tree. */
export function findComment(nodes, id) {
  for (const node of nodes || []) {
    if (node.id === id) return node;
    const hit = findComment(node.children, id);
    if (hit) return hit;
  }
  return null;
}

function dedupe(existing, incoming) {
  const seen = new Set((existing || []).map((c) => c.id));
  return (existing || []).concat((incoming || []).filter((c) => !seen.has(c.id)));
}

/** Append a page of top-level comments (de-duplicated by id). */
export function appendTopLevel(nodes, page) {
  return dedupe(nodes, page);
}

/**
 * Merge a children page (GET /comments/{id}/children) under its parent.
 * @param {object[]} nodes
 * @param {string} parentId
 * @param {{ items: object[], nextCursor: string|null }} page
 */
export function appendChildren(nodes, parentId, page) {
  return mapTree(nodes, (node) => {
    if (node.id !== parentId) return node;
    const children = dedupe(node.children, page.items);
    const added = children.length - (node.children || []).length;
    const remaining = node.moreChildren ? Math.max(0, (node.moreChildren.remainingCount || 0) - added) : 0;
    return { ...node, children, moreChildren: page.nextCursor ? { remainingCount: Math.max(remaining, 1), cursor: page.nextCursor } : null };
  });
}

/**
 * Insert a newly created comment. A top-level comment goes first; a reply goes first under its
 * parent (the server's parentCommentId, which is the flattened sibling parent at max depth).
 */
export function insertComment(nodes, comment) {
  const item = { ...comment, children: comment.children || [] };
  if (!comment.parentCommentId) return [item].concat(nodes || []);
  let inserted = false;
  const next = mapTree(nodes, (node) => {
    if (node.id !== comment.parentCommentId) return node;
    inserted = true;
    return { ...node, childCount: (node.childCount || 0) + 1, descendantCount: (node.descendantCount || 0) + 1, children: [item].concat(node.children || []) };
  });
  return inserted ? next : nodes;
}

/** Replace a comment's own fields (after an edit or reaction), keeping its loaded children. */
export function replaceComment(nodes, updated) {
  return mapTree(nodes, (node) => (node.id === updated.id ? { ...node, ...updated, children: node.children, moreChildren: node.moreChildren } : node));
}

/** Patch a comment by id with fn(node). */
export function patchComment(nodes, id, fn) {
  return mapTree(nodes, (node) => (node.id === id ? fn(node) : node));
}

/**
 * Remove a deleted comment: it becomes a Deleted placeholder when it still has replies, and
 * disappears otherwise (contract 7.2 and 7.6).
 */
export function removeComment(nodes, id) {
  return mapTree(nodes, (node) => {
    if (node.id !== id) return node;
    const hasReplies = (node.children && node.children.length) || (node.moreChildren && node.moreChildren.remainingCount);
    if (!hasReplies) return null;
    return {
      ...node,
      state: 'Deleted',
      author: null,
      body: null,
      mentions: [],
      attachments: [],
      linkPreview: null,
      reactionCounts: {},
      reactionTotal: 0,
      myReactions: [],
      editedUtc: null,
      revisionCount: 0,
      viewer: { canEdit: false, canDelete: false, canReply: false, canReact: false, canReport: false, canModerate: false }
    };
  });
}

/**
 * After blocking a member, show their comments as `BlockedAuthor` placeholders (contract 9.2),
 * as the server will on the next load.
 */
export function hideAuthor(nodes, userId) {
  return mapTree(nodes, (node) =>
    node.author && node.author.id === userId
      ? { ...node, state: 'BlockedAuthor', author: null, body: null, mentions: [], attachments: [], linkPreview: null, moderation: null, viewer: { canEdit: false, canDelete: false, canReply: false, canReact: false, canReport: false, canModerate: false } }
      : node
  );
}

/** Number of visible replies under a comment (for "N replies" on a collapsed comment). */
export function replyCount(comment) {
  if (!comment) return 0;
  if (typeof comment.descendantCount === 'number') return comment.descendantCount;
  return (comment.children || []).reduce((n, c) => n + 1 + replyCount(c), 0);
}

/**
 * Flatten the tree into list rows, depth first. Collapsed comments hide their subtree. A
 * `more` row follows the last loaded child when the server reported more.
 * @param {object[]} nodes
 * @param {Set<string>} collapsed
 * @returns {({ type: 'comment', key: string, comment: object, depth: number, collapsed: boolean }|{ type: 'more', key: string, parentId: string, remainingCount: number, cursor: string|null, depth: number })[]}
 */
export function flattenTree(nodes, collapsed = new Set(), depth = 0, out = []) {
  for (const comment of nodes || []) {
    const isCollapsed = collapsed.has(comment.id);
    const level = typeof comment.depth === 'number' ? comment.depth : depth;
    out.push({ type: 'comment', key: comment.id, comment, depth: level, collapsed: isCollapsed });
    if (isCollapsed) continue;
    flattenTree(comment.children, collapsed, level + 1, out);
    if (comment.moreChildren && comment.moreChildren.remainingCount > 0) {
      out.push({ type: 'more', key: 'more:' + comment.id, parentId: comment.id, remainingCount: comment.moreChildren.remainingCount, cursor: comment.moreChildren.cursor || null, depth: level + 1 });
    }
  }
  return out;
}
