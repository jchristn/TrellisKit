import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { brandPalette, palettePresets, resolvePalettePreset } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import Icon from '../../components/Icon.jsx';
import PaletteSwatches from '../../components/branding/PaletteSwatches.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import { setPalettePreference, usePalettePreference } from '../../property/palettePreference.js';

const s = strings.appearance;
const DEFAULT = '';

/**
 * /app/settings/appearance (docs/TRELLISKIT.md section 2.1): the member picks the property palette
 * (the default) or one of the preset palettes the community allows (`GET /api/v1/property`
 * `brand.presets`). The choice is saved to the account with `PUT /me/appearance` and applies at
 * once in light and dark; a preset the administrator later removes falls back to the property
 * palette. The logo and icon never change.
 */
function AppearanceSettingsView() {
  const { client } = useAuth();
  const { property, fill } = useProperty();
  const toast = useToast();
  useDocumentMeta(s.meta, s.summary);
  const fetcher = useCallback((signal) => client.getAppearance(signal), [client]);
  const query = useApiQuery(fetcher);
  const local = usePalettePreference();
  const [busy, setBusy] = useState(false);
  const presets = palettePresets(property);
  // The account copy wins over this browser's copy once it loads.
  const loaded = query.data;
  useEffect(() => {
    if (loaded && typeof loaded === 'object') setPalettePreference(loaded.palettePreset || null);
  }, [loaded]);
  const stored = local;
  const applied = resolvePalettePreset(property, stored);
  const selected = applied ? applied.id : DEFAULT;

  const choose = async (presetId) => {
    if (busy || presetId === selected) return;
    const before = local;
    setPalettePreference(presetId || null);
    setBusy(true);
    try {
      const saved = await client.updateAppearance(presetId || null);
      setPalettePreference(saved && typeof saved === 'object' ? saved.palettePreset || null : presetId || null);
      toast.success(s.saved);
    } catch {
      setPalettePreference(before);
      toast.error(s.saveError);
    } finally {
      setBusy(false);
    }
  };

  const defaultName = fill(s.propertyDefault);
  return (
    <div className="member-view settings-view">
      <Link to="/app/profile" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{s.back}</span></Link>
      <header className="page-header">
        <div className="page-header-text">
          <h1>{s.title}</h1>
          <p className="page-summary">{s.summary}</p>
        </div>
      </header>
      {query.loading && !query.data && <Loading />}
      <ErrorBanner message={query.error && !query.data ? s.loadError : ''} onRetry={query.reload} />
      {stored && !applied && <Banner tone="info" icon="info">{s.disallowed}</Banner>}
      <section className="panel" aria-labelledby="appearance-palette-title">
        <h2 id="appearance-palette-title">{s.paletteTitle}</h2>
        <p className="muted small">{s.paletteHint}</p>
        {presets.length === 0 && <p className="muted">{s.noPresets}</p>}
        <fieldset className="radio-list" disabled={busy}>
          <legend className="visually-hidden">{s.paletteTitle}</legend>
          <ul className="appearance-options">
            <li>
              <label className="appearance-option" data-testid="appearance-default">
                <span className="appearance-option-head">
                  <input type="radio" name="palette" value={DEFAULT} checked={selected === DEFAULT} onChange={() => choose(DEFAULT)} />
                  <strong>{defaultName}</strong>
                </span>
                <PaletteSwatches palette={brandPalette(property)} label={s.swatchesLabel(defaultName)} />
                <span className="muted small">{s.propertyDefaultBody}</span>
              </label>
            </li>
            {presets.map((preset) => (
              <li key={preset.id}>
                <label className="appearance-option" data-testid={'appearance-' + preset.id}>
                  <span className="appearance-option-head">
                    <input type="radio" name="palette" value={preset.id} checked={selected === preset.id} onChange={() => choose(preset.id)} />
                    <strong>{preset.name}</strong>
                  </span>
                  <PaletteSwatches palette={preset} label={s.swatchesLabel(preset.name)} />
                  {preset.description && <span className="muted small">{preset.description}</span>}
                </label>
              </li>
            ))}
          </ul>
        </fieldset>
      </section>
    </div>
  );
}

export default AppearanceSettingsView;
