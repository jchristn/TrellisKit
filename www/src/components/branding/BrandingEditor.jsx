import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  BrandBackgrounds, BrandColorKeys, BrandSchemes, checkPaletteContrast, contrastFieldKey, formatRatio, normalizeHex, paletteChanges
} from '@trelliskit/shared';
import strings from '../../strings.js';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { formatDateTime } from '../../utils/format.js';
import ConfirmModal from '../ConfirmModal.jsx';
import CopyableId from '../CopyableId.jsx';
import Icon from '../Icon.jsx';
import { Badge } from '../Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../States.jsx';
import MemberPresetsPanel from './MemberPresetsPanel.jsx';
import PaletteVersions from './PaletteVersions.jsx';
import PresetGallery from './PresetGallery.jsx';
import './branding.css';

const s = strings.admin.branding;

/** Neutral page colors of each scheme in the preview (the engine's, never the property's). */
const NEUTRALS = Object.freeze({
  light: { text: '#17202C', muted: '#576274', surface: '#FFFFFF', border: '#DDE2EA' },
  dark: { text: '#E6EBF2', muted: '#9AA6B8', surface: '#151C27', border: '#2A3444' }
});

function paletteOf(view) {
  return { light: { ...(view && view.light) }, dark: { ...(view && view.dark) } };
}

function overridesOf(view) {
  const out = {};
  for (const o of (view && view.overrides) || []) out[o.scheme + '|' + o.pair] = { on: true, reason: o.reason || '' };
  return out;
}

/**
 * The edited palette with invalid entries replaced by the saved value, so contrast and the preview
 * always have colors to work with; `invalid` lists the `scheme.key` entries that are not hex.
 */
export function resolveDraft(saved, draft) {
  const palette = { light: {}, dark: {} };
  const invalid = [];
  for (const scheme of BrandSchemes) {
    for (const key of BrandColorKeys) {
      const raw = draft[scheme][key];
      const value = normalizeHex(raw);
      if (!value) invalid.push(scheme + '.' + key);
      palette[scheme][key] = value || normalizeHex(saved[scheme][key]) || '#000000';
    }
  }
  return { palette, invalid };
}

/**
 * The `PUT .../branding` body for an edit: changed keys per scheme, an override for every failing
 * pair the admin ticked, and the note. Returns null when nothing would change.
 */
export function brandingUpdateBody(saved, palette, overrides, savedOverrides, note) {
  const changes = paletteChanges(saved, palette);
  const failing = checkPaletteContrast(palette).filter((c) => !c.passes);
  const contrastOverrides = failing
    .filter((c) => overrides[c.scheme + '|' + c.pair] && overrides[c.scheme + '|' + c.pair].on)
    .map((c) => {
      const reason = (overrides[c.scheme + '|' + c.pair].reason || '').trim();
      return reason ? { scheme: c.scheme, pair: c.pair, reason } : { scheme: c.scheme, pair: c.pair };
    });
  const sameOverrides = JSON.stringify(contrastOverrides.map((o) => o.scheme + o.pair + (o.reason || '')).sort())
    === JSON.stringify((savedOverrides || []).map((o) => o.scheme + o.pair + (o.reason || '')).sort());
  if (!Object.keys(changes).length && sameOverrides) return null;
  const body = { ...changes };
  if (contrastOverrides.length) body.contrastOverrides = contrastOverrides;
  if (note && note.trim()) body.note = note.trim();
  return body;
}

function previewStyle(scheme, set) {
  const n = NEUTRALS[scheme];
  return {
    '--pv-bg': BrandBackgrounds[scheme], '--pv-surface': n.surface, '--pv-text': n.text, '--pv-muted': n.muted, '--pv-border': n.border,
    '--pv-primary': set.primary, '--pv-primary-hover': set.primaryHover, '--pv-on-primary': set.onPrimary,
    '--pv-accent': set.accent, '--pv-on-accent': set.onAccent, '--pv-soft': set.accentSoft, '--pv-on-soft': set.onAccentSoft
  };
}

