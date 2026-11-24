import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import FeedView from '../src/views/member/FeedView.jsx';
import MarkdownBody from '../src/components/feed/MarkdownBody.jsx';
import ReactionBar from '../src/components/feed/ReactionBar.jsx';
import PostComposer, { validatePostDraft } from '../src/components/feed/PostComposer.jsx';
import { postingPermissions } from '../src/utils/engagement.js';
import { useAuth } from '../src/context/AuthContext.jsx';
import strings from '../src/strings.js';
import { json, makeMe } from './helpers.jsx';
import { enumPage, makePost, memberFetch, renderMember, user } from './engagementHelpers.jsx';

describe('Markdown rendering is sanitized', () => {
  it('renders headings, code, rules, and tables as elements', () => {
    const body = '# Title\n\nUse `@x **y**` now\n\n```js\nconst a = "<b>";\n```\n\n---\n\n| Belt | Years |\n|:---|---:|\n| Blue | 2 |';
    const { container } = render(<MemoryRouter><MarkdownBody body={body} /></MemoryRouter>);
    expect(container.querySelector('h3.md-heading-1')).toHaveTextContent('Title');
    expect(container.querySelector('code.md-inline-code')).toHaveTextContent('@x **y**');
    expect(container.querySelector('pre.md-code-block')).toHaveAttribute('data-lang', 'js');
    expect(container.querySelector('pre.md-code-block').textContent).toBe('const a = "<b>";');
    expect(container.querySelector('b')).toBeNull();
    expect(container.querySelector('hr.md-rule')).not.toBeNull();
    const cells = Array.from(container.querySelectorAll('table.md-table th, table.md-table td')).map((c) => c.textContent);
    expect(cells).toEqual(['Belt', 'Years', 'Blue', '2']);
    expect(container.querySelector('table.md-table td:last-child').style.textAlign).toBe('right');
  });

  it('renders the subset as elements and never as HTML', () => {
    const body = [
      '## Heading **bold**',
      '<script>alert(1)</script> <img src=x onerror=alert(1)>',
      '[evil](javascript:alert(1)) [ok](https://example.test/a) @alexchen @nobody',
      '> quote',
      '- one',
      '- two'
    ].join('\n');
    const { container } = render(<MemoryRouter><MarkdownBody body={body} mentions={[{ userId: 'usr_2', handle: 'alexchen', displayName: 'Alex Chen' }]} /></MemoryRouter>);
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('[onerror]')).toBeNull();
    const links = Array.from(container.querySelectorAll('a'));
    for (const a of links) expect(a.getAttribute('href')).toMatch(/^(https?:\/\/|\/app\/members\/)/);
    const ok = screen.getByRole('link', { name: 'ok' });
    expect(ok).toHaveAttribute('rel', 'noopener noreferrer nofollow');
    expect(ok).toHaveAttribute('target', '_blank');
    expect(screen.getByRole('link', { name: '@alexchen' })).toHaveAttribute('href', '/app/members/usr_2');
    expect(screen.queryByRole('link', { name: '@nobody' })).toBeNull();
    expect(container.textContent).toContain('<script>alert(1)</script>');
    expect(container.textContent).toContain('[evil](javascript:alert(1))');
    expect(container.querySelector('h4')).toHaveTextContent('Heading bold');
    expect(container.querySelector('blockquote')).toHaveTextContent('quote');
    expect(container.querySelectorAll('li')).toHaveLength(2);
  });

  it('renders comment headings as plain text and public mentions without links', () => {
    const { container } = render(<MemoryRouter><MarkdownBody body="## not a heading @alexchen" mentions={[{ userId: 'u', handle: 'alexchen' }]} allowHeadings={false} linkMentions={false} /></MemoryRouter>);
    expect(container.querySelector('h3')).toBeNull();
    expect(container.textContent).toBe('## not a heading @alexchen');
    expect(container.querySelector('a')).toBeNull();
  });
});

