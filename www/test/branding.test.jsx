import { useState } from 'react';
import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError, brandPalette, checkPaletteContrast } from '@trelliskit/shared';
import strings from '../src/strings.js';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { PropertyProvider, useRuntimeProperty } from '../src/context/PropertyContext.jsx';
import { applyRuntimePalette, isPaletteCss } from '../src/property/runtimeTheme.js';
import { PALETTE_STORAGE_KEY, RUNTIME_PALETTE_STYLE_ID, paletteCss } from '../src/property/theme.js';
import BrandingEditor, { brandingUpdateBody, resolveDraft } from '../src/components/branding/BrandingEditor.jsx';
import AiSwitchPanel from '../src/components/branding/AiSwitchPanel.jsx';
import BrandingView from '../src/views/admin/BrandingView.jsx';
import AiSettingsView from '../src/views/admin/AiSettingsView.jsx';
import EmbedOriginsEditor from '../src/views/admin/ai/EmbedOriginsEditor.jsx';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import { ToastProvider } from '../src/context/ToastContext.jsx';
import { createFetch, json, renderAdmin } from './helpers.jsx';

const b = strings.admin.branding;
const seed = brandPalette(BUILD_SITE.property);

function brandingView(overrides = {}) {
  const palette = overrides.palette || seed;
  return {
    propertyId: BUILD_SITE.property.id, version: 2, source: 'Edit', note: null,
    light: { ...palette.light }, dark: { ...palette.dark }, keys: Object.keys(palette.light),
    backgrounds: { light: '#FFFFFF', dark: '#0E141D' },
    contrast: checkPaletteContrast(palette, overrides.overrides || []), overrides: overrides.overrides || [],
    seed: { version: null, light: { ...seed.light }, dark: { ...seed.dark } },
    previous: { version: 1, source: 'Seed', createdUtc: '2026-11-01T00:00:00Z', light: { ...seed.light, primary: '#123456' }, dark: { ...seed.dark } },
    syncsFromFolder: true, createdByUserId: 'usr_1', createdUtc: '2026-11-12T00:00:00Z',
    ...overrides.extra
  };
}

function renderEditor(props) {
  return render(<ToastProvider><BrandingEditor {...props} /></ToastProvider>);
}

beforeEach(() => {
  window.localStorage.clear();
  const old = document.getElementById(RUNTIME_PALETTE_STYLE_ID);
  if (old) old.remove();
});

describe('runtime palette', () => {
  it('applies the live palette as html:root custom properties, stores it, and keeps the style last', () => {
    const property = { brand: { colors: { light: { ...seed.light, primary: '#0B3D91' }, dark: seed.dark } } };
    const css = applyRuntimePalette(property);
    const style = document.getElementById(RUNTIME_PALETTE_STYLE_ID);
    expect(style).not.toBeNull();
    expect(style.textContent).toBe(css);
    expect(css).toContain("html:root {\n  --brand-primary: #0b3d91;");
    expect(css).toContain("html:root[data-theme='dark'] {");
    expect(css).toContain('--brand-accent-soft-contrast: ' + seed.light.onAccentSoft.toLowerCase());
    expect(document.head.lastElementChild).toBe(style);
    expect(window.localStorage.getItem(PALETTE_STORAGE_KEY)).toBe(css);
    expect(isPaletteCss(css)).toBe(true);
    document.head.appendChild(document.createElement('meta'));
    applyRuntimePalette(property);
    expect(document.head.lastElementChild).toBe(style);
  });

  it('never writes anything but hex colors', () => {
    const css = paletteCss({ light: { primary: 'red;} body{display:none', accent: '#ABCDEF' }, dark: {} });
    expect(css).not.toContain('red');
    expect(css).toContain('--brand-accent: #abcdef;');
    expect(isPaletteCss('html:root {\n  --x: url(evil);\n}\n')).toBe(false);
    expect(applyRuntimePalette({ brand: { colors: {} } })).toBe('');
  });

  it('PropertyProvider applies the palette from GET /property once it loads, without a rebuild', async () => {
    const runtime = { ...BUILD_SITE.property, brand: { ...BUILD_SITE.property.brand, colors: { light: { ...seed.light, primary: '#0B3D91' }, dark: seed.dark }, paletteVersion: 3 } };
    const client = { property: () => Promise.resolve(runtime) };
    function Probe() {
      useRuntimeProperty(client);
      return null;
    }
    render(<PropertyProvider site={BUILD_SITE}><Probe /></PropertyProvider>);
    await waitFor(() => expect(document.getElementById(RUNTIME_PALETTE_STYLE_ID)).not.toBeNull());
    expect(document.getElementById(RUNTIME_PALETTE_STYLE_ID).textContent).toContain('--brand-primary: #0b3d91;');
  });

  it('the build-time site has no runtime style until the live property loads', () => {
    render(<PropertyProvider site={BUILD_SITE}><span>static</span></PropertyProvider>);
    expect(document.getElementById(RUNTIME_PALETTE_STYLE_ID)).toBeNull();
  });
});

