import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom';
import strings from '../strings.js';
import { useAuth } from '../context/AuthContext.jsx';
import { useProperty, useRuntimeProperty } from '../context/PropertyContext.jsx';
import Icon from '../components/Icon.jsx';
import MarketingThemeToggle from '../views/marketing/MarketingThemeToggle.jsx';
import { siteMetaFor } from '../views/marketing/siteMeta.js';
import '../views/marketing/marketing.css';
import { signedOutBrowsable } from '@trelliskit/shared';
import BannerSlot from '../components/BannerSlot.jsx';

const n = strings.marketing.nav;
const f = strings.marketing.footer;

/**
 * Public site shell: header with navigation, content, and footer with legal links. Navigation
 * lists the property's content pages that set `nav` in front matter; legal links list the
 * property's documents with `footer: true` (or every standard legal page without a `documents`
 * list).
 *
 * The prerendered HTML carries the build-time property. After hydration the layout loads the live
 * `GET /api/v1/property` (an effect, so the first client render still matches the static markup)
 * and every price, membership, and gating string re-renders from the server's current access
 * policy, so a change in the admin console shows without a rebuild.
 */
function MarketingLayout() {
  const { isSignedIn, client } = useAuth();
  useRuntimeProperty(client);
  const { site, property, fill } = useProperty();
  const meta = siteMetaFor(site);
  const nav = meta.navRoutes.map((r) => ({ to: r.path, label: r.nav }));
  const legalLinks = meta.footerRoutes.map((r) => ({ to: r.path, label: r.linkLabel }));
  const [menuOpen, setMenuOpen] = useState(false);
  const location = useLocation();
  const [lastPath, setLastPath] = useState(location.pathname);
  if (lastPath !== location.pathname) {
    setLastPath(location.pathname);
    setMenuOpen(false);
  }

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [location.pathname]);

  const appLink = isSignedIn ? '/app' : '/sign-in';
  // Always "Sign in": signed-in members go straight to the app from the same button.
  const appLabel = n.memberSignIn;

  return (
    <div className="site">
      <a className="skip-link" href="#main">{strings.common.skipToContent}</a>
      <header className="site-header">
        <div className="site-container site-header-inner">
          <Link to="/" className="brand" aria-label={property.name}>
            <img className="brand-mark" src="/brand-mark.png" alt="" aria-hidden="true" width="32" height="32" />
            <span className="brand-name">{property.name}</span>
          </Link>
          <nav className={'site-nav' + (menuOpen ? ' open' : '')} aria-label={n.primary} id="site-nav">
            {nav.map((item) => (
              <NavLink key={item.to} to={item.to} className="site-nav-link">{item.label}</NavLink>
            ))}
            {!isSignedIn && signedOutBrowsable(property, 'feed') && <Link to="/app/feed" className="site-nav-link">{n.browseFeed}</Link>}
            {/* Wide screens: the theme toggle sits between the links and the sign-in button. */}
            <span className="site-nav-theme"><MarketingThemeToggle /></span>
            <Link to={appLink} className="button button-primary button-sm site-nav-cta">{appLabel}</Link>
            {!isSignedIn && <Link to="/sign-up" className="button button-secondary button-sm site-nav-cta">{n.memberSignUp}</Link>}
          </nav>
          <div className="site-header-actions">
            {/* Narrow screens: the links fold into the menu, so the toggle stays in the header. */}
            <span className="site-header-theme"><MarketingThemeToggle /></span>
            <button
              type="button"
              className="icon-button site-menu-toggle"
              aria-expanded={menuOpen}
              aria-controls="site-nav"
              aria-label={menuOpen ? strings.common.closeMenu : strings.common.openMenu}
              onClick={() => setMenuOpen((v) => !v)}
            >
              <Icon name={menuOpen ? 'close' : 'menu'} size={20} />
            </button>
          </div>
        </div>
      </header>
      <main id="main" className="site-main" tabIndex={-1}>
        <BannerSlot placement="Global" className="site-container tk-banners-site" />
        {location.pathname === '/sign-in' && <BannerSlot placement="SignIn" className="site-container tk-banners-site" />}
        <Outlet />
      </main>
      <footer className="site-footer">
        <div className="site-container footer-grid">
          <div className="footer-about">
            <Link to="/" className="brand footer-brand">
              <img className="brand-mark" src="/brand-mark.png" alt="" aria-hidden="true" width="32" height="32" />
              <span className="brand-name">{property.name}</span>
            </Link>
            {property.description && <p className="muted small">{property.description}</p>}
            <p className="muted small" suppressHydrationWarning>{fill(f.copyright(new Date().getFullYear()))}</p>
          </div>
          <nav aria-label={f.explore}>
            <h2 className="footer-heading">{f.explore}</h2>
            <ul className="footer-links">
              {nav.map((item) => <li key={item.to}><Link to={item.to}>{item.label}</Link></li>)}
              <li><Link to={appLink}>{appLabel}</Link></li>
            </ul>
          </nav>
          <nav aria-label={f.legal}>
            <h2 className="footer-heading">{f.legal}</h2>
            <ul className="footer-links">
              {legalLinks.map((item) => <li key={item.to}><Link to={item.to}>{item.label}</Link></li>)}
            </ul>
          </nav>
        </div>
      </footer>
    </div>
  );
}

export default MarketingLayout;
