/**
 * Vite plugin that builds the website for the active property (TK_PROPERTY_DIR, see
 * scripts/property.mjs). It provides:
 *
 * - `virtual:trelliskit/site`: the site bundle `{ property, content, legal, engine }` as a
 *   module, imported by src/property/buildSite.js.
 * - `virtual:trelliskit/theme.css`: the generated property theme (imported after index.css).
 * - `virtual:trelliskit/app-manifest`: the fallback content manifest inlined for the first launch offline
 *   (docs/LIVE_UPDATES_CONTRACT.md section 11.3), read at build time from TK_APP_MANIFEST_FILE or the API at
 *   TK_APP_MANIFEST_URL (shared/scripts/app-manifest.mjs); empty without a source and in tests.
 * - index.html head values (title, description, theme color, site name) from the property.
 * - Brand images, site.webmanifest, sitemap.xml, sitemap-pages.xml, and robots.txt: emitted into the client build
 *   and served by the dev server, so nothing brand-specific is committed under www/public.
 */
import path from 'node:path';
import { buildBrandAssets } from './brand-assets.mjs';
import { buildManifest, loadActiveSite, themeCssFor } from './property.mjs';
import { escapeHtml, siteMetaFor } from '../src/views/marketing/siteMeta.js';
import { buildFallbackManifest } from '../../shared/scripts/app-manifest.mjs';

const SITE_ID = 'virtual:trelliskit/site';
const THEME_ID = 'virtual:trelliskit/theme.css';
const RESOLVED_SITE = '\0' + SITE_ID;
const RESOLVED_THEME = '\0trelliskit-theme.css';
const MANIFEST_ID = 'virtual:trelliskit/app-manifest';
const RESOLVED_MANIFEST = '\0trelliskit-app-manifest';

const TYPES = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.ico': 'image/x-icon', '.webmanifest': 'application/manifest+json', '.xml': 'application/xml', '.txt': 'text/plain' };

/** @param {{ env?: Record<string, string|undefined> }} [options] */
export default function trellisKit(options = {}) {
  const env = options.env || process.env;
  let loaded = loadActiveSite(env);
  let assets = null;
  let isSsr = false;
  let fallbackManifest = null;
  const manifest = () => {
    if (!fallbackManifest) fallbackManifest = buildFallbackManifest({ client: 'web', env: env.VITEST ? {} : env });
    return fallbackManifest;
  };

  const generatedFiles = () => {
    if (!assets) assets = buildBrandAssets(loaded);
    const meta = siteMetaFor(loaded.site);
    return {
      ...assets,
      'site.webmanifest': Buffer.from(buildManifest(loaded.site.property)),
      // sitemap.xml is the marketing pages' sitemap for static hosting; behind the engine's nginx the backend serves
      // /sitemap.xml as a sitemap index that lists sitemap-pages.xml (the same content) with the public landing pages.
      'sitemap.xml': Buffer.from(meta.buildSitemap()),
      'sitemap-pages.xml': Buffer.from(meta.buildSitemap()),
      'robots.txt': Buffer.from(meta.buildRobots())
    };
  };

  return {
    name: 'trelliskit-property',
    configResolved(config) {
      isSsr = Boolean(config.build && config.build.ssr);
    },
    resolveId(id) {
      if (id === SITE_ID) return RESOLVED_SITE;
      if (id === THEME_ID) return RESOLVED_THEME;
      if (id === MANIFEST_ID) return RESOLVED_MANIFEST;
      return null;
    },
    load(id) {
      if (id === RESOLVED_SITE) return 'export default ' + JSON.stringify(loaded.site) + ';\n';
      if (id === RESOLVED_THEME) return themeCssFor(loaded);
      if (id === RESOLVED_MANIFEST) return manifest().then((m) => 'export default ' + JSON.stringify(m) + ';\n');
      return null;
    },
    transformIndexHtml(html) {
      const property = loaded.site.property;
      const values = {
        '__TK_NAME__': property.name,
        '__TK_DESCRIPTION__': property.description || property.name,
        '__TK_THEME_COLOR__': property.brand.colors.light.themeColor
      };
      return Object.entries(values).reduce((out, [key, value]) => out.split(key).join(escapeHtml(value)), html);
    },
    configureServer(server) {
      server.watcher.add(loaded.dir);
      server.watcher.on('change', (file) => {
        if (!file.startsWith(loaded.dir + path.sep)) return;
        loaded = loadActiveSite(env);
        assets = null;
        for (const id of [RESOLVED_SITE, RESOLVED_THEME]) {
          const mod = server.moduleGraph.getModuleById(id);
          if (mod) server.moduleGraph.invalidateModule(mod);
        }
        server.ws.send({ type: 'full-reload' });
      });
      server.middlewares.use((req, res, next) => {
        const name = (req.url || '').split('?')[0].replace(/^\//, '');
        if (!name || (name.includes('/') && !/^images\/[\w.-]+$/.test(name))) return next();
        const files = generatedFiles();
        if (!files[name]) return next();
        res.setHeader('Content-Type', TYPES[path.extname(name)] || 'application/octet-stream');
        res.end(files[name]);
      });
    },
    generateBundle() {
      if (isSsr) return;
      for (const [fileName, source] of Object.entries(generatedFiles())) {
        this.emitFile({ type: 'asset', fileName, source });
      }
    }
  };
}
