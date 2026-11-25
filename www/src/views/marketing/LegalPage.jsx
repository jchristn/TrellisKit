import { useEffect, useMemo, useState } from 'react';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import Icon from '../../components/Icon.jsx';
import { formatDate } from '../../utils/format.js';
import useRouteMeta from './useRouteMeta.js';
import { renderBlocks, splitLegalDocument } from './markdown.jsx';
import { legalSource, siteMetaFor } from './siteMeta.js';

const mk = strings.marketing;

/**
 * Load the live copy of a property document (`GET /api/v1/public/documents/{key}`) after
 * hydration, so an admin edit shows without a rebuild. Resolves null until it loads (and when it
 * fails, which keeps the build-time copy).
 */
function useLiveDocument(client, key) {
  const [live, setLive] = useState(null);
  useEffect(() => {
    if (!key || !client || typeof client.getPublicDocument !== 'function') return undefined;
    const controller = new AbortController();
    client.getPublicDocument(key, controller.signal).then(
      (doc) => { if (!controller.signal.aborted && doc && typeof doc.content === 'string') setLive({ key, doc }); },
      () => {}
    );
    return () => controller.abort();
  }, [client, key]);
  return live && live.key === key ? live.doc : null;
}

/**
 * Legal page rendered from Markdown: a document from the property's `documents` list (prerendered
 * from the build-time file, then replaced by the live current version with its version and last
 * updated date), or a standard draft in the property's legal/ folder. The draft notice at the top
 * of the document is shown as a banner.
 * @param {{ path: string }} props
 */
function LegalPage({ path }) {
  const { site } = useProperty();
  const { client } = useAuth();
  useRouteMeta(path);
  const route = siteMetaFor(site).getRoute(path);
  const live = useLiveDocument(client, route && route.documentKey);
  const source = live ? live.content : legalSource(site, route);
  const doc = useMemo(() => splitLegalDocument(source), [source]);
  if (!route) return null;
  const heading = (live && live.title) || route.heading;
  const version = (live && live.version) || route.version;
  const updated = live && live.updatedUtc ? formatDate(live.updatedUtc) : '';
  return (
    <article className="site-container page legal" aria-labelledby="legal-title" data-document={route.documentKey || undefined}>
      {doc.notice && <div className="draft-banner" role="note">
        <Icon name="alert" size={18} />
        <div>
          <strong className="draft-banner-title">{mk.draftNotice}</strong>
          <span>{mk.draftBanner}</span>
        </div>
      </div>}
      <h1 id="legal-title">{heading}</h1>
      {route.documentKey && (version || updated) && (
        <p className="muted small legal-meta">
          {version && <span className="legal-version">{mk.documents.version(version)}</span>}
          {version && updated && <span aria-hidden="true">{' · '}</span>}
          {updated && <span className="legal-updated">{mk.documents.updated(updated)}</span>}
        </p>
      )}
      <div className="legal-body">{renderBlocks(doc.blocks)}</div>
    </article>
  );
}

export default LegalPage;
