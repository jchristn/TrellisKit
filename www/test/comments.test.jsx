import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import CommentThread, { treePage, treeQuery } from '../src/components/feed/CommentThread.jsx';
import { addChildren, addTopLevelPage, emptyTree, insertComment, nestedComment, removeComment } from '../src/utils/commentTree.js';
import { json } from './helpers.jsx';
import { enumPage, makeComment, makePost, memberFetch, renderMember, user } from './engagementHelpers.jsx';

describe('comment tree helpers', () => {
  const page = {
    items: [makeComment('cmt_A', { childCount: 3, descendantCount: 4, children: [makeComment('cmt_B', { parentCommentId: 'cmt_A', depth: 1, childCount: 1, descendantCount: 1, children: [], moreChildren: { remainingCount: 1, cursor: null } })], moreChildren: { remainingCount: 2, cursor: 'c1' } })],
    nextCursor: 'top2',
    commentCount: 5
  };

  it('normalizes pages, appends children with the remaining count, and reattaches flattened replies', () => {
    let tree = addTopLevelPage(emptyTree(), page, { replace: true });
    expect(tree.roots).toEqual(['cmt_A']);
    expect(tree.more.cmt_A).toEqual({ remainingCount: 2, cursor: 'c1' });
    tree = addChildren(tree, 'cmt_A', { items: [makeComment('cmt_C', { parentCommentId: 'cmt_A', depth: 1 })], nextCursor: 'c2' });
    expect(tree.childIds.cmt_A).toEqual(['cmt_B', 'cmt_C']);
    expect(tree.more.cmt_A).toEqual({ remainingCount: 1, cursor: 'c2' });
    tree = addChildren(tree, 'cmt_A', { items: [makeComment('cmt_D', { parentCommentId: 'cmt_A' })], nextCursor: null });
    expect(tree.more.cmt_A).toBeNull();

    // A reply to cmt_B at the maximum depth comes back as a sibling of cmt_B.
    tree = insertComment(tree, makeComment('cmt_R', { parentCommentId: 'cmt_A', replyToCommentId: 'cmt_B', replyToUser: user('usr_3', 'Sam Lee'), depth: 1 }));
    expect(tree.childIds.cmt_A).toContain('cmt_R');
    expect(tree.childIds.cmt_B).toEqual([]);
    expect(tree.byId.cmt_A.childCount).toBe(4);
    expect(tree.commentCount).toBe(6);

    tree = removeComment(tree, 'cmt_B');
    expect(tree.byId.cmt_B.state).toBe('Deleted');
    tree = removeComment(tree, 'cmt_D');
    expect(tree.byId.cmt_D).toBeUndefined();
    expect(nestedComment(tree, 'cmt_A').children.map((c) => c.id)).toEqual(['cmt_B', 'cmt_C', 'cmt_R']);
  });

  it('builds enumeration queries and reads tree pages in both shapes', () => {
    expect(treeQuery('new', { continuationToken: 't' })).toEqual({ ordering: 'CreatedDescending', maxResults: 20, continuationToken: 't', filters: { depth: 3, childLimit: 5 } });
    expect(treePage(enumPage([1], { continuationToken: 'n', commentCount: 4 }))).toEqual({ items: [1], nextCursor: 'n', commentCount: 4 });
    expect(treePage({ sort: 'top', items: [2], nextCursor: null, commentCount: 1 })).toEqual({ items: [2], nextCursor: null, commentCount: 1 });
  });
});