describe('Branding editor helpers', () => {
  it('resolveDraft keeps the saved value for invalid entries and reports them', () => {
    const draft = { light: { ...seed.light, primary: 'nope' }, dark: { ...seed.dark } };
    const { palette, invalid } = resolveDraft(seed, draft);
    expect(invalid).toEqual(['light.primary']);
    expect(palette.light.primary).toBe(seed.light.primary);
  });

  it('brandingUpdateBody sends changed keys, ticked overrides of failing pairs, and the note', () => {
    expect(brandingUpdateBody(seed, seed, {}, [], '')).toBeNull();
    const next = { light: { ...seed.light, onAccentSoft: '#F7C9C9' }, dark: { ...seed.dark } };
    const ticked = { 'light|onAccentSoft/accentSoft': { on: true, reason: ' review ' }, 'dark|onPrimary/primary': { on: true } };
    expect(brandingUpdateBody(seed, next, ticked, [], 'Lighter')).toEqual({
      light: { onAccentSoft: '#F7C9C9' },
      contrastOverrides: [{ scheme: 'light', pair: 'onAccentSoft/accentSoft', reason: 'review' }],
      note: 'Lighter'
    });
  });
});

describe('Branding editor', () => {
  it('shows pickers for both schemes, previews of key components, and the contrast table', async () => {
    renderEditor({ load: () => Promise.resolve(brandingView()), save: () => Promise.reject(new Error('x')), revert: () => Promise.reject(new Error('x')) });
    expect(await screen.findByLabelText(b.hexLabel('primary', 'light'))).toHaveValue(seed.light.primary);
    expect(screen.getByLabelText(b.pickerLabel('onAccentSoft', 'dark'))).toBeInTheDocument();
    expect(screen.getByTestId('brand-preview-light')).toBeInTheDocument();
    const dark = screen.getByTestId('brand-preview-dark');
    expect(dark.style.getPropertyValue('--pv-primary')).toBe(seed.dark.primary);
    expect(within(dark).getByText(b.previewInitials)).toBeInTheDocument();
    expect(within(dark).getByText(b.previewChip)).toBeInTheDocument();
    expect(within(dark).getByText(b.previewLink)).toBeInTheDocument();
    expect(screen.getAllByText(b.pass).length).toBe(14);
    expect(screen.getByText(b.folderBanner)).toBeInTheDocument();
  });

  it('a failing pair blocks the save until it is overridden, then sends the override', async () => {
    const saves = [];
    const save = (body) => {
      saves.push(body);
      const palette = { light: { ...seed.light, ...body.light }, dark: { ...seed.dark } };
      return Promise.resolve(brandingView({ palette, overrides: body.contrastOverrides.map((o) => ({ ...o, ratio: 1.5, required: 4.5 })), extra: { version: 3 } }));
    };
    renderEditor({ load: () => Promise.resolve(brandingView()), save, revert: () => Promise.reject(new Error('x')) });
    const input = await screen.findByLabelText(b.hexLabel('onAccentSoft', 'light'));
    await userEvent.clear(input);
    await userEvent.type(input, '#F7C9C9');
    const row = screen.getByTestId('contrast-light-onAccentSoft/accentSoft');
    expect(within(row).getByText(b.fail)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: b.save }));
    expect(saves).toHaveLength(0);
    expect(screen.getAllByText(b.failingSummary(1)).length).toBeGreaterThan(0);
    await userEvent.click(within(row).getByRole('checkbox'));
    expect(within(row).getByText(b.overridden)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: b.save }));
    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0]).toEqual({ light: { onAccentSoft: '#F7C9C9' }, contrastOverrides: [{ scheme: 'light', pair: 'onAccentSoft/accentSoft' }] });
    expect(await screen.findByText(b.saved(3))).toBeInTheDocument();
  });

  it('shows server field errors on the pair and on the color', async () => {
    const error = new ApiError('Invalid', { status: 400, code: 'ValidationFailed', body: { error: 'ValidationFailed', fieldErrors: { 'contrast.light.onPrimary/primary': 'light onPrimary on primary is 2.10:1; needs 4.5:1.', 'light.accent': 'Must be a hex color.' } } });
    const load = () => Promise.resolve(brandingView());
    renderEditor({ load, save: () => Promise.reject(error), revert: () => Promise.reject(new Error('x')) });
    const input = await screen.findByLabelText(b.hexLabel('accent', 'light'));
    await userEvent.clear(input);
    await userEvent.type(input, '#222222');
    await userEvent.click(screen.getByRole('button', { name: b.save }));
    expect(await screen.findByText('light onPrimary on primary is 2.10:1; needs 4.5:1.')).toBeInTheDocument();
    expect(screen.getByText('Must be a hex color.')).toBeInTheDocument();
  });

  it('loads presets into the editor and reverts on the server after confirmation', async () => {
    const reverts = [];
    const revert = (to) => {
      reverts.push(to);
      return Promise.resolve(brandingView({ extra: { version: 4, source: 'RevertSeed' } }));
    };
    renderEditor({ load: () => Promise.resolve(brandingView()), save: () => Promise.reject(new Error('x')), revert });
    await screen.findByLabelText(b.hexLabel('primary', 'light'));
    await userEvent.click(screen.getByRole('button', { name: b.presetPrevious(1) }));
    expect(screen.getByLabelText(b.hexLabel('primary', 'light'))).toHaveValue('#123456');
    await userEvent.click(screen.getByRole('button', { name: b.presetSeed }));
    expect(screen.getByLabelText(b.hexLabel('primary', 'light'))).toHaveValue(seed.light.primary);
    await userEvent.click(screen.getByRole('button', { name: b.revertSeed }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: b.revertSeed }));
    await waitFor(() => expect(reverts).toEqual(['Seed']));
    expect(await screen.findByText(b.reverted(4))).toBeInTheDocument();
  });

  it('the admin page loads GET /admin/property/branding and saves with PUT', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/property/branding' && call.method === 'GET') return json(brandingView());
      if (call.path === '/api/v1/admin/property/branding' && call.method === 'PUT') return json(brandingView({ extra: { version: 3 } }));
      if (call.path === '/api/v1/property') return json(BUILD_SITE.property);
      return undefined;
    });
    renderAdmin(<BrandingView />, { fetchImpl, path: '/admin/branding', routePath: '/admin/branding', site: BUILD_SITE });
    const input = await screen.findByLabelText(b.hexLabel('primary', 'dark'));
    await userEvent.clear(input);
    await userEvent.type(input, '#9FC2F5');
    await userEvent.click(screen.getByRole('button', { name: b.save }));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')).toBeTruthy());
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ dark: { primary: '#9FC2F5' } });
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true));
  });
});