/** Key components (navigation, buttons, cards, chips, avatar initials, links, alerts) in one scheme. */
export function BrandPreview({ scheme, set }) {
  return (
    <figure className="brand-preview" style={previewStyle(scheme, set)} data-testid={'brand-preview-' + scheme} aria-label={s.preview + ': ' + s[scheme]}>
      <figcaption className="brand-preview-caption">{s[scheme]}</figcaption>
      <div className="brand-preview-nav" aria-label={s.previewNav}>
        <span className="brand-preview-nav-item active">{s.previewNavActive}</span>
        <span className="brand-preview-nav-item">{s.previewNavOther}</span>
      </div>
      <div className="brand-preview-row">
        <span className="brand-preview-button" role="presentation">{s.previewPrimary}</span>
        <span className="brand-preview-button hover" role="presentation">{s.keys.primaryHover.label}</span>
        <span className="brand-preview-button secondary" role="presentation">{s.previewSecondary}</span>
      </div>
      <div className="brand-preview-card">
        <div className="brand-preview-card-head">
          <span className="brand-preview-avatar" aria-hidden="true">{s.previewInitials}</span>
          <strong>{s.previewCardTitle}</strong>
        </div>
        <p>{s.previewCardBody} <span className="brand-preview-link">{s.previewLink}</span>.</p>
        <div className="brand-preview-row">
          <span className="brand-preview-chip selected">{s.previewChip}</span>
          <span className="brand-preview-chip">{s.previewChipOther}</span>
        </div>
      </div>
      <div className="brand-preview-alert" role="presentation"><Icon name="info" size={14} /> {s.previewAlert}</div>
      <div className="brand-preview-accent">{s.previewAccent}</div>
    </figure>
  );
}

function ColorField({ scheme, colorKey, value, saved, error, onChange, disabled }) {
  const valid = normalizeHex(value);
  const id = 'brand-' + scheme + '-' + colorKey;
  const meta = s.keys[colorKey];
  const changed = valid && valid !== normalizeHex(saved);
  return (
    <div className={'brand-color-field' + (changed ? ' changed' : '')}>
      <label className="label-text" htmlFor={id}>{meta.label}</label>
      <div className="brand-color-inputs">
        <input type="color" aria-label={s.pickerLabel(colorKey, scheme)} value={(valid || normalizeHex(saved) || '#000000').toLowerCase()} onChange={(e) => onChange(e.target.value.toUpperCase())} disabled={disabled} />
        <input id={id} className="mono" value={value} onChange={(e) => onChange(e.target.value)} maxLength={7} spellCheck="false" autoComplete="off"
          aria-label={s.hexLabel(colorKey, scheme)} aria-invalid={Boolean(!valid || error)} aria-describedby={id + '-hint'} disabled={disabled} />
      </div>
      <span id={id + '-hint'} className="muted small">{meta.hint}</span>
      {(!valid || error) && <span className="field-error" role="alert">{error || s.invalidHex}</span>}
    </div>
  );
}

/**
 * The Branding editor (docs/TRELLISKIT.md section 2.1), shared by the admin portal's Branding page
 * and the platform console's per-property Branding tab: color pickers for every key in light and
 * dark, a live preview of key components in both themes, the WCAG 2.1 AA contrast table with
 * per-pair overrides, presets (the property.json seed and the previous version), save, and
 * server-side revert, and (with `listVersions` and `restoreVersion`) the list of every kept version
 * with restore of any one. The engine's preset palettes (the view's `presets`) fill the editor, and
 * (with `saveMemberPresets`) the administrator chooses which presets members may pick.
 * @param {{ load: (signal?: AbortSignal) => Promise<object>, save: (body: object) => Promise<object>, revert: (to: string) => Promise<object>, onSaved?: (view: object) => void, auditLink?: React.ReactNode,
 *   listVersions?: (query: object, signal?: AbortSignal) => Promise<object>, restoreVersion?: (version: number) => Promise<object>, saveMemberPresets?: (body: object) => Promise<object> }} props
 */
