import { useEffect, useMemo, useState } from 'react';
import { CREATABLE_FIELD_TYPES, PROFILE_MAX_CARD_FIELDS, ProfileCollectAt, ProfilePreviewTarget, ProfileSubFieldType, ProfileViewer, ProfileVisibility } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import {
  DEFAULT_RANK_COLOR,
  canEdit,
  canFilter,
  canSearch,
  draftToBody,
  emptyOption,
  emptySubField,
  fieldToDraft,
  hasOptions,
  normalizeDraft,
  optionKeysInUse,
  otherCardFields,
  previewFieldsWithDraft,
  validateFieldDraft
} from '../../utils/profileFieldForm.js';
import Modal from '../../components/Modal.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';

const s = strings.admin.profileFields;

function FieldError({ id, message }) {
  if (!message) return null;
  return <span id={id} className="field-error" role="alert">{message}</span>;
}

function TextInput({ id, label, value, onChange, error, hint, disabled, mono, maxLength, type = 'text', placeholder, readOnly }) {
  const hintId = hint ? id + '-hint' : undefined;
  const errorId = error ? id + '-error' : undefined;
  return (
    <div className="field">
      <label className="label-text" htmlFor={id}>{label}</label>
      <input
        id={id}
        type={type}
        className={mono ? 'mono' : undefined}
        value={value}
        onChange={(ev) => onChange(ev.target.value)}
        disabled={disabled}
        readOnly={readOnly}
        maxLength={maxLength}
        placeholder={placeholder}
        aria-invalid={Boolean(error)}
        aria-describedby={[hintId, errorId].filter(Boolean).join(' ') || undefined}
      />
      {hint && <span id={hintId} className="muted small">{hint}</span>}
      <FieldError id={errorId} message={error} />
    </div>
  );
}

function Check({ id, label, checked, onChange, disabled, hint, error }) {
  return (
    <div className="field">
      <label className="check-field" htmlFor={id}>
        <input id={id} type="checkbox" checked={checked} onChange={(ev) => onChange(ev.target.checked)} disabled={disabled} aria-describedby={hint ? id + '-hint' : undefined} />
        <span>{label}</span>
      </label>
      {hint && <span id={id + '-hint'} className="muted small">{hint}</span>}
      <FieldError id={id + '-error'} message={error} />
    </div>
  );
}

function Select({ id, label, value, onChange, options, disabled, hint, error }) {
  return (
    <div className="field">
      <label className="label-text" htmlFor={id}>{label}</label>
      <select id={id} value={value} onChange={(ev) => onChange(ev.target.value)} disabled={disabled} aria-invalid={Boolean(error)} aria-describedby={hint ? id + '-hint' : undefined}>
        {options.map((o) => <option key={o.value} value={o.value} disabled={o.disabled}>{o.label}</option>)}
      </select>
      {hint && <span id={id + '-hint'} className="muted small">{hint}</span>}
      <FieldError id={id + '-error'} message={error} />
    </div>
  );
}

function move(list, index, delta) {
  const target = index + delta;
  if (target < 0 || target >= list.length) return list;
  const next = list.slice();
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}

/**
 * Options editor of a select, rank, or History SingleSelect sub-field. Options with member values
 * (from the field's stats) cannot be removed, only retired: removing one is `409 OptionInUse`.
 */
