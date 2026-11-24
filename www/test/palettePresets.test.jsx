import { describe, expect, it, beforeEach } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { brandPalette, checkPaletteContrast } from '@trelliskit/shared';
import strings from '../src/strings.js';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { PropertyProvider, useRuntimeProperty } from '../src/context/PropertyContext.jsx';
import { RUNTIME_PALETTE_STYLE_ID } from '../src/property/theme.js';
import { PALETTE_PRESET_STORAGE_KEY, getPalettePreference, resetPalettePreference, setPalettePreference } from '../src/property/palettePreference.js';
import BrandingEditor from '../src/components/branding/BrandingEditor.jsx';
import { ToastProvider } from '../src/context/ToastContext.jsx';
import AppearanceSettingsView from '../src/views/member/AppearanceSettingsView.jsx';
import { fakeMemberFetch, renderPhase5, siteWith } from './phase5Helpers.jsx';
import catalogDocument from '../../backend/src/TrellisKit.Core/Assets/palette-presets.json';

// The engine catalog is one file (the backend's embedded asset); clients get it from the API.
const catalog = catalogDocument.presets;
const ocean = catalog.find((p) => p.id === 'ocean');
const graphite = catalog.find((p) => p.id === 'graphite');
const seed = brandPalette(BUILD_SITE.property);
const g = strings.admin.branding.gallery;
const m = strings.admin.branding.members;

function brandingView(extra = {}) {
  return {
    propertyId: BUILD_SITE.property.id, version: 2, source: 'Edit', note: null,
    light: { ...seed.light }, dark: { ...seed.dark }, keys: Object.keys(seed.light),
    backgrounds: { light: '#FFFFFF', dark: '#0E141D' }, contrast: checkPaletteContrast(seed), overrides: [],
    seed: { version: null, light: { ...seed.light }, dark: { ...seed.dark } }, previous: null,
    presets: catalog, memberPresets: { mode: 'All', presetIds: [], allowedPresetIds: catalog.map((p) => p.id), revision: 0 },
    syncsFromFolder: false, createdByUserId: null, createdUtc: '2026-11-12T00:00:00Z',
    ...extra
  };
}

beforeEach(() => {
  window.localStorage.clear();
  resetPalettePreference();
  const old = document.getElementById(RUNTIME_PALETTE_STYLE_ID);
  if (old) old.remove();
});

describe('member palette preference', () => {
  it('stores a valid preset id, clears with null, and ignores invalid ids', () => {
    setPalettePreference('ocean');
    expect(getPalettePreference()).toBe('ocean');
    expect(window.localStorage.getItem(PALETTE_PRESET_STORAGE_KEY)).toBe('ocean');
    setPalettePreference('Not An Id');
    expect(getPalettePreference()).toBeNull();
    expect(window.localStorage.getItem(PALETTE_PRESET_STORAGE_KEY)).toBeNull();
  });

  it('PropertyProvider applies an allowed preset live and falls back to the property palette when it is not offered', async () => {
    const runtime = { ...BUILD_SITE.property, brand: { ...BUILD_SITE.property.brand, colors: seed, presets: [ocean] } };
    const client = { property: () => Promise.resolve(runtime) };
    function Probe() {
      useRuntimeProperty(client);
      return null;
    }
    render(<PropertyProvider site={BUILD_SITE}><Probe /></PropertyProvider>);
    await waitFor(() => expect(document.getElementById(RUNTIME_PALETTE_STYLE_ID)).not.toBeNull());
    const style = () => document.getElementById(RUNTIME_PALETTE_STYLE_ID).textContent;
    expect(style()).toContain('--brand-primary: ' + seed.light.primary.toLowerCase());
    act(() => setPalettePreference('ocean'));
    await waitFor(() => expect(style()).toContain('--brand-primary: ' + ocean.light.primary.toLowerCase()));
    expect(style()).toContain('--color-accent-soft: ' + ocean.dark.accentSoft.toLowerCase());
    act(() => setPalettePreference('graphite'));
    await waitFor(() => expect(style()).toContain('--brand-primary: ' + seed.light.primary.toLowerCase()));
  });
});

