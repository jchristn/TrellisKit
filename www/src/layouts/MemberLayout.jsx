import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import strings from '../strings.js';
import { useAuth } from '../context/AuthContext.jsx';
import ThemeToggle from '../components/ThemeToggle.jsx';
import Icon from '../components/Icon.jsx';
import Avatar from '../components/Avatar.jsx';
import { Banner } from '../components/States.jsx';
import { useProperty, useRuntimeProperty } from '../context/PropertyContext.jsx';
import { guestRoutesFor, memberRoutesFor } from '../views/member/memberRoutes.js';
import { LockBadge } from '../components/AccessPrompt.jsx';
import { AGE_PATH, LEVELS, onboardingRedirect } from '../utils/onboarding.js';
import { takeFlash } from '../utils/flash.js';
import { canUseIdeas, formatUnreadBadge, membershipStepRequired, purchasesEnabled, showsLockBadge } from '@trelliskit/shared';
import { RealtimeProvider, useConversationList } from '../context/RealtimeContext.jsx';
import { NotificationsProvider, useNotifications } from '../context/NotificationsContext.jsx';
import useMediaGrant from '../hooks/useMediaGrant.js';
import BannerSlot, { placementForPath } from '../components/BannerSlot.jsx';
import { useAppContent } from '../context/AppContentContext.jsx';
import useLocalStorage from '../hooks/useLocalStorage.js';

const m = strings.member;

function UserMenu({ me, isAdmin, onSignOut, showMembership }) {
  const [open, setOpen] = useState(false);
  const ref = useRef(null);
  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);
  const name = me.displayName || me.email;
  return (
    <div className="popover-anchor" ref={ref}>
      <button type="button" className="user-button" aria-haspopup="menu" aria-expanded={open} aria-label={m.userMenu(name)} onClick={() => setOpen((v) => !v)}>
        <Avatar src={me.avatarUrl} name={name} size={30} />
        <span className="user-name hide-xs">{name}</span>
        <Icon name="chevronDown" size={14} />
      </button>
      {open && (
        <div className="popover user-menu" role="menu">
          <Link role="menuitem" className="action-menu-item" to="/app/profile" onClick={() => setOpen(false)}><Icon name="user" size={14} /><span>{m.menuProfile}</span></Link>
          {showMembership && <Link role="menuitem" className="action-menu-item" to="/app/membership" onClick={() => setOpen(false)}><Icon name="card" size={14} /><span>{m.menuMembership}</span></Link>}
          {isAdmin && <Link role="menuitem" className="action-menu-item" to="/admin/home" onClick={() => setOpen(false)}><Icon name="shield" size={14} /><span>{m.menuAdmin}</span></Link>}
          <button type="button" role="menuitem" className="action-menu-item" onClick={() => { setOpen(false); onSignOut(); }}><Icon name="logout" size={14} /><span>{m.signOut}</span></button>
        </div>
      )}
    </div>
  );
}

/** The unread badge of the Messages entry: unread conversations plus pending requests. */
function MessagesBadge() {
  const { unread } = useConversationList();
  const count = (unread.conversations || 0) + (unread.requests || 0);
  const badge = formatUnreadBadge(count);
  if (!badge) return null;
  return <span className="count-badge" aria-label={strings.messaging.unreadBadge(count)}>{badge}</span>;
}

/** Whether a section shows the members-only lock badge (access features, and the ideas board). */
function sectionLocked(property, me, route) {
  if (route.lock === 'ideas') return !canUseIdeas(property, me, 'write').allowed && canUseIdeas(property, me, 'write').requires === 'Membership';
  return Boolean(route.access) && showsLockBadge(property, me, route.access);
}

/**
 * Top bar search: the same search as /app/search (submitting opens it with the text), plus a link to
 * the full page for the filters. On narrow screens only the icon link to the search page shows.
 */
