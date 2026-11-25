import { Suspense, lazy } from 'react';
import { BrowserRouter, Navigate, Route, Routes, useLocation, useParams } from 'react-router-dom';
import { ThemeProvider } from './context/ThemeContext.jsx';
import { AuthProvider } from './context/AuthContext.jsx';
import { AppContentProvider, useRemoteStringsRevision } from './context/AppContentContext.jsx';
import { ToastProvider } from './context/ToastContext.jsx';
import MarketingLayout from './layouts/MarketingLayout.jsx';
import MemberLayout from './layouts/MemberLayout.jsx';
import { PropertyProvider, useProperty } from './context/PropertyContext.jsx';
import ContentPage from './views/marketing/ContentPage.jsx';
import LegalPage from './views/marketing/LegalPage.jsx';
import { siteMetaFor } from './views/marketing/siteMeta.js';
import NotFoundPage from './views/marketing/NotFoundPage.jsx';
import PublicThreadPage from './views/marketing/PublicThreadPage.jsx';
import PublicLessonPage from './views/marketing/PublicLessonPage.jsx';
import SignInPage from './views/auth/SignInPage.jsx';
import SignUpPage from './views/auth/SignUpPage.jsx';
import ForgotPasswordPage from './views/auth/ForgotPasswordPage.jsx';
import ResetPasswordPage from './views/auth/ResetPasswordPage.jsx';
import VerifyEmailPage from './views/auth/VerifyEmailPage.jsx';
import JoinPage from './views/auth/JoinPage.jsx';
import AppleCallbackPage from './views/auth/AppleCallbackPage.jsx';
import AccountDeletedPage from './views/auth/AccountDeletedPage.jsx';
import RequireMember from './views/member/RequireMember.jsx';
import { CatalogHomeView, CatalogEntryView } from './views/catalog/CatalogViews.jsx';
import MyRankView from './views/ranks/MyRankView.jsx';
import CheckInView from './views/ranks/CheckInView.jsx';
import { CurriculaListView, CurriculumDetailView } from './views/ranks/CurriculaViews.jsx';
import { MemberLanding } from './views/member/MemberHomeView.jsx';
import GuestHomeView from './views/member/GuestHomeView.jsx';
import ComingSoonView from './views/member/ComingSoonView.jsx';
import AgeAttestationView from './views/member/AgeAttestationView.jsx';
import DocumentsReviewView from './views/member/DocumentsReviewView.jsx';
import ProfileSetupView from './views/member/ProfileSetupView.jsx';
import MembershipView from './views/member/MembershipView.jsx';
import AccountView, { AccountRedirect } from './views/member/AccountView.jsx';
import FeedView from './views/member/FeedView.jsx';
import FeedPostView from './views/member/FeedPostView.jsx';
import NotificationsView from './views/member/NotificationsView.jsx';
import NotificationSettingsView from './views/member/NotificationSettingsView.jsx';
import BlockedMembersView from './views/member/BlockedMembersView.jsx';
import MemberPublicProfileView from './views/member/MemberPublicProfileView.jsx';
import ForumsIndexView from './views/member/ForumsIndexView.jsx';
import ForumsFollowingView from './views/member/ForumsFollowingView.jsx';
import ForumView from './views/member/ForumView.jsx';
import ThreadView from './views/member/ThreadView.jsx';
import SearchView from './views/member/SearchView.jsx';
import MemberDirectoryView from './views/member/MemberDirectoryView.jsx';
import LibraryHomeView from './views/library/LibraryHomeView.jsx';
import SeriesView from './views/library/SeriesView.jsx';
import LessonView from './views/library/LessonView.jsx';
import EventsView from './views/events/EventsView.jsx';
import EventDetailView from './views/events/EventDetailView.jsx';
import { EventProposeView, MyEventProposalsView } from './views/events/EventProposalViews.jsx';
import DirectoryView from './views/directory/DirectoryView.jsx';
import { InstructorView, VenueView } from './views/directory/VenueViews.jsx';
import { MySubmissionsView, SubmissionView } from './views/directory/SubmissionViews.jsx';
import MessagesView from './views/member/MessagesView.jsx';
import MessagingSettingsView from './views/member/MessagingSettingsView.jsx';
import AppearanceSettingsView from './views/member/AppearanceSettingsView.jsx';
import SupportView, { NewTicketView } from './views/member/SupportView.jsx';
import TicketView from './views/member/TicketView.jsx';
import IdeasView from './views/member/IdeasView.jsx';
import IdeaView from './views/member/IdeaView.jsx';
import AnnouncementsView, { AnnouncementView } from './views/member/AnnouncementsView.jsx';
import { RealtimeConfigContext } from './context/RealtimeContext.jsx';
import AssistantsListView from './views/ai/AssistantsListView.jsx';
import AssistantChatView from './views/ai/AssistantChatView.jsx';
import { EmbedAssistantView, PublicAssistantView } from './views/ai/PublicAssistantView.jsx';
import { aiAvailable, purchasesEnabled } from '@trelliskit/shared';
import { AccessPromptProvider } from './components/AccessPrompt.jsx';
import TrainingLayout from './views/training/TrainingLayout.jsx';
import OverviewView from './views/training/OverviewView.jsx';
import LibraryView from './views/training/LibraryView.jsx';
import SavedView from './views/training/SavedView.jsx';
import ScheduleView from './views/training/ScheduleView.jsx';
import ProgressView from './views/training/ProgressView.jsx';
import CurriculumView from './views/training/CurriculumView.jsx';
import { trainingHubEnabled, trainingPaths } from './views/training/trainingModel.js';
import { MEMBER_ROUTES, featureEnabled, memberRoutesFor, routeAvailable } from './views/member/memberRoutes.js';
import { LEVELS } from './utils/onboarding.js';
import { Loading } from './components/States.jsx';
import './App.css';
import './engagement.css';
import './forums.css';
import './ai.css';
import './phase4.css';
import './phase5.css';
import './training.css';

