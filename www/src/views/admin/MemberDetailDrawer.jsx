import { useCallback, useState } from 'react';
import ForumMutesTab from './ForumMutesTab.jsx';
import { MemberLibraryHistory } from './library/LibraryAdminView.jsx';
import MemberDocumentsHistory from '../../components/documents/MemberDocumentsHistory.jsx';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage, getFieldErrors } from '../../utils/api.js';
import { formatDateTime, formatShortDate, formatShortDateTime } from '../../utils/format.js';
import { membershipText, optionsFrom, redemptionTone, subscriptionTone } from '../../utils/labels.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import { buildProfilePatch, customFields, documentsOf, publicPagesConfig, purchasesEnabled, toFormValues, validateProfileValues } from '@trelliskit/shared';
import { ProfileFieldList } from '../../components/profile/ProfileFieldDisplay.jsx';
import { ProfileFieldsEditor } from '../../components/profile/ProfileFieldInputs.jsx';
import { profileErrorsText, serverFieldErrors } from '../../utils/profileErrors.js';
import { useAdminProfileFields } from './adminProfileFields.js';
import { isoToLocalInput, localInputToIso } from '../../utils/time.js';
import Modal from '../../components/Modal.jsx';
import ReasonModal from '../../components/ReasonModal.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Avatar from '../../components/Avatar.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import MetadataPanel from '../../components/MetadataPanel.jsx';
import { PublicProfilePageSettings } from '../../components/PublicPageFields.jsx';
import { capabilityAvailable } from '../member/memberRoutes.js';
import TableColumns from '../../components/TableColumns.jsx';
import MemberRankPanel from './ranks/MemberRankPanel.jsx';

const s = strings.admin.members;
const c = strings.admin.common;
const en = strings.enums;

const POSTING_FLAGS = ['canPostToFeed', 'canPost', 'canPostMedia', 'canUploadVideo'];
const OTHER_FLAGS = ['canPromoteToFeed', 'canSendDirectMessages'];
const FLAG_KEYS = [...POSTING_FLAGS, ...OTHER_FLAGS];
/** Flag defaults when the server omits one (Phase 2 contract section 1.1). */
export const FLAG_DEFAULTS = Object.freeze({ canPostToFeed: false, canPost: true, canPostMedia: true, canUploadVideo: true, canPromoteToFeed: false, canSendDirectMessages: true });
const flagValue = (flags, key) => (flags && typeof flags[key] === 'boolean' ? flags[key] : FLAG_DEFAULTS[key]);
const ROLE_OPTIONS = ['SystemAdmin', 'Moderator', 'FeedModerator', 'DirectoryModerator', 'EventManager', 'LibraryEditor', 'SupportAgent', 'Communicator', 'ReleaseManager', 'RankAwarder'];
const DEFAULT_SCOPE = { SystemAdmin: 'Global', Moderator: 'Forum', FeedModerator: 'Feed', DirectoryModerator: 'Directory', EventManager: 'Events', LibraryEditor: 'Library', SupportAgent: 'Global', Communicator: 'Global', ReleaseManager: 'Global', RankAwarder: 'Ranks' };

let roleKey = 0;
const withKey = (role) => ({ key: 'r' + (roleKey += 1), role: role.role, resourceType: role.resourceType || DEFAULT_SCOPE[role.role] || 'Global', resourceId: role.resourceId || '*' });

/** Validate an edited role list; returns a map of row key to message. */
export function validateRoles(rows) {
  const errors = {};
  for (const row of rows) {
    if (row.resourceType !== 'Global' && !String(row.resourceId || '').trim()) errors[row.key] = s.resourceIdRequired;
  }
  return errors;
}

/** Roles body for PUT /admin/members/{id}/roles. Global roles always use resourceId `*`. */
export function rolesBody(rows) {
  return rows.map((row) => ({ role: row.role, resourceType: row.resourceType, resourceId: row.resourceType === 'Global' ? '*' : String(row.resourceId).trim() }));
}

