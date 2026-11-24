import { Suspense, lazy } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { AdminAuthProvider, useAdminAuth } from './context/AdminAuthContext.jsx';
import { useProperty, useRuntimeProperty } from './context/PropertyContext.jsx';
import AdminLayout from './layouts/AdminLayout.jsx';
import RequireAdmin from './views/admin/RequireAdmin.jsx';
import AdminLoginView from './views/admin/AdminLoginView.jsx';
import HomeView from './views/admin/HomeView.jsx';
import RequestHistoryView from './views/admin/RequestHistoryView.jsx';
import ApiExplorerView from './views/admin/ApiExplorerView.jsx';
import SettingsView from './views/admin/SettingsView.jsx';
import ObservabilityView from './views/admin/ObservabilityView.jsx';
import UsageView from './views/admin/UsageView.jsx';
import PlaceholderView from './views/admin/PlaceholderView.jsx';
import MembersView from './views/admin/MembersView.jsx';
import SubscriptionsView from './views/admin/SubscriptionsView.jsx';
import SignupCodesView from './views/admin/SignupCodesView.jsx';
import SignupCodeDetailView from './views/admin/SignupCodeDetailView.jsx';
import AuditLogView from './views/admin/AuditLogView.jsx';
import FeedAdminView from './views/admin/FeedAdminView.jsx';
import ModerationView from './views/admin/ModerationView.jsx';
import ContentReviewView from './views/admin/ContentReviewView.jsx';
import MediaLibraryView from './views/admin/MediaLibraryView.jsx';
import ForumsAdminView from './views/admin/ForumsAdminView.jsx';
import ThreadsAdminView from './views/admin/ThreadsAdminView.jsx';
import TrendingView from './views/admin/TrendingView.jsx';
import PropertyConfigView from './views/admin/PropertyConfigView.jsx';
import AccessModelView from './views/admin/AccessModelView.jsx';
import BrandingView from './views/admin/BrandingView.jsx';
import DocumentsView from './views/admin/DocumentsView.jsx';
import AiSettingsView from './views/admin/AiSettingsView.jsx';
import ProfileFieldsView from './views/admin/ProfileFieldsView.jsx';
import EmailView from './views/admin/EmailView.jsx';
import IntegrationsView from './views/admin/IntegrationsView.jsx';
import UpdatesView from './views/admin/UpdatesView.jsx';
import UpdateDetailView from './views/admin/UpdateDetailView.jsx';
import UpdateChannelsView from './views/admin/UpdateChannelsView.jsx';
import UpdateKeysView from './views/admin/UpdateKeysView.jsx';
import FlagsView from './views/admin/FlagsView.jsx';
import KillSwitchesView from './views/admin/KillSwitchesView.jsx';
import StringsView from './views/admin/StringsView.jsx';
import BannersView from './views/admin/BannersView.jsx';
import AssetsView from './views/admin/AssetsView.jsx';
import AiEndpointsView from './views/admin/ai/AiEndpointsView.jsx';
import AiAssistantsView from './views/admin/ai/AiAssistantsView.jsx';
import AiConversationsView from './views/admin/ai/AiConversationsView.jsx';
import AiAnalyticsView from './views/admin/ai/AiAnalyticsView.jsx';
import AiPromptsView from './views/admin/ai/AiPromptsView.jsx';
import AiRolesView from './views/admin/ai/AiRolesView.jsx';
import JobsView from './views/admin/jobs/JobsView.jsx';
import MetricsView from './views/admin/metrics/MetricsView.jsx';
import LibraryAdminView from './views/admin/library/LibraryAdminView.jsx';
import LessonEditorView from './views/admin/library/LessonEditorView.jsx';
import EventsAdminView from './views/admin/events/EventsAdminView.jsx';
import EventEditorView from './views/admin/events/EventEditorView.jsx';
import DirectoryAdminView from './views/admin/directory/DirectoryAdminView.jsx';
import { featureEnabled } from './views/member/memberRoutes.js';
import { PLACEHOLDER_ROUTES, adminRouteAvailable, findAdminRoute } from './views/admin/adminRoutes.js';
import { Loading } from './components/States.jsx';

