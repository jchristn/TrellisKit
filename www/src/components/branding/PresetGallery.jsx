import strings from '../../strings.js';
import { Badge } from '../Badges.jsx';
import PaletteSwatches from './PaletteSwatches.jsx';

const s = strings.admin.branding.gallery;

/**
 * The engine's preset palettes on the Branding page (docs/TRELLISKIT.md section 2.1): a card per
 * preset with its light and dark preview. "Use this palette" fills the editor; saving it stores an
 * ordinary new palette version, so revert and the version history keep working.
 * @param {{ presets: Array<{ id: string, name: string, description: string, light: object, dark: object }>, allowedIds?: string[], onUse: (preset: object) => void, disabled?: boolean }} props
 */
function PresetGallery({ presets, allowedIds = [], onUse, disabled = false }) {
  if (!Array.isArray(presets) || presets.length === 0) return null;
  const allowed = new Set(allowedIds);
  return (
    <section className="panel" aria-labelledby="brand-gallery-title">
      <h2 id="brand-gallery-title" className="section-title">{s.title}</h2>
      <p className="muted small">{s.hint}</p>
      <ul className="preset-gallery">
        {presets.map((preset) => (
          <li key={preset.id} className="preset-card" data-testid={'preset-' + preset.id}>
            <PaletteSwatches palette={preset} label={s.swatchesLabel(preset.name, strings.admin.branding.light + ' / ' + strings.admin.branding.dark)} />
            <div className="preset-card-text">
              <strong>{preset.name}</strong>
              <span className="muted small">{preset.description}</span>
              {allowed.has(preset.id) && <Badge tone="info">{s.memberAllowed}</Badge>}
            </div>
            <button type="button" className="button button-secondary button-sm" onClick={() => onUse(preset)} disabled={disabled} aria-label={s.useLabel(preset.name)}>{s.use}</button>
          </li>
        ))}
      </ul>
    </section>
  );
}

export default PresetGallery;