export function OptionsEditor({ idPrefix, type, options, onChange, errors, prefix, counts = {}, inUse = [], disabled }) {
  const rank = type === 'Rank';
  const select = type === 'SingleSelect' || type === 'MultiSelect';
  const set = (index, patch) => onChange(options.map((o, i) => (i === index ? { ...o, ...patch } : o)));
  return (
    <fieldset className="settings-section">
      <legend><span>{s.editor.optionsLegend(options.length)}</span></legend>
      <p className="muted small">{rank ? s.editor.rankOptionsHint : s.editor.optionsHint}</p>
      <FieldError id={idPrefix + '-options-error'} message={errors[prefix]} />
      {options.length > 0 && (
        <div className="table-scroll">
          <table className="data-table compact">
            <thead>
              <tr>
                <th scope="col">{s.editor.optionKey}</th>
                <th scope="col">{s.editor.optionLabel}</th>
                {rank && <th scope="col">{s.editor.optionColor}</th>}
                {rank && <th scope="col">{s.editor.optionMaxLevel}</th>}
                {select && <th scope="col">{s.editor.optionFreeText}</th>}
                <th scope="col">{s.editor.optionRetired}</th>
                <th scope="col">{s.editor.optionValues}</th>
                <th scope="col"><span className="visually-hidden">{strings.admin.common.rowActions('')}</span></th>
              </tr>
            </thead>
            <tbody>
              {options.map((o, i) => {
                const p = prefix + '[' + i + '].';
                const count = o.id ? counts[o.key] || 0 : 0;
                const locked = count > 0 || inUse.includes(o.key);
                const n = i + 1;
                return (
                  <tr key={o.rowKey} className={inUse.includes(o.key) ? 'row-failed' : ''}>
                    <td>
                      <input className="mono" aria-label={s.editor.optionKeyN(n)} value={o.key} onChange={(ev) => set(i, { key: ev.target.value })} readOnly={locked} disabled={disabled} maxLength={64} aria-invalid={Boolean(errors[p + 'key'])} />
                      <FieldError message={errors[p + 'key']} />
                    </td>
                    <td>
                      <input aria-label={s.editor.optionLabelN(n)} value={o.label} onChange={(ev) => set(i, { label: ev.target.value })} disabled={disabled} maxLength={60} aria-invalid={Boolean(errors[p + 'label'])} />
                      <FieldError message={errors[p + 'label']} />
                    </td>
                    {rank && (
                      <td>
                        <span className="color-input">
                          <input type="color" aria-label={s.editor.optionColorN(n)} value={/^#[0-9A-Fa-f]{6}$/.test(o.color) ? o.color : DEFAULT_RANK_COLOR} onChange={(ev) => set(i, { color: ev.target.value })} disabled={disabled} />
                          <input className="mono input-compact" aria-label={s.editor.optionColorHexN(n)} value={o.color} onChange={(ev) => set(i, { color: ev.target.value })} disabled={disabled} maxLength={9} size={8} />
                        </span>
                        <FieldError message={errors[p + 'color']} />
                      </td>
                    )}
                    {rank && (
                      <td>
                        <input type="number" min={0} max={20} className="input-number" aria-label={s.editor.optionMaxLevelN(n)} value={o.maxLevel} placeholder={s.editor.inherit} onChange={(ev) => set(i, { maxLevel: ev.target.value })} disabled={disabled} />
                        <FieldError message={errors[p + 'maxLevel']} />
                      </td>
                    )}
                    {select && <td><input type="checkbox" aria-label={s.editor.optionFreeTextN(n)} checked={o.freeText} onChange={(ev) => set(i, { freeText: ev.target.checked })} disabled={disabled} /></td>}
                    <td><input type="checkbox" aria-label={s.editor.optionRetiredN(n)} checked={o.retired} onChange={(ev) => set(i, { retired: ev.target.checked })} disabled={disabled} /></td>
                    <td className="num">{o.id ? count : <span className="muted small">{s.editor.newOption}</span>}</td>
                    <td className="cell-actions nowrap">
                      <button type="button" className="icon-button" onClick={() => onChange(move(options, i, -1))} disabled={disabled || i === 0} aria-label={s.editor.optionUp(n)} title={s.editor.optionUp(n)}><Icon name="chevronUp" size={14} /></button>
                      <button type="button" className="icon-button" onClick={() => onChange(move(options, i, 1))} disabled={disabled || i === options.length - 1} aria-label={s.editor.optionDown(n)} title={s.editor.optionDown(n)}><Icon name="chevronDown" size={14} /></button>
                      <button
                        type="button"
                        className="icon-button"
                        onClick={() => onChange(options.filter((_, x) => x !== i))}
                        disabled={disabled || locked}
                        aria-label={s.editor.optionRemove(n)}
                        title={locked ? s.editor.optionRetireInstead : s.editor.optionRemove(n)}
                      >
                        <Icon name="trash" size={14} />
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      <div>
        <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...options, emptyOption(type)])} disabled={disabled || options.length >= (prefix === 'options' ? 200 : 50)}>
          <Icon name="plus" size={14} /><span>{s.editor.addOption}</span>
        </button>
      </div>
    </fieldset>
  );
}

/** History sub-fields editor: key, label, type, and the settings of each type. */
export function SubFieldsEditor({ subFields, onChange, errors, rankFields, disabled }) {
  const set = (index, patch) => onChange(subFields.map((x, i) => (i === index ? { ...x, ...patch } : x)));
  return (
    <fieldset className="settings-section">
      <legend><span>{s.editor.subFieldsLegend(subFields.length)}</span></legend>
      <p className="muted small">{s.editor.subFieldsHint}</p>
      <FieldError message={errors.subFields} />
      {subFields.map((sub, i) => {
        const p = 'subFields[' + i + '].';
        const id = 'pf-sub-' + sub.rowKey;
        const n = i + 1;
        return (
          <div key={sub.rowKey} className="subfield-card panel" aria-label={s.editor.subFieldN(n)} role="group">
            <div className="row-between">
              <strong>{s.editor.subFieldN(n)}</strong>
              <span className="nowrap">
                <button type="button" className="icon-button" onClick={() => onChange(move(subFields, i, -1))} disabled={disabled || i === 0} aria-label={s.editor.subFieldUp(n)} title={s.editor.subFieldUp(n)}><Icon name="chevronUp" size={14} /></button>
                <button type="button" className="icon-button" onClick={() => onChange(move(subFields, i, 1))} disabled={disabled || i === subFields.length - 1} aria-label={s.editor.subFieldDown(n)} title={s.editor.subFieldDown(n)}><Icon name="chevronDown" size={14} /></button>
                <button type="button" className="icon-button" onClick={() => onChange(subFields.filter((_, x) => x !== i))} disabled={disabled} aria-label={s.editor.subFieldRemove(n)} title={s.editor.subFieldRemove(n)}><Icon name="trash" size={14} /></button>
              </span>
            </div>
            <div className="form-grid">
              <TextInput id={id + '-key'} label={s.editor.subKey} value={sub.key} onChange={(v) => set(i, { key: v })} error={errors[p + 'key']} mono maxLength={40} disabled={disabled} />
              <TextInput id={id + '-label'} label={s.editor.subLabel} value={sub.label} onChange={(v) => set(i, { label: v })} error={errors[p + 'label']} maxLength={60} disabled={disabled} />
              <Select
                id={id + '-type'}
                label={s.editor.subType}
                value={sub.type}
                onChange={(v) => set(i, { type: v, options: v === 'SingleSelect' && sub.options.length === 0 ? [emptyOption(v)] : sub.options })}
                options={Object.values(ProfileSubFieldType).map((t) => ({ value: t, label: s.subTypes[t] || t }))}
                disabled={disabled}
                error={errors[p + 'type']}
              />
              <Check id={id + '-required'} label={s.editor.subRequired} checked={sub.required} onChange={(v) => set(i, { required: v })} disabled={disabled} />
              {sub.type === 'Text' && <TextInput id={id + '-max'} type="number" label={s.editor.maxLength} value={sub.maxLength} onChange={(v) => set(i, { maxLength: v })} error={errors[p + 'maxLength']} hint={s.editor.subMaxLengthHint} disabled={disabled} />}
              {sub.type === 'Number' && <TextInput id={id + '-min-value'} type="number" label={s.editor.minValue} value={sub.minValue} onChange={(v) => set(i, { minValue: v })} error={errors[p + 'minValue']} disabled={disabled} />}
              {sub.type === 'Number' && <TextInput id={id + '-max-value'} type="number" label={s.editor.maxValue} value={sub.maxValue} onChange={(v) => set(i, { maxValue: v })} error={errors[p + 'maxValue']} disabled={disabled} />}
              {sub.type === 'Date' && <Check id={id + '-future'} label={s.editor.notInFuture} checked={sub.notInFuture} onChange={(v) => set(i, { notInFuture: v })} disabled={disabled} />}
              {sub.type === 'RankValue' && (
                <Select
                  id={id + '-rank-field'}
                  label={s.editor.rankFieldKey}
                  value={sub.rankFieldKey}
                  onChange={(v) => set(i, { rankFieldKey: v })}
                  options={[{ value: '', label: s.editor.chooseRankField }, ...rankFields.map((f) => ({ value: f.key, label: f.label + ' (' + f.key + ')' }))]}
                  hint={rankFields.length ? null : s.editor.noRankFields}
                  error={errors[p + 'rankFieldKey']}
                  disabled={disabled}
                />
              )}
              {sub.type === 'RankValue' && <Check id={id + '-level'} label={s.editor.withLevel} checked={sub.withLevel} onChange={(v) => set(i, { withLevel: v })} disabled={disabled} />}
            </div>
            {sub.type === 'SingleSelect' && (
              <OptionsEditor idPrefix={id} type="SingleSelect" options={sub.options} onChange={(options) => set(i, { options })} errors={errors} prefix={p + 'options'} disabled={disabled} />
            )}
          </div>
        );
      })}
      <div>
        <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...subFields, emptySubField()])} disabled={disabled || subFields.length >= 10}>
          <Icon name="plus" size={14} /><span>{s.editor.addSubField}</span>
        </button>
      </div>
    </fieldset>
  );
}

function parseValues(text) {
  const trimmed = String(text || '').trim();
  if (!trimmed) return { values: undefined, error: '' };
  try {
    const parsed = JSON.parse(trimmed);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return { values: undefined, error: s.preview.valuesInvalid };
    return { values: parsed, error: '' };
  } catch {
    return { values: undefined, error: s.preview.valuesInvalid };
  }
}

/**
 * Preview of how fields render (`POST /admin/profile-fields/preview`) for a target and viewer.
 * `buildFields` returns the unsaved draft list to preview (or undefined for the saved fields).
 */
export function ProfilePreviewPanel({ buildFields, idPrefix = 'pf-preview' }) {
  const { api } = useAdminAuth();
  const [target, setTarget] = useState(ProfilePreviewTarget.ProfilePage);
  const [viewer, setViewer] = useState(ProfileViewer.Member);
  const [valuesText, setValuesText] = useState('');
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const run = async () => {
    const parsed = parseValues(valuesText);
    if (parsed.error) {
      setError(parsed.error);
      return;
    }
    setBusy(true);
    setError('');
    setFieldErrors({});
    try {
      const fields = buildFields ? buildFields() : undefined;
      const body = { target, viewer };
      if (fields) body.fields = fields;
      if (parsed.values) body.values = parsed.values;
      setResult(await api.previewProfileFields(body));
    } catch (err) {
      setFieldErrors(getFieldErrors(err));
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const items = (result && result.items) || [];
  const problems = Object.entries(fieldErrors);
  return (
    <section className="detail-block profile-preview" aria-labelledby={idPrefix + '-title'}>
      <h3 id={idPrefix + '-title'}>{s.preview.title}</h3>
      <p className="muted small">{buildFields ? s.preview.draftHint : s.preview.savedHint}</p>
      <div className="form-grid">
        <Select id={idPrefix + '-target'} label={s.preview.target} value={target} onChange={setTarget} options={Object.values(ProfilePreviewTarget).map((t) => ({ value: t, label: s.preview.targets[t] }))} />
        <Select id={idPrefix + '-viewer'} label={s.preview.viewer} value={viewer} onChange={setViewer} options={Object.values(ProfileViewer).map((v) => ({ value: v, label: s.preview.viewers[v] }))} />
      </div>
      <div className="field">
        <label className="label-text" htmlFor={idPrefix + '-values'}>{s.preview.values}</label>
        <textarea id={idPrefix + '-values'} className="mono" rows={3} value={valuesText} onChange={(ev) => setValuesText(ev.target.value)} placeholder={s.preview.valuesPlaceholder} aria-describedby={idPrefix + '-values-hint'} />
        <span id={idPrefix + '-values-hint'} className="muted small">{s.preview.valuesHint}</span>
      </div>
      <div>
        <button type="button" className="button button-secondary button-sm" onClick={run} disabled={busy}>
          <Icon name="play" size={14} /><span>{busy ? s.preview.running : s.preview.run}</span>
        </button>
      </div>
      <ErrorBanner message={error} onDismiss={() => setError('')} />
      {problems.length > 0 && (
        <ul className="metadata-problems" role="alert">
          {problems.map(([key, message]) => <li key={key} className="field-error"><code className="mono">{key}</code>: {Array.isArray(message) ? message.join(' ') : String(message)}</li>)}
        </ul>
      )}
      {result && (
        items.length === 0 ? <p className="muted">{s.preview.empty}</p> : (
          <div className="table-scroll">
            <table className="data-table compact">
              <caption className="visually-hidden">{s.preview.title}</caption>
              <thead><tr><th scope="col">{s.preview.label}</th><th scope="col">{s.preview.key}</th><th scope="col">{s.preview.control}</th><th scope="col">{s.preview.formattedValue}</th></tr></thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.key}>
                    <td>
                      <strong>{item.label}</strong>
                      {item.required && <> <Badge tone="warning">{strings.profileFields.required}</Badge></>}
                      {item.builtIn && <> <Badge>{s.builtIn}</Badge></>}
                      {item.helpText && <div className="muted small">{item.helpText}</div>}
                    </td>
                    <td><code className="mono">{item.key}</code></td>
                    <td><code className="mono">{item.control}</code></td>
                    <td>{item.formattedValue || <span className="muted">{s.preview.noValue}</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      )}
    </section>
  );
}

function typeOptions(current) {
  const types = CREATABLE_FIELD_TYPES.includes(current) ? CREATABLE_FIELD_TYPES : [current, ...CREATABLE_FIELD_TYPES];
  return types.map((t) => ({ value: t, label: s.types[t] || t }));
}

/**
 * Create or edit a profile field definition. Every attribute has its own control; built-in
 * fields show only the attributes the backend allows for them. Client checks give instant
 * feedback, and the server's `fieldErrors` land on the same controls.
 *
 * @param {{ field: object|null, fields: object[], onClose: () => void, onSaved: (field: object, mode: 'create'|'edit') => void }} props
 */
function ProfileFieldEditor({ field, fields, onClose, onSaved }) {
  const { api, client } = useAdminAuth();
  const [draft, setDraft] = useState(() => fieldToDraft(field));
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [counts, setCounts] = useState({});
  const [statsLoading, setStatsLoading] = useState(Boolean(field && !field.builtIn && field.valueCount > 0 && hasOptions(field.type)));
  const [inUse, setInUse] = useState([]);
  const builtIn = Boolean(field && field.builtIn);
  const hasValues = Boolean(field && field.valueCount > 0);
  const editing = Boolean(field);

  useEffect(() => {
    if (!statsLoading) return undefined;
    const controller = new AbortController();
    let active = true;
    api.getProfileFieldStats(field.id, controller.signal).then(
      (stats) => {
        if (!active) return;
        const map = {};
        for (const row of (stats && stats.optionCounts) || []) map[row.key] = row.count;
        setCounts(map);
        setStatsLoading(false);
      },
      () => { if (active) setStatsLoading(false); }
    );
    return () => { active = false; controller.abort(); };
  }, [api, field, statsLoading]);

  const rankFields = useMemo(() => (fields || []).filter((f) => f.type === 'Rank' && (!field || f.id !== field.id)), [fields, field]);
  const cardCount = otherCardFields(fields, field);

  const update = (patch) => {
    setDraft((d) => normalizeDraft({ ...d, ...patch }));
    const keys = Object.keys(patch);
    setErrors((prev) => {
      const next = { ...prev };
      for (const key of Object.keys(prev)) if (keys.some((k) => key === k || key.startsWith(k + '.') || key.startsWith(k + '['))) delete next[key];
      return next;
    });
  };
  const updateValidation = (patch) => update({ validation: { ...draft.validation, ...patch } });

  const changeType = (type) => {
    const patch = { type };
    if (hasOptions(type)) {
      const options = draft.options.length ? draft.options : [emptyOption(type)];
      patch.options = type === 'Rank' ? options.map((o) => ({ ...o, color: o.color || DEFAULT_RANK_COLOR, freeText: false })) : options.map((o) => ({ ...o, maxLevel: '' }));
    }
    if (type === 'History' && draft.subFields.length === 0) patch.subFields = [emptySubField()];
    update(patch);
  };

  const submit = async (ev) => {
    ev.preventDefault();
    const found = validateFieldDraft(draft, { field, fields, maxCardFields: PROFILE_MAX_CARD_FIELDS });
    setErrors(found);
    if (Object.keys(found).length) {
      setError(s.editor.fixBelow);
      return;
    }
    setBusy(true);
    setError('');
    setInUse([]);
    try {
      const body = draftToBody(draft, field);
      const saved = editing ? await api.updateProfileField(field.id, body) : await api.createProfileField(body);
      onSaved(saved, editing ? 'edit' : 'create');
    } catch (err) {
      const fe = getFieldErrors(err);
      setErrors(fe);
      if (err && err.code === 'OptionInUse') {
        setInUse(optionKeysInUse(err.message));
        setError(s.editor.optionInUse(errorMessage(err, '')));
      } else if (err && err.code === 'FieldHasValues') {
        setError(s.editor.fieldHasValues);
      } else {
        setError(Object.keys(fe).length ? s.editor.fixBelow + ' ' + errorMessage(err, '') : errorMessage(err, strings.common.errorGeneric));
      }
      setBusy(false);
    }
  };

  const id = (name) => 'pf-' + name;
  const t = draft.type;
  const adminOnly = draft.collectAt === ProfileCollectAt.AdminOnly;
  const collectOptions = Object.values(ProfileCollectAt)
    .filter((x) => !builtIn || x !== ProfileCollectAt.AdminOnly)
    .map((x) => ({ value: x, label: s.collectAt[x] }));
  const visibilityOptions = Object.values(ProfileVisibility)
    .filter((x) => !builtIn || x !== ProfileVisibility.Staff)
    .map((x) => ({ value: x, label: s.visibility[x], disabled: x === ProfileVisibility.Staff && !adminOnly }));
  const textType = t === 'Text' || t === 'LongText';
  const title = editing ? s.editor.editTitle(field.label || field.key) : s.editor.createTitle;

  return (
    <Modal
      open
      size="lg"
      onClose={busy ? () => {} : onClose}
      title={title}
      subtitle={editing ? <code className="mono">{field.key}</code> : null}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="profile-field-form" className="button button-primary" disabled={busy}>{busy ? s.editor.saving : (editing ? s.editor.save : s.editor.create)}</button>
        </>
      )}
    >
      <form id="profile-field-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} onDismiss={() => setError('')} />
        {builtIn && <Banner tone="info" icon="lock">{s.editor.builtInNote}</Banner>}

        <fieldset className="settings-section" disabled={busy}>
          <legend><span>{s.editor.basicsLegend}</span></legend>
          <div className="form-grid">
            <TextInput
              id={id('key')}
              label={s.editor.key}
              value={draft.key}
              onChange={(v) => update({ key: v })}
              error={errors.key}
              hint={builtIn ? s.editor.keyBuiltIn : hasValues ? s.editor.keyLocked(field.valueCount) : s.editor.keyHint}
              mono
              maxLength={40}
              readOnly={builtIn || hasValues}
            />
            <TextInput id={id('label')} label={s.editor.label} value={draft.label} onChange={(v) => update({ label: v })} error={errors.label} maxLength={60} />
          </div>
          <TextInput id={id('help')} label={s.editor.helpText} value={draft.helpText} onChange={(v) => update({ helpText: v })} error={errors.helpText} hint={s.editor.helpTextHint} maxLength={300} />
          <TextInput id={id('placeholder')} label={s.editor.placeholder} value={draft.placeholder} onChange={(v) => update({ placeholder: v })} error={errors.placeholder} maxLength={100} />
          {!builtIn && (
            <Select
              id={id('type')}
              label={s.editor.type}
              value={t}
              onChange={changeType}
              options={typeOptions(t)}
              disabled={hasValues}
              hint={hasValues ? s.editor.typeLocked(field.valueCount) : s.typeHints[t]}
              error={errors.type}
            />
          )}
          {builtIn && <p className="muted small">{s.editor.typeFixed(s.types[t] || t)}</p>}
        </fieldset>

        {!builtIn && hasOptions(t) && (
          <>
            {statsLoading && <Loading inline label={s.editor.loadingCounts} />}
            <OptionsEditor idPrefix={id('opt')} type={t} options={draft.options} onChange={(options) => update({ options })} errors={errors} prefix="options" counts={counts} inUse={inUse} disabled={busy} />
          </>
        )}

        {!builtIn && t === 'Rank' && (
          <fieldset className="settings-section" disabled={busy}>
            <legend><span>{s.editor.rankLegend}</span></legend>
            <div className="form-grid">
              <TextInput id={id('level-label')} label={s.editor.levelLabel} value={draft.rank.levelLabel} onChange={(v) => update({ rank: { ...draft.rank, levelLabel: v } })} error={errors['rank.levelLabel']} hint={s.editor.levelLabelHint} maxLength={30} />
              <TextInput id={id('max-level')} type="number" label={s.editor.maxLevel} value={draft.rank.maxLevel} onChange={(v) => update({ rank: { ...draft.rank, maxLevel: v } })} error={errors['rank.maxLevel']} hint={s.editor.maxLevelHint} />
            </div>
          </fieldset>
        )}

        {!builtIn && t === 'History' && (
          <SubFieldsEditor subFields={draft.subFields} onChange={(subFields) => update({ subFields })} errors={errors} rankFields={rankFields} disabled={busy} />
        )}

        {!builtIn && (
          <fieldset className="settings-section" disabled={busy}>
            <legend><span>{s.editor.validationLegend}</span></legend>
            <Check
              id={id('required')}
              label={s.editor.required}
              checked={draft.validation.required}
              onChange={(v) => updateValidation({ required: v })}
              disabled={adminOnly}
              hint={adminOnly ? s.editor.requiredAdminOnly : s.editor.requiredHint}
              error={errors['validation.required']}
            />
            <div className="form-grid">
              {textType && <TextInput id={id('min-length')} type="number" label={s.editor.minLength} value={draft.validation.minLength} onChange={(v) => updateValidation({ minLength: v })} error={errors['validation.minLength']} />}
              {textType && <TextInput id={id('max-length')} type="number" label={s.editor.maxLength} value={draft.validation.maxLength} onChange={(v) => updateValidation({ maxLength: v })} error={errors['validation.maxLength']} hint={s.editor.maxLengthHint(t === 'LongText' ? 2000 : 200, t === 'LongText' ? 10000 : 1000)} />}
              {t === 'Number' && <TextInput id={id('min-value')} type="number" label={s.editor.minValue} value={draft.validation.minValue} onChange={(v) => updateValidation({ minValue: v })} error={errors['validation.minValue']} />}
              {t === 'Number' && <TextInput id={id('max-value')} type="number" label={s.editor.maxValue} value={draft.validation.maxValue} onChange={(v) => updateValidation({ maxValue: v })} error={errors['validation.maxValue']} />}
              {t === 'MultiSelect' && <TextInput id={id('min-entries')} type="number" label={s.editor.minEntries} value={draft.validation.minEntries} onChange={(v) => updateValidation({ minEntries: v })} error={errors['validation.minEntries']} />}
              {(t === 'MultiSelect' || t === 'History') && <TextInput id={id('max-entries')} type="number" label={s.editor.maxEntries} value={draft.validation.maxEntries} onChange={(v) => updateValidation({ maxEntries: v })} error={errors['validation.maxEntries']} hint={t === 'History' ? s.editor.historyMaxHint : null} />}
              {t === 'Date' && <TextInput id={id('min-date')} type="date" label={s.editor.minDate} value={draft.validation.minDate} onChange={(v) => updateValidation({ minDate: v })} error={errors['validation.minDate']} />}
              {t === 'Date' && <TextInput id={id('max-date')} type="date" label={s.editor.maxDate} value={draft.validation.maxDate} onChange={(v) => updateValidation({ maxDate: v })} error={errors['validation.maxDate']} />}
            </div>
            {t === 'Number' && <Check id={id('integer')} label={s.editor.integerOnly} checked={draft.validation.integerOnly} onChange={(v) => updateValidation({ integerOnly: v })} />}
            {t === 'Date' && <Check id={id('future')} label={s.editor.notInFuture} checked={draft.validation.notInFuture} onChange={(v) => updateValidation({ notInFuture: v })} />}
            {t === 'Text' && (
              <div className="form-grid">
                <TextInput id={id('pattern')} label={s.editor.pattern} value={draft.validation.pattern} onChange={(v) => updateValidation({ pattern: v })} error={errors['validation.pattern']} hint={s.editor.patternHint} mono maxLength={200} />
                <TextInput id={id('pattern-message')} label={s.editor.patternMessage} value={draft.validation.patternMessage} onChange={(v) => updateValidation({ patternMessage: v })} error={errors['validation.patternMessage']} hint={s.editor.patternMessageHint} maxLength={200} disabled={!draft.validation.pattern.trim()} />
              </div>
            )}
          </fieldset>
        )}

        {(canEdit(field, 'collectAt') || canEdit(field, 'visibility')) && (
          <fieldset className="settings-section" disabled={busy}>
            <legend><span>{s.editor.collectionLegend}</span></legend>
            <div className="form-grid">
              {canEdit(field, 'collectAt') && (
                <Select id={id('collect')} label={s.editor.collectAt} value={draft.collectAt} onChange={(v) => update({ collectAt: v })} options={collectOptions} hint={s.collectAtHints[draft.collectAt]} error={errors.collectAt} />
              )}
              {!builtIn && (
                <div className="field">
                  <span className="label-text">{s.editor.editableBy}</span>
                  <span><Badge tone={draft.editableBy === 'Staff' ? 'warning' : 'neutral'}>{s.editableBy[draft.editableBy]}</Badge></span>
                  <span className="muted small">{s.editor.editableByHint}</span>
                  <FieldError message={errors.editableBy} />
                </div>
              )}
              {canEdit(field, 'visibility') && (
                <Select id={id('visibility')} label={s.editor.visibility} value={draft.visibility} onChange={(v) => update({ visibility: v })} options={visibilityOptions} hint={s.visibilityHints[draft.visibility] + (adminOnly || builtIn ? '' : ' ' + s.editor.staffNeedsAdminOnly)} error={errors.visibility} />
              )}
            </div>
          </fieldset>
        )}

        {(canEdit(field, 'showOnProfile') || canEdit(field, 'enabled')) && (
          <fieldset className="settings-section" disabled={busy}>
            <legend><span>{s.editor.displayLegend}</span></legend>
            {canEdit(field, 'enabled') && <Check id={id('enabled')} label={s.editor.enabled} checked={draft.enabled} onChange={(v) => update({ enabled: v })} hint={s.editor.enabledHint} />}
            {canEdit(field, 'showOnProfile') && <Check id={id('profile')} label={s.editor.showOnProfile} checked={draft.showOnProfile} onChange={(v) => update({ showOnProfile: v })} error={errors.showOnProfile} />}
            {!builtIn && (
              <>
                <Check
                  id={id('card')}
                  label={s.editor.showOnCard}
                  checked={draft.showOnCard}
                  onChange={(v) => update({ showOnCard: v })}
                  disabled={!draft.showOnCard && cardCount >= PROFILE_MAX_CARD_FIELDS}
                  hint={!draft.showOnCard && cardCount >= PROFILE_MAX_CARD_FIELDS ? s.editor.cardFull(PROFILE_MAX_CARD_FIELDS) : s.editor.cardHint(cardCount, PROFILE_MAX_CARD_FIELDS)}
                  error={errors.showOnCard}
                />
                <Check id={id('searchable')} label={s.editor.searchable} checked={draft.searchable} onChange={(v) => update({ searchable: v })} disabled={!canSearch(t)} hint={canSearch(t) ? s.editor.searchableHint : s.editor.searchableNot} error={errors.searchable} />
                <Check id={id('filterable')} label={s.editor.filterable} checked={draft.filterable} onChange={(v) => update({ filterable: v })} disabled={!canFilter(t)} hint={canFilter(t) ? s.editor.filterableHint : s.editor.filterableNot} error={errors.filterable} />
              </>
            )}
          </fieldset>
        )}

        <MetadataEditor value={draft.metadata} onChange={(metadata) => setDraft((d) => ({ ...d, metadata }))} isStaff client={client} targetType="ProfileField" disabled={busy} collapsible />

        <ProfilePreviewPanel idPrefix="pf-editor-preview" buildFields={() => previewFieldsWithDraft(fields, field, draft)} />
      </form>
    </Modal>
  );
}

export default ProfileFieldEditor;