describe('feed paging', () => {
  it('shows pinned posts first, pages with continuation tokens, and de-duplicates', async () => {
    const pages = {
      '': enumPage([makePost('fpo_p', { isPinned: true, title: 'Pinned rules' }), makePost('fpo_1'), makePost('fpo_2')], { continuationToken: 'tok2' }),
      tok2: enumPage([makePost('fpo_2', { title: 'Post fpo_2 again' }), makePost('fpo_3'), makePost('fpo_x', { kind: 'FutureKind', title: 'Unknown kind' })])
    };
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/feed') return json(pages[call.query.continuationToken || '']);
      return undefined;
    });
    await renderMember(<FeedView />, { fetchImpl, path: '/app/feed', routePath: '/app/feed' });
    expect(await screen.findByText('Pinned rules')).toBeInTheDocument();
    const pinned = screen.getByRole('region', { name: 'Pinned' }) || screen.getByText('Pinned');
    expect(within(pinned).getByText('Pinned rules')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/feed').query).toEqual({ maxResults: '20' });

    await userEvent.click(screen.getByRole('button', { name: 'Load more posts' }));
    expect(await screen.findByText('Post fpo_3')).toBeInTheDocument();
    expect(calls.filter((c) => c.path === '/api/v1/feed').at(-1).query).toEqual({ maxResults: '20', continuationToken: 'tok2' });
    expect(screen.getAllByText(/Post fpo_2/)).toHaveLength(1);
    expect(screen.getByText('Post fpo_2 again')).toBeInTheDocument();
    expect(screen.queryByText('Unknown kind')).toBeNull();
    expect(screen.getByText('You are all caught up.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more posts' })).toBeNull();
  });

  it('applies label and tag filters as enumeration parameters and keeps them in the URL', async () => {
    const { fetchImpl, calls } = memberFetch((call) => (call.path === '/api/v1/feed' ? json(enumPage([makePost('fpo_1')])) : undefined));
    await renderMember(<FeedView />, { fetchImpl, path: '/app/feed?labels=topics&tag.level=beginner&labelMatch=Any', routePath: '/app/feed' });
    await screen.findByText('Post fpo_1');
    expect(calls.find((c) => c.path === '/api/v1/feed').search).toBe('?maxResults=20&labels=topics&labelMatch=Any&tag.level=beginner');
    expect(screen.getByText('Label: topics')).toBeInTheDocument();
    expect(screen.getByText('level = beginner')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Remove filter Label: topics' }));
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/feed').at(-1).search).toBe('?maxResults=20&tag.level=beginner'));
    await userEvent.click(screen.getByRole('button', { name: 'Clear all' }));
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/feed').at(-1).search).toBe('?maxResults=20'));
  });

  it('shows the composer only to members who may post', async () => {
    const viewer = memberFetch((call) => (call.path === '/api/v1/feed' ? json(enumPage([])) : undefined));
    const first = await renderMember(<FeedView />, { fetchImpl: viewer.fetchImpl, path: '/app/feed', routePath: '/app/feed' });
    await screen.findByText('Nothing has been posted yet.');
    expect(screen.queryByRole('button', { name: 'Write a post' })).toBeNull();
    first.unmount();

    const poster = memberFetch((call) => (call.path === '/api/v1/feed' ? json(enumPage([])) : undefined), makeMe({ flags: { canPostToFeed: true, canPost: true } }));
    await renderMember(<FeedView />, { fetchImpl: poster.fetchImpl, path: '/app/feed', routePath: '/app/feed' });
    expect(await screen.findByRole('button', { name: 'Write a post' })).toBeEnabled();
  });
});