describe('palette versions', () => {
  const version = (n, extra = {}) => ({ version: n, source: n === 1 ? 'Seed' : 'Edit', note: n === 2 ? 'Darker blue' : null, createdUtc: '2026-11-0' + n + 'T00:00:00Z', light: { ...seed.light }, dark: { ...seed.dark }, isCurrent: false, labels: [], tags: {}, ...extra });
  const enumeration = (objects, token) => ({ success: true, maxResults: 10, skip: 0, continuationToken: token || null, endOfResults: !token, totalRecords: 12, recordsRemaining: 0, objects });

  it('lists every version with paging and restores any version after confirmation', async () => {
    const restored = brandingView({ extra: { version: 13, source: 'RevertVersion' } });
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/property/branding' && call.method === 'GET') return json(brandingView({ extra: { version: 12 } }));
      if (call.path === '/api/v1/admin/property/branding/versions/enumerate') {
        return call.body.continuationToken === 'p2'
          ? json(enumeration([version(2), version(1)]))
          : json(enumeration([version(12, { isCurrent: true }), ...Array.from({ length: 9 }, (_, i) => version(11 - i))], 'p2'));
      }
      if (call.path === '/api/v1/admin/property/branding/revert') return json(restored);
      if (call.path === '/api/v1/property') return json(BUILD_SITE.property);
      return undefined;
    });
    renderAdmin(<BrandingView />, { fetchImpl, path: '/admin/branding', routePath: '/admin/branding', site: BUILD_SITE });
    const section = await screen.findByRole('region', { name: b.versions.title });
    await within(section).findByText(b.versions.current);
    expect(within(section).queryByRole('button', { name: b.versions.restoreLabel(12) })).toBeNull();
    expect(calls.find((c) => c.path === '/api/v1/admin/property/branding/versions/enumerate').body).toEqual({ maxResults: 10 });
    await userEvent.click(within(section).getByRole('button', { name: b.versions.older }));
    await within(section).findByText('Darker blue');
    expect(calls.filter((c) => c.path === '/api/v1/admin/property/branding/versions/enumerate').at(-1).body).toEqual({ maxResults: 10, continuationToken: 'p2' });
    await userEvent.click(within(section).getByRole('button', { name: b.versions.restoreLabel(2) }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: b.versions.restore }));
    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/admin/property/branding/revert')).toBeTruthy());
    expect(calls.find((c) => c.path === '/api/v1/admin/property/branding/revert').body).toEqual({ to: 'Version', version: 2 });
    expect(await screen.findByText(b.versions.restored(2, 13))).toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true));
  });

  it('the editor hides the list when no version source is given', async () => {
    renderEditor({ load: () => Promise.resolve(brandingView()), save: () => Promise.reject(new Error('x')), revert: () => Promise.reject(new Error('x')) });
    await screen.findByLabelText(b.hexLabel('primary', 'light'));
    expect(screen.queryByRole('region', { name: b.versions.title })).toBeNull();
  });
});

