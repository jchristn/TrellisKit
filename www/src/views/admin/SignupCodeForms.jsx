import { useState } from 'react';
import { isSignupCode, metadataForSave, normalizeSignupCode } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { browser } from '../../utils/browser.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { toCsv } from '../../utils/csv.js';
import { optionsFrom } from '../../utils/labels.js';
import Modal from '../../components/Modal.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import { Banner, ErrorBanner } from '../../components/States.jsx';

const f = strings.admin.codes.form;
const b = strings.admin.codes.batch;
const en = strings.enums;

function pad(n) {
  return String(n).padStart(2, '0');
}

/** ISO timestamp to a local YYYY-MM-DD for date inputs. */
export function isoToDateInput(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
}

/** Local date input to an ISO timestamp at the start (00:00:00) or end (23:59:59) of that day. */
export function dateInputToIso(value, endOfDay = false) {
  if (!value) return null;
  const d = new Date(value + (endOfDay ? 'T23:59:59' : 'T00:00:00'));
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

/** Initial form values from an existing code (edit and duplicate) or defaults (create). */
export function codeFormValues(code, mode) {
  const c = code || {};
  return {
    code: mode === 'edit' ? (c.code || '') : '',
    name: c.name || '',
    description: c.description || '',
    notes: c.notes || '',
    grantType: c.grantType || 'DurationMonths',
    grantMonths: c.grantMonths ? String(c.grantMonths) : '12',
    grantEnd: isoToDateInput(c.grantEndUtc),
    ...discountFormValues(c),
    maxRedemptions: c.maxRedemptions === null || c.maxRedemptions === undefined ? '' : String(c.maxRedemptions),
    starts: isoToDateInput(c.startsUtc),
    expires: isoToDateInput(c.expiresUtc),
    metadata: { labels: Array.isArray(c.labels) ? c.labels : (Array.isArray(c.tags) ? c.tags : []), tags: c.tags && typeof c.tags === 'object' && !Array.isArray(c.tags) ? c.tags : {} },
    isActive: c.isActive === undefined ? true : Boolean(c.isActive)
  };
}

/** Discount form values (kind, value as typed, duration) from a code or batch. */
export function discountFormValues(c) {
  const amount = c && c.discountAmountCents !== null && c.discountAmountCents !== undefined;
  return {
    discountKind: amount ? 'amount' : 'percent',
    discountValue: amount ? (c.discountAmountCents / 100).toFixed(2) : (c && c.discountPercent ? String(c.discountPercent) : '20'),
    discountDuration: (c && c.discountDuration) || 'Once'
  };
}

/**
 * Labels and tags for a code body (docs/ENUMERATION_AND_METADATA.md): from the metadata editor,
 * or from the older comma-separated text field (which becomes labels).
 */
export function metadataBody(values) {
  if (values.metadata) return metadataForSave(values.metadata, { isStaff: true });
  return { labels: parseTags(values.tags || '') };
}

/** Parse comma-separated tags. */
export function parseTags(text) {
  return String(text || '').split(',').map((t) => t.trim()).filter(Boolean);
}

function validateGrant(values, errors, today) {
  if (values.grantType === 'DurationMonths') {
    const n = Number(values.grantMonths);
    if (!Number.isInteger(n) || n < 1 || n > 120) errors.grantMonths = f.monthsInvalid;
  }
  if (values.grantType === 'FixedEndDate') {
    const iso = dateInputToIso(values.grantEnd, true);
    if (!iso || new Date(iso) <= today) errors.grantEnd = f.grantEndInvalid;
  }
  if (values.grantType === 'Discount') {
    const n = Number(values.discountValue);
    if (values.discountKind === 'amount') {
      if (!/^\d+(\.\d{1,2})?$/.test(String(values.discountValue || '').trim()) || n <= 0 || n > 10000) errors.discountValue = f.discountAmountInvalid;
    } else if (!Number.isInteger(n) || n < 1 || n > 100) errors.discountValue = f.discountPercentInvalid;
  }
}

/**
 * Validate the single-code form.
 * @param {object} values
 * @param {{ mode?: 'create'|'edit'|'duplicate', locked?: boolean, redemptionCount?: number, today?: Date }} [options]
 */
export function validateCodeForm(values, { mode = 'create', locked = false, redemptionCount = 0, today = new Date() } = {}) {
  const errors = {};
  // On create and duplicate a blank code means the server generates one; on edit the code is required.
  if (!locked && (mode === 'edit' || values.code.trim()) && !isSignupCode(values.code)) errors.code = f.codeHint;
  if (!values.name.trim()) errors.name = f.nameRequired;
  if (!locked) validateGrant(values, errors, today);
  if (values.maxRedemptions.trim()) {
    const n = Number(values.maxRedemptions);
    if (!Number.isInteger(n) || n < 1) errors.maxRedemptions = f.maxInvalid;
    else if (mode === 'edit' && n < redemptionCount) errors.maxRedemptions = f.maxBelowCount(redemptionCount);
  }
  const starts = dateInputToIso(values.starts);
  const expires = dateInputToIso(values.expires, true);
  if (starts && expires && new Date(starts) >= new Date(expires)) errors.expires = f.rangeInvalid;
  return errors;
}

/** Grant (and discount) fields of a code or batch body. */
export function grantFields(values) {
  const body = { grantType: values.grantType, grantMonths: null, grantEndUtc: null };
  if (values.grantType === 'DurationMonths') body.grantMonths = Number(values.grantMonths);
  if (values.grantType === 'FixedEndDate') body.grantEndUtc = dateInputToIso(values.grantEnd, true);
  // The server clears the discount when the grant type changes, so other grants omit these fields.
  if (values.grantType === 'Discount') {
    const amount = values.discountKind === 'amount';
    body.discountPercent = amount ? null : Number(values.discountValue);
    body.discountAmountCents = amount ? Math.round(Number(values.discountValue) * 100) : null;
    body.discountDuration = values.discountDuration || 'Once';
  }
  return body;
}

/**
 * Request body for POST (create) or PUT (edit) /admin/signup-codes. Locked codes (already
 * redeemed) never send the code string or grant fields.
 */
export function buildCodeBody(values, { mode = 'create', locked = false } = {}) {
  const body = {
    name: values.name.trim(),
    description: values.description.trim() || null,
    notes: values.notes.trim() || null,
    maxRedemptions: values.maxRedemptions.trim() ? Number(values.maxRedemptions) : null,
    startsUtc: dateInputToIso(values.starts),
    expiresUtc: dateInputToIso(values.expires, true),
    ...metadataBody(values)
  };
  if (!locked) {
    Object.assign(body, grantFields(values));
    if (values.code.trim()) body.code = normalizeSignupCode(values.code);
  }
  if (mode === 'edit') body.isActive = values.isActive;
  return body;
}

function GrantInputs({ values, set, errors, disabled, idPrefix }) {
  return (
    <div className="form-grid">
      <div className="field">
        <label className="label-text" htmlFor={idPrefix + '-grant'}>{f.grantType}</label>
        <select id={idPrefix + '-grant'} value={values.grantType} onChange={set('grantType')} disabled={disabled}>
          {optionsFrom({ ...en.grantType, Discount: f.discountOption }).map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      </div>
      {values.grantType === 'Discount' && (
        <>
          <div className="field">
            <label className="label-text" htmlFor={idPrefix + '-discount-kind'}>{f.discountKind}</label>
            <select id={idPrefix + '-discount-kind'} value={values.discountKind} onChange={set('discountKind')} disabled={disabled}>
              <option value="percent">{f.discountPercent}</option>
              <option value="amount">{f.discountAmount}</option>
            </select>
          </div>
          <div className="field">
            <label className="label-text" htmlFor={idPrefix + '-discount-value'}>{values.discountKind === 'amount' ? f.discountAmountLabel : f.discountPercentLabel}</label>
            <input id={idPrefix + '-discount-value'} type="number" min={values.discountKind === 'amount' ? '0.01' : '1'} max={values.discountKind === 'amount' ? '10000' : '100'} step={values.discountKind === 'amount' ? '0.01' : '1'} value={values.discountValue} onChange={set('discountValue')} disabled={disabled} aria-invalid={Boolean(errors.discountValue)} />
            {errors.discountValue && <span className="field-error">{errors.discountValue}</span>}
          </div>
          <div className="field">
            <label className="label-text" htmlFor={idPrefix + '-discount-duration'}>{f.discountDuration}</label>
            <select id={idPrefix + '-discount-duration'} value={values.discountDuration} onChange={set('discountDuration')} disabled={disabled}>
              <option value="Once">{f.discountOnce}</option>
              <option value="Forever">{f.discountForever}</option>
            </select>
          </div>
          <p className="muted small form-grid-full">{f.discountHint}</p>
        </>
      )}
      {values.grantType === 'DurationMonths' && (
        <div className="field">
          <label className="label-text" htmlFor={idPrefix + '-months'}>{f.months}</label>
          <input id={idPrefix + '-months'} type="number" min="1" max="120" value={values.grantMonths} onChange={set('grantMonths')} disabled={disabled} aria-invalid={Boolean(errors.grantMonths)} />
          {errors.grantMonths && <span className="field-error">{errors.grantMonths}</span>}
        </div>
      )}
      {values.grantType === 'FixedEndDate' && (
        <div className="field">
          <label className="label-text" htmlFor={idPrefix + '-grant-end'}>{f.grantEnd}</label>
          <input id={idPrefix + '-grant-end'} type="date" value={values.grantEnd} onChange={set('grantEnd')} disabled={disabled} aria-invalid={Boolean(errors.grantEnd)} />
          {errors.grantEnd && <span className="field-error">{errors.grantEnd}</span>}
        </div>
      )}
    </div>
  );
}

/**
 * Create, edit, or duplicate a single sign-up code. The grant is a free membership or (Discount) a
 * discount at web checkout. After the first redemption the code string, grant, and discount are
 * locked (contract: `409 CodeLocked`).
 *
 * @param {{ mode: 'create'|'edit'|'duplicate', code?: object, onClose: () => void, onSaved: (code: object) => void }} props
 */
export function SignupCodeFormModal({ mode, code, onClose, onSaved }) {
  const { api, client } = useAdminAuth();
  const [values, setValues] = useState(() => codeFormValues(code, mode));
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const redemptionCount = (code && mode === 'edit' && code.redemptionCount) || 0;
  const locked = mode === 'edit' && redemptionCount > 0;

  const set = (key) => (e) => {
    const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
    setValues((v) => ({ ...v, [key]: value }));
  };

  const submit = async (e) => {
    e.preventDefault();
    const found = validateCodeForm(values, { mode, locked, redemptionCount });
    setErrors(found);
    setError(Object.keys(found).length ? f.fixBelow : '');
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const body = buildCodeBody(values, { mode, locked });
      const saved = mode === 'edit' ? await api.updateSignupCode(code.id, body) : await api.createSignupCode(body);
      onSaved(saved, mode);
    } catch (err) {
      const fe = getFieldErrors(err);
      const mapped = {};
      for (const [k, v] of Object.entries(fe)) mapped[k === 'grantEndUtc' ? 'grantEnd' : k === 'expiresUtc' ? 'expires' : (k === 'discountPercent' || k === 'discountAmountCents') ? 'discountValue' : k] = Array.isArray(v) ? v.join(' ') : String(v);
      if (err && err.code === 'CodeInUse') mapped.code = f.codeInUse;
      setErrors(mapped);
      setError(err && err.code === 'CodeLocked' ? f.codeLocked : (Object.keys(mapped).length ? f.fixBelow : errorMessage(err, strings.common.errorGeneric)));
      setBusy(false);
    }
  };

  const title = mode === 'edit' ? f.editTitle(code.code) : (mode === 'duplicate' ? f.duplicateTitle : f.createTitle);
  return (
    <Modal
      open
      onClose={busy ? () => {} : onClose}
      title={title}
      size="md"
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="code-form" className="button button-primary" disabled={busy}>{busy ? f.saving : (mode === 'edit' ? f.save : f.create)}</button>
        </>
      )}
    >
      <form id="code-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        {locked && <Banner tone="info" icon="lock">{f.locked}</Banner>}
        <div className="field">
          <label className="label-text" htmlFor="code-code">{f.code}</label>
          <input id="code-code" type="text" className="mono" maxLength={40} spellCheck="false" autoComplete="off" placeholder={mode === 'edit' ? undefined : f.codePlaceholder} value={values.code} onChange={set('code')} disabled={busy || locked} aria-invalid={Boolean(errors.code)} aria-describedby="code-code-hint" />
          <span id="code-code-hint" className="muted small">{mode === 'edit' ? f.codeHint : f.codeHintOptional}</span>
          {errors.code && <span className="field-error">{errors.code}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="code-name">{f.name}</label>
          <input id="code-name" type="text" maxLength={100} value={values.name} onChange={set('name')} disabled={busy} aria-invalid={Boolean(errors.name)} aria-describedby="code-name-hint" />
          <span id="code-name-hint" className="muted small">{f.nameHint}</span>
          {errors.name && <span className="field-error">{errors.name}</span>}
        </div>
        <div className="field">
          <label className="label-text" htmlFor="code-description">{f.description}</label>
          <textarea id="code-description" rows={2} value={values.description} onChange={set('description')} disabled={busy} aria-describedby="code-description-hint" />
          <span id="code-description-hint" className="muted small">{f.descriptionHint}</span>
        </div>
        <GrantInputs values={values} set={set} errors={errors} disabled={busy || locked} idPrefix="code" />
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="code-max">{f.maxRedemptions}</label>
            <input id="code-max" type="number" min={Math.max(1, redemptionCount)} value={values.maxRedemptions} onChange={set('maxRedemptions')} disabled={busy} aria-invalid={Boolean(errors.maxRedemptions)} aria-describedby="code-max-hint" />
            <span id="code-max-hint" className="muted small">{f.maxHint}</span>
            {errors.maxRedemptions && <span className="field-error">{errors.maxRedemptions}</span>}
          </div>
          <div className="field">
            <label className="label-text" htmlFor="code-starts">{f.starts}</label>
            <input id="code-starts" type="date" value={values.starts} onChange={set('starts')} disabled={busy} />
          </div>
          <div className="field">
            <label className="label-text" htmlFor="code-expires">{f.expires}</label>
            <input id="code-expires" type="date" value={values.expires} onChange={set('expires')} disabled={busy} aria-invalid={Boolean(errors.expires)} />
            {errors.expires && <span className="field-error">{errors.expires}</span>}
          </div>
        </div>
        <MetadataEditor value={values.metadata} onChange={(metadata) => setValues((v) => ({ ...v, metadata }))} isStaff client={client} targetType="SignupCode" disabled={busy} />
        <div className="field">
          <label className="label-text" htmlFor="code-notes">{f.notes}</label>
          <textarea id="code-notes" rows={2} value={values.notes} onChange={set('notes')} disabled={busy} aria-describedby="code-notes-hint" />
          <span id="code-notes-hint" className="muted small">{f.notesHint}</span>
        </div>
        {mode === 'edit' && (
          <label className="toggle">
            <input type="checkbox" checked={values.isActive} onChange={set('isActive')} disabled={busy} />
            <span className="toggle-track"><span className="toggle-thumb" /></span>
            <span>{f.active}</span>
          </label>
        )}
      </form>
    </Modal>
  );
}