describe('reactions', () => {
  const target = { type: 'FeedPost', id: 'fpo_1' };

  it('updates optimistically, then takes the server counts', async () => {
    let resolve;
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.method === 'PUT' && call.path === '/api/v1/feed/fpo_1/reactions/star') return new Promise((r) => { resolve = () => r(json({ reactionCounts: { clap: 2, star: 5 }, reactionTotal: 7, myReactions: ['star'] })); });
      return undefined;
    });
    await renderMember(<ReactionBar target={target} value={{ reactionCounts: { clap: 2 }, reactionTotal: 2, myReactions: [] }} />, { fetchImpl });
    expect(screen.getByRole('button', { name: 'Clap, 2 reactions' })).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(screen.getByRole('button', { name: 'Add a reaction' }));
    await userEvent.click(screen.getByRole('button', { name: 'React with Star' }));
    expect(screen.getByRole('button', { name: 'Star, 1 reaction' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText('3 reactions')).toBeInTheDocument();
    resolve();
    expect(await screen.findByRole('button', { name: 'Star, 5 reactions' })).toBeInTheDocument();
    expect(screen.getByText('7 reactions')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'PUT' && c.path.endsWith('/reactions/star'))).toBe(true);
  });

  it('rolls back and explains when the server refuses', async () => {
    const { fetchImpl } = memberFetch((call) => {
      if (call.method === 'PUT') return json({ error: 'Muted', message: 'Muted until later.' }, 403);
      if (call.method === 'DELETE') return json({ error: 'InteractionBlocked', message: 'x' }, 403);
      return undefined;
    });
    await renderMember(<ReactionBar target={target} value={{ reactionCounts: { clap: 1 }, reactionTotal: 1, myReactions: ['clap'] }} />, { fetchImpl });
    // The like reaction shows once: the one-tap like button carries its count (no separate chip).
    expect(screen.getAllByRole('button', { name: /Clap/ })).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Clap, 1 reaction' })).toHaveTextContent('Clap1');
    await userEvent.click(screen.getByRole('button', { name: 'Clap, 1 reaction' }));
    expect(await screen.findByText('You cannot react to this member.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Clap, 1 reaction' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Add a reaction' }));
    await userEvent.click(screen.getByRole('button', { name: 'React with Star' }));
    expect(await screen.findByText('You are muted and cannot react right now.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Star, / })).toBeNull();
  });

  it('loads a bounded preview on hover and opens the matching tab with paged people', async () => {
    const rows = Array.from({ length: 5 }, (_, i) => ({ id: 'rx_' + i, user: user('usr_' + i, 'Person ' + i), key: 'clap' }));
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path !== '/api/v1/feed/fpo_1/reactions') return undefined;
      if (call.query.continuationToken) return json(enumPage([{ id: 'rx_5', user: user('usr_5', 'Last Person'), key: 'clap' }]));
      return json(enumPage(rows, { continuationToken: 'next-people', totalRecords: 6 }));
    });
    await renderMember(<ReactionBar target={target} value={{ reactionCounts: { clap: 6 }, reactionTotal: 6, myReactions: [] }} />, { fetchImpl });
    expect(calls.filter((c) => c.path.endsWith('/reactions'))).toHaveLength(0);
    await userEvent.hover(screen.getByRole('button', { name: 'Clap, 6 reactions' }));
    const preview = await screen.findByRole('dialog', { name: 'People who reacted with Clap' });
    expect(await within(preview).findByText('Person 4')).toBeInTheDocument();
    expect(within(preview).getByText('More people reacted.')).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/reactions')).query).toEqual({ maxResults: '5', key: 'clap' });
    await userEvent.click(within(preview).getByRole('button', { name: 'See all people' }));
    const modal = await screen.findByRole('dialog', { name: 'Reactions' });
    expect(within(modal).getByRole('tab', { name: 'Clap, 6 reactions' })).toHaveAttribute('aria-selected', 'true');
    await userEvent.click(await within(modal).findByRole('button', { name: 'Load more' }));
    expect(await within(modal).findByRole('link', { name: 'Last Person' })).toBeInTheDocument();
    expect(calls.filter((c) => c.method === 'PUT' || c.method === 'DELETE')).toHaveLength(0);
    expect(calls.filter((c) => c.path.endsWith('/reactions')).at(-1).query).toEqual({ maxResults: '50', key: 'clap', continuationToken: 'next-people' });
  });

  it('allows keyboard inspection without reaction permission and retries failed previews', async () => {
    let attempts = 0;
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path !== '/api/v1/feed/fpo_1/reactions') return undefined;
      attempts += 1;
      return attempts === 1 ? json({ error: 'Unavailable' }, 503) : json(enumPage([{ id: 'rx_1', user: user('usr_9', 'Kim Park'), key: 'star' }]));
    });
    await renderMember(<ReactionBar canReact={false} target={target} value={{ reactionCounts: { star: 1 }, reactionTotal: 1, myReactions: [] }} />, { fetchImpl });
    await userEvent.tab();
    const preview = await screen.findByRole('dialog', { name: 'People who reacted with Star' });
    const retry = await within(preview).findByRole('button', { name: 'Try again' });
    await userEvent.keyboard('{ArrowDown}');
    expect(retry).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    expect(await within(preview).findByText('Kim Park')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByRole('button', { name: 'Star, 1 reaction' })).toHaveFocus();
    expect(calls.filter((c) => c.method === 'PUT' || c.method === 'DELETE')).toHaveLength(0);
  });

  it('lists who reacted from the reactions enumeration', async () => {
    const { fetchImpl, calls } = memberFetch((call) => {
      if (call.path === '/api/v1/feed/fpo_1/reactions') return json(enumPage([{ user: user('usr_9', 'Kim Park'), key: 'clap', createdUtc: '2026-11-10T00:00:00Z' }]));
      return undefined;
    });
    await renderMember(<ReactionBar target={target} value={{ reactionCounts: { clap: 1 }, reactionTotal: 1, myReactions: [] }} />, { fetchImpl });
    await userEvent.click(screen.getByRole('button', { name: '1 reaction' }));
    expect(await screen.findByText('Kim Park')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/api/v1/feed/fpo_1/reactions').query).toEqual({ maxResults: '50' });
  });
});

