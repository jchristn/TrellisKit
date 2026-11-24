import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { fillDirectory, formatAddress, formatDuration, formatEventShort, normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useHydrated from './useHydrated.js';
import { appPaths } from '../../utils/phase4.js';

const t = strings.publicCatalog;

function section(property, key) {
  return property && property[key] && typeof property[key] === 'object' ? property[key] : {};
}

/**
 * Whether a public catalog can be shown on marketing pages (docs/PHASE4_CONTRACT.md sections 3.11,
 * 4.9, and 5.12): the feature is on, and its public switch is not explicitly off in the property
 * (`events.publicListingsEnabled`, `directory.publicListingsEnabled`, `library.publicSamplesEnabled`).
 * The server is the final word: a disabled listing answers `404 NotAvailable` and nothing renders.
 * @param {object} property
 * @param {'events'|'venues'|'lessons'} kind
 */
export function publicCatalogEnabled(property, kind) {
  const features = (property && property.features) || {};
  if (features.publicSamples === false) return false;
  if (kind === 'events') return features.events !== false && section(property, 'events').publicListingsEnabled !== false;
  if (kind === 'venues') return features.directory !== false && section(property, 'directory').publicListingsEnabled !== false;
  if (kind === 'lessons') return features.library !== false && section(property, 'library').publicSamplesEnabled !== false;
  return false;
}

const KINDS = Object.freeze({
  events: {
    load: (client, limit, signal) => client.enumeratePublicEvents({ maxResults: Math.min(20, limit) }, signal),
    available: (client) => typeof client.enumeratePublicEvents === 'function',
    path: (item) => appPaths.event(item.slug || item.id),
    title: (item) => item.title,
    details: (item) => [formatEventShort(item), item.location && (item.location.name || (item.location.venue && item.location.venue.name)), item.format === 'Online' ? t.online : ''].filter(Boolean),
    excerpt: (item) => item.summary || ''
  },
  venues: {
    load: (client, limit, signal) => client.enumeratePublicVenues({ maxResults: Math.min(20, limit) }, signal),
    available: (client) => typeof client.enumeratePublicVenues === 'function',
    path: (item) => appPaths.venue(item.slug || item.id),
    title: (item) => item.name,
    details: (item) => [formatAddress(item.address, item.addressVisibility, { includeCountry: false }), item.verified ? t.verified : ''].filter(Boolean),
    excerpt: (item) => item.summary || item.description || ''
  },
  lessons: {
    load: (client, limit, signal) => client.enumeratePublicLessons({ maxResults: Math.min(20, limit) }, signal),
    available: (client) => typeof client.enumeratePublicLessons === 'function',
    path: (item) => appPaths.lesson(item.slug || item.id),
    // Signed-out visitors watch a public sample on the marketing site (no account, public media paths).
    publicPath: (item) => '/lessons/' + encodeURIComponent(item.slug || item.id),
    title: (item) => item.title,
    details: (item) => [item.series && item.series.title ? t.inSeries(item.series.title) : '', formatDuration(item.durationSeconds)].filter(Boolean),
    excerpt: (item) => item.summary || ''
  }
});

/**
 * Public events, venues, or sample lessons for marketing pages (`@public-events`, `@public-venues`,
 * `@public-lessons`; the home page shows each at the end when it has no widget of that kind). Read
 * anonymously from the public enumerations (bounded: at most `limit`, never more than 20) in the
 * browser after hydration, so nothing is prerendered. Nothing renders when the catalog is off, the
 * request fails, or it is empty. Signed-out visitors get a join call to action; signed-in members
 * open the item in the member app.
 * @param {{ kind: 'events'|'venues'|'lessons', limit?: number }} props
 */
function PublicSampleCatalog({ kind, limit = 3 }) {
  const hydrated = useHydrated();
  const { client, isSignedIn } = useAuth();
  const { property, fill, runtimeSettled, canLoadRuntime } = useProperty();
  const config = KINDS[kind];
  // The build-time property.json has no public switches, so wait for the live property (or its
  // failure) before deciding: a listing turned off on the server is then never requested.
  const settled = !canLoadRuntime || runtimeSettled;
  const enabled = settled && Boolean(config) && publicCatalogEnabled(property, kind);
  const [items, setItems] = useState([]);

  useEffect(() => {
    if (!hydrated || !enabled || !client || !config.available(client)) return undefined;
    const controller = new AbortController();
    config.load(client, limit, controller.signal).then(
      (body) => { if (!controller.signal.aborted) setItems(normalizeEnumerationResult(body).objects.filter((x) => x && x.id).slice(0, limit)); },
      () => {}
    );
    return () => controller.abort();
  }, [hydrated, enabled, client, config, limit]);

  if (!hydrated || !enabled || !items.length) return null;
  const text = t[kind];
  const term = (template) => fill(fillDirectory(template, property));
  const titleId = 'public-' + kind + '-title';
  return (
    <section className="section public-samples" aria-labelledby={titleId}>
      <div className="site-container">
        <h2 id={titleId}>{term(text.title)}</h2>
        <p className="muted">{term(text.summary)}</p>
        <div className="public-sample-list">
          {items.map((item) => {
            const title = config.title(item) || item.id;
            // Signed-out visitors open the server-rendered landing page when the item has one (its `publicUrl`).
            const landing = !isSignedIn && item.publicUrl ? item.publicUrl : null;
            const to = isSignedIn ? config.path(item) : config.publicPath ? config.publicPath(item) : '/sign-up';
            const action = isSignedIn || landing ? text.open : config.publicPath && text.watch ? text.watch : term(text.join);
            return (
              <article key={item.id} className="post-card public-sample" aria-labelledby={'public-' + kind + '-' + item.id}>
                <h3 id={'public-' + kind + '-' + item.id} className="post-title">{title}</h3>
                {config.details(item).length > 0 && <p className="muted small">{config.details(item).join(' · ')}</p>}
                {config.excerpt(item) && <p>{config.excerpt(item)}</p>}
                <footer className="post-foot">
                  {landing
                    ? <a href={landing} className="button button-secondary button-sm" aria-label={action + ': ' + title}>{action}</a>
                    : <Link to={to} className="button button-secondary button-sm" aria-label={action + ': ' + title}>{action}</Link>}
                </footer>
              </article>
            );
          })}
        </div>
        {!isSignedIn && <Link to="/sign-up" className="button button-primary">{term(text.joinAll)}</Link>}
      </div>
    </section>
  );
}

export default PublicSampleCatalog;
