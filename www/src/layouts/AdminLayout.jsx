import { useCallback, useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import strings from '../strings.js';
import { isForumModerator } from '../utils/engagement.js';
import { useAdminAuth } from '../context/AdminAuthContext.jsx';
import { useProperty } from '../context/PropertyContext.jsx';
import { ToastProvider } from '../context/ToastContext.jsx';
import useApiQuery from '../hooks/useApiQuery.js';
import Icon from '../components/Icon.jsx';
import CopyButton from '../components/CopyButton.jsx';
import ThemeToggle from '../components/ThemeToggle.jsx';
import { HealthDot } from '../components/Badges.jsx';
import { adminRouteAvailable, findAdminRoute, groupedAdminRoutes, routeTurnedOff } from '../views/admin/adminRoutes.js';
import useMediaGrant from '../hooks/useMediaGrant.js';
import DelegationBanner from '../components/DelegationBanner.jsx';
import useLocalStorage from '../hooks/useLocalStorage.js';

const tb = strings.admin.topbar;
const HEALTH_POLL_MS = 30000;

function useHealth() {
  const { api } = useAdminAuth();
  const fetcher = useCallback((signal) => api.getHealth(signal), [api]);
  const query = useApiQuery(fetcher);
  const { reload } = query;
  useEffect(() => {
    const timer = setInterval(reload, HEALTH_POLL_MS);
    return () => clearInterval(timer);
  }, [reload]);
  return query;
}

function healthLabel(query) {
  if (query.error) return { status: 'Unreachable', label: tb.unreachable };
  if (!query.data) return { status: '', label: tb.checking };
  const map = { Healthy: tb.healthy, Degraded: tb.degraded, Unhealthy: tb.unhealthy };
  return { status: query.data.status, label: map[query.data.status] || query.data.status };
}

/** Admin console shell: topbar with context and utilities, grouped sidebar, and content outlet. */
function AdminLayout() {
  const { logout, mode, principal, isModeratorOnly, client, api, me, staffAreas, staff } = useAdminAuth();
  useMediaGrant(client, { enabled: mode === 'session', cacheKey: me?.id || null });
  const { site, property, fill } = useProperty();
  const repositoryUrl = site.engine && site.engine.repositoryUrl;
  const location = useLocation();
  // Collapsed to an icon rail or not; remembered in this browser.
  const [collapsed, setCollapsed] = useLocalStorage('tk.admin.sidebarCollapsed', false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [lastPath, setLastPath] = useState(location.pathname);
  const health = useHealth();
  const metricPagesFetch = useCallback((signal) => api.metricPages(signal), [api]);
  const metricPages = useApiQuery(metricPagesFetch, { enabled: !isModeratorOnly && Boolean(api?.metricPages) });
  const h = healthLabel(health);
  const origin = window.location.origin;

  if (lastPath !== location.pathname) {
    setLastPath(location.pathname);
    setMobileOpen(false);
  }

  const relativePath = location.pathname.replace(/^\/admin\/?/, '');
  const sectionId = relativePath.startsWith('metrics/') ? relativePath : relativePath.split('/')[0] || 'home';
  const route = findAdminRoute(sectionId, metricPages.data?.objects || []);
  useEffect(() => {
    if (route) document.title = fill(strings.admin.metaTitle(route.label));
  }, [route, fill]);

  const toggleSidebar = () => {
    if (window.matchMedia && window.matchMedia('(max-width: 900px)').matches) setMobileOpen((v) => !v);
    else setCollapsed((v) => !v);
  };

  return (
    <ToastProvider>
      <div className={'admin-shell' + (collapsed ? ' sidebar-collapsed' : '') + (mobileOpen ? ' sidebar-open' : '')}>
        <a className="skip-link" href="#admin-main">{strings.common.skipToContent}</a>
        <header className="admin-topbar">
          <div className="topbar-left">
            <button type="button" className="icon-button" onClick={toggleSidebar} aria-label={tb.toggleSidebar} title={tb.toggleSidebar} aria-expanded={!collapsed || mobileOpen}>
              <Icon name="menu" size={18} />
            </button>
            <span className="brand">
              <img className="brand-mark" src="/brand-mark.png" alt="" aria-hidden="true" width="32" height="32" />
              <span className="brand-name">{fill(strings.admin.consoleName)}</span>
            </span>
            <span className="chip chip-server" title={tb.server + ': ' + origin}>
              <span className="chip-label">{tb.server}</span>
              <code className="mono truncate">{origin}</code>
              <CopyButton value={origin} label={tb.server} className="chip-copy" />
            </span>
          </div>
          <div className="topbar-right">
            <span className="chip" title={tb.healthTitle(h.label)}>
              <HealthDot status={h.status} label={h.label} />
              {health.data && health.data.version && <span className="muted small hide-sm">{tb.versionShort(health.data.version)}</span>}
            </span>
            <span className="chip hide-sm" title={principal || tb.principal}><Icon name="user" size={14} /><span className="truncate">{mode === 'delegated' ? strings.admin.delegation.principal : mode === 'session' && principal ? principal : tb.principal}</span></span>
            <span className={'badge hide-xs ' + (mode === 'delegated' ? 'badge-warning' : 'badge-info')}>{mode === 'delegated' ? strings.admin.delegation.role : isModeratorOnly ? tb.roleModerator : tb.role}</span>
            {repositoryUrl && (
              <a className="icon-button" href={repositoryUrl} target="_blank" rel="noreferrer" aria-label={tb.repository} title={tb.repository}>
                <Icon name="github" size={18} />
              </a>
            )}
            <ThemeToggle />
            <button type="button" className="icon-button" onClick={logout} aria-label={tb.logout} title={tb.logout}>
              <Icon name="logout" size={18} />
            </button>
          </div>
        </header>
        <div className="sidebar-scrim" onClick={() => setMobileOpen(false)} aria-hidden="true" />
        <aside className="admin-sidebar">
          <nav className="sidebar-nav" aria-label={strings.admin.sidebar.navLabel}>
            {groupedAdminRoutes(property, { metricPages: metricPages.data?.objects || [], moderatorOnly: isModeratorOnly, forumModerator: isForumModerator(me), staffAreas, moderator: staff ? staff.moderator : true, supportStaff: Boolean(staff && staff.supportStaff), communicator: Boolean(staff && staff.communicator), releaseManager: Boolean(staff && staff.releaseManager) }).map((group) => (
              <div key={group.label} className="nav-group">
                <div className="nav-group-label">{group.label}</div>
                {group.routes.map((item) => (
                  <NavLink key={item.id} to={'/admin/' + item.id} className="nav-item" title={item.label}>
                    <Icon name={item.icon} size={16} />
                    <span className="nav-item-label">{item.label}</span>
                    {!adminRouteAvailable(property, item) && (routeTurnedOff(property, item)
                      ? <span className="nav-phase" aria-label={strings.admin.placeholder.offBadge}>{strings.admin.placeholder.navOffBadge}</span>
                      : <span className="nav-phase" aria-label={strings.admin.placeholder.badge}>{strings.admin.placeholder.navBadge}</span>)}
                  </NavLink>
                ))}
              </div>
            ))}
          </nav>
          <div className="sidebar-footer">
            <span>{strings.admin.sidebar.version(health.data ? health.data.version : '1.0.0')}</span>
            <span className="muted">{property.name}</span>
            <span className="muted small">{strings.admin.sidebar.poweredBy}</span>
          </div>
        </aside>
        <main id="admin-main" className="admin-main" tabIndex={-1}>
          <DelegationBanner />
          <Outlet context={{ health }} />
        </main>
      </div>
    </ToastProvider>
  );
}

export default AdminLayout;