// Phase 5 staff pages load on demand so the console bundle stays small.
const SupportInboxView = lazy(() => import('./views/admin/SupportInboxView.jsx'));
const IdeasAdminView = lazy(() => import('./views/admin/IdeasAdminView.jsx'));
const BroadcastsView = lazy(() => import('./views/admin/BroadcastsView.jsx'));

/**
 * Loads the runtime property through the admin client, so a delegated session (which names its
 * property with X-TK-Property on the platform host) reads the property it manages.
 */
function AdminRuntimeProperty() {
  useRuntimeProperty(useAdminAuth().client);
  return null;
}

/** Admin console routes, mounted at /admin/*. Loaded lazily so the public site stays small. */
function AdminApp({ fetchImpl = null }) {
  const { property } = useProperty();
  // Sections whose server capability is missing open the coming-soon page and make no calls.
  const gate = (id, element) => {
    const route = findAdminRoute(id);
    return adminRouteAvailable(property, route) ? element : <PlaceholderView route={route} property={property} />;
  };
  // Phase 5 sections whose property feature is off go back to Home (their sidebar entries are hidden).
  const feature = (key, element) => (featureEnabled(property, key) ? <Suspense fallback={<Loading />}>{element}</Suspense> : <Navigate to="/admin/home" replace />);
  return (
    <AdminAuthProvider fetchImpl={fetchImpl}>
      <AdminRuntimeProperty />
      <Routes>
        <Route path="login" element={<AdminLoginView />} />
        <Route element={<RequireAdmin><AdminLayout /></RequireAdmin>}>
          <Route index element={<Navigate to="home" replace />} />
          <Route path="home" element={<HomeView />} />
          <Route path="moderation" element={<ModerationView />} />
          <Route path="content-review" element={<RequireAdmin adminOnly><ContentReviewView /></RequireAdmin>} />
          <Route path="requests" element={<RequireAdmin adminOnly><RequestHistoryView /></RequireAdmin>} />
          <Route path="explorer" element={<RequireAdmin adminOnly><ApiExplorerView /></RequireAdmin>} />
          <Route path="settings" element={<RequireAdmin adminOnly><SettingsView /></RequireAdmin>} />
          <Route path="property" element={<RequireAdmin adminOnly><PropertyConfigView /></RequireAdmin>} />
          <Route path="access" element={<RequireAdmin adminOnly><AccessModelView /></RequireAdmin>} />
          <Route path="branding" element={<RequireAdmin adminOnly><BrandingView /></RequireAdmin>} />
          <Route path="documents" element={<RequireAdmin adminOnly><DocumentsView /></RequireAdmin>} />
          <Route path="ai-settings" element={<RequireAdmin adminOnly><AiSettingsView /></RequireAdmin>} />
          <Route path="observability" element={<RequireAdmin adminOnly><ObservabilityView /></RequireAdmin>} />
          <Route path="usage" element={<RequireAdmin adminOnly><UsageView /></RequireAdmin>} />
          <Route path="metrics/:pageId" element={<RequireAdmin adminOnly><MetricsView /></RequireAdmin>} />
          <Route path="jobs" element={<RequireAdmin adminOnly><JobsView /></RequireAdmin>} />
          <Route path="members" element={<RequireAdmin adminOnly><MembersView /></RequireAdmin>} />
          <Route path="profile-fields" element={<RequireAdmin adminOnly><ProfileFieldsView /></RequireAdmin>} />
          <Route path="email" element={<RequireAdmin adminOnly><EmailView /></RequireAdmin>} />
          <Route path="integrations" element={<RequireAdmin adminOnly><IntegrationsView /></RequireAdmin>} />
          <Route path="subscriptions" element={<RequireAdmin adminOnly><SubscriptionsView /></RequireAdmin>} />
          <Route path="signup-codes" element={<RequireAdmin adminOnly><SignupCodesView /></RequireAdmin>} />
          <Route path="signup-codes/:id" element={<RequireAdmin adminOnly><SignupCodeDetailView /></RequireAdmin>} />
          <Route path="audit" element={<RequireAdmin adminOnly><AuditLogView /></RequireAdmin>} />
          <Route path="feed" element={<RequireAdmin adminOnly><FeedAdminView /></RequireAdmin>} />
          <Route path="media" element={<RequireAdmin adminOnly><MediaLibraryView /></RequireAdmin>} />
          <Route path="forums" element={<RequireAdmin adminOnly>{gate('forums', <ForumsAdminView />)}</RequireAdmin>} />
          <Route path="threads" element={<RequireAdmin forumModerator>{gate('threads', <ThreadsAdminView />)}</RequireAdmin>} />
          <Route path="ai-endpoints" element={<RequireAdmin adminOnly>{gate('ai-endpoints', <AiEndpointsView />)}</RequireAdmin>} />
          <Route path="ai-assistants" element={<RequireAdmin adminOnly>{gate('ai-assistants', <AiAssistantsView />)}</RequireAdmin>} />
          <Route path="ai-conversations" element={<RequireAdmin adminOnly>{gate('ai-conversations', <AiConversationsView />)}</RequireAdmin>} />
          <Route path="ai-analytics" element={<RequireAdmin adminOnly>{gate('ai-analytics', <AiAnalyticsView />)}</RequireAdmin>} />
          <Route path="ai-prompts" element={<RequireAdmin adminOnly><AiPromptsView /></RequireAdmin>} />
          <Route path="ai-roles" element={<RequireAdmin adminOnly><AiRolesView /></RequireAdmin>} />
          <Route path="trending" element={<RequireAdmin forumModerator>{gate('trending', <TrendingView />)}</RequireAdmin>} />
          {featureEnabled(property, 'library') && <Route path="library" element={<RequireAdmin area="library">{gate('library', <LibraryAdminView />)}</RequireAdmin>} />}
          {featureEnabled(property, 'library') && <Route path="library/lessons/:id" element={<RequireAdmin area="library"><LessonEditorView /></RequireAdmin>} />}
          {featureEnabled(property, 'events') && <Route path="events" element={<RequireAdmin area="events">{gate('events', <EventsAdminView />)}</RequireAdmin>} />}
          {featureEnabled(property, 'events') && <Route path="events/new" element={<RequireAdmin area="events"><EventEditorView /></RequireAdmin>} />}
          {featureEnabled(property, 'events') && <Route path="events/:id" element={<RequireAdmin area="events"><EventEditorView /></RequireAdmin>} />}
          {featureEnabled(property, 'directory') && <Route path="directory" element={<RequireAdmin area="directory">{gate('directory', <DirectoryAdminView />)}</RequireAdmin>} />}
          <Route path="support" element={<RequireAdmin supportStaff>{feature('support', <SupportInboxView />)}</RequireAdmin>} />
          <Route path="ideas" element={<RequireAdmin supportStaff>{feature('ideas', <IdeasAdminView />)}</RequireAdmin>} />
          <Route path="broadcasts" element={<RequireAdmin communicator>{feature('broadcasts', <BroadcastsView />)}</RequireAdmin>} />
          <Route path="updates" element={<RequireAdmin releaseManager><UpdatesView /></RequireAdmin>} />
          <Route path="updates/:id" element={<RequireAdmin releaseManager><UpdateDetailView /></RequireAdmin>} />
          <Route path="update-channels" element={<RequireAdmin releaseManager><UpdateChannelsView /></RequireAdmin>} />
          <Route path="update-keys" element={<RequireAdmin adminOnly><UpdateKeysView /></RequireAdmin>} />
          <Route path="flags" element={<RequireAdmin releaseManager><FlagsView /></RequireAdmin>} />
          <Route path="kill-switches" element={<RequireAdmin releaseManager><KillSwitchesView /></RequireAdmin>} />
          <Route path="strings" element={<RequireAdmin releaseManager><StringsView /></RequireAdmin>} />
          <Route path="banners" element={<RequireAdmin releaseManager><BannersView /></RequireAdmin>} />
          <Route path="assets" element={<RequireAdmin releaseManager><AssetsView /></RequireAdmin>} />
          {PLACEHOLDER_ROUTES.map((route) => (
            <Route key={route.id} path={route.id} element={<RequireAdmin adminOnly><PlaceholderView route={route} /></RequireAdmin>} />
          ))}
          <Route path="*" element={<Navigate to="home" replace />} />
        </Route>
      </Routes>
    </AdminAuthProvider>
  );
}

export default AdminApp;