function TopbarSearch() {
  const navigate = useNavigate();
  const location = useLocation();
  const t = strings.search.topbar;
  const current = location.pathname === '/app/search' ? new URLSearchParams(location.search).get('search') || '' : '';
  const [text, setText] = useState(current);
  const [lastPath, setLastPath] = useState(location.pathname + location.search);
  if (lastPath !== location.pathname + location.search) {
    setLastPath(location.pathname + location.search);
    setText(current);
  }
  const submit = (e) => {
    e.preventDefault();
    const q = text.trim();
    navigate(q ? '/app/search?search=' + encodeURIComponent(q) : '/app/search');
  };
  return (
    <>
      <form className="topbar-search" role="search" aria-label={t.label} onSubmit={submit}>
        <label className="visually-hidden" htmlFor="topbar-search-input">{t.label}</label>
        <input id="topbar-search-input" type="search" className="input-compact" value={text} onChange={(e) => setText(e.target.value)} placeholder={t.placeholder} maxLength={200} autoComplete="off" />
        <button type="submit" className="button button-secondary button-sm"><Icon name="search" size={14} /><span>{t.submit}</span></button>
        <Link to="/app/search" className="small topbar-search-advanced">{t.advanced}</Link>
      </form>
      <Link to="/app/search" className="icon-button topbar-search-icon" aria-label={t.label} title={t.label}><Icon name="search" size={18} /></Link>
    </>
  );
}

function NotificationBell() {
  const { unreadCount } = useNotifications();
  const badge = formatUnreadBadge(unreadCount);
  return (
    <Link to="/app/notifications" className="icon-button bell-button" aria-label={strings.notifications.bell(badge)} title={strings.notifications.title}>
      <Icon name="bell" size={18} />
      {badge && <span className="bell-badge" aria-hidden="true">{badge}</span>}
    </Link>
  );
}

/**
 * Member app shell: brand bar with the notification bell and the account menu, section
 * navigation, and one-time notices. Once the member is fully onboarded it polls the unread
 * count and keeps the media grant cookie fresh.
 */
function MemberLayout() {
  const auth = useAuth();
  const { property } = useProperty();
  useRuntimeProperty(auth.client);
  const navigate = useNavigate();
  const location = useLocation();
  const [flash, setFlash] = useState(null);
  const me = auth.me;
  const ready = Boolean(me) && onboardingRedirect(me) === null;

  useEffect(() => {
    const value = takeFlash();
    if (value) setFlash(value);
  }, [location.pathname]);

  const signOut = async () => {
    await auth.signOut();
    navigate('/', { replace: true });
  };

  return (
    <NotificationsProvider client={auth.client} enabled={ready}>
      <RealtimeProvider client={auth.client} me={me} property={property} enabled={ready} refreshSession={auth.refresh}>
        <MemberShell ready={ready} guest={!me && auth.status === 'signedOut'} me={me} property={property} isAdmin={auth.isAdmin} client={auth.client} flash={flash} setFlash={setFlash} signOut={signOut} />
      </RealtimeProvider>
    </NotificationsProvider>
  );
}