describe('CommentThread', () => {
  const post = makePost('fpo_1', { commentCount: 3 });

  function backend(extra = () => undefined) {
    return memberFetch((call) => {
      if (call.path === '/api/v1/feed/fpo_1/comments' && call.method === 'GET') {
        return json(enumPage([
          makeComment('cmt_A', {
            body: 'Top comment', childCount: 3, descendantCount: 3,
            children: [makeComment('cmt_B', { parentCommentId: 'cmt_A', depth: 8, body: 'Deep reply', author: user('usr_4', 'Deep Diver'), viewer: { canReply: true, canReact: true, canReport: true } })],
            moreChildren: { remainingCount: 2, cursor: 'more1' }
          }),
          makeComment('cmt_X', { state: 'Deleted', body: null, author: null, childCount: 1, descendantCount: 1, viewer: {}, children: [makeComment('cmt_Y', { parentCommentId: 'cmt_X', depth: 1, body: 'Orphan reply' })] })
        ], { continuationToken: 'top2', commentCount: 5 }));
      }
      if (call.path === '/api/v1/comments/cmt_A/children') return json(enumPage([makeComment('cmt_C', { parentCommentId: 'cmt_A', depth: 1, body: 'Loaded later' })], { continuationToken: 'more2' }));
      return extra(call);
    });
  }

  it('renders the tree with placeholders, collapses, and loads more replies with the continuation token', async () => {
    const { fetchImpl, calls } = backend();
    await renderMember(<CommentThread post={post} />, { fetchImpl });
    expect(await screen.findByText('Top comment')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/feed/fpo_1/comments').search).toBe('?maxResults=20&ordering=Top&depth=3&childLimit=5');
    expect(screen.getByText('Deep reply')).toBeInTheDocument();
    expect(screen.getAllByText('[deleted]').length).toBeGreaterThan(0);
    expect(screen.getByText('Orphan reply')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Load 2 more replies' }));
    expect(await screen.findByText('Loaded later')).toBeInTheDocument();
    const childCall = calls.find((c) => c.path === '/api/v1/comments/cmt_A/children');
    expect(childCall.query).toMatchObject({ continuationToken: 'more1', ordering: 'Top' });
    expect(screen.getByRole('button', { name: 'Load 1 more reply' })).toBeInTheDocument();

    const top = document.getElementById('comment-cmt_A');
    await userEvent.click(within(top).getAllByRole('button', { name: 'Collapse thread' })[0]);
    expect(screen.queryByText('Deep reply')).toBeNull();
    expect(screen.getByText('3 replies hidden')).toBeInTheDocument();
    await userEvent.click(within(top).getByRole('button', { name: 'Expand thread' }));
    expect(screen.getByText('Deep reply')).toBeInTheDocument();
  });

  it('reattaches a reply at the maximum depth as a sibling and shows "replying to"', async () => {
    const { fetchImpl, calls } = backend((call) => {
      if (call.path === '/api/v1/feed/fpo_1/comments' && call.method === 'POST') {
        return json(makeComment('cmt_R', { parentCommentId: 'cmt_A', replyToCommentId: 'cmt_B', replyToUser: user('usr_4', 'Deep Diver', { handle: 'deepdiver' }), depth: 8, body: call.body.body }), 201);
      }
      return undefined;
    });
    await renderMember(<CommentThread post={post} />, { fetchImpl });
    const deep = (await screen.findByText('Deep reply')).closest('li');
    await userEvent.click(within(deep).getByRole('button', { name: 'Reply' }));
    await userEvent.type(within(deep).getByRole('combobox', { name: 'Reply to Deep Diver' }), 'Same level please');
    await userEvent.click(within(deep).getAllByRole('button', { name: 'Reply' }).find((b) => b.type === 'submit'));
    const created = await screen.findByText('Same level please');
    const post_ = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/feed/fpo_1/comments');
    expect(post_.body).toMatchObject({ body: 'Same level please', parentCommentId: 'cmt_B', mediaIds: [] });
    const node = created.closest('li');
    expect(node.parentElement.closest('li').id).toBe('comment-cmt_A');
    expect(within(node).getByText('replying to @deepdiver')).toBeInTheDocument();
  });

  it('orders by new and opens the permalink view with ancestors', async () => {
    const { fetchImpl, calls } = backend((call) => {
      if (call.path === '/api/v1/comments/cmt_B') {
        return json({ target: { type: 'FeedPost', id: 'fpo_1' }, ancestors: [makeComment('cmt_A', { body: 'Ancestor text' })], comment: makeComment('cmt_B', { parentCommentId: 'cmt_A', body: 'Focused text' }) });
      }
      return undefined;
    });
    const first = await renderMember(<CommentThread post={post} />, { fetchImpl });
    await screen.findByText('Top comment');
    await userEvent.click(screen.getByRole('radio', { name: 'New' }));
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/feed/fpo_1/comments').at(-1).query.ordering).toBe('CreatedDescending'));
    first.unmount();

    await renderMember(<CommentThread post={post} focusId="cmt_B" />, { fetchImpl });
    expect(await screen.findByText('Focused text')).toBeInTheDocument();
    expect(screen.getByText('Ancestor text')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'View all comments' })).toHaveAttribute('href', '/app/feed/fpo_1#comments');
  });
});
