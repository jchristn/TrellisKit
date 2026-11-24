import path from 'node:path';
import { mkdtempSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import os from 'node:os';
import { PNG } from 'pngjs';
import { describe, expect, it } from 'vitest';
import { DEFAULT_PROPERTY_DIR, ENGINE_ROOT, buildManifest, buildThemeCss, loadSite, resolvePropertyDir, themeCssFor } from '../scripts/property.mjs';
import { buildBrandAssets, buildIco, fitPng } from '../scripts/brand-assets.mjs';
import trelliskit from '../scripts/vite-plugin-trelliskit.mjs';
import { fixtureDir, loadFixtureSite } from './helpers.jsx';

const size = (buf) => {
  const png = PNG.sync.read(buf);
  return [png.width, png.height];
};

describe('property selection (TK_PROPERTY_DIR)', () => {
  it('defaults to the neutral sample property under the engine root', () => {
    expect(DEFAULT_PROPERTY_DIR).toBe('samples/example');
    expect(resolvePropertyDir({})).toBe(path.resolve(ENGINE_ROOT, 'samples/example'));
    const loaded = loadSite(resolvePropertyDir({}));
    expect(loaded.site.property.id).toMatch(/^app_[a-z0-9]+_[A-Za-z0-9]+$/);
    expect(loaded.site.property.slug).toBe('example');
    expect(Object.keys(loaded.site.content).length).toBeGreaterThan(0);
  });

  it('accepts an absolute path or one relative to the engine root', () => {
    const dir = fixtureDir('bookclub');
    expect(resolvePropertyDir({ TK_PROPERTY_DIR: dir })).toBe(dir);
    expect(resolvePropertyDir({ TK_PROPERTY_DIR: path.relative(ENGINE_ROOT, dir) })).toBe(dir);
  });

  it('refuses a folder without property.json or with an invalid one, naming the problem', () => {
    const tmp = mkdtempSync(path.join(os.tmpdir(), 'td-prop-'));
    try {
      expect(() => loadSite(tmp)).toThrow(/no property.json/);
      writeFileSync(path.join(tmp, 'property.json'), JSON.stringify({ schemaVersion: 1, id: 'x' }));
      expect(() => loadSite(tmp)).toThrow(/name is required/);
    } finally {
      rmSync(tmp, { recursive: true, force: true });
    }
  });

  it('loads content and legal Markdown from the property folder', () => {
    const site = loadFixtureSite('dojo');
    expect(Object.keys(site.content).sort()).toEqual(['about.md', 'guide.md', 'home.md', 'membership.md', 'support.md']);
    expect(Object.keys(site.legal)).toHaveLength(5);
    expect(site.engine).toEqual({ repositoryUrl: '' });
  });
});

describe('theme generation', () => {
  it('writes brand tokens for light and dark from property.json, then the optional overrides', () => {
    const loaded = loadSite(fixtureDir('dojo'));
    const css = themeCssFor(loaded);
    expect(css).toContain(':root {\n  --brand-primary: #aa2200;');
    expect(css).toContain("  --color-accent-soft: #ffeeee;");
    expect(css).toMatch(/:root\[data-theme='dark'\] \{\n {2}--brand-primary: #ff8866;/);
    expect(css).toContain('/* Property overrides (web/theme.css) */\n/* Fixture override */');
    expect(css.indexOf('--brand-primary')).toBeLessThan(css.indexOf('Fixture override'));
  });

  it('needs nothing but property.json colors', () => {
    const property = loadFixtureSite('bookclub').property;
    const css = buildThemeCss(property);
    expect(css).toContain('--brand-primary: #2e7d32;');
    expect(css).toContain('--brand-theme-color: #102010;');
    // onAccentSoft defaults to primary when property.json leaves it out.
    expect(css).toContain('--brand-accent-soft-contrast: #2e7d32;');
    expect(css).not.toContain('overrides');
  });
});

describe('brand assets and manifest', () => {
  it('falls back to the light logo and accepts a property-owned dark logo', () => {
    const loaded = loadSite(fixtureDir('dojo'));
    const fallback = buildBrandAssets({ ...loaded, files: { ...loaded.files, logoDark: null } });
    expect(fallback['logo-dark.png']).toEqual(fallback['logo.png']);
    const tmp = mkdtempSync(path.join(os.tmpdir(), 'tk-dark-logo-'));
    try {
      const logo = new PNG({ width: 2, height: 2 });
      logo.data.fill(255);
      const file = path.join(tmp, 'logo-dark.png');
      writeFileSync(file, PNG.sync.write(logo));
      const assets = buildBrandAssets({ ...loaded, files: { ...loaded.files, logoDark: file } });
      expect(size(assets['logo-dark.png'])).toEqual([512, 512]);
      expect(assets['logo-dark.png']).not.toEqual(assets['logo.png']);
    } finally {
      rmSync(tmp, { recursive: true, force: true });
    }
  });

  it('generates every icon size, the logo, and an Open Graph image from the brand folder', () => {
    const assets = buildBrandAssets(loadSite(fixtureDir('dojo')));
    expect(Object.keys(assets).sort()).toEqual(['apple-touch-icon.png', 'brand-mark.png', 'favicon-32.png', 'favicon.ico', 'icon-192.png', 'icon-512.png', 'logo-dark.png', 'logo.png', 'og-image.png']);
    expect(size(assets['favicon-32.png'])).toEqual([32, 32]);
    expect(size(assets['icon-192.png'])).toEqual([192, 192]);
    expect(size(assets['icon-512.png'])).toEqual([512, 512]);
    expect(size(assets['apple-touch-icon.png'])).toEqual([180, 180]);
    expect(size(assets['og-image.png'])).toEqual([1200, 630]);
  });

  it('keeps aspect ratio and fills the background when asked', () => {
    const src = new PNG({ width: 4, height: 2 });
    src.data.fill(255);
    const out = PNG.sync.read(fitPng(src, 8, 8, { background: '#000000' }));
    // Wide source centered vertically: top row is background, middle row is the image.
    expect(Array.from(out.data.slice(0, 4))).toEqual([0, 0, 0, 255]);
    expect(Array.from(out.data.slice(4 * 8 * 4, 4 * 8 * 4 + 4))).toEqual([255, 255, 255, 255]);
  });

  it('builds the web manifest from the property', () => {
    const manifest = JSON.parse(buildManifest(loadFixtureSite('bookclub').property));
    expect(manifest).toMatchObject({ name: 'Fixture Book Club', short_name: 'Book Club', theme_color: '#263238', start_url: '/' });
    expect(manifest.icons.map((i) => i.sizes)).toEqual(['192x192', '512x512']);
  });
});

describe('vite plugin', () => {
  const plugin = trelliskit({ env: { TK_PROPERTY_DIR: fixtureDir('bookclub'), TK_REPOSITORY_URL: 'https://git.example.test/engine' } });

  it('serves the site bundle and theme as virtual modules', () => {
    const siteId = plugin.resolveId('virtual:trelliskit/site');
    const code = plugin.load(siteId);
    const site = JSON.parse(code.replace(/^export default /, '').replace(/;\n$/, ''));
    expect(site.property.name).toBe('Fixture Book Club');
    expect(site.engine.repositoryUrl).toBe('https://git.example.test/engine');
    expect(plugin.load(plugin.resolveId('virtual:trelliskit/theme.css'))).toContain('--brand-primary: #2e7d32;');
    expect(plugin.resolveId('react')).toBeNull();
  });

  it('inlines the fallback content manifest from TK_APP_MANIFEST_FILE, and the empty one without a source', async () => {
    const file = path.resolve(ENGINE_ROOT, 'shared', 'test', 'fixtures', 'app-manifest.json');
    const withFile = trelliskit({ env: { TK_PROPERTY_DIR: fixtureDir('bookclub'), TK_APP_MANIFEST_FILE: file } });
    const id = withFile.resolveId('virtual:trelliskit/app-manifest');
    const parse = (code) => JSON.parse(code.replace(/^export default /, '').replace(/;\n$/, ''));
    const bundled = parse(await withFile.load(id));
    expect(bundled).toMatchObject({ fallback: true, contentVersion: 7, killSwitches: { feed: { killed: true, message: 'Paused' } } });
    const empty = parse(await plugin.load(plugin.resolveId('virtual:trelliskit/app-manifest')));
    expect(empty).toMatchObject({ fallback: true, contentVersion: null, flags: {}, killSwitches: {} });
  });

  it('fills index.html with the property name, description, and theme color (escaped)', () => {
    const html = plugin.transformIndexHtml('<title>__TK_NAME__</title><meta content="__TK_DESCRIPTION__"><meta content="__TK_THEME_COLOR__">');
    expect(html).toBe('<title>Fixture Book Club</title><meta content="A fixture property with no ranks."><meta content="#263238">');
  });

  it('emits brand files, the manifest, sitemap, and robots into the client build only', () => {
    const emitted = [];
    plugin.configResolved({ build: { ssr: false } });
    plugin.generateBundle.call({ emitFile: (f) => emitted.push(f.fileName) });
    expect(emitted.sort()).toEqual(['apple-touch-icon.png', 'brand-mark.png', 'favicon-32.png', 'favicon.ico', 'icon-192.png', 'icon-512.png', 'logo-dark.png', 'logo.png', 'og-image.png', 'robots.txt', 'site.webmanifest', 'sitemap-pages.xml', 'sitemap.xml']);
    const ssrEmitted = [];
    plugin.configResolved({ build: { ssr: 'entry.jsx' } });
    plugin.generateBundle.call({ emitFile: (f) => ssrEmitted.push(f.fileName) });
    expect(ssrEmitted).toEqual([]);
  });

  it('builds a 16, 32, and 48 px favicon.ico from the icon when the property has no brand.iconIco', () => {
    const src = new PNG({ width: 64, height: 64 });
    src.data.fill(255);
    const ico = buildIco(src);
    expect(ico.readUInt16LE(2)).toBe(1);
    expect(ico.readUInt16LE(4)).toBe(3);
    expect([ico.readUInt8(6), ico.readUInt8(22), ico.readUInt8(38)]).toEqual([16, 32, 48]);
    const first = ico.readUInt32LE(6 + 12);
    expect(ico.subarray(first, first + 8)).toEqual(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
    expect(ico.readUInt32LE(6 + 8) + first).toBe(ico.readUInt32LE(22 + 12));
  });

  it('copies web/images/ files to images/<name> and skips other files', () => {
    const tmp = mkdtempSync(path.join(os.tmpdir(), 'td-img-'));
    try {
      const property = loadFixtureSite('bookclub').property;
      mkdirSync(path.join(tmp, 'brand'));
      mkdirSync(path.join(tmp, 'web', 'images'), { recursive: true });
      writeFileSync(path.join(tmp, 'brand', 'logo.png'), PNG.sync.write(new PNG({ width: 2, height: 2 })));
      writeFileSync(path.join(tmp, 'web', 'images', 'cover.jpg'), Buffer.from([0xff, 0xd8, 0xff]));
      writeFileSync(path.join(tmp, 'web', 'images', 'notes.txt'), 'not an image');
      writeFileSync(path.join(tmp, 'property.json'), JSON.stringify(property));
      const assets = buildBrandAssets(loadSite(tmp));
      expect(assets['images/cover.jpg']).toEqual(Buffer.from([0xff, 0xd8, 0xff]));
      expect(Object.keys(assets).filter((k) => k.startsWith('images/'))).toEqual(['images/cover.jpg']);
    } finally {
      rmSync(tmp, { recursive: true, force: true });
    }
  });

  it('includes favicon.ico when the property provides brand.iconIco', () => {
    const tmp = mkdtempSync(path.join(os.tmpdir(), 'td-ico-'));
    try {
      const property = loadFixtureSite('bookclub').property;
      mkdirSync(path.join(tmp, 'brand'));
      const png = new PNG({ width: 2, height: 2 });
      writeFileSync(path.join(tmp, 'brand', 'logo.png'), PNG.sync.write(png));
      writeFileSync(path.join(tmp, 'brand', 'icon.ico'), Buffer.from([0, 0, 1, 0]));
      writeFileSync(path.join(tmp, 'property.json'), JSON.stringify(property));
      const assets = buildBrandAssets(loadSite(tmp));
      expect(assets['favicon.ico']).toEqual(Buffer.from([0, 0, 1, 0]));
    } finally {
      rmSync(tmp, { recursive: true, force: true });
    }
  });
});