const AdminApp = lazy(() => import('./AdminApp.jsx'));
const PlatformApp = lazy(() => import('./PlatformApp.jsx'));
const EMPTY_REALTIME = Object.freeze({});
const forumsRoute = MEMBER_ROUTES.find((route) => route.id === 'forums');
const searchRoute = MEMBER_ROUTES.find((route) => route.id === 'search');
const libraryRoute = MEMBER_ROUTES.find((route) => route.id === 'library');
const trainingRoute = MEMBER_ROUTES.find((route) => route.id === 'training');
const eventsRoute = MEMBER_ROUTES.find((route) => route.id === 'events');
const directoryRoute = MEMBER_ROUTES.find((route) => route.id === 'directory');
const catalogRoute = MEMBER_ROUTES.find((route) => route.id === 'catalog');

/**
 * A redirect that keeps the query string and hash, for addresses that moved into the Training hub
 * (`/app/library` to `/app/training/library/courses`, `/app/lessons/:id` to
 * `/app/training/lessons/:id`, and so on).
 * @param {{ to: (params: object) => string }} props
 */
function KeepQueryRedirect({ to }) {
  const params = useParams();
  const location = useLocation();
  return <Navigate to={{ pathname: to(params), search: location.search, hash: location.hash }} replace />;
}

/** `/announcements/:id` (the notification path) opens the announcement in the member app. */
function AnnouncementRedirect() {
  const { id } = useParams();
  return <Navigate to={'/app/announcements/' + encodeURIComponent(id || '')} replace />;
}

function marketingRoute(route, element) {
  if (route.path === '/') return <Route key="/" index element={element} />;
  return <Route key={route.path} path={route.path.slice(1)} element={element} />;
}

/**
 * All application routes: marketing and auth pages (/), the member app (/app), and the admin
 * console (/admin), inside the session and toast providers. Marketing routes come from the
 * active property (content pages and legal documents).
 *
 * `realtime` overrides the realtime socket settings (`{ WebSocketImpl, timers, random, url }`, tests).
 *
 * @param {{ fetchImpl?: typeof fetch, adminFetchImpl?: typeof fetch, restoreOnLoad?: boolean, realtime?: object }} props
 */