/** Validate the batch form. */
export function validateBatchForm(values, today = new Date()) {
  const errors = {};
  if (!values.name.trim()) errors.name = f.nameRequired;
  const q = Number(values.quantity);
  if (!Number.isInteger(q) || q < 1 || q > 1000) errors.quantity = b.quantityInvalid;
  validateGrant(values, errors, today);
  return errors;
}

/** Body for POST /admin/signup-codes/batches. */
export function buildBatchBody(values) {
  return {
    name: values.name.trim(),
    quantity: Number(values.quantity),
    ...grantFields(values),
    expiresUtc: dateInputToIso(values.expires, true),
    ...metadataBody(values)
  };
}

/** CSV text for a generated batch: code, join link, grant, and expiry. */
export function batchCsv(codes) {
  return toCsv(codes || [], [
    { key: 'code', label: 'Code' },
    { key: 'joinUrl', label: 'Join link' },
    { key: 'grantSummary', label: 'Grant' },
    { key: 'expiresUtc', label: 'Expires (UTC)' },
    { key: 'name', label: 'Name' }
  ]);
}

/** Batch of single-use codes with an immediate CSV download of the generated codes. */
export function SignupCodeBatchModal({ onClose, onCreated }) {
  const { api } = useAdminAuth();
  const [values, setValues] = useState({ name: '', quantity: '25', grantType: 'DurationMonths', grantMonths: '12', grantEnd: '', ...discountFormValues(null), expires: '', tags: '' });
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState(null);

  const set = (key) => (e) => setValues((v) => ({ ...v, [key]: e.target.value }));

  const submit = async (e) => {
    e.preventDefault();
    const found = validateBatchForm(values);
    setErrors(found);
    setError(Object.keys(found).length ? f.fixBelow : '');
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      const created = await api.createSignupCodeBatch(buildBatchBody(values));
      setResult(created);
      onCreated(created);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const download = () => browser.download(batchCsv(result.codes), b.csvName(result.batchId || 'batch'), 'text/csv;charset=utf-8');

  if (result) {
    const codes = result.codes || [];
    return (
      <Modal
        open
        onClose={onClose}
        title={b.resultTitle(codes.length)}
        size="md"
        footer={(
          <>
            <button type="button" className="button button-secondary" onClick={onClose}>{b.done}</button>
            <button type="button" className="button button-primary" onClick={download}>{b.download}</button>
          </>
        )}
      >
        <p>{b.resultBody}</p>
        <ul className="code-list">
          {codes.slice(0, 50).map((x) => <li key={x.id || x.code}><code className="mono">{x.code}</code></li>)}
        </ul>
        {codes.length > 50 && <p className="muted small">{'+' + (codes.length - 50)}</p>}
      </Modal>
    );
  }

  return (
    <Modal
      open
      onClose={busy ? () => {} : onClose}
      title={b.title}
      size="md"
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="batch-form" className="button button-primary" disabled={busy}>{busy ? b.submitting : b.submit}</button>
        </>
      )}
    >
      <form id="batch-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} />
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="batch-name">{b.name}</label>
            <input id="batch-name" type="text" maxLength={100} value={values.name} onChange={set('name')} disabled={busy} aria-invalid={Boolean(errors.name)} />
            {errors.name && <span className="field-error">{errors.name}</span>}
          </div>
          <div className="field">
            <label className="label-text" htmlFor="batch-quantity">{b.quantity}</label>
            <input id="batch-quantity" type="number" min="1" max="1000" value={values.quantity} onChange={set('quantity')} disabled={busy} aria-invalid={Boolean(errors.quantity)} />
            {errors.quantity && <span className="field-error">{errors.quantity}</span>}
          </div>
        </div>
        <GrantInputs values={values} set={set} errors={errors} disabled={busy} idPrefix="batch" />
        <div className="form-grid">
          <div className="field">
            <label className="label-text" htmlFor="batch-expires">{f.expires}</label>
            <input id="batch-expires" type="date" value={values.expires} onChange={set('expires')} disabled={busy} />
          </div>
          <div className="field">
            <label className="label-text" htmlFor="batch-tags">{strings.metadata.labels}</label>
            <input id="batch-tags" type="text" value={values.tags} onChange={set('tags')} disabled={busy} />
          </div>
        </div>
      </form>
    </Modal>
  );
}
