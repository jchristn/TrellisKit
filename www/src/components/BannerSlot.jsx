import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { activeBanners, isBannerDismissible } from '@trelliskit/shared';
import strings from '../strings.js';
import { useAppContent } from '../context/AppContentContext.jsx';
import useRemoteAsset from '../hooks/useRemoteAsset.js';
import MarkdownBody from './feed/MarkdownBody.jsx';
import Icon from './Icon.jsx';
import './appContent.css';

const ICONS = Object.freeze({ Info: 'info', Success: 'check', Warning: 'alert', Critical: 'alert' });

function prefersDark() {
  try {
    return typeof document !== 'undefined' && document.documentElement.getAttribute('data-theme') === 'dark';
  } catch {
    return false;
  }
}

function BannerImage({ asset }) {
  const src = useRemoteAsset(asset ? asset.key : null, { dark: prefersDark() });
  if (!src) return null;
  return <img className="tk-banner-image" src={src} alt={asset.altText || ''} width={asset.width || undefined} height={asset.height || undefined} loading="lazy" />;
}

/**
 * One banner (docs/LIVE_UPDATES_CONTRACT.md section 14.1): style, title, Markdown body, optional
 * image (from the remote asset cache), an in-app link (router) or an external https link, and a
 * dismiss button when the banner is dismissible (never for `Critical`). Also used by the console
 * preview (`preview` renders links inert).
 * @param {{ banner: object, onDismiss?: (banner: object) => void, assets?: Record<string, object>, preview?: boolean }} props
 */
export function BannerCard({ banner, onDismiss, assets = {}, preview = false }) {
  const style = ICONS[banner.style] ? banner.style : 'Info';
  const link = banner.link && (banner.link.path || banner.link.url) ? banner.link : null;
  const asset = banner.imageAssetId ? assets[banner.imageAssetId] || null : null;
  const dismissible = isBannerDismissible(banner) && Boolean(onDismiss);
  const label = link ? (link.label || strings.appContent.banner.details) : '';
  return (
    <div className={'tk-banner tk-banner-' + style.toLowerCase()} role={style === 'Critical' || style === 'Warning' ? 'alert' : 'status'} data-banner-id={banner.id || undefined}>
      <Icon name={ICONS[style]} />
      {asset && <BannerImage asset={asset} />}
      <div className="tk-banner-content">
        <strong className="tk-banner-title">{banner.title}</strong>
        {banner.body && <MarkdownBody body={banner.body} allowHeadings={false} linkMentions={false} className="tk-banner-body" />}
        {link && (preview
          ? <span className="tk-banner-link">{label}</span>
          : link.path
            ? <Link className="tk-banner-link" to={link.path}>{label}</Link>
            : <a className="tk-banner-link" href={link.url} target="_blank" rel="noopener noreferrer">{label}<Icon name="external" size={12} /></a>)}
      </div>
      {dismissible && (
        <button type="button" className="icon-button tk-banner-dismiss" onClick={() => onDismiss(banner)} aria-label={strings.appContent.banner.dismiss(banner.title)} title={strings.common.dismiss}>
          <Icon name="close" size={14} />
        </button>
      )}
    </div>
  );
}

/**
 * The active banners of one placement (`Global`, `Home`, `Feed`, `Forums`, `Library`, `Events`,
 * `Directory`, `Messages`, `SignIn`, `Account`), highest priority first, inside their schedule and
 * not dismissed on this device. Renders nothing when there are none.
 * @param {{ placement?: string, className?: string }} props
 */
function BannerSlot({ placement = 'Global', className = '' }) {
  const { state, dismissed, dismiss } = useAppContent();
  const banners = useMemo(() => activeBanners(state.banners, { placement, dismissed, now: Date.now() }), [state.banners, placement, dismissed]);
  if (!banners.length) return null;
  return (
    <div className={'tk-banners' + (className ? ' ' + className : '')} role="region" data-placement={placement} aria-label={strings.appContent.banner.region}>
      {banners.map((banner) => <BannerCard key={banner.id} banner={banner} assets={state.assets} onDismiss={dismiss} />)}
    </div>
  );
}

/** The page placement of a member app path (`/app/feed` is `Feed`), or null. */
export function placementForPath(pathname) {
  const path = String(pathname || '').replace(/\/+$/, '');
  if (path === '/app') return 'Home';
  if (path === '/sign-in') return 'SignIn';
  const section = path.startsWith('/app/') ? path.slice(5).split('/')[0] : '';
  const map = { feed: 'Feed', forums: 'Forums', library: 'Library', events: 'Events', directory: 'Directory', messages: 'Messages', account: 'Account' };
  return map[section] || null;
}

export default BannerSlot;