export function AppRoutes({ fetchImpl = null, adminFetchImpl = null, restoreOnLoad, realtime = null }) {
  const { site, property } = useProperty();
  // Re-render every route when remote string overrides change (strings apply on the next render).
  useRemoteStringsRevision();
  const meta = siteMetaFor(site);
  // The Training hub owns the library addresses when it is on; otherwise they keep working alone.
  const hub = trainingHubEnabled(property) && routeAvailable(property, trainingRoute) && routeAvailable(property, libraryRoute);
  const catalogOn = featureEnabled(property, 'catalog') && routeAvailable(property, catalogRoute);
  return (
    <RealtimeConfigContext.Provider value={realtime || EMPTY_REALTIME}>
    <AuthProvider fetchImpl={fetchImpl || adminFetchImpl} restoreOnLoad={restoreOnLoad}>
      <AppContentProvider>
      <ToastProvider>
        <AccessPromptProvider>
        <Routes>
          <Route element={<MarketingLayout />}>
            {meta.contentRoutes.map((route) => marketingRoute(route, <ContentPage path={route.path} />))}
            {meta.legalRoutes.map((route) => marketingRoute(route, <LegalPage path={route.path} />))}
            <Route path="sign-in" element={<SignInPage />} />
            <Route path="sign-up" element={<SignUpPage />} />
            <Route path="forgot-password" element={<ForgotPasswordPage />} />
            <Route path="reset-password" element={<ResetPasswordPage />} />
            <Route path="verify-email" element={<VerifyEmailPage />} />
            <Route path="join" element={purchasesEnabled(property) ? <JoinPage /> : <Navigate to="/sign-up" replace />} />
            <Route path="auth/apple/callback" element={<AppleCallbackPage />} />
            <Route path="account-deleted" element={<AccountDeletedPage />} />
            {aiAvailable(property) && <Route path="assistants/:id" element={<PublicAssistantView fetchImpl={fetchImpl} />} />}
            <Route path="threads/:id" element={<PublicThreadPage />} />
            {featureEnabled(property, 'library') && <Route path="lessons/:id" element={<PublicLessonPage />} />}
            <Route path="announcements/:id" element={<AnnouncementRedirect />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
          <Route path="app" element={<RequireMember level={LEVELS.signedIn} browse="any"><MemberLayout /></RequireMember>}>
            <Route path="onboarding/age" element={<AgeAttestationView />} />
            <Route path="onboarding/documents" element={<RequireMember level={LEVELS.age}><DocumentsReviewView /></RequireMember>} />
            <Route path="onboarding/profile" element={<RequireMember level={LEVELS.age}><ProfileSetupView /></RequireMember>} />
            {/* /app/profile is the member's profile and account settings; the old /app/account address redirects there. */}
            <Route path="account" element={<AccountRedirect />} />
            <Route path="membership" element={purchasesEnabled(property) ? <RequireMember level={LEVELS.profile}><MembershipView /></RequireMember> : <Navigate to="/app/account" replace />} />
            <Route index element={<RequireMember level={LEVELS.member} guest={<GuestHomeView />}><MemberLanding /></RequireMember>} />
            <Route path="profile" element={<RequireMember level={LEVELS.age}><AccountView /></RequireMember>} />
            <Route path="feed" element={<RequireMember level={LEVELS.member} browse="feed"><FeedView /></RequireMember>} />
            <Route path="feed/:id" element={<RequireMember level={LEVELS.member} browse="feed"><FeedPostView /></RequireMember>} />
            <Route path="notifications" element={<RequireMember level={LEVELS.member}><NotificationsView /></RequireMember>} />
            {routeAvailable(property, forumsRoute) ? (
              <>
                <Route path="forums" element={<RequireMember level={LEVELS.member} browse="forums" feature="forums"><ForumsIndexView /></RequireMember>} />
                <Route path="forums/following" element={<RequireMember level={LEVELS.member} feature="forums"><ForumsFollowingView /></RequireMember>} />
                <Route path="forums/:slug" element={<RequireMember level={LEVELS.member} browse="forums" feature="forums"><ForumView /></RequireMember>} />
                <Route path="threads/:id" element={<RequireMember level={LEVELS.member} browse="forums" feature="forums"><ThreadView /></RequireMember>} />
              </>
            ) : (
              <>
                <Route path="forums/*" element={<RequireMember level={LEVELS.member}><ComingSoonView route={forumsRoute} /></RequireMember>} />
                <Route path="threads/*" element={<RequireMember level={LEVELS.member}><ComingSoonView route={forumsRoute} /></RequireMember>} />
              </>
            )}
            <Route path="search" element={<RequireMember level={LEVELS.member} feature="search">{routeAvailable(property, searchRoute) ? <SearchView /> : <ComingSoonView route={searchRoute} />}</RequireMember>} />
            {featureEnabled(property, 'library') && routeAvailable(property, libraryRoute) && !hub && (
              <>
                <Route path="library" element={<RequireMember level={LEVELS.member} catalog="library"><LibraryHomeView /></RequireMember>} />
                <Route path="series/:id" element={<RequireMember level={LEVELS.member} catalog="library"><SeriesView /></RequireMember>} />
                <Route path="lessons/:id" element={<RequireMember level={LEVELS.member} catalog="library"><LessonView /></RequireMember>} />
              </>
            )}
            {hub && (
              <>
                {/* The Library lives inside Training; the old addresses keep their query string. */}
                <Route path="library" element={<KeepQueryRedirect to={() => trainingPaths.segment('courses')} />} />
                <Route path="series/:id" element={<KeepQueryRedirect to={(params) => trainingPaths.series(params.id)} />} />
                <Route path="lessons/:id" element={<KeepQueryRedirect to={(params) => trainingPaths.lesson(params.id)} />} />
                {catalogOn && <Route path="catalog" element={<KeepQueryRedirect to={() => trainingPaths.segment('techniques')} />} />}
                {catalogOn && <Route path="catalog/:id" element={<KeepQueryRedirect to={(params) => trainingPaths.catalog(params.id)} />} />}
                <Route path="training" element={<RequireMember level={LEVELS.member} catalog="library"><TrainingLayout /></RequireMember>}>
                  <Route index element={<OverviewView />} />
                  <Route path="library" element={<LibraryView />}>
                    <Route index element={<KeepQueryRedirect to={() => trainingPaths.segment('courses')} />} />
                    <Route path="courses" element={<LibraryHomeView embedded />} />
                    {catalogOn && <Route path="techniques" element={<CatalogHomeView embedded />} />}
                    <Route path="saved" element={<SavedView />} />
                    <Route path="*" element={<KeepQueryRedirect to={() => trainingPaths.segment('courses')} />} />
                  </Route>
                  <Route path="schedule" element={<ScheduleView />} />
                  <Route path="progress" element={<ProgressView />} />
                  {featureEnabled(property, 'ranks') && <Route path="curricula" element={<CurriculaListView />} />}
                </Route>
                <Route path="training/series/:id" element={<RequireMember level={LEVELS.member} catalog="library"><SeriesView /></RequireMember>} />
                <Route path="training/lessons/:id" element={<RequireMember level={LEVELS.member} catalog="library"><LessonView /></RequireMember>} />
                {catalogOn && <Route path="training/catalog/:id" element={<RequireMember level={LEVELS.member} catalog="library"><CatalogEntryView /></RequireMember>} />}
                {featureEnabled(property, 'ranks') && <Route path="training/curricula/:id" element={<RequireMember level={LEVELS.member} feature="ranks"><CurriculumView /></RequireMember>} />}
              </>
            )}
            {featureEnabled(property, 'events') && routeAvailable(property, eventsRoute) && (
              <>
                <Route path="events" element={<RequireMember level={LEVELS.member} catalog="events"><EventsView /></RequireMember>} />
                <Route path="events/propose" element={<RequireMember level={LEVELS.member} catalog="events"><EventProposeView /></RequireMember>} />
                <Route path="events/proposals" element={<RequireMember level={LEVELS.member} catalog="events"><MyEventProposalsView /></RequireMember>} />
                <Route path="events/:id" element={<RequireMember level={LEVELS.member} catalog="events"><EventDetailView /></RequireMember>} />
              </>
            )}
            {featureEnabled(property, 'directory') && routeAvailable(property, directoryRoute) && (
              <>
                <Route path="directory" element={<RequireMember level={LEVELS.member} catalog="directory"><DirectoryView /></RequireMember>} />
                <Route path="directory/submit" element={<RequireMember level={LEVELS.member} catalog="directory"><SubmissionView /></RequireMember>} />
                <Route path="directory/submissions" element={<RequireMember level={LEVELS.member} catalog="directory"><MySubmissionsView /></RequireMember>} />
                <Route path="venues/:id" element={<RequireMember level={LEVELS.member} catalog="directory"><VenueView /></RequireMember>} />
                <Route path="instructors/:id" element={<RequireMember level={LEVELS.member} catalog="directory"><InstructorView /></RequireMember>} />
              </>
            )}
            {catalogOn && !hub && (
              <>
                <Route path="catalog" element={<RequireMember level={LEVELS.member} catalog="library"><CatalogHomeView /></RequireMember>} />
                <Route path="catalog/:id" element={<RequireMember level={LEVELS.member} catalog="library"><CatalogEntryView /></RequireMember>} />
              </>
            )}
            {featureEnabled(property, 'ranks') && (
              <>
                <Route path="rank" element={<RequireMember level={LEVELS.member} feature="ranks"><MyRankView /></RequireMember>} />
                <Route path="curricula" element={<RequireMember level={LEVELS.member} feature="ranks"><CurriculaListView /></RequireMember>} />
                <Route path="curricula/:id" element={<RequireMember level={LEVELS.member} feature="ranks"><CurriculumDetailView /></RequireMember>} />
                <Route path="check-in" element={<RequireMember level={LEVELS.member} feature="ranks"><CheckInView /></RequireMember>} />
              </>
            )}
            <Route path="members" element={<RequireMember level={LEVELS.member} feature="directory"><MemberDirectoryView /></RequireMember>} />
            <Route path="members/:id" element={<RequireMember level={LEVELS.member} feature="members"><MemberPublicProfileView /></RequireMember>} />
            {aiAvailable(property) && (
              <>
                <Route path="assistants" element={<RequireMember level={LEVELS.age} browse="assistants" feature="assistants"><AssistantsListView /></RequireMember>} />
                <Route path="assistants/:id" element={<RequireMember level={LEVELS.age} browse="assistants" feature="assistants"><AssistantChatView /></RequireMember>} />
                <Route path="assistants/:id/c/:conversationId" element={<RequireMember level={LEVELS.age} feature="assistants"><AssistantChatView /></RequireMember>} />
              </>
            )}
            {featureEnabled(property, 'messaging') && (
              <>
                <Route path="messages" element={<RequireMember level={LEVELS.member} feature="messaging"><MessagesView /></RequireMember>} />
                <Route path="messages/requests" element={<RequireMember level={LEVELS.member} feature="messaging"><MessagesView requests /></RequireMember>} />
                <Route path="messages/:id" element={<RequireMember level={LEVELS.member} feature="messaging"><MessagesView /></RequireMember>} />
                <Route path="settings/messaging" element={<RequireMember level={LEVELS.age}><MessagingSettingsView /></RequireMember>} />
                <Route path="settings/appearance" element={<RequireMember level={LEVELS.age}><AppearanceSettingsView /></RequireMember>} />
              </>
            )}
            {/* Support is an account route: every signed-in account reaches it, including from the paywall (contract 0.2). */}
            <Route path="support" element={<RequireMember level={LEVELS.age}><SupportView /></RequireMember>} />
            <Route path="support/new" element={<RequireMember level={LEVELS.age}><NewTicketView /></RequireMember>} />
            <Route path="support/tickets/:id" element={<RequireMember level={LEVELS.age}><TicketView /></RequireMember>} />
            {featureEnabled(property, 'ideas') && (
              <>
                <Route path="ideas" element={<RequireMember level={LEVELS.member}><IdeasView /></RequireMember>} />
                <Route path="ideas/:id" element={<RequireMember level={LEVELS.member}><IdeaView /></RequireMember>} />
              </>
            )}
            <Route path="announcements" element={<RequireMember level={LEVELS.member}><AnnouncementsView /></RequireMember>} />
            <Route path="announcements/:id" element={<RequireMember level={LEVELS.member}><AnnouncementView /></RequireMember>} />
            <Route path="settings/notifications" element={<RequireMember level={LEVELS.age}><NotificationSettingsView /></RequireMember>} />
            <Route path="settings/blocked" element={<RequireMember level={LEVELS.age}><BlockedMembersView /></RequireMember>} />
            {memberRoutesFor(property).filter((route) => route.id !== 'profile' && !route.live).map((route) => (
              <Route key={route.id} path={route.id} element={<RequireMember level={LEVELS.member}><ComingSoonView route={route} /></RequireMember>} />
            ))}
            <Route path="*" element={<Navigate to="/app" replace />} />
          </Route>
          {aiAvailable(property) && <Route path="embed/assistants/:id" element={<EmbedAssistantView fetchImpl={fetchImpl} />} />}
          <Route
            path="platform/*"
            element={(
              <Suspense fallback={<div className="center-page"><Loading /></div>}>
                <PlatformApp fetchImpl={adminFetchImpl || fetchImpl} />
              </Suspense>
            )}
          />
          <Route
            path="admin/*"
            element={(
              <Suspense fallback={<div className="center-page"><Loading /></div>}>
                <AdminApp fetchImpl={adminFetchImpl || fetchImpl} />
              </Suspense>
            )}
          />
        </Routes>
        </AccessPromptProvider>
      </ToastProvider>
      </AppContentProvider>
    </AuthProvider>
    </RealtimeConfigContext.Provider>
  );
}

/** Root component. */
function App() {
  return (
    <PropertyProvider>
      <ThemeProvider>
        <BrowserRouter>
          <AppRoutes />
        </BrowserRouter>
      </ThemeProvider>
    </PropertyProvider>
  );
}

export default App;
