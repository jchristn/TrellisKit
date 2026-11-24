/**
 * Build step 3 of 3 (see README "Prerendering"): render the public marketing routes to static HTML.
 *
 * 1. Builds scripts/prerender-entry.jsx with Vite in SSR mode into node_modules/.cache (deleted after).
 * 2. Reads dist/index.html from the client build and writes, for every route of the active property
 *    (content pages, legal pages, and /join; see src/views/marketing/siteMeta.js):
 *      /            -> dist/index.html
 *      /book        -> dist/book/index.html   (and so on)
 *    each with its own title, description, canonical, Open Graph, Twitter, and JSON-LD tags.
 * 3. Writes dist/spa.html: the untouched client shell (plus noindex) for every other path, so
 *    /app, /admin, and auth pages stay client-rendered.
 */
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const WWW = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DIST = path.join(WWW, 'dist');
const SSR_OUT = path.join(WWW, 'node_modules', '.cache', 'trelliskit-prerender');

/** Output file for a route path. */
export function outputFile(routePath) {
  return routePath === '/' ? path.join(DIST, 'index.html') : path.join(DIST, routePath.slice(1), 'index.html');
}

async function main() {
  rmSync(SSR_OUT, { recursive: true, force: true });
  await build({
    root: WWW,
    configFile: path.join(WWW, 'vite.config.js'),
    logLevel: 'warn',
    build: {
      ssr: 'scripts/prerender-entry.jsx',
      outDir: SSR_OUT,
      emptyOutDir: true,
      sourcemap: false,
      copyPublicDir: false,
      rollupOptions: { output: { entryFileNames: '[name].mjs', chunkFileNames: '[name]-[hash].mjs' } }
    }
  });

  try {
    const entry = await import(pathToFileURL(path.join(SSR_OUT, 'prerender-entry.mjs')).href);
    const template = readFileSync(path.join(DIST, 'index.html'), 'utf8');
    writeFileSync(path.join(DIST, 'spa.html'), entry.renderShell(template));
    const written = [];
    for (const route of entry.ROUTES.filter((r) => r.prerender)) {
      const file = outputFile(route.path);
      mkdirSync(path.dirname(file), { recursive: true });
      writeFileSync(file, entry.renderPage(template, route));
      written.push(path.relative(WWW, file));
    }
    console.log('prerender: wrote dist/spa.html and ' + written.length + ' pages: ' + written.join(', '));
  } finally {
    rmSync(SSR_OUT, { recursive: true, force: true });
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