describe('AI switch', () => {
  const ai = (enabled, extra = {}) => ({ propertyId: 'p', enabled, deploymentEnabled: true, available: enabled, seedEnabled: false, syncsFromFolder: false, revision: 1, updatedByUserId: null, updatedUtc: '2026-11-12T00:00:00Z', ...extra });

  it('turns AI on at once and asks before turning it off', async () => {
    const saves = [];
    let current = ai(false);
    const save = (enabled) => {
      saves.push(enabled);
      current = ai(enabled, { revision: current.revision + 1 });
      return Promise.resolve(current);
    };
    render(<ToastProvider><AiSwitchPanel load={() => Promise.resolve(current)} save={save} /></ToastProvider>);
    const toggle = await screen.findByRole('switch');
    expect(toggle).not.toBeChecked();
    await userEvent.click(toggle);
    await waitFor(() => expect(saves).toEqual([true]));
    expect(await screen.findByText(strings.admin.aiSettings.saved(true))).toBeInTheDocument();
    await userEvent.click(screen.getByRole('switch'));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: strings.admin.aiSettings.confirmOff }));
    await waitFor(() => expect(saves).toEqual([true, false]));
  });

  it('warns when the deployment setting keeps AI unavailable', async () => {
    render(<ToastProvider><AiSwitchPanel load={() => Promise.resolve(ai(true, { deploymentEnabled: false, available: false }))} save={() => Promise.resolve(null)} /></ToastProvider>);
    expect(await screen.findByText(strings.admin.aiSettings.deploymentOff)).toBeInTheDocument();
  });

  it('the admin page uses /admin/property/ai and refreshes GET /property', async () => {
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/property/ai' && call.method === 'GET') return json(ai(false));
      if (call.path === '/api/v1/admin/property/ai' && call.method === 'PUT') return json(ai(true));
      if (call.path === '/api/v1/property') return json({ ...BUILD_SITE.property, features: { ...BUILD_SITE.property.features, ai: true } });
      return undefined;
    });
    renderAdmin(<AiSettingsView />, { fetchImpl, path: '/admin/ai-settings', routePath: '/admin/ai-settings', site: BUILD_SITE });
    await userEvent.click(await screen.findByRole('switch'));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')).toBeTruthy());
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ enabled: true });
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/property')).toBe(true));
  });

  it('the sidebar always offers AI settings and Branding, even with AI off', () => {
    const ids = (property) => groupedAdminRoutes(property).flatMap((g) => g.routes.map((r) => r.id));
    expect(ids({ features: { ai: false } })).toEqual(expect.arrayContaining(['ai-settings', 'branding']));
    expect(ids({ features: { ai: false } })).not.toContain('ai-assistants');
  });
});

describe('Embed origins editor', () => {
  function Harness({ initial = '' }) {
    const [value, setValue] = useState(initial);
    return (
      <>
        <EmbedOriginsEditor value={value} onChange={setValue} />
        <output data-testid="value">{value}</output>
      </>
    );
  }

  it('adds https and local development origins, labels the local ones, and rejects other http origins', async () => {
    render(<Harness initial="https://partner.example" />);
    const s = strings.ai.admin.assistants;
    const input = screen.getByLabelText(s.embedOriginAdd);
    await userEvent.type(input, 'http://partner.example');
    await userEvent.click(screen.getByRole('button', { name: s.embedOriginAdd }));
    expect(screen.getByRole('alert')).toHaveTextContent('http://partner.example');
    await userEvent.clear(input);
    await userEvent.click(screen.getByRole('button', { name: s.embedOriginAddLocal }));
    expect(input).toHaveValue('http://localhost:');
    await userEvent.type(input, '5500{Enter}');
    expect(screen.getByTestId('value')).toHaveTextContent('https://partner.example http://localhost:5500');
    const local = screen.getByTestId('embed-origin-http://localhost:5500');
    expect(within(local).getByText(s.embedOriginLocal)).toBeInTheDocument();
    expect(within(screen.getByTestId('embed-origin-https://partner.example')).queryByText(s.embedOriginLocal)).toBeNull();
    await userEvent.type(input, 'HTTP://127.0.0.1:8080/{Enter}');
    expect(screen.getByTestId('value').textContent).toBe('https://partner.example\nhttp://localhost:5500\nhttp://127.0.0.1:8080');
    await userEvent.click(screen.getByRole('button', { name: s.embedOriginRemove('http://localhost:5500') }));
    expect(screen.getByTestId('value').textContent).toBe('https://partner.example\nhttp://127.0.0.1:8080');
  });
});