describe('Branding page presets', () => {
  it('a preset fills the editor with its colors and a note; saving stores it as an ordinary edit', async () => {
    const saves = [];
    const save = async (body) => {
      saves.push(body);
      return brandingView({ version: 3, light: { ...ocean.light }, dark: { ...ocean.dark }, contrast: checkPaletteContrast(ocean) });
    };
    render(<ToastProvider><BrandingEditor load={async () => brandingView()} save={save} revert={async () => brandingView()} /></ToastProvider>);
    const card = await screen.findByTestId('preset-ocean');
    expect(within(card).getByText(ocean.name)).toBeInTheDocument();
    expect(within(card).getByText(g.memberAllowed)).toBeInTheDocument();
    await userEvent.click(within(card).getByRole('button', { name: g.useLabel(ocean.name) }));
    expect(screen.getByLabelText(strings.admin.branding.note)).toHaveValue('Preset: ' + ocean.name);
    await userEvent.click(screen.getByRole('button', { name: strings.admin.branding.save }));
    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].light.primary).toBe(ocean.light.primary.toUpperCase());
    expect(saves[0].dark.accent).toBe(ocean.dark.accent.toUpperCase());
    expect(saves[0].contrastOverrides).toBeUndefined();
    expect(saves[0].note).toBe('Preset: ' + ocean.name);
  });

  it('the member choices panel saves All, None, or a selected subset in catalog order', async () => {
    const bodies = [];
    const saveMemberPresets = async (body) => {
      bodies.push(body);
      const allowed = body.mode === 'Selected' ? body.presetIds : body.mode === 'None' ? [] : catalog.map((p) => p.id);
      return brandingView({ memberPresets: { mode: body.mode, presetIds: body.presetIds || [], allowedPresetIds: allowed, revision: bodies.length } });
    };
    render(<ToastProvider><BrandingEditor load={async () => brandingView()} save={async () => brandingView()} revert={async () => brandingView()} saveMemberPresets={saveMemberPresets} /></ToastProvider>);
    await screen.findByText(m.title);
    await userEvent.click(screen.getByRole('radio', { name: new RegExp(m.modes.Selected.label) }));
    await userEvent.click(screen.getByRole('button', { name: m.selectNone }));
    await userEvent.click(screen.getByRole('button', { name: m.save }));
    expect(await screen.findByText(m.pickOne)).toBeInTheDocument();
    expect(bodies).toHaveLength(0);
    await userEvent.click(screen.getByTestId('member-preset-ocean'));
    await userEvent.click(screen.getByTestId('member-preset-' + graphite.id));
    await userEvent.click(screen.getByRole('button', { name: m.save }));
    await waitFor(() => expect(bodies).toHaveLength(1));
    const order = catalog.map((p) => p.id).filter((id) => id === 'ocean' || id === graphite.id);
    expect(bodies[0]).toEqual({ mode: 'Selected', presetIds: order });
    await screen.findByText(m.allowedCount(2));
    await userEvent.click(screen.getByRole('radio', { name: new RegExp(m.modes.None.label) }));
    await userEvent.click(screen.getByRole('button', { name: m.save }));
    await waitFor(() => expect(bodies).toHaveLength(2));
    expect(bodies[1]).toEqual({ mode: 'None' });
  });
});

describe('Appearance settings (member)', () => {
  it('lists the property palette and the allowed presets, saves the choice to the account, and applies it locally', async () => {
    let stored = 'graphite';
    const fake = {
      handle: (call) => {
        if (call.path !== '/api/v1/me/appearance') return undefined;
        if (call.method === 'PUT') stored = call.body.palettePreset;
        return { status: 200, body: { palettePreset: stored, effectivePreset: stored === 'graphite' ? null : stored, presetsAvailable: true } };
      }
    };
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    const site = siteWith({ brand: { ...BUILD_SITE.property.brand, presets: [ocean] } });
    await renderPhase5(<AppearanceSettingsView />, { fetchImpl, site, path: '/app/settings/appearance', routePath: '/app/settings/appearance' });
    const a = strings.appearance;
    expect(await screen.findByText(a.disallowed)).toBeInTheDocument();
    const fallback = within(screen.getByTestId('appearance-default')).getByRole('radio');
    expect(fallback).toBeChecked();
    expect(screen.queryByTestId('appearance-graphite')).toBeNull();
    await userEvent.click(within(screen.getByTestId('appearance-ocean')).getByRole('radio'));
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT' && c.path === '/api/v1/me/appearance').at(-1).body).toEqual({ palettePreset: 'ocean' }));
    await waitFor(() => expect(getPalettePreference()).toBe('ocean'));
    expect(within(screen.getByTestId('appearance-ocean')).getByRole('radio')).toBeChecked();
    await userEvent.click(fallback);
    await waitFor(() => expect(calls.filter((c) => c.method === 'PUT').at(-1).body).toEqual({ palettePreset: null }));
    await waitFor(() => expect(getPalettePreference()).toBeNull());
  });
});