function BrandingEditor({ load, save, revert, onSaved, auditLink = null, listVersions = null, restoreVersion = null, saveMemberPresets = null }) {
  const toast = useToast();
  const fetcher = useCallback((signal) => load(signal), [load]);
  const query = useApiQuery(fetcher);
  const [view, setView] = useState(null);
  const [draft, setDraft] = useState(null);
  const [overrides, setOverrides] = useState({});
  const [note, setNote] = useState('');
  const [problems, setProblems] = useState({});
  const [summary, setSummary] = useState('');
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState(null);

  const adopt = useCallback((next) => {
    setView(next);
    setDraft(paletteOf(next));
    setOverrides(overridesOf(next));
    setNote('');
    setProblems({});
    setSummary('');
  }, []);

  useEffect(() => {
    if (query.data) adopt(query.data);
  }, [query.data, adopt]);

  const saved = useMemo(() => paletteOf(view), [view]);
  const resolved = useMemo(() => (draft ? resolveDraft(saved, draft) : null), [saved, draft]);
  const checks = useMemo(() => {
    if (!resolved) return [];
    const ticked = Object.entries(overrides).filter(([, v]) => v.on).map(([k]) => ({ scheme: k.split('|')[0], pair: k.split('|')[1] }));
    return checkPaletteContrast(resolved.palette, ticked);
  }, [resolved, overrides]);

  if (!view || !draft) {
    return (
      <div className="branding-editor">
        {query.loading && <Loading />}
        {query.error && <ErrorBanner message={s.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      </div>
    );
  }

  const failing = checks.filter((c) => !c.passes && !c.overridden);
  const setColor = (scheme, key, value) => {
    setDraft((d) => ({ ...d, [scheme]: { ...d[scheme], [key]: value } }));
    setProblems({});
    setSummary('');
  };
  const setOverride = (k, patch) => setOverrides((o) => ({ ...o, [k]: { on: false, reason: '', ...o[k], ...patch } }));
  const loadPreset = (preset) => {
    setDraft(paletteOf(preset));
    setProblems({});
    setSummary('');
    toast.info(s.presetLoaded);
  };
  const usePreset = (preset) => {
    setDraft(paletteOf(preset));
    setProblems({});
    setSummary('');
    setNote(('Preset: ' + preset.name).slice(0, 200));
    toast.info(s.gallery.loaded(preset.name));
  };

  const submit = async (ev) => {
    ev.preventDefault();
    if (resolved.invalid.length) {
      setSummary(s.fixBelow);
      return;
    }
    if (failing.length) {
      setSummary(s.failingSummary(failing.length));
      return;
    }
    const body = brandingUpdateBody(saved, resolved.palette, overrides, view.overrides, note);
    if (!body) {
      toast.info(s.noChanges);
      return;
    }
    setBusy(true);
    try {
      const next = await save(body);
      adopt(next);
      toast.success(s.saved(next.version));
      if (onSaved) onSaved(next);
    } catch (err) {
      const fe = getFieldErrors(err);
      const mapped = {};
      for (const [key, message] of Object.entries(fe)) mapped[key] = Array.isArray(message) ? message.join(' ') : String(message);
      setProblems(mapped);
      setSummary(Object.keys(mapped).length ? s.fixBelow : errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const doRevert = async () => {
    const to = confirm;
    setConfirm(null);
    setBusy(true);
    try {
      const next = await revert(to);
      adopt(next);
      toast.success(s.reverted(next.version));
      if (onSaved) onSaved(next);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="branding-editor">
      <ErrorBanner message={summary} onDismiss={() => setSummary('')} />
      {view.syncsFromFolder && <Banner tone="info" icon="info">{s.folderBanner}</Banner>}

      <section className="panel" aria-labelledby="branding-status-title">
        <h2 id="branding-status-title" className="visually-hidden">{s.version}</h2>
        <dl className="kv-grid">
          <div className="kv-item"><dt>{s.version}</dt><dd className="mono">{view.version}</dd></div>
          <div className="kv-item"><dt>{s.source}</dt><dd><Badge tone="info">{s.sources[view.source] || view.source}</Badge></dd></div>
          <div className="kv-item"><dt>{s.updated}</dt><dd>{formatDateTime(view.createdUtc)}</dd></div>
          {view.createdByUserId && <div className="kv-item"><dt>{s.updatedBy}</dt><dd><CopyableId value={view.createdByUserId} /></dd></div>}
        </dl>
        <p className="muted small">{s.runtimeNote} {s.auditNote} {auditLink}</p>
        <div className="form-actions brand-actions">
          <span className="label-text">{s.presets}</span>
          <button type="button" className="button button-secondary button-sm" onClick={() => loadPreset(view.seed)} disabled={busy}>{s.presetSeed}</button>
          {view.previous && <button type="button" className="button button-secondary button-sm" onClick={() => loadPreset(view.previous)} disabled={busy}>{s.presetPrevious(view.previous.version)}</button>}
          <button type="button" className="button button-secondary button-sm" onClick={() => loadPreset(view)} disabled={busy}>{s.presetCurrent}</button>
          <span className="brand-actions-spacer" />
          <button type="button" className="button button-secondary button-sm" onClick={() => setConfirm('Previous')} disabled={busy || !view.previous}><Icon name="reply" size={14} /><span>{s.revertPrevious}</span></button>
          <button type="button" className="button button-secondary button-sm" onClick={() => setConfirm('Seed')} disabled={busy}><Icon name="refresh" size={14} /><span>{s.revertSeed}</span></button>
        </div>
      </section>

      <PresetGallery presets={view.presets} allowedIds={view.memberPresets ? view.memberPresets.allowedPresetIds : []} onUse={usePreset} disabled={busy} />

      <form className="form-stack" onSubmit={submit} noValidate aria-label={s.colors}>
        <div className="brand-grid">
          {BrandSchemes.map((scheme) => (
            <section key={scheme} className="panel brand-scheme" aria-labelledby={'brand-' + scheme + '-title'}>
              <h2 id={'brand-' + scheme + '-title'} className="section-title">{s[scheme]}</h2>
              <div className="brand-colors">
                {BrandColorKeys.map((key) => (
                  <ColorField key={key} scheme={scheme} colorKey={key} value={draft[scheme][key] || ''} saved={view[scheme][key]} error={problems[scheme + '.' + key]} onChange={(v) => setColor(scheme, key, v)} disabled={busy} />
                ))}
              </div>
              <BrandPreview scheme={scheme} set={resolved.palette[scheme]} />
            </section>
          ))}
        </div>

        <section className="panel" aria-labelledby="brand-contrast-title">
          <h2 id="brand-contrast-title" className="section-title">{s.contrast}</h2>
          <p className="muted small">{s.contrastHint}</p>
          <div className="table-frame">
            <div className="table-scroll">
              <table className="data-table brand-contrast-table">
                <thead>
                  <tr>
                    {['scheme', 'pair', 'usage', 'colors', 'ratio', 'required', 'result', 'override'].map((c) => <th key={c} scope="col">{s.columns[c]}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {checks.map((c) => {
                    const k = c.scheme + '|' + c.pair;
                    const serverError = problems[contrastFieldKey(c.scheme, c.pair)];
                    return (
                      <tr key={k} data-testid={'contrast-' + c.scheme + '-' + c.pair} className={c.passes ? '' : 'brand-contrast-failing'}>
                        <td>{s[c.scheme]}</td>
                        <td className="mono">{c.foreground} / {c.background === 'background' ? s.page : c.background}</td>
                        <td>{c.usage}{serverError && <span className="field-error" role="alert">{serverError}</span>}</td>
                        <td>
                          <span className="brand-swatch-pair" style={{ background: c.backgroundColor, color: c.foregroundColor }} aria-label={c.foregroundColor + ' on ' + c.backgroundColor}>Aa</span>
                          <span className="mono small"> {c.foregroundColor} / {c.backgroundColor}</span>
                        </td>
                        <td className="mono">{formatRatio(c.ratio)}</td>
                        <td className="mono">{c.required.toFixed(1)}:1</td>
                        <td>{c.passes ? <Badge tone="success">{s.pass}</Badge> : c.overridden ? <Badge tone="warning">{s.overridden}</Badge> : <Badge tone="danger">{s.fail}</Badge>}</td>
                        <td>
                          {!c.passes && (
                            <div className="brand-override">
                              <label className="check-field">
                                <input type="checkbox" checked={Boolean(overrides[k] && overrides[k].on)} onChange={(e) => setOverride(k, { on: e.target.checked })} disabled={busy} aria-label={s.overrideLabel(c.scheme + ' ' + c.pair)} />
                                <span className="small">{s.overrideLabel('')}</span>
                              </label>
                              {overrides[k] && overrides[k].on && (
                                <input aria-label={s.overrideReason} placeholder={s.overrideReason} value={overrides[k].reason || ''} maxLength={200} onChange={(e) => setOverride(k, { reason: e.target.value })} disabled={busy} />
                              )}
                            </div>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
          {failing.length > 0 && <Banner tone="warning" icon="alert">{s.failingSummary(failing.length)}</Banner>}
        </section>

        <section className="panel form-stack">
          <label className="field" htmlFor="brand-note">
            <span className="label-text">{s.note}</span>
            <input id="brand-note" value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} disabled={busy} />
            {problems.note && <span className="field-error" role="alert">{problems.note}</span>}
          </label>
          <div className="form-actions">
            <button type="button" className="button button-secondary" onClick={() => adopt(view)} disabled={busy}>{strings.common.cancel}</button>
            <button type="submit" className="button button-primary" disabled={busy}>{busy ? s.saving : s.save}</button>
          </div>
        </section>
      </form>

      {saveMemberPresets && Array.isArray(view.presets) && view.presets.length > 0 && (
        <MemberPresetsPanel
          key={'members-' + ((view.memberPresets && view.memberPresets.revision) || 0)}
          presets={view.presets}
          memberPresets={view.memberPresets}
          save={saveMemberPresets}
          disabled={busy}
          onSaved={(next) => {
            setView((v) => ({ ...v, presets: next.presets, memberPresets: next.memberPresets }));
            if (onSaved) onSaved(next);
          }}
        />
      )}

      {listVersions && restoreVersion && (
        <PaletteVersions
          key={view.version}
          list={listVersions}
          restore={restoreVersion}
          currentVersion={view.version}
          disabled={busy}
          onRestored={(next, from) => {
            adopt(next);
            toast.success(s.versions.restored(from, next.version));
            if (onSaved) onSaved(next);
          }}
        />
      )}

      <ConfirmModal
        open={Boolean(confirm)}
        title={s.revertTitle}
        message={confirm === 'Seed' ? s.revertSeedBody : s.revertPreviousBody(view.previous ? view.previous.version : '')}
        confirmLabel={confirm === 'Seed' ? s.revertSeed : s.revertPrevious}
        danger={false}
        onCancel={() => setConfirm(null)}
        onConfirm={doRevert}
      />
    </div>
  );
}

export default BrandingEditor;
