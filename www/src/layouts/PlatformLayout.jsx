import { useCallback, useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import strings from '../strings.js';
import { usePlatformAuth } from '../context/PlatformAuthContext.jsx';
import { ToastProvider } from '../context/ToastContext.jsx';
import useApiQuery from '../hooks/useApiQuery.js';
import Icon from '../components/Icon.jsx';
import ThemeToggle from '../components/ThemeToggle.jsx';
import { HealthDot } from '../components/Badges.jsx';
import useLocalStorage from '../hooks/useLocalStorage.js';

const tb = strings.admin.topbar;
const pt = strings.platform.topbar;
const r = strings.platform.routes;
const g = strings.platform.groups;
const HEALTH_POLL_MS = 30000;

/** Platform console route inventory (sidebar order). */
export const PLATFORM_ROUTES = [
  { id: 'properties', group: g.properties, icon: 'grid', ...r.properties },
  { id: 'properties/new', group: g.properties, icon: 'plus', ...r['new-property'] },
  { id: 'health', group: g.observability, icon: 'activity', ...r.health },
  { id: 'metrics', group: g.observability, icon: 'trending', ...r.metrics },
  { id: 'usage', group: g.observability, icon: 'activity', ...r.usage },
  { id: 'requests', group: g.observability, icon: 'activity', ...r.requests },
  { id: 'admins', group: g.administration, icon: 'users', ...r.admins },
  { id: 'audit', group: g.administration, icon: 'clipboard', ...r.audit },
  { id: 'settings', group: g.system, icon: 'gear', ...r.settings }
];

function grouped() {
  const groups = [];
  for (const route of PLATFORM_ROUTES) {
    let group = groups.find((x) => x.label === route.group);
    if (!group) {
      group = { label: route.group, routes: [] };
      groups.push(group);
    }
    group.routes.push(route);
  }
  return groups;
}

function routeFor(pathname) {
  const rest = pathname.replace(/^\/platform\/?/, '');
  if (rest.startsWith('properties/new')) return PLATFORM_ROUTES[1];
  return PLATFORM_ROUTES.find((x) => rest.split('/')[0] === x.id) || PLATFORM_ROUTES[0];
}

/** Platform console shell: topbar (host, health, identity, theme, sign out), grouped sidebar, content. */
function PlatformLayout() {
  const { admin, client, logout } = usePlatformAuth();
  const location = useLocation();
  // Collapsed to an icon rail or not; remembered in this browser.
  const [collapsed, setCollapsed] = useLocalStorage('tk.platform.sidebarCollapsed', false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [lastPath, setLastPath] = useState(location.pathname);
  const healthFetcher = useCallback((signal) => client.health(signal), [client]);
  const health = useApiQuery(healthFetcher);
  const { reload } = health;
  useEffect(() => {
    const timer = setInterval(reload, HEALTH_POLL_MS);
    return () => clearInterval(timer);
  }, [reload]);

  if (lastPath !== location.pathname) {
    setLastPath(location.pathname);
    setMobileOpen(false);
  }

  const route = routeFor(location.pathname);
  useEffect(() => {
    document.title = strings.platform.metaTitle(route.label);
  }, [route]);

  const map = { Healthy: tb.healthy, Degraded: tb.degraded, Unhealthy: tb.unhealthy };
  const h = health.error ? { status: 'Unreachable', label: tb.unreachable } : !health.data ? { status: '', label: tb.checking } : { status: health.data.status, label: map[health.data.status] || health.data.status };
  const who = admin ? (admin.displayName || admin.email) : pt.breakGlass;
  const origin = window.location.origin;

  const toggleSidebar = () => {
    if (window.matchMedia && window.matchMedia('(max-width: 900px)').matches) setMobileOpen((v) => !v);
    else setCollapsed((v) => !v);
  };

  return (
    <ToastProvider>
      <div className={'admin-shell platform-shell' + (collapsed ? ' sidebar-collapsed' : '') + (mobileOpen ? ' sidebar-open' : '')}>
        <a className="skip-link" href="#platform-main">{strings.common.skipToContent}</a>
        <header className="admin-topbar">
          <div className="topbar-left">
            <button type="button" className="icon-button" onClick={toggleSidebar} aria-label={tb.toggleSidebar} title={tb.toggleSidebar} aria-expanded={!collapsed || mobileOpen}>
              <Icon name="menu" size={18} />
            </button>
            <span className="brand">
              <img className="brand-mark" src="/brand-mark.png" alt="" aria-hidden="true" width="32" height="32" />
              <span className="brand-name">{strings.platform.consoleName}</span>
            </span>
            <span className="chip chip-server" title={pt.host + ': ' + origin}>
              <span className="chip-label">{pt.host}</span>
              <code className="mono truncate">{origin}</code>
            </span>
          </div>
          <div className="topbar-right">
            <span className="chip" title={tb.healthTitle(h.label)}>
              <HealthDot status={h.status} label={h.label} />
              {health.data && health.data.version && <span className="muted small hide-sm">{tb.versionShort(health.data.version)}</span>}
            </span>
            <span className="chip hide-sm" title={who}><Icon name="user" size={14} /><span className="truncate">{who}</span></span>
            <span className="badge badge-info hide-xs">{pt.role}</span>
            <ThemeToggle />
            <button type="button" className="icon-button" onClick={logout} aria-label={pt.logout} title={pt.logout}>
              <Icon name="logout" size={18} />
            </button>
          </div>
        </header>
        <div className="sidebar-scrim" onClick={() => setMobileOpen(false)} aria-hidden="true" />
        <aside className="admin-sidebar">
          <nav className="sidebar-nav" aria-label={strings.platform.sidebar.navLabel}>
            {grouped().map((group) => (
              <div key={group.label} className="nav-group">
                <div className="nav-group-label">{group.label}</div>
                {group.routes.map((item) => (
                  <NavLink key={item.id} to={'/platform/' + item.id} className={() => 'nav-item' + (route.id === item.id ? ' active' : '')} aria-current={route.id === item.id ? 'page' : undefined} title={item.label}>
                    <Icon name={item.icon} size={16} />
                    <span className="nav-item-label">{item.label}</span>
                  </NavLink>
                ))}
              </div>
            ))}
          </nav>
          <div className="sidebar-footer">
            <span>{strings.admin.sidebar.version(health.data ? health.data.version : '1.0.0')}</span>
            <span className="muted">{strings.platform.sidebar.footer}</span>
          </div>
        </aside>
        <main id="platform-main" className="admin-main" tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </ToastProvider>
  );
}

export default PlatformLayout;
