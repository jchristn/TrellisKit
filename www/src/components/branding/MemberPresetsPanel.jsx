import { useState } from 'react';
import { MemberPaletteMode, memberPresetsBody } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useToast } from '../../context/ToastContext.jsx';
import { errorMessage } from '../../utils/api.js';
import { ErrorBanner } from '../States.jsx';

const s = strings.admin.branding.members;

function initialIds(memberPresets, presets) {
  if (memberPresets && memberPresets.mode === MemberPaletteMode.Selected) return memberPresets.presetIds || [];
  return presets.map((p) => p.id);
}

/**
 * Which preset palettes members may choose in Settings, Appearance (docs/TRELLISKIT.md section
 * 2.1): every preset, none (the property palette only), or a selected subset, saved with
 * `PUT .../branding/member-presets` (audited). The property palette is always the members' default.
 * @param {{ presets: Array<{ id: string, name: string }>, memberPresets: object, save: (body: object) => Promise<object>, onSaved: (view: object) => void, disabled?: boolean }} props
 */
function MemberPresetsPanel({ presets, memberPresets, save, onSaved, disabled = false }) {
  const toast = useToast();
  const [mode, setMode] = useState((memberPresets && memberPresets.mode) || MemberPaletteMode.All);
  const [ids, setIds] = useState(() => initialIds(memberPresets, presets));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const allowed = memberPresets && Array.isArray(memberPresets.allowedPresetIds) ? memberPresets.allowedPresetIds.length : presets.length;

  const toggle = (id, on) => setIds((list) => (on ? [...list.filter((x) => x !== id), id] : list.filter((x) => x !== id)));

  const submit = async (ev) => {
    ev.preventDefault();
    if (mode === MemberPaletteMode.Selected && ids.length === 0) {
      setError(s.pickOne);
      return;
    }
    setBusy(true);
    setError('');
    try {
      // Catalog order, so the stored list reads like the gallery.
      const ordered = presets.map((p) => p.id).filter((id) => ids.includes(id));
      const next = await save(memberPresetsBody(mode, ordered));
      toast.success(s.saved);
      onSaved(next);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const off = disabled || busy;
  return (
    <section className="panel" aria-labelledby="brand-members-title">
      <h2 id="brand-members-title" className="section-title">{s.title}</h2>
      <p className="muted small">{s.hint}</p>
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      <form className="form-stack" onSubmit={submit} noValidate>
        <fieldset className="radio-list" disabled={off}>
          <legend className="label-text">{s.modeLegend}</legend>
          {[MemberPaletteMode.All, MemberPaletteMode.Selected, MemberPaletteMode.None].map((m) => (
            <label key={m} className="radio-row">
              <input type="radio" name="member-palette-mode" value={m} checked={mode === m} onChange={() => setMode(m)} />
              <span>
                <span className="radio-label">{s.modes[m].label}</span>
                <span className="muted small">{s.modes[m].description}</span>
              </span>
            </label>
          ))}
        </fieldset>
        {mode === MemberPaletteMode.Selected && (
          <fieldset className="member-presets-list" disabled={off}>
            <legend className="label-text">{s.presetsLegend}</legend>
            <div className="form-actions">
              <button type="button" className="button button-secondary button-sm" onClick={() => setIds(presets.map((p) => p.id))}>{s.selectAll}</button>
              <button type="button" className="button button-secondary button-sm" onClick={() => setIds([])}>{s.selectNone}</button>
            </div>
            <div className="member-presets-grid">
              {presets.map((p) => (
                <label key={p.id} className="check-field">
                  <input type="checkbox" checked={ids.includes(p.id)} onChange={(e) => toggle(p.id, e.target.checked)} data-testid={'member-preset-' + p.id} />
                  <span>{p.name}</span>
                </label>
              ))}
            </div>
          </fieldset>
        )}
        <div className="form-actions">
          <span className="muted small">{s.allowedCount(allowed)}</span>
          <button type="submit" className="button button-primary" disabled={off}>{busy ? s.saving : s.save}</button>
        </div>
      </form>
    </section>
  );
}

export default MemberPresetsPanel;
