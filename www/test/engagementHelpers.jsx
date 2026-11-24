import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider, useAuth } from '../src/context/AuthContext.jsx';
import { ToastProvider } from '../src/context/ToastContext.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { authResponse, createFetch, json, makeMe } from './helpers.jsx';

/** The build-time site with extra `features` (for example server capabilities). */
export function siteWithFeatures(features) {
  return { ...BUILD_SITE, property: { ...BUILD_SITE.property, features: { ...(BUILD_SITE.property.features || {}), ...features } } };
}

/** An EnumerationResult page. */
export function enumPage(objects, { continuationToken = null, totalRecords = null, ...extra } = {}) {
  return { success: true, maxResults: 25, skip: 0, continuationToken, endOfResults: !continuationToken, totalRecords, recordsRemaining: null, timestampUtc: '2026-11-11T00:00:00Z', objects, ...extra };
}

/** A UserSummary. */
export function user(id, name, extra = {}) {
  return { id, displayName: name, handle: name.toLowerCase().replace(/\s+/g, ''), avatarUrl: null, rank: null, rankLevel: 0, isStaff: false, isDeleted: false, ...extra };
}

/** A Published FeedPost. */
export function makePost(id, extra = {}) {
  return {
    id, kind: 'Original', author: user('usr_2', 'Alex Chen'), title: 'Post ' + id, body: 'Body of ' + id, excerpt: 'Body of ' + id, mentions: [], attachments: [], linkPreview: null,
    visibility: 'Members', state: 'Published', commentsEnabled: true, isPinned: false, pinnedUtc: null, publishAtUtc: null, publishedUtc: '2026-11-10T18:00:00Z', createdUtc: '2026-11-10T18:00:00Z',
    updatedUtc: '2026-11-10T18:00:00Z', editedUtc: null, revisionCount: 0, commentCount: 0, reactionCounts: {}, reactionTotal: 0, myReactions: [], commentTarget: { type: 'FeedPost', id },
    labels: [], tags: {}, source: null, moderation: null,
    viewer: { canEdit: false, canDelete: false, canPin: false, canComment: true, canReact: true, canReport: true, canModerate: false, canAttachMedia: true, canAttachVideo: true },
    ...extra
  };
}

/** A visible Comment. */
export function makeComment(id, extra = {}) {
  return {
    id, targetType: 'FeedPost', targetId: 'fpo_1', parentCommentId: null, replyToCommentId: null, replyToUser: null, depth: 0, state: 'Visible', author: user('usr_3', 'Sam Lee'),
    body: 'Comment ' + id, mentions: [], attachments: [], linkPreview: null, createdUtc: '2026-11-10T19:00:00Z', editedUtc: null, revisionCount: 0, childCount: 0, descendantCount: 0,
    reactionCounts: {}, reactionTotal: 0, myReactions: [], labels: [], tags: {},
    viewer: { canEdit: false, canDelete: false, canReply: true, canReact: true, canReport: true, canModerate: false, canAttachMedia: true, canAttachVideo: true },
    children: [], moreChildren: null, ...extra
  };
}

/**
 * A fetch mock with a signed-in member session: `/auth/refresh` returns `me`, the media grant
 * and unread count are answered, and everything else goes to `handler`.
 */
export function memberFetch(handler, me = makeMe()) {
  return createFetch((call) => {
    if (call.path === '/api/v1/auth/refresh') return json(authResponse(me));
    if (call.path === '/api/v1/media/grant') return json({ mode: 'Local', expiresUtc: '2099-01-01T00:00:00Z', cookies: [] });
    if (call.path === '/api/v1/notifications/unread-count') return json({ unreadCount: 0 });
    if (call.path === '/api/v1/labels/enumerate') return json(enumPage([{ label: 'topics', count: 4 }, { label: 'guides', count: 2 }]));
    return handler(call);
  });
}

/** Render `ui` at `path` inside a restored member session; waits for the session. */
export async function renderMember(ui, { fetchImpl, path = '/test', routePath = '/test', extraRoutes = null, features = null } = {}) {
  const result = render(
    <PropertyProvider site={features ? siteWithFeatures(features) : BUILD_SITE}>
    <ThemeProvider>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider fetchImpl={fetchImpl} restoreOnLoad>
          <ToastProvider>
            <SessionGate>
              <Routes>
                <Route path={routePath} element={ui} />
                {extraRoutes}
              </Routes>
            </SessionGate>
          </ToastProvider>
        </AuthProvider>
      </MemoryRouter>
    </ThemeProvider>
    </PropertyProvider>
  );
  await screen.findByTestId('session-ready');
  return result;
}


function SessionGate({ children }) {
  const auth = useAuth();
  if (!auth.isSignedIn) return null;
  return <><span data-testid="session-ready" hidden />{children}</>;
}