function MemberShell({ ready, guest, me, property, isAdmin, client, flash, setFlash, signOut }) {
  const { state: appContent } = useAppContent();
  // Sections whose kill switch is on are hidden from the navigation (LIVE_UPDATES_CONTRACT.md section 13.2).
  const routeKilled = (route) => Boolean(route.killSwitch && appContent.killSwitches && appContent.killSwitches[route.killSwitch] && appContent.killSwitches[route.killSwitch].killed);
  useMediaGrant(client, { enabled: ready, cacheKey: me?.id || null });
  // Membership opens once age and profile are done; until then the link is shown disabled.
  const setupStep = me ? onboardingRedirect(me, LEVELS.profile) : null;
  // The membership entry shows only where something is sold, and during setup only when the
  // paywall step applies (Me.membershipRequired).
  const sells = purchasesEnabled(property);
  const location = useLocation();
  // The navigation as an icon rail or with names; remembered in this browser.
  const [navCollapsed, setNavCollapsed] = useLocalStorage('tk.member.navCollapsed', false);
  const signInHref = '/sign-in?next=' + encodeURIComponent(location.pathname + location.search);
  return (
    <div className={'member-app' + (navCollapsed ? ' nav-collapsed' : '')}>
      <a className="skip-link" href="#member-main">{strings.common.skipToContent}</a>
      <header className="member-topbar">
        <div className="member-topbar-left">
          <button type="button" className="icon-button member-nav-toggle" onClick={() => setNavCollapsed((v) => !v)} aria-label={navCollapsed ? m.expandNav : m.collapseNav} title={navCollapsed ? m.expandNavHint : m.collapseNavHint} aria-pressed={navCollapsed} data-testid="member-nav-toggle">
            <Icon name="menu" size={18} />
          </button>
          <Link to="/app" className="brand">
            <img className="brand-mark" src="/brand-mark.png" alt="" aria-hidden="true" width="32" height="32" />
            <span className="brand-name">{property.name}</span>
          </Link>
        </div>
        {me && ready && memberRoutesFor(property).some((route) => route.id === 'search' && !routeKilled(route)) && <TopbarSearch />}
        <div className="member-topbar-actions">
          <Link to="/" className="button button-secondary button-sm hide-xs">{m.backToSite}</Link>
          <ThemeToggle />
          {ready && <NotificationBell />}
          {me && <UserMenu me={me} isAdmin={isAdmin} onSignOut={signOut} showMembership={sells} />}
          {guest && (
            <>
              <Link to={signInHref} className="button button-primary button-sm">{strings.access.signInCta}</Link>
              <Link to="/sign-up" className="button button-secondary button-sm hide-xs">{strings.access.signUpCta}</Link>
            </>
          )}
        </div>
      </header>
      <div className="member-body">
        <nav className="member-nav" aria-label={m.navLabel}>
          {guest ? (
            <>
              <span className="member-nav-note muted small">{strings.access.guestNavNote}</span>
              {guestRoutesFor(property).map((route) => (
                <NavLink key={route.id} to={'/app/' + route.id} className="member-nav-link" title={route.label}>
                  <Icon name={route.icon} size={18} />
                  <span>{route.label}</span>
                </NavLink>
              ))}
            </>
          ) : ready ? memberRoutesFor(property, routeKilled).filter((route) => route.id !== 'notifications').map((route) => (
            <NavLink key={route.id} to={'/app/' + route.id} className="member-nav-link" title={route.label}>
              <Icon name={route.icon} size={18} />
              <span>{route.label}</span>
              {route.id === 'messages' && <MessagesBadge />}
              {sectionLocked(property, me, route) && <LockBadge feature={route.access || route.lock} />}
            </NavLink>
          )) : (
            <>
              <span className="member-nav-note muted small">{m.onboardingNavNote}</span>
              {setupStep ? (
                <>
                  <NavLink to={setupStep} className="member-nav-link" title={m.continueSetup}><Icon name="user" size={18} /><span>{m.continueSetup}</span></NavLink>
                  {sells && membershipStepRequired(me) && <span className="member-nav-link member-nav-link-disabled" aria-disabled="true" aria-describedby="member-nav-membership-note">
                    <Icon name="card" size={18} /><span>{m.menuMembership}</span>
                  </span>}
                  {sells && membershipStepRequired(me) && <span id="member-nav-membership-note" className="member-nav-note muted small">{setupStep === AGE_PATH ? m.membershipLockedAgeNote : m.membershipLockedNote}</span>}
                </>
              ) : sells && (
                <NavLink to="/app/membership" className="member-nav-link" title={m.menuMembership}><Icon name="card" size={18} /><span>{m.menuMembership}</span></NavLink>
              )}
              <NavLink to="/app/profile" className="member-nav-link" title={m.menuAccount}><Icon name="gear" size={18} /><span>{m.menuAccount}</span></NavLink>
              {me && <NavLink to="/app/support" className="member-nav-link" title={m.nav.support}><Icon name="help" size={18} /><span>{m.nav.support}</span></NavLink>}
            </>
          )}
        </nav>
        <main id="member-main" className="member-main" tabIndex={-1}>
          {flash && (
            <div className="member-flash">
              <Banner
                tone={flash.tone}
                icon={flash.tone === 'success' ? 'check' : (flash.tone === 'danger' ? 'alert' : 'info')}
                actions={<button type="button" className="icon-button" onClick={() => setFlash(null)} aria-label={strings.common.dismiss} title={strings.common.dismiss}><Icon name="close" size={14} /></button>}
              >
                {flash.message}
              </Banner>
            </div>
          )}
          <BannerSlot placement="Global" />
          {placementForPath(location.pathname) && <BannerSlot key={placementForPath(location.pathname)} placement={placementForPath(location.pathname)} />}
          <Outlet />
        </main>
      </div>
    </div>
  );
}

export default MemberLayout;
