/**
 * Pure helpers for a Reddit-style comment tree (contract section 7). The tree is normalized:
 * `byId` holds comments without `children`, `childIds` the loaded reply ids per comment, `more`
 * the `moreChildren` marker per comment, and `roots` the loaded top-level ids. Every helper
 * returns a new tree.
 */

/** An empty tree. */
export function emptyTree() {
  return { byId: {}, childIds: {}, more: {}, roots: [], nextCursor: null, commentCount: 0, collapsed: {} };
}

function strip(comment) {
  const { children: _children, moreChildren: _more, ...rest } = comment;
  return rest;
}

function addNode(tree, comment) {
  const id = comment.id;
  const existing = tree.byId[id];
  tree.byId[id] = existing ? { ...existing, ...strip(comment) } : strip(comment);
  const kids = Array.isArray(comment.children) ? comment.children : [];
  const known = tree.childIds[id] || [];
  const next = known.slice();
  for (const child of kids) {
    addNode(tree, child);
    if (!next.includes(child.id)) next.push(child.id);
  }
  tree.childIds[id] = next;
  if (Object.prototype.hasOwnProperty.call(comment, 'moreChildren')) tree.more[id] = comment.moreChildren || null;
  else if (!existing) tree.more[id] = null;
}

function clone(tree) {
  return { ...tree, byId: { ...tree.byId }, childIds: { ...tree.childIds }, more: { ...tree.more }, roots: tree.roots.slice(), collapsed: { ...tree.collapsed } };
}

/**
 * Add a page of top-level comments (`GET .../comments`). `replace` starts over (first page or a
 * new sort); otherwise items append, skipping ids already loaded.
 */
export function addTopLevelPage(tree, page, { replace = false } = {}) {
  const next = replace ? emptyTree() : clone(tree);
  for (const comment of (page && page.items) || []) {
    addNode(next, comment);
    if (!next.roots.includes(comment.id)) next.roots.push(comment.id);
  }
  next.nextCursor = (page && page.nextCursor) || null;
  if (page && typeof page.commentCount === 'number') next.commentCount = page.commentCount;
  return next;
}

/**
 * Add replies loaded for `parentId` (`GET /comments/{id}/children`). The parent's `moreChildren`
 * becomes the response cursor, with the remaining count reduced by what arrived.
 */
export function addChildren(tree, parentId, page) {
  const next = clone(tree);
  const ids = (next.childIds[parentId] || []).slice();
  let added = 0;
  for (const comment of (page && page.items) || []) {
    addNode(next, comment);
    if (!ids.includes(comment.id)) {
      ids.push(comment.id);
      added += 1;
    }
  }
  next.childIds[parentId] = ids;
  const before = next.more[parentId];
  const remaining = Math.max(0, (before ? before.remainingCount : 0) - added);
  next.more[parentId] = page && page.nextCursor ? { remainingCount: Math.max(remaining, 1), cursor: page.nextCursor } : null;
  return next;
}

function ancestorsOf(tree, id) {
  const chain = [];
  let current = tree.byId[id];
  const seen = new Set();
  while (current && current.parentCommentId && !seen.has(current.parentCommentId)) {
    seen.add(current.parentCommentId);
    chain.push(current.parentCommentId);
    current = tree.byId[current.parentCommentId];
  }
  return chain;
}

function bumpAncestors(tree, parentId, delta, { direct = true } = {}) {
  if (!parentId || !tree.byId[parentId]) return;
  const parent = tree.byId[parentId];
  tree.byId[parentId] = {
    ...parent,
    childCount: Math.max(0, (parent.childCount || 0) + (direct ? delta : 0)),
    descendantCount: Math.max(0, (parent.descendantCount || 0) + delta)
  };
  for (const id of ancestorsOf(tree, parentId)) {
    const node = tree.byId[id];
    tree.byId[id] = { ...node, descendantCount: Math.max(0, (node.descendantCount || 0) + delta) };
  }
}

/**
 * Insert a newly created comment where the server placed it. A reply to a comment at the maximum
 * depth comes back as a sibling (same `parentCommentId`, `replyToCommentId` set), so the comment is
 * attached under `parentCommentId`, never under the comment the member clicked. Top-level
 * comments go first so the author sees their own comment.
 */
export function insertComment(tree, comment) {
  const next = clone(tree);
  addNode(next, { ...comment, children: comment.children || [] });
  const parentId = comment.parentCommentId;
  if (!parentId) {
    next.roots = [comment.id, ...next.roots.filter((id) => id !== comment.id)];
  } else {
    const ids = (next.childIds[parentId] || []).filter((id) => id !== comment.id);
    ids.push(comment.id);
    next.childIds[parentId] = ids;
    bumpAncestors(next, parentId, 1);
    next.collapsed[parentId] = false;
  }
  next.commentCount = (next.commentCount || 0) + 1;
  return next;
}

/** Replace a comment's fields after an edit (children are kept). */
export function replaceComment(tree, comment) {
  const next = clone(tree);
  next.byId[comment.id] = { ...(next.byId[comment.id] || {}), ...strip(comment) };
  return next;
}

/**
 * Apply a delete: a comment with loaded or counted replies becomes a `Deleted` placeholder (the
 * tree keeps its shape); one without replies disappears. Counts on ancestors drop by one.
 */
export function removeComment(tree, id) {
  const node = tree.byId[id];
  if (!node) return tree;
  const next = clone(tree);
  const hasReplies = (next.childIds[id] || []).length > 0 || (node.childCount || 0) > 0;
  if (hasReplies) {
    next.byId[id] = { ...node, state: 'Deleted', author: null, body: null, mentions: [], attachments: [], linkPreview: null, reactionCounts: {}, reactionTotal: 0, myReactions: [], editedUtc: null, viewer: {} };
  } else {
    delete next.byId[id];
    delete next.childIds[id];
    delete next.more[id];
    if (node.parentCommentId) next.childIds[node.parentCommentId] = (next.childIds[node.parentCommentId] || []).filter((x) => x !== id);
    else next.roots = next.roots.filter((x) => x !== id);
  }
  bumpAncestors(next, node.parentCommentId, -1);
  next.commentCount = Math.max(0, (next.commentCount || 0) - 1);
  return next;
}

/** Collapse or expand a comment's replies. */
export function toggleCollapsed(tree, id) {
  return { ...tree, collapsed: { ...tree.collapsed, [id]: !tree.collapsed[id] } };
}

/** A comment with its loaded children nested again (for rendering or tests). */
export function nestedComment(tree, id) {
  const node = tree.byId[id];
  if (!node) return null;
  return { ...node, children: (tree.childIds[id] || []).map((child) => nestedComment(tree, child)).filter(Boolean), moreChildren: tree.more[id] || null };
}

/** Whether a comment is a placeholder (no content to show). */
export function isPlaceholder(comment) {
  return Boolean(comment && comment.state && comment.state !== 'Visible' && !comment.body);
}
