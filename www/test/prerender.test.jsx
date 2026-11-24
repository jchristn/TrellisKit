import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { StrictMode, act } from 'react';
import { hydrateRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ROUTES as MARKETING_ROUTES, renderPage, renderShell } from '../scripts/prerender-entry.jsx';
import { ThemeProvider } from '../src/context/ThemeContext.jsx';
import { AppRoutes } from '../src/App.jsx';
import { SESSION_HINT_KEY } from '../src/context/AuthContext.jsx';

const TEMPLATE = readFileSync(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'index.html'), 'utf8');
const route = (path) => MARKETING_ROUTES.find((r) => r.path === path);

function parse(html) {
  return new DOMParser().parseFromString(html, 'text/html');
}

function jsonLd(doc) {
  return JSON.parse(doc.querySelector('script[type="application/ld+json"]').textContent);
}

describe('prerender output', () => {
  it.each(MARKETING_ROUTES.filter((r) => r.body).map((r) => [r.path, r]))('renders %s with its title, head tags, JSON-LD, and content', (path, r) => {
    const doc = parse(renderPage(TEMPLATE, r));
    expect(doc.title).toBe(r.title);
    expect(doc.querySelectorAll('title')).toHaveLength(1);
    expect(doc.querySelectorAll('meta[name="description"]')).toHaveLength(1);
    expect(doc.querySelector('meta[name="description"]').getAttribute('content')).toBe(r.description);
    expect(doc.querySelector('link[rel="canonical"]').getAttribute('href')).toBe('https://dojo.test' + path);
    expect(doc.querySelectorAll('meta[property="og:image"]')).toHaveLength(1);
    expect(jsonLd(doc)['@context']).toBe('https://schema.org');
    const root = doc.getElementById('root');
    expect(root.getAttribute('data-prerendered')).toBe(path);
    expect(root.querySelector('h1')).not.toBeNull();
    expect(root.querySelector('main#main')).not.toBeNull();
    expect(root.querySelector('footer a[href="/privacy"]')).not.toBeNull();
    expect(root.querySelector(':scope > link')).toBeNull();
  });

  it('includes the Book and Person JSON-LD and the buy link on the fixture /guide page', () => {
    const html = renderPage(TEMPLATE, route('/guide'));
    const doc = parse(html);
    expect(html).toContain('<title>The Fixture Guide | Fixture Dojo</title>');
    const types = jsonLd(doc)['@graph'].map((n) => n['@type']);
    expect(types).toEqual(expect.arrayContaining(['Organization', 'WebSite', 'Person', 'Book']));
    expect(doc.querySelector('h1').textContent).toBe('The Fixture Guide');
    expect(doc.querySelector('a[href="https://shop.dojo.test/guide"]')).not.toBeNull();
  });

  it('renders legal pages from Markdown with the draft notice', () => {
    const doc = parse(renderPage(TEMPLATE, route('/subscription-terms')));
    expect(doc.querySelector('h1').textContent).toBe('Subscription terms');
    expect(doc.querySelector('.draft-banner').textContent).toContain('DRAFT for review by counsel; not legal advice.');
    expect(doc.body.textContent).toContain('Fixture text for Fixture Dojo.');
  });

  it('prerenders only the head for /join, which redirects on the client', () => {
    const doc = parse(renderPage(TEMPLATE, route('/join')));
    expect(doc.title).toBe('Join Fixture Dojo');
    expect(doc.querySelector('meta[name="robots"]').getAttribute('content')).toBe('noindex, follow');
    expect(doc.getElementById('root').innerHTML).toBe('');
  });

  it('marks the client-only shell noindex', () => {
    expect(renderShell(TEMPLATE)).toContain('<meta name="robots" content="noindex" />');
  });
});

describe('hydration of prerendered pages', () => {
  let root = null;
  let container = null;

  afterEach(() => {
    if (root) act(() => root.unmount());
    if (container) container.remove();
    root = null;
    container = null;
  });

  async function hydrate(path) {
    const doc = parse(renderPage(TEMPLATE, route(path)));
    container = document.createElement('div');
    container.id = 'root';
    container.innerHTML = doc.getElementById('root').innerHTML;
    document.body.appendChild(container);
    const errors = [];
    const error = vi.spyOn(console, 'error').mockImplementation((...args) => errors.push(args.join(' ')));
    const warn = vi.spyOn(console, 'warn').mockImplementation((...args) => errors.push(args.join(' ')));
    const fetchImpl = async () => new Response(JSON.stringify({ error: 'InvalidRefreshToken' }), { status: 401, headers: { 'Content-Type': 'application/json' } });
    await act(async () => {
      root = hydrateRoot(
        container,
        <StrictMode>
          <ThemeProvider>
            <MemoryRouter initialEntries={[path]}>
              <AppRoutes fetchImpl={fetchImpl} />
            </MemoryRouter>
          </ThemeProvider>
        </StrictMode>,
        { onRecoverableError: (err) => errors.push('recoverable: ' + err.message) }
      );
    });
    error.mockRestore();
    warn.mockRestore();
    return errors;
  }

  it.each(MARKETING_ROUTES.filter((r) => r.body).map((r) => r.path))('hydrates %s without warnings', async (path) => {
    expect(await hydrate(path)).toEqual([]);
    expect(container.querySelector('h1')).not.toBeNull();
  });

  it('hydrates without warnings when the saved theme is dark, then shows the light-theme toggle', async () => {
    window.localStorage.setItem('tk.theme', 'dark');
    expect(await hydrate('/')).toEqual([]);
    expect(container.querySelector('[aria-label="Switch to light theme"]')).not.toBeNull();
  });

  it('hydrates without warnings while a remembered session is being restored', async () => {
    window.localStorage.setItem(SESSION_HINT_KEY, '1');
    expect(await hydrate('/membership')).toEqual([]);
  });
});
