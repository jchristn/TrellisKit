/**
 * Server entry for prerendering (built with `vite build --ssr` by scripts/prerender.mjs).
 *
 * It renders the same tree as main.jsx and App.jsx (PropertyProvider, ThemeProvider, a router,
 * AppRoutes) so the static HTML matches what the client renders on its first pass. The member session is never restored
 * on the server (`restoreOnLoad={false}`), so every page renders signed out.
 */
import { renderToString } from 'react-dom/server';
import { StaticRouter } from 'react-router-dom';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { PropertyProvider } from '../src/context/PropertyContext.jsx';
import { AppRoutes } from '../src/App.jsx';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { headTagsToHtml, siteMetaFor } from '../src/views/marketing/siteMeta.js';

const meta = siteMetaFor(BUILD_SITE);

/** Every marketing route of the property this build is for. */
export const ROUTES = meta.routes;

/**
 * ThemeProvider reads localStorage and matchMedia while rendering. On the server, provide a stub
 * that reports no saved theme and a light system preference (index.html applies the real theme
 * before first paint, and MarketingThemeToggle reconciles after hydration).
 */
function withBrowserStub(fn) {
  if (typeof window !== 'undefined') return fn();
  globalThis.window = {
    localStorage: { getItem: () => null, setItem: () => {}, removeItem: () => {} },
    matchMedia: () => ({ matches: false, addEventListener: () => {}, removeEventListener: () => {} })
  };
  try {
    return fn();
  } finally {
    delete globalThis.window;
  }
}

/** Render the body markup for a path. */
export function renderBody(path) {
  return withBrowserStub(() => renderToString(
    <PropertyProvider>
      <ThemeProvider>
        <StaticRouter location={path}>
          <AppRoutes restoreOnLoad={false} />
        </StaticRouter>
      </ThemeProvider>
    </PropertyProvider>
  ));
}

const HEAD_TAG = /\s*<(title>[\s\S]*?<\/title>|meta\s+(name="(description|robots|twitter:[^"]*)"|property="og:[^"]*")[^>]*>)/g;

/**
 * Produce a complete HTML document for a marketing route from the built index.html template:
 * the template's generic title and social tags are replaced with the route's head tags, and the
 * rendered markup goes into #root (left empty for head-only routes such as /join).
 * @param {string} template contents of dist/index.html from the client build
 * @param {object} route an entry from ROUTES
 */
export function renderPage(template, route) {
  const rendered = route.body ? renderBody(route.path) : '';
  // React emits resource hints (for example image preloads) at the start of the markup because
  // renderToString has no document head. Move them into <head> so #root holds only the app.
  const hints = rendered.match(/^(<link [^>]*\/>)+/);
  const body = hints ? rendered.slice(hints[0].length) : rendered;
  const head = headTagsToHtml(meta.buildHeadTags(route.path)) + (hints ? '\n    ' + hints[0].replace(/\/>(?=<)/g, '/>\n    ') : '');
  const stripped = template.replace(HEAD_TAG, '');
  if (!stripped.includes('</head>') || !stripped.includes('<div id="root"></div>')) {
    throw new Error('prerender: index.html is missing </head> or <div id="root"></div>');
  }
  return stripped
    .replace('</head>', '  ' + head + '\n  </head>')
    .replace('<div id="root"></div>', '<div id="root"' + (body ? ' data-prerendered="' + route.path + '"' : '') + '>' + body + '</div>');
}

/** The SPA shell served for every other path (/app, /admin, sign-in, 404): not indexable. */
export function renderShell(template) {
  return template.replace('</head>', '  <meta name="robots" content="noindex" />\n  </head>');
}