/** A fake storage PUT for the upload helper. */
function fakePut(log, { failFirst = false } = {}) {
  let failed = false;
  return async ({ url, onProgress, body }) => {
    log.push('put ' + url);
    if (failFirst && !failed) {
      failed = true;
      const err = new Error('offline');
      err.status = 0;
      throw err;
    }
    if (onProgress) onProgress(body.size || 0);
    return { etag: '"e1"' };
  };
}

function uploadBackend({ status = 'Ready', extra } = {}) {
  const log = [];
  const media = { id: 'med_1', kind: 'Image', status: 'Uploading', altText: null, image: null };
  const ready = { ...media, status, image: { variants: { thumb: { width: 320, height: 240, webpUrl: '/m/t.webp', jpegUrl: '/m/t.jpg' }, medium: { width: 1080, height: 810, webpUrl: '/m/m.webp', jpegUrl: '/m/m.jpg' } } } };
  const handler = (call) => {
    if (call.path === '/api/v1/media/uploads') { log.push('create ' + call.body.kind); return json({ media, upload: { mode: 'Single', single: { method: 'PUT', url: 'https://store.test/1', headers: { 'Content-Type': 'image/png' } }, multipart: null, uploadedParts: [] } }, 201); }
    if (call.path === '/api/v1/media/med_1/upload-urls') { log.push('refresh'); return json({ upload: { mode: 'Single', single: { method: 'PUT', url: 'https://store.test/2', headers: {} }, multipart: null, uploadedParts: [] } }); }
    if (call.path === '/api/v1/media/med_1/complete') { log.push('complete'); return json({ ...media, status: 'Processing' }, 202); }
    if (call.path === '/api/v1/media/enumerate') { log.push('poll'); return json({ objects: [ready], endOfResults: true }); }
    if (call.path === '/api/v1/members/search') return json(enumPage([user('usr_5', 'Alex Chen', { handle: 'alexchen' })]));
    if (call.path === '/api/v1/link-previews') return json({ error: 'LinkPreviewUnavailable' }, 400);
    if (call.path === '/api/v1/feed' && call.method === 'POST') { log.push('post'); return json(makePost('fpo_new', call.body), 201); }
    return extra ? extra(call) : undefined;
  };
  return { log, handler };
}