function FlagsEditor({ member, onSaved, onMute, onUnmute }) {
  const { api } = useAdminAuth();
  const toast = useToast();
  const initial = Object.fromEntries(FLAG_KEYS.map((k) => [k, flagValue(member.flags, k)]));
  const [flags, setFlags] = useState(() => ({ ...initial }));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const dirty = FLAG_KEYS.some((k) => flags[k] !== initial[k]);
  const muted = Boolean(member.flags && member.flags.isMuted);

  const save = async () => {
    setBusy(true);
    setError('');
    try {
      const changes = {};
      for (const k of FLAG_KEYS) if (flags[k] !== initial[k]) changes[k] = flags[k];
      onSaved(await api.updateMemberFlags(member.id, changes));
      toast.success(s.flagsSaved);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const toggle = (k) => (
    <label key={k} className="toggle toggle-hinted">
      <input type="checkbox" checked={flags[k]} onChange={(e) => setFlags((f) => ({ ...f, [k]: e.target.checked }))} disabled={busy} aria-describedby={s.flagHints[k] ? 'flag-hint-' + k : undefined} />
      <span className="toggle-track"><span className="toggle-thumb" /></span>
      <span className="toggle-text">
        <span>{s.flags[k]}</span>
        {s.flagHints[k] && <span id={'flag-hint-' + k} className="muted small">{s.flagHints[k]}</span>}
      </span>
    </label>
  );

  return (
    <section className="detail-block" aria-labelledby="member-flags-title">
      <h3 id="member-flags-title">{s.postingTitle}</h3>
      <p className="muted small">{s.postingHint}</p>
      <ErrorBanner message={error} />
      <div className="toggle-list">{POSTING_FLAGS.map(toggle)}</div>
      <div className="row-actions">
        {muted
          ? <button type="button" className="button button-secondary button-sm" onClick={onUnmute} disabled={busy}><Icon name="mute" size={14} /><span>{s.actions.unmute}</span></button>
          : <button type="button" className="button button-danger-outline button-sm" onClick={onMute} disabled={busy}><Icon name="mute" size={14} /><span>{s.actions.mute}</span></button>}
      </div>
      <h4 className="label-text">{s.otherFlagsTitle}</h4>
      <div className="toggle-list">{OTHER_FLAGS.map(toggle)}</div>
      <div className="form-actions">
        <button type="button" className="button button-primary button-sm" onClick={save} disabled={busy || !dirty}>{s.saveFlags}</button>
      </div>
    </section>
  );
}

function FeedModeratorToggle({ member, onChanged }) {
  const { api } = useAdminAuth();
  const toast = useToast();
  const m = strings.admin.moderators;
  const has = (member.roles || []).some((r) => r.role === 'FeedModerator');
  const [busy, setBusy] = useState(false);
  const change = async (on) => {
    setBusy(true);
    try {
      if (on) await api.grantModerator(member.id, 'Feed');
      else await api.revokeModerator(member.id, 'Feed');
      toast.success(on ? m.granted(member.displayName) : m.revoked(member.displayName));
      onChanged();
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="detail-block" aria-label={m.memberToggle}>
      <label className="toggle toggle-hinted">
        <input type="checkbox" checked={has} onChange={(e) => change(e.target.checked)} disabled={busy} aria-describedby="feed-mod-hint" />
        <span className="toggle-track"><span className="toggle-thumb" /></span>
        <span className="toggle-text"><span>{m.memberToggle}</span><span id="feed-mod-hint" className="muted small">{m.memberToggleHint}</span></span>
      </label>
    </section>
  );
}

function RolesEditor({ member, onSaved }) {
  const { api } = useAdminAuth();
  const toast = useToast();
  const [rows, setRows] = useState(() => (member.roles || []).filter((r) => r.role !== 'User').map(withKey));
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const update = (key, changes) => setRows((list) => list.map((row) => (row.key === key ? { ...row, ...changes } : row)));
  const changeRole = (key, role) => update(key, { role, resourceType: DEFAULT_SCOPE[role] || 'Global', resourceId: '*' });

  const save = async () => {
    const found = validateRoles(rows);
    setErrors(found);
    setError('');
    if (Object.keys(found).length) return;
    setBusy(true);
    try {
      onSaved(await api.updateMemberRoles(member.id, rolesBody(rows)));
      toast.success(s.rolesSaved);
    } catch (err) {
      setError(err && err.code === 'CannotRemoveOwnAdmin' ? s.cannotRemoveOwnAdmin : errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="detail-block" aria-labelledby="member-roles-title">
      <div className="row-between">
        <h3 id="member-roles-title">{s.rolesTitle}</h3>
        <button type="button" className="button button-secondary button-sm" onClick={() => setRows((list) => [...list, withKey({ role: 'Moderator' })])} disabled={busy}>
          <Icon name="plus" size={14} /><span>{s.addRole}</span>
        </button>
      </div>
      <p className="muted small">{s.rolesHint}</p>
      <ErrorBanner message={error} />
      {rows.length === 0 && <p className="muted small">{s.rolesEmpty}</p>}
      <ul className="role-list">
        {rows.map((row) => (
          <li key={row.key} className="role-row">
            <div className="field field-compact">
              <label className="label-text" htmlFor={'role-' + row.key}>{s.roleLabel}</label>
              <select id={'role-' + row.key} className="select-compact" value={row.role} onChange={(e) => changeRole(row.key, e.target.value)} disabled={busy}>
                {ROLE_OPTIONS.map((r) => <option key={r} value={r}>{en.role[r]}</option>)}
              </select>
            </div>
            <div className="field field-compact">
              <label className="label-text" htmlFor={'scope-' + row.key}>{s.scopeLabel}</label>
              <select id={'scope-' + row.key} className="select-compact" value={row.resourceType} onChange={(e) => update(row.key, { resourceType: e.target.value, resourceId: '*' })} disabled={busy}>
                {Object.keys(en.resourceType).map((t) => <option key={t} value={t}>{en.resourceType[t]}</option>)}
              </select>
            </div>
            <div className="field field-compact field-grow">
              <label className="label-text" htmlFor={'rid-' + row.key}>{s.resourceIdLabel}</label>
              <input
                id={'rid-' + row.key}
                className="input-compact mono"
                value={row.resourceType === 'Global' ? '*' : row.resourceId}
                onChange={(e) => update(row.key, { resourceId: e.target.value })}
                placeholder={s.resourceIdPlaceholder}
                disabled={busy || row.resourceType === 'Global'}
                aria-invalid={Boolean(errors[row.key])}
              />
              {errors[row.key] && <span className="field-error">{errors[row.key]}</span>}
            </div>
            <button type="button" className="icon-button" onClick={() => setRows((list) => list.filter((r) => r.key !== row.key))} disabled={busy} aria-label={s.removeRole} title={s.removeRole}>
              <Icon name="trash" size={14} />
            </button>
          </li>
        ))}
      </ul>
      <div className="form-actions">
        <button type="button" className="button button-primary button-sm" onClick={save} disabled={busy}>{s.saveRoles}</button>
      </div>
    </section>
  );
}

function GrantCompModal({ member, onClose, onDone }) {
  const { api } = useAdminAuth();
  const [grantType, setGrantType] = useState('DurationMonths');
  const [months, setMonths] = useState('12');
  const [endDate, setEndDate] = useState('');

  const validateExtra = () => {
    if (grantType === 'DurationMonths') {
      const n = Number(months);
      if (!Number.isInteger(n) || n < 1 || n > 120) return s.monthsInvalid;
    }
    if (grantType === 'FixedEndDate' && (!endDate || new Date(endDate + 'T23:59:59') <= new Date())) return s.endDateInvalid;
    return '';
  };

  const confirm = async (reason) => {
    const body = { grantType, reason };
    if (grantType === 'DurationMonths') body.months = Number(months);
    if (grantType === 'FixedEndDate') body.endUtc = new Date(endDate + 'T23:59:59').toISOString();
    await api.grantComp(member.id, body);
    onDone(s.compGranted);
  };

  return (
    <ReasonModal open title={s.grantComp} confirmLabel={s.grantComp} danger={false} onConfirm={confirm} onCancel={onClose} validateExtra={validateExtra}>
      <div className="field">
        <label className="label-text" htmlFor="comp-grant">{s.grantType}</label>
        <select id="comp-grant" value={grantType} onChange={(e) => setGrantType(e.target.value)}>
          {optionsFrom(en.grantType).map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      </div>
      {grantType === 'DurationMonths' && (
        <div className="field">
          <label className="label-text" htmlFor="comp-months">{s.months}</label>
          <input id="comp-months" type="number" min="1" max="120" value={months} onChange={(e) => setMonths(e.target.value)} />
        </div>
      )}
      {grantType === 'FixedEndDate' && (
        <div className="field">
          <label className="label-text" htmlFor="comp-end">{s.endDate}</label>
          <input id="comp-end" type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} />
        </div>
      )}
    </ReasonModal>
  );
}

function MuteModal({ member, onClose, onDone }) {
  const { api } = useAdminAuth();
  const [until, setUntil] = useState(() => isoToLocalInput(new Date(Date.now() + 7 * 86400000).toISOString()));
  const validateExtra = () => {
    const iso = localInputToIso(until);
    return !iso || new Date(iso) <= new Date() ? s.muteUntilInvalid : '';
  };
  const confirm = async (reason) => {
    await api.muteMember(member.id, localInputToIso(until), reason);
    onDone(s.mutedToast);
  };
  return (
    <ReasonModal open title={s.muteTitle(member.displayName)} confirmLabel={s.actions.mute} onConfirm={confirm} onCancel={onClose} validateExtra={validateExtra}>
      <div className="field">
        <label className="label-text" htmlFor="mute-until">{s.muteUntil}</label>
        <input id="mute-until" type="datetime-local" value={until} onChange={(e) => setUntil(e.target.value)} />
      </div>
    </ReasonModal>
  );
}

/**
 * A bounded preview from the member detail (`{ objects, totalRecords, enumerate }`, at most 5
 * objects; docs/ENUMERATION_AND_METADATA.md section 2.4) as `{ objects, totalRecords, enumerate }`.
 * Plain arrays are accepted too.
 */
export function previewOf(value) {
  if (Array.isArray(value)) return { objects: value, totalRecords: value.length, enumerate: null };
  if (value && Array.isArray(value.objects)) {
    return { objects: value.objects, totalRecords: typeof value.totalRecords === 'number' ? value.totalRecords : value.objects.length, enumerate: value.enumerate || null };
  }
  return { objects: [], totalRecords: 0, enumerate: null };
}

/**
 * Rows of a bounded preview, with "Load more" paging through the preview's enumerate route by
 * continuation token when the preview holds fewer rows than the total.
 */
function usePreviewRows(preview) {
  const { client } = useAdminAuth();
  const [extra, setExtra] = useState({ key: null, rows: [], token: null, done: false, busy: false, error: '' });
  const key = preview.enumerate + ':' + preview.totalRecords + ':' + preview.objects.map((o) => o.id).join(',');
  const state = extra.key === key ? extra : { key, rows: [], token: null, done: false, busy: false, error: '' };
  const rows = state.rows.length ? state.rows : preview.objects;
  const more = Boolean(preview.enumerate) && !state.done && rows.length < preview.totalRecords;
  const loadMore = async () => {
    setExtra({ ...state, busy: true, error: '' });
    try {
      const path = preview.enumerate.replace(/^\/api\/v1/, '');
      const body = state.token ? { maxResults: PREVIEW_PAGE, continuationToken: state.token } : { maxResults: PREVIEW_PAGE };
      const page = await client.post(path, body);
      const seen = new Set(state.rows.map((r) => r.id));
      const merged = [...state.rows, ...((page && page.objects) || []).filter((r) => !seen.has(r.id))];
      setExtra({ key, rows: merged, token: page && page.continuationToken, done: !page || page.endOfResults || !page.continuationToken, busy: false, error: '' });
    } catch (err) {
      setExtra({ ...state, busy: false, error: errorMessage(err, strings.common.errorGeneric) });
    }
  };
  return { rows, more, loadMore, busy: state.busy, error: state.error };
}

/** Rows fetched per "Load more" from a preview's enumerate route. */
const PREVIEW_PAGE = 25;

function SmallTable({ caption, columns, rows, empty, rowKey, preview }) {
  const paged = usePreviewRows(preview || previewOf(rows));
  const list = preview ? paged.rows : rows;
  if (!list || list.length === 0) return <p className="muted small">{empty}</p>;
  return (
    <>
      <SmallTableBody caption={caption} columns={columns} rows={list} rowKey={rowKey} />
      {preview && (paged.more || list.length < preview.totalRecords) && (
        <div className="row-between">
          <span className="muted small">{s.previewShowing(list.length, preview.totalRecords)}</span>
          {paged.more && <button type="button" className="button button-secondary button-sm" onClick={paged.loadMore} disabled={paged.busy}>{paged.busy ? strings.common.loading : s.previewLoadMore}</button>}
        </div>
      )}
      {paged.error && <p className="field-error small" role="alert">{paged.error}</p>}
    </>
  );
}

function SmallTableBody({ caption, columns, rows, rowKey }) {
  return (
    <div className="table-scroll">
      <TableColumns id="tk.admin.MemberDetailDrawer"><table className="data-table">
        <caption className="visually-hidden">{caption}</caption>
        <thead><tr>{columns.map((col) => <th key={col.key} scope="col">{col.label}</th>)}</tr></thead>
        <tbody>
          {rows.map((row, i) => <tr key={rowKey ? rowKey(row) : i}>{columns.map((col) => <td key={col.key} className={col.className}>{col.render(row)}</td>)}</tr>)}
        </tbody>
      </table></TableColumns>
    </div>
  );
}

/**
 * Staff edit of a member's configurable profile fields (`PUT /admin/members/{id}/profile`): every
 * enabled field, AdminOnly and Staff fields included. Only changed keys are sent; errors keyed
 * `profile.<key>` and `profile.<key>[i].<subKey>` land on their inputs.
 */
export function EditProfileModal({ member, fields, onClose, onSaved }) {
  const { api } = useAdminAuth();
  const editable = customFields(fields);
  const [initial] = useState(() => toFormValues(editable, member.profile || {}));
  const [values, setValues] = useState(initial);
  const [errors, setErrors] = useState({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (ev) => {
    ev.preventDefault();
    const patch = buildProfilePatch(editable, values, initial, fields);
    const changedFields = editable.filter((f) => Object.prototype.hasOwnProperty.call(patch, f.key));
    const found = profileErrorsText(validateProfileValues(changedFields, values, { fields }));
    setErrors(found);
    if (Object.keys(found).length) {
      setError(s.profileEdit.fixBelow);
      return;
    }
    if (!Object.keys(patch).length) {
      onClose();
      return;
    }
    setBusy(true);
    setError('');
    try {
      const next = await api.updateMemberProfile(member.id, patch);
      onSaved(next);
    } catch (err) {
      const fe = serverFieldErrors(getFieldErrors(err));
      setErrors(fe);
      setError(Object.keys(fe).length ? s.profileEdit.fixBelow : errorMessage(err, strings.common.errorGeneric));
      setBusy(false);
    }
  };
  return (
    <Modal
      open
      size="lg"
      onClose={busy ? () => {} : onClose}
      title={s.profileEdit.title(member.displayName || member.email)}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form="member-profile-form" className="button button-primary" disabled={busy}>{busy ? s.profileEdit.saving : s.profileEdit.save}</button>
        </>
      )}
    >
      <form id="member-profile-form" className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={error} onDismiss={() => setError('')} />
        <p className="muted small">{s.profileEdit.hint}</p>
        {editable.length === 0 ? <p className="muted">{s.profileEdit.none}</p> : (
          <ProfileFieldsEditor
            fields={editable}
            allFields={fields}
            values={values}
            onChange={(key, value) => {
              setValues((v) => ({ ...v, [key]: value }));
              setErrors((prev) => {
                const next = { ...prev };
                for (const k of Object.keys(prev)) if (k === 'profile.' + key || k.startsWith('profile.' + key + '[')) delete next[k];
                return next;
              });
            }}
            errors={errors}
            disabled={busy}
            idPrefix="admin-profile"
          />
        )}
      </form>
    </Modal>
  );
}

/**
 * Member detail drawer: profile, membership, permissions, roles, comps, subscriptions, code
 * redemptions, sessions, and moderation actions (ban, mute, revoke sessions).
 */
function MemberDetailDrawer({ id, onClose, onChanged }) {
  const { property } = useProperty();
  const profileFields = useAdminProfileFields();
  const forumsOn = capabilityAvailable(property, 'forums');
  const { api, client } = useAdminAuth();
  const toast = useToast();
  const fetcher = useCallback((signal) => api.getMember(id, signal), [api, id]);
  const query = useApiQuery(fetcher);
  const [override, setOverride] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [jsonOpen, setJsonOpen] = useState(false);
  const [libraryOpen, setLibraryOpen] = useState(false);
  const [documentsOpen, setDocumentsOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const member = override || query.data;
  const loadPublicPage = useCallback((signal) => client.adminGetMemberPublicPage(id, signal), [client, id]);
  const savePublicPage = useCallback((body) => client.adminUpdateMemberPublicPage(id, body), [client, id]);

  const saved = (next) => {
    if (next && next.id) setOverride(next);
    else {
      setOverride(null);
      query.reload();
    }
    onChanged();
  };

  const done = (message) => {
    toast.success(message);
    setDialog(null);
    setOverride(null);
    query.reload();
    onChanged();
  };

  const simple = async (fn, message) => {
    setBusy(true);
    try {
      await fn();
      done(message);
    } catch (err) {
      toast.error(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const name = member ? (member.displayName || member.email || member.id) : id;
  const comps = previewOf(member && member.comps);
  const subscriptions = previewOf(member && member.subscriptions);
  const redemptions = previewOf(member && member.redemptions);
  const sessions = previewOf(member && member.sessions);
  const flags = (member && member.flags) || {};

  return (
    <Modal
      open
      variant="drawer"
      size="lg"
      onClose={onClose}
      title={name}
      subtitle={<CopyableId value={id} label={s.fields.id} />}
      headerExtra={member && (
        <button type="button" className="button button-secondary button-sm" onClick={() => setJsonOpen(true)}>
          <Icon name="code" size={14} /><span>{c.viewJson}</span>
        </button>
      )}
    >
      {query.loading && !member && <Loading />}
      {query.error && <ErrorBanner message={s.loadError + ' ' + errorMessage(query.error, '')} onRetry={query.reload} />}
      {member && (
        <div className="detail-stack">
          <section className="detail-block" aria-labelledby="member-profile-title">
            <div className="profile-head">
              <Avatar src={member.avatarUrl} name={member.displayName} size={56} />
              <div>
                <h3 id="member-profile-title">{member.displayName}</h3>
                {member.handle && <p className="muted mono">@{member.handle}</p>}
                <div className="badge-row">
                  {member.isBanned && <Badge tone="danger">{s.banned}</Badge>}
                  {flags.isMuted && <Badge tone="warning">{s.muted}</Badge>}
                  {!member.isBanned && !flags.isMuted && <Badge tone="success">{s.okStatus}</Badge>}
                </div>
              </div>
            </div>
            <dl className="kv-grid">
              <div className="kv-item"><dt>{s.fields.email}</dt><dd>{member.email} {member.emailVerified ? <Badge tone="success">{strings.account.verified}</Badge> : <Badge tone="warning">{strings.account.unverified}</Badge>}</dd></div>
              <div className="kv-item"><dt>{s.fields.signInMethods}</dt><dd>{(member.signInMethods || []).map((m) => en.signInMethod[m] || m).join(', ') || '-'}</dd></div>
              <div className="kv-item"><dt>{s.fields.ageAttested}</dt><dd>{member.ageAttested ? c.yes : c.no}</dd></div>
              <div className="kv-item"><dt>{s.fields.profileComplete}</dt><dd>{member.profileComplete ? c.yes : c.no}</dd></div>
              <div className="kv-item"><dt>{s.fields.created}</dt><dd title={member.createdUtc}>{formatDateTime(member.createdUtc)}</dd></div>
              <div className="kv-item"><dt>{s.fields.lastSeen}</dt><dd title={member.lastSeenUtc || ''}>{member.lastSeenUtc ? formatDateTime(member.lastSeenUtc) : c.never}</dd></div>
              {member.isBanned && <div className="kv-item"><dt>{s.fields.bannedReason}</dt><dd>{member.bannedReason || '-'}</dd></div>}
              {flags.isMuted && <div className="kv-item"><dt>{s.fields.mutedUntil}</dt><dd title={flags.mutedUntilUtc || ''}>{flags.mutedUntilUtc ? formatDateTime(flags.mutedUntilUtc) : '-'}</dd></div>}
            </dl>
            <div className="row-between">
              <h4 className="label-text">{s.profileEdit.sectionTitle}</h4>
              <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ kind: 'profile' })}>
                <Icon name="edit" size={14} /><span>{s.profileEdit.action}</span>
              </button>
            </div>
            <ProfileFieldList fields={customFields(profileFields)} profile={member.profile || {}} allFields={profileFields} showEmpty={customFields(profileFields)} emptyText={s.profileEdit.notSet} />
          </section>

          <section className="detail-block" aria-labelledby="member-membership-title">
            <div className="row-between">
              <h3 id="member-membership-title">{s.membershipTitle}</h3>
              {purchasesEnabled(property) && (
                <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ kind: 'comp' })}>
                  <Icon name="plus" size={14} /><span>{s.grantComp}</span>
                </button>
              )}
            </div>
            <dl className="kv-grid">
              <div className="kv-item"><dt>{strings.membershipApp.statusTitle}</dt><dd><Badge tone={member.membership && member.membership.isActive ? 'success' : 'neutral'}>{membershipText(member.membership)}</Badge></dd></div>
              {member.membership && member.membership.isActive && (
                <div className="kv-item"><dt>{strings.membershipApp.expires}</dt><dd>{member.membership.expiresUtc ? formatShortDate(member.membership.expiresUtc) : s.lifetime}</dd></div>
              )}
            </dl>
            <h4 className="label-text">{s.compsTitle}</h4>
            <SmallTable
              caption={s.compsTitle}
              preview={comps}
              rowKey={(row) => row.id}
              empty={s.compsEmpty}
              columns={[
                { key: 'grant', label: s.compColumns.grant, render: (row) => row.grantSummary || en.grantType[row.grantType] || row.grantType },
                { key: 'starts', label: s.compColumns.starts, className: 'nowrap', render: (row) => formatShortDate(row.startsUtc) },
                { key: 'ends', label: s.compColumns.ends, className: 'nowrap', render: (row) => (row.endsUtc ? formatShortDate(row.endsUtc) : s.lifetime) },
                { key: 'reason', label: s.compColumns.reason, render: (row) => row.reason || '-' },
                { key: 'status', label: s.compColumns.status, render: (row) => <Badge tone={redemptionTone(row.status || (row.isActive ? 'Active' : 'Expired'))}>{row.status || (row.isActive ? 'Active' : 'Ended')}</Badge> },
                {
                  key: 'actions', label: <span className="visually-hidden">{s.compColumns.actions}</span>,
                  render: (row) => ((row.status || (row.isActive ? 'Active' : '')) === 'Active'
                    ? <button type="button" className="button button-danger-outline button-sm" onClick={() => setDialog({ kind: 'endComp', comp: row })}>{s.endComp}</button>
                    : null)
                }
              ]}
            />
          </section>

          <FlagsEditor key={'flags-' + JSON.stringify(member.flags || {})} member={member} onSaved={saved} onMute={() => setDialog({ kind: 'mute' })} onUnmute={() => setDialog({ kind: 'unmute' })} />
          <FeedModeratorToggle key={'fm-' + JSON.stringify(member.roles || [])} member={member} onChanged={() => { setOverride(null); query.reload(); onChanged(); }} />
          <MetadataPanel client={client} targetType="User" targetId={member.id} isStaff initial={member.labels ? member : null} />
          {publicPagesConfig(property).enabled && (
            <PublicProfilePageSettings
              client={client} load={loadPublicPage} save={savePublicPage} objectId={member.id} idPrefix="member-public-page" headingLevel={3} className="detail-block"
              title={strings.publicPages.admin.title} intro={strings.publicPages.admin.intro} toggleLabel={strings.publicPages.admin.toggle} toggleHint={strings.publicPages.admin.toggleHint}
              savedText={strings.publicPages.admin.saved} onToast={(text) => toast.success(text)}
            />
          )}
          <RolesEditor key={'roles-' + JSON.stringify(member.roles || [])} member={member} onSaved={saved} />
          <MemberRankPanel userId={member.id} />
          {forumsOn ? <ForumMutesTab userId={member.id} compact /> : (
            <section className="detail-block" aria-labelledby="member-forum-mutes-title" data-testid="forum-mutes-coming-soon">
              <h3 id="member-forum-mutes-title">{strings.admin.forumMutes.title} <Badge tone="info">{strings.admin.placeholder.badge}</Badge></h3>
              <p className="muted small">{strings.admin.forumMutes.comingSoon}</p>
            </section>
          )}

          <section className="detail-block" aria-labelledby="member-subs-title">
            <h3 id="member-subs-title">{s.subscriptionsTitle}</h3>
            <SmallTable
              caption={s.subscriptionsTitle}
              preview={subscriptions}
              rowKey={(row) => row.id}
              empty={s.subscriptionsEmpty}
              columns={[
                { key: 'store', label: strings.admin.subscriptions.columns.store, render: (row) => en.store[row.store] || row.store },
                { key: 'status', label: strings.admin.subscriptions.columns.status, render: (row) => <Badge tone={subscriptionTone(row.status)}>{en.subscriptionStatus[row.status] || row.status}</Badge> },
                { key: 'end', label: strings.admin.subscriptions.columns.periodEnd, className: 'nowrap', render: (row) => formatShortDate(row.currentPeriodEndUtc) },
                { key: 'open', label: <span className="visually-hidden">{c.openDetail}</span>, render: (row) => <Link to={'/admin/subscriptions?subscription=' + encodeURIComponent(row.id)} className="small">{c.view}</Link> }
              ]}
            />
          </section>

          <section className="detail-block" aria-labelledby="member-redemptions-title">
            <h3 id="member-redemptions-title">{s.redemptionsTitle}</h3>
            <SmallTable
              caption={s.redemptionsTitle}
              preview={redemptions}
              rowKey={(row) => row.id}
              empty={s.redemptionsEmpty}
              columns={[
                { key: 'code', label: strings.admin.codes.columns.code, render: (row) => (row.signupCodeId
                  ? <Link to={'/admin/signup-codes/' + encodeURIComponent(row.signupCodeId)} className="mono">{row.code || row.signupCodeId}</Link>
                  : <code className="mono">{row.code}</code>) },
                { key: 'redeemed', label: strings.admin.codes.detail.redemptionColumns.redeemed, className: 'nowrap', render: (row) => formatShortDate(row.redeemedUtc) },
                { key: 'ends', label: strings.admin.codes.detail.redemptionColumns.ends, className: 'nowrap', render: (row) => (row.membershipEndsUtc ? formatShortDate(row.membershipEndsUtc) : s.lifetime) },
                { key: 'status', label: strings.admin.codes.detail.redemptionColumns.status, render: (row) => <Badge tone={redemptionTone(row.status)}>{en.redemptionStatus[row.status] || row.status}</Badge> }
              ]}
            />
          </section>

          {property.features && property.features.library !== false && (
            <section className="detail-block" aria-labelledby="member-library-title">
              <h3 id="member-library-title">{strings.admin.libraryAdmin.historyTitle}</h3>
              {libraryOpen ? <MemberLibraryHistory client={client} userId={member.id} /> : (
                <button type="button" className="button button-secondary button-sm" onClick={() => setLibraryOpen(true)}><Icon name="book" size={14} /><span>{strings.admin.libraryAdmin.showHistory}</span></button>
              )}
            </section>
          )}

          {documentsOf(property).length > 0 && (
            <section className="detail-block" aria-labelledby="member-documents-title">
              <h3 id="member-documents-title">{strings.documents.admin.drawer.title}</h3>
              {documentsOpen ? <MemberDocumentsHistory client={client} userId={member.id} /> : (
                <button type="button" className="button button-secondary button-sm" onClick={() => setDocumentsOpen(true)}><Icon name="clipboard" size={14} /><span>{strings.documents.admin.drawer.show}</span></button>
              )}
            </section>
          )}

          <section className="detail-block" aria-labelledby="member-sessions-title">
            <div className="row-between">
              <h3 id="member-sessions-title">{s.sessionsTitle}</h3>
              <button type="button" className="button button-danger-outline button-sm" onClick={() => setDialog({ kind: 'revoke' })} disabled={sessions.totalRecords === 0}>{s.revokeAll}</button>
            </div>
            <SmallTable
              caption={s.sessionsTitle}
              preview={sessions}
              rowKey={(row) => row.id}
              empty={s.sessionsEmpty}
              columns={[
                { key: 'client', label: s.sessionColumns.client, render: (row) => row.client },
                { key: 'created', label: s.sessionColumns.created, className: 'nowrap', render: (row) => <span title={row.createdUtc}>{formatShortDateTime(row.createdUtc)}</span> },
                { key: 'lastUsed', label: s.sessionColumns.lastUsed, className: 'nowrap', render: (row) => <span title={row.lastUsedUtc || ''}>{row.lastUsedUtc ? formatShortDateTime(row.lastUsedUtc) : '-'}</span> },
                { key: 'ip', label: s.sessionColumns.sourceIp, render: (row) => <code className="mono">{row.sourceIp}</code> }
              ]}
            />
          </section>

          <section className="detail-block detail-danger" aria-labelledby="member-danger-title">
            <h3 id="member-danger-title">{s.dangerTitle}</h3>
            <div className="row-actions">
              {member.isBanned
                ? <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ kind: 'unban' })} disabled={busy}><Icon name="ban" size={14} /><span>{s.actions.unban}</span></button>
                : <button type="button" className="button button-danger button-sm" onClick={() => setDialog({ kind: 'ban' })} disabled={busy}><Icon name="ban" size={14} /><span>{s.actions.ban}</span></button>}
              {flags.isMuted
                ? <button type="button" className="button button-secondary button-sm" onClick={() => setDialog({ kind: 'unmute' })} disabled={busy}><Icon name="mute" size={14} /><span>{s.actions.unmute}</span></button>
                : <button type="button" className="button button-danger-outline button-sm" onClick={() => setDialog({ kind: 'mute' })} disabled={busy}><Icon name="mute" size={14} /><span>{s.actions.mute}</span></button>}
              <button type="button" className="button button-danger-outline button-sm" onClick={() => setDialog({ kind: 'revoke' })} disabled={busy}><Icon name="logout" size={14} /><span>{s.actions.revokeSessions}</span></button>
            </div>
          </section>
        </div>
      )}

      {member && dialog && dialog.kind === 'profile' && (
        <EditProfileModal member={member} fields={profileFields} onClose={() => setDialog(null)} onSaved={(next) => { toast.success(s.profileEdit.saved); setDialog(null); saved(next); }} />
      )}
      {member && dialog && dialog.kind === 'comp' && <GrantCompModal member={member} onClose={() => setDialog(null)} onDone={done} />}
      {member && dialog && dialog.kind === 'mute' && <MuteModal member={member} onClose={() => setDialog(null)} onDone={done} />}
      {member && dialog && dialog.kind === 'ban' && (
        <ReasonModal open title={s.banTitle(name)} message={s.banBody} confirmLabel={s.actions.ban} onCancel={() => setDialog(null)} onConfirm={async (reason) => { await api.banMember(member.id, reason); done(s.bannedToast); }} />
      )}
      {member && dialog && dialog.kind === 'endComp' && (
        <ReasonModal open title={s.endCompTitle} message={s.endCompBody} confirmLabel={s.endComp} onCancel={() => setDialog(null)} onConfirm={async (reason) => { await api.endComp(member.id, dialog.comp.id, reason); done(s.compEnded); }} />
      )}
      <ConfirmModal
        open={Boolean(member && dialog && ['unban', 'unmute', 'revoke'].includes(dialog.kind))}
        title={dialog && dialog.kind === 'unban' ? s.unbanTitle(name) : dialog && dialog.kind === 'unmute' ? s.unmuteTitle(name) : s.revokeTitle}
        message={dialog && dialog.kind === 'unban' ? s.unbanBody : dialog && dialog.kind === 'unmute' ? s.unmuteBody : s.revokeBody(name)}
        confirmLabel={dialog && dialog.kind === 'unban' ? s.actions.unban : dialog && dialog.kind === 'unmute' ? s.actions.unmute : s.actions.revokeSessions}
        danger={Boolean(dialog && dialog.kind === 'revoke')}
        busy={busy}
        onCancel={() => setDialog(null)}
        onConfirm={() => {
          if (dialog.kind === 'unban') simple(() => api.unbanMember(member.id), s.unbannedToast);
          else if (dialog.kind === 'unmute') simple(() => api.unmuteMember(member.id), s.unmutedToast);
          else simple(() => api.revokeMemberSessions(member.id), s.revoked);
        }}
      />
      <JsonViewerModal open={jsonOpen} onClose={() => setJsonOpen(false)} subtitle={id} value={member} />
    </Modal>
  );
}

export default MemberDetailDrawer;
