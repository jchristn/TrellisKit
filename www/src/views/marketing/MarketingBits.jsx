import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { purchasesEnabled } from '@trelliskit/shared';

const mk = strings.marketing;

/**
 * Typographic cover stand-in (for example for a book or a course) until the property supplies an
 * image. It is marked as a placeholder for sighted users and labelled for screen readers.
 */
export function CoverArt({ title, subtitle = '', label = '', size = 'md', src = '' }) {
  if (src) return <img className={'book-cover-img book-cover-' + size} src={src} alt={label || title} width="900" height="1350" />;
  return (
    <div className={'book-cover-art book-cover-' + size} role="img" aria-label={label || mk.mediaPlaceholder(title)}>
      <span className="book-cover-tag">{strings.common.placeholderTag}</span>
      <span className="book-cover-title">{title}</span>
      {subtitle && <span className="book-cover-author">{subtitle}</span>}
    </div>
  );
}

/**
 * App Store and Google Play badge placeholders. They are not links: the official badges and store
 * URLs are added once the apps are published.
 */
export function StoreBadges() {
  const badges = [mk.storeBadgeApple, mk.storeBadgeGoogle];
  return (
    <div className="store-badge-group">
      <ul className="store-badges" aria-label={mk.storeBadgesLabel}>
        {badges.map((badge) => (
          <li key={badge.large} className="store-badge-placeholder">
            <span className="store-badge-small">{badge.small}</span>
            <span className="store-badge-large">{badge.large}</span>
          </li>
        ))}
      </ul>
      <p className="muted small">{mk.storeBadgeNote}</p>
    </div>
  );
}

/** Membership price from the property, for example "$12.50 per month". */
export function Price() {
  const { fill, property } = useProperty();
  // Nothing is sold in the Open access model: no price anywhere.
  if (!purchasesEnabled(property)) return null;
  return (
    <p className="price">
      <span className="price-amount">{fill('{price}')}</span>
      <span className="price-period">{fill(mk.priceLabel)}</span>
    </p>
  );
}

/** Sign-up button for visitors, or "Manage your membership" for signed-in members. */
export function JoinButton({ className = 'button button-primary button-lg button-block' }) {
  const { isSignedIn } = useAuth();
  const { property } = useProperty();
  const sells = purchasesEnabled(property);
  return (
    <Link to={isSignedIn ? (sells ? '/app/membership' : '/app') : '/sign-up'} className={className}>
      {isSignedIn ? (sells ? mk.manageMembershipCta : mk.openAppCta) : mk.signUpCta}
    </Link>
  );
}

/** A sign-up link shown only to signed-out visitors. */
export function SignUpLink({ label, className = 'button button-secondary' }) {
  const { isSignedIn } = useAuth();
  if (isSignedIn) return null;
  return <Link to="/sign-up" className={className}>{label || mk.signUpCta}</Link>;
}

/**
 * The property logo (generated at build time as /logo.png, with /logo-dark.png for the dark theme).
 * Both are in the markup because the theme can change after the page is prerendered; the style
 * sheet shows the one that matches. The pair is one labelled image for screen readers.
 */
export function Logo() {
  const { fill } = useProperty();
  return (
    <span className="theme-logo" role="img" aria-label={fill(mk.ogImageAlt)}>
      <img className="theme-logo-light" src="/logo.png" alt="" width="512" height="512" />
      <img className="theme-logo-dark" src="/logo-dark.png" alt="" width="512" height="512" loading="lazy" />
    </span>
  );
}

/** Small uppercase label above a heading. */
export function Eyebrow({ children }) {
  return <p className="eyebrow">{children}</p>;
}