describe('post composer', () => {
  const posterMe = makeMe({ flags: { canPostToFeed: true, canPost: true, canPostMedia: true, canUploadVideo: false } });

  it('derives posting permissions from Me flags', () => {
    expect(postingPermissions(makeMe())).toMatchObject({ canCreateFeedPost: false, canAttachMedia: true });
    expect(postingPermissions(posterMe)).toMatchObject({ canCreateFeedPost: true, canAttachMedia: true, canAttachVideo: false });
    expect(postingPermissions(makeMe({ flags: { canPostToFeed: true, canPost: false } }))).toMatchObject({ canCreateFeedPost: false, postingBlocked: true, canAttachMedia: false });
    expect(postingPermissions(makeMe({ flags: { canPostToFeed: true, canPostMedia: false } }))).toMatchObject({ canCreateFeedPost: true, canAttachMedia: false, canAttachVideo: false });
    expect(postingPermissions(makeMe({ flags: { canPostToFeed: true, isMuted: true, mutedUntilUtc: '2099-01-01T00:00:00Z' } }))).toMatchObject({ muted: true, canPost: false, canAttachMedia: false });
    expect(postingPermissions(makeMe({ roles: [{ role: 'SystemAdmin', resourceType: 'Global', resourceId: '*' }], flags: { canPost: false } }))).toMatchObject({ isAdmin: true, canCreateFeedPost: true, canAttachVideo: true });
    expect(validatePostDraft({ title: '', body: '  ', mediaCount: 0, publishAt: null, maxBodyLength: 10 })).toEqual({ body: 'Write something or attach media.' });
    expect(validatePostDraft({ title: 'x'.repeat(141), body: 'b', mediaCount: 0, publishAt: new Date(Date.now() + 1000).toISOString(), maxBodyLength: 10 })).toMatchObject({ title: expect.any(String), publishAtUtc: expect.any(String) });
  });

  it('hides attach controls for text-only members and shows the muted banner', async () => {
    const { fetchImpl } = memberFetch(() => undefined);
    const perms = postingPermissions(makeMe({ flags: { canPostToFeed: true, canPostMedia: false, isMuted: true, mutedUntilUtc: '2099-01-01T00:00:00Z' } }));
    await renderMember(<PostComposer client={null} perms={perms} onSubmit={async () => ({})} onSaved={() => {}} />, { fetchImpl });
    expect(screen.queryByRole('button', { name: /Attach/ })).toBeNull();
    expect(screen.getByText(/You are muted until/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Post' })).toBeDisabled();
  });

  it('uploads an image (progress, processing, ready), suggests mentions, and posts with mediaIds', async () => {
    const backend = uploadBackend();
    const { fetchImpl } = memberFetch(backend.handler, posterMe);
    let saved = null;
    const Harness = () => {
      const auth = useAuth();
      return <PostComposer client={auth.client} perms={postingPermissions(posterMe)} onSubmit={(body) => auth.client.createFeedPost(body)} onSaved={(p) => { saved = p; }} uploadOptions={{ put: fakePut(backend.log), sleep: async () => {} }} />;
    };
    await renderMember(<Harness />, { fetchImpl });
    expect(screen.getByRole('button', { name: 'Attach photos' })).toBeInTheDocument();
    const file = new File([new Uint8Array(10)], 'photo.png', { type: 'image/png' });
    await userEvent.upload(screen.getByLabelText('Attach photos'), file);
    expect(await screen.findByText('Ready')).toBeInTheDocument();
    expect(backend.log).toEqual(['create Image', 'put https://store.test/1', 'complete', 'poll']);

    const textarea = screen.getByRole('combobox', { name: 'Post' });
    await userEvent.type(textarea, 'Thanks @al');
    const option = await screen.findByRole('option', { name: /Alex Chen/ });
    await userEvent.click(option);
    expect(textarea).toHaveValue('Thanks @alexchen ');
    await userEvent.click(screen.getByRole('button', { name: 'Post' }));
    await waitFor(() => expect(saved).not.toBeNull());
    expect(saved.mediaIds).toEqual(['med_1']);
    expect(saved.body).toBe('Thanks @alexchen');
    expect(saved.labels).toEqual([]);
  });

  it('clears the wait-for-uploads notice once the upload finishes', async () => {
    const backend = uploadBackend();
    const { fetchImpl } = memberFetch(backend.handler, posterMe);
    let release;
    const gate = new Promise((resolve) => { release = resolve; });
    const put = async (request) => {
      await gate;
      return fakePut(backend.log)(request);
    };
    const Harness = () => {
      const auth = useAuth();
      return <PostComposer client={auth.client} perms={postingPermissions(posterMe)} onSubmit={async () => ({})} onSaved={() => {}} uploadOptions={{ put, sleep: async () => {} }} />;
    };
    await renderMember(<Harness />, { fetchImpl });
    await userEvent.type(screen.getByRole('combobox', { name: 'Post' }), 'Hello');
    await userEvent.upload(screen.getByLabelText('Attach photos'), new File([new Uint8Array(4)], 'photo.png', { type: 'image/png' }));
    await userEvent.click(screen.getByRole('button', { name: 'Post' }));
    expect(await screen.findByText(strings.composer.waitForUploads)).toBeInTheDocument();
    release();
    expect(await screen.findByText('Ready')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText(strings.composer.waitForUploads)).not.toBeInTheDocument());
  });

  it('rejects files the account may not attach and offers resume after a failed upload', async () => {
    const backend = uploadBackend();
    const { fetchImpl } = memberFetch(backend.handler, posterMe);
    const Harness = () => {
      const auth = useAuth();
      return <PostComposer client={auth.client} perms={postingPermissions(posterMe)} onSubmit={async () => ({})} onSaved={() => {}} uploadOptions={{ put: fakePut(backend.log, { failFirst: true }), sleep: async () => {} }} />;
    };
    await renderMember(<Harness />, { fetchImpl });
    const input = screen.getByLabelText('Attach photos');
    expect(input).toHaveAttribute('accept', 'image/jpeg,image/png,image/webp');
    await userEvent.upload(input, new File([new Uint8Array(4)], 'photo.png', { type: 'image/png' }));
    // The first PUT fails with a network error; the helper refreshes the URLs and resumes.
    expect(await screen.findByText('Ready')).toBeInTheDocument();
    expect(backend.log).toEqual(['create Image', 'put https://store.test/1', 'refresh', 'put https://store.test/2', 'complete', 'poll']);
  });
});
