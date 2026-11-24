import { useCallback, useMemo, useState } from 'react';
import { MemberThreadOrderings, ThreadCreationModes, ThreadTypes, audienceOptions, composerAudiences, metadataForSave, metadataOf, moveItem, validateForumForm } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useForumList from '../../hooks/useForumList.js';
import useTableState from '../../hooks/useTableState.js';
import useCursorList from '../../hooks/useCursorList.js';
import { ADMIN_FORUM_FILTERS, buildAdminForumsQuery } from '../../utils/adminQuery.js';
import { optionsFrom } from '../../utils/labels.js';
import { authorName, errorText, composerErrorText } from '../../utils/engagement.js';
import { appForumPath, forumErrors } from '../../utils/forums.js';
import { formatNumber } from '../../utils/format.js';
import PageHeader from '../../components/PageHeader.jsx';
import AdminFilterPanel from '../../components/AdminFilterPanel.jsx';
import MetadataEditor from '../../components/MetadataEditor.jsx';
import ActionMenu from '../../components/ActionMenu.jsx';
import ConfirmModal from '../../components/ConfirmModal.jsx';
import JsonViewerModal from '../../components/JsonViewerModal.jsx';
import CopyableId from '../../components/CopyableId.jsx';
import Modal from '../../components/Modal.jsx';
import Icon from '../../components/Icon.jsx';
import { Badge } from '../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../components/States.jsx';
import MarkdownBody from '../../components/feed/MarkdownBody.jsx';
import AttachmentList from '../../components/feed/AttachmentList.jsx';
import useMediaUploads from '../../components/feed/useMediaUploads.js';
import ForumIcon from '../../components/forums/ForumIcon.jsx';
import TableColumns from '../../components/TableColumns.jsx';

const s = strings.admin.forumsAdmin;
const c = strings.admin.common;
const col = s.columns;
const fo = strings.forums;

function emptyForm(property) {
  const audiences = audienceOptions(property);
  // New forums default to Members when forums are gated, else to the property default audience.
  const defaultAudience = composerAudiences(property, { isSystemAdmin: true, feature: 'forums' }).defaultAudience;
  return {
    name: '', slug: '', description: '', guidelines: '', iconEmoji: '', iconMediaId: null, color: '', section: '',
    audience: audiences.includes(defaultAudience) ? defaultAudience : audiences[0], threadCreation: 'Members', allowedThreadTypes: ThreadTypes.slice(),
    defaultOrdering: 'Active', allowMedia: true, allowVideo: true, maxOpenFeedbackPerMember: '', labels: [], tags: {}
  };
}

function formFrom(forum, property) {
  if (!forum) return emptyForm(property);
  const icon = forum.icon || {};
  return {
    name: forum.name || '', slug: forum.slug || '', description: forum.description || '', guidelines: forum.guidelines || '', iconEmoji: icon.emoji || '', iconMediaId: icon.mediaId || null,
    color: forum.color || '', section: forum.section || '', audience: forum.audience || 'Members', threadCreation: forum.threadCreation || 'Members',
    allowedThreadTypes: forum.allowedThreadTypes || ThreadTypes.slice(), defaultOrdering: forum.defaultOrdering || 'Active', allowMedia: forum.allowMedia !== false, allowVideo: forum.allowVideo !== false,
    maxOpenFeedbackPerMember: forum.maxOpenFeedbackPerMember === null || forum.maxOpenFeedbackPerMember === undefined ? '' : String(forum.maxOpenFeedbackPerMember),
    ...metadataOf(forum)
  };
}

/** The POST or PUT /admin/forums body for a form (section 3.2). */
export function forumBody(form) {
  const body = {
    name: form.name.trim(),
    description: form.description.trim(),
    guidelines: form.guidelines.trim(),
    icon: form.iconMediaId ? { emoji: null, mediaId: form.iconMediaId } : { emoji: form.iconEmoji.trim() || null, mediaId: null },
    color: form.color || null,
    section: form.section.trim() || null,
    audience: form.audience,
    threadCreation: form.threadCreation,
    allowedThreadTypes: form.allowedThreadTypes,
    defaultOrdering: form.defaultOrdering,
    allowMedia: form.allowMedia,
    allowVideo: form.allowMedia && form.allowVideo,
    ...metadataForSave({ labels: form.labels, tags: form.tags }, { isStaff: true })
  };
  if (form.slug.trim()) body.slug = form.slug.trim();
  if (String(form.maxOpenFeedbackPerMember).trim() !== '') body.maxOpenFeedbackPerMember = Number(form.maxOpenFeedbackPerMember);
  return body;
}

/** Create and edit drawer with every forum field (section 12). */
function ForumEditor({ forum, onClose, onSaved }) {
  const { client } = useAdminAuth();
  const { property } = useProperty();
  const [form, setForm] = useState(() => formFrom(forum, property));
  const [problems, setProblems] = useState({});
  const [metaProblems, setMetaProblems] = useState([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState(false);
  const uploads = useMediaUploads({ client, maxCount: 1, maxVideos: 0, allowMedia: true, allowVideo: false, isAdmin: true, onError: setError });
  const set = (changes) => setForm((f) => ({ ...f, ...changes }));
  const audiences = audienceOptions(property);
  const fields = s.fields;
  const id = forum ? 'forum-edit-' + forum.id : 'forum-new';
  const newIcon = uploads.mediaIds[0] || null;

  const submit = async (e) => {
    e.preventDefault();
    const next = newIcon ? { ...form, iconMediaId: newIcon } : form;
    const found = validateForumForm(next);
    setProblems(found);
    if (Object.keys(found).length || metaProblems.length) return;
    if (uploads.busy) {
      setError(strings.composer.waitForUploads);
      return;
    }
    setBusy(true);
    setError('');
    try {
      const saved = forum ? await client.adminUpdateForum(forum.id, forumBody(next)) : await client.adminCreateForum(forumBody(next));
      onSaved(saved);
    } catch (err) {
      const fe = (err && err.fieldErrors) || {};
      setProblems((p) => ({ ...p, ...fe }));
      setError(errorText(err, s.errors));
      setBusy(false);
    }
  };

  const text = (code) => (code ? s.problems[code] || code : '');
  const field = (key, label, input, hint) => (
    <div className="field">
      <label className="label-text" htmlFor={id + '-' + key}>{label}</label>
      {input}
      {hint && <span className="muted small">{hint}</span>}
      {problems[key] && <span className="field-error" role="alert">{text(problems[key])}</span>}
    </div>
  );
  const toggle = (key, label, disabled = false) => (
    <label className="toggle">
      <input type="checkbox" checked={Boolean(form[key])} onChange={(e) => set({ [key]: e.target.checked })} disabled={busy || disabled} />
      <span className="toggle-track"><span className="toggle-thumb" /></span>
      <span>{label}</span>
    </label>
  );

  return (
    <Modal open variant="drawer" size="lg" onClose={busy ? () => {} : onClose} title={forum ? s.editTitle(forum.name) : s.createTitle}
      footer={(
        <>
          <button type="button" className="button button-secondary" onClick={onClose} disabled={busy}>{strings.common.cancel}</button>
          <button type="submit" form={id} className="button button-primary" disabled={busy}>{busy ? strings.common.loading : strings.common.save}</button>
        </>
      )}
    >
      <form id={id} className="stack-form" onSubmit={submit} noValidate>
        <ErrorBanner message={composerErrorText(error, uploads)} onDismiss={() => setError('')} />
        {field('name', fields.name, <input id={id + '-name'} value={form.name} onChange={(e) => set({ name: e.target.value })} maxLength={80} disabled={busy} aria-invalid={Boolean(problems.name)} />)}
        {field('slug', fields.slug, <input id={id + '-slug'} className="mono" value={form.slug} onChange={(e) => set({ slug: e.target.value.toLowerCase() })} maxLength={60} disabled={busy} aria-invalid={Boolean(problems.slug)} />, fields.slugHint)}
        {field('description', fields.description, <textarea id={id + '-description'} rows={2} value={form.description} onChange={(e) => set({ description: e.target.value })} disabled={busy} />)}
        <div className="field">
          <div className="row-between">
            <label className="label-text" htmlFor={id + '-guidelines'}>{fields.guidelines}</label>
            <button type="button" className="link-button small" aria-pressed={preview} onClick={() => setPreview((v) => !v)}>{fields.guidelinesPreview}</button>
          </div>
          <textarea id={id + '-guidelines'} rows={5} value={form.guidelines} onChange={(e) => set({ guidelines: e.target.value })} disabled={busy} aria-invalid={Boolean(problems.guidelines)} />
          {preview && <div className="panel"><MarkdownBody body={form.guidelines} allowHeadings={false} linkMentions={false} /></div>}
          {problems.guidelines && <span className="field-error" role="alert">{text(problems.guidelines)}</span>}
        </div>
        <div className="composer-options">
          {field('iconEmoji', fields.iconEmoji, <input id={id + '-iconEmoji'} value={form.iconEmoji} onChange={(e) => set({ iconEmoji: e.target.value })} maxLength={8} disabled={busy || Boolean(form.iconMediaId || newIcon)} />)}
          <div className="field">
            <span className="label-text">{fields.iconImage}</span>
            {form.iconMediaId && !newIcon ? (
              <span className="badge-row"><ForumIcon forum={forum} size={32} /><button type="button" className="link-button small" onClick={() => set({ iconMediaId: null })}>{fields.removeIcon}</button></span>
            ) : (
              <AttachmentList uploads={uploads} allowMedia allowVideo={false} maxCount={1} label={fields.iconImage} disabled={busy} showAlt={false} />
            )}
            <span className="muted small">{fields.iconImageHint}</span>
          </div>
          <div className="field field-compact">
            <label className="label-text" htmlFor={id + '-color'}>{fields.color}</label>
            <input id={id + '-color'} type="color" value={form.color || '#888888'} onChange={(e) => set({ color: e.target.value.toUpperCase() })} disabled={busy || !form.color} />
            <label className="small"><input type="checkbox" checked={!form.color} onChange={(e) => set({ color: e.target.checked ? '' : '#1F5FBF' })} disabled={busy} /> {fields.noColor}</label>
          </div>
        </div>
        {field('section', fields.section, <input id={id + '-section'} value={form.section} onChange={(e) => set({ section: e.target.value })} maxLength={80} disabled={busy} />, fields.sectionHint)}
        <div className="composer-options">
          {field('audience', fields.audience, (
            <select id={id + '-audience'} value={form.audience} onChange={(e) => set({ audience: e.target.value })} disabled={busy}>
              {audiences.map((a) => <option key={a} value={a}>{fo.audiences[a]}</option>)}
            </select>
          ))}
          {field('threadCreation', fields.threadCreation, (
            <select id={id + '-threadCreation'} value={form.threadCreation} onChange={(e) => set({ threadCreation: e.target.value })} disabled={busy}>
              {ThreadCreationModes.map((m) => <option key={m} value={m}>{s.threadCreation[m]}</option>)}
            </select>
          ))}
          {field('defaultOrdering', fields.defaultOrdering, (
            <select id={id + '-defaultOrdering'} value={form.defaultOrdering} onChange={(e) => set({ defaultOrdering: e.target.value })} disabled={busy}>
              {MemberThreadOrderings.concat('CreatedAscending').map((o) => <option key={o} value={o}>{fo.orderings[o]}</option>)}
            </select>
          ))}
        </div>
        <fieldset className="reason-list" aria-invalid={Boolean(problems.allowedThreadTypes)}>
          <legend className="label-text">{fields.allowedThreadTypes}</legend>
          {ThreadTypes.map((type) => (
            <label key={type} className="small">
              <input type="checkbox" checked={form.allowedThreadTypes.includes(type)} onChange={(e) => set({ allowedThreadTypes: e.target.checked ? ThreadTypes.filter((x) => x === type || form.allowedThreadTypes.includes(x)) : form.allowedThreadTypes.filter((x) => x !== type) })} disabled={busy} /> {fo.types[type]}
            </label>
          ))}
          {problems.allowedThreadTypes && <span className="field-error" role="alert">{text(problems.allowedThreadTypes)}</span>}
        </fieldset>
        <div className="composer-options">
          {toggle('allowMedia', fields.allowMedia)}
          {toggle('allowVideo', fields.allowVideo, !form.allowMedia)}
          {form.allowedThreadTypes.includes('Feedback') && field('maxOpenFeedbackPerMember', fields.maxOpenFeedback, <input id={id + '-maxOpenFeedbackPerMember'} type="number" min={0} max={100} value={form.maxOpenFeedbackPerMember} onChange={(e) => set({ maxOpenFeedbackPerMember: e.target.value })} disabled={busy} />, fields.maxOpenFeedbackHint)}
        </div>
        <MetadataEditor value={{ labels: form.labels, tags: form.tags }} onChange={(next, found) => { set(next); setMetaProblems(found); }} isStaff client={client} targetType="Forum" disabled={busy} />
      </form>
    </Modal>
  );
}

/**
 * Resolve what an admin typed (a member id, an exact handle with or without @, or an exact email) to a member id.
 * Throws an Error with a readable message when nothing matches exactly.
 */
export async function resolveMemberId(client, value) {
  const raw = String(value || '').trim();
  if (/^usr_/.test(raw)) return raw;
  const needle = raw.replace(/^@/, '').toLowerCase();
  const page = await client.listMembers({ search: needle, maxResults: 25 });
  const rows = (page && Array.isArray(page.objects)) ? page.objects : [];
  const match = rows.find((m) => (m.handle && m.handle.toLowerCase() === needle) || (m.email && m.email.toLowerCase() === needle));
  if (!match) throw new Error(s.grantNoMatch(raw));
  return match.id;
}

/** Moderators covering a forum, with Grant (this forum or all forums) and Revoke (section 11.6). */
function ModeratorsDrawer({ forum, onClose }) {
  const { client } = useAdminAuth();
  const toast = useToast();
  const [userId, setUserId] = useState('');
  const [scope, setScope] = useState('forum');
  const [busy, setBusy] = useState(false);
  const fetchPage = useCallback((cursor, signal) => client.adminEnumerateForumModerators(forum.id, { continuationToken: cursor, maxResults: 50 }, signal), [client, forum.id]);
  const list = useCursorList(fetchPage);
  const run = async (fn, message) => {
    setBusy(true);
    try {
      await fn();
      toast.success(message);
      list.refresh();
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal open variant="drawer" size="md" onClose={onClose} title={s.moderatorsTitle(forum.name)}>
      <form className="inline-form" onSubmit={(e) => { e.preventDefault(); if (userId.trim()) run(async () => client.grantForumModerator(await resolveMemberId(client, userId), scope === 'all' ? '*' : forum.id), s.granted).then(() => setUserId('')); }}>
        <label className="label-text" htmlFor="grant-moderator">{s.grantLabel}</label>
        <input id="grant-moderator" value={userId} onChange={(e) => setUserId(e.target.value)} placeholder="@handle" autoComplete="off" disabled={busy} />
        <label className="label-text" htmlFor="grant-scope">{s.grantScope}</label>
        <select id="grant-scope" value={scope} onChange={(e) => setScope(e.target.value)} disabled={busy}>
          <option value="forum">{s.scopeForum}</option>
          <option value="all">{s.scopeAll}</option>
        </select>
        <button type="submit" className="button button-primary button-sm" disabled={busy || !userId.trim()}>{s.grant}</button>
      </form>
      <ErrorBanner message={list.error ? errorText(list.error) : ''} onRetry={list.refresh} />
      {list.loading && <Loading inline />}
      {list.loaded && !list.items.length && !list.loading && <p className="muted small">{s.moderatorsEmpty}</p>}
      <ul className="link-list">
        {list.items.map((row) => {
          const all = row.scope === 'AllForums';
          return (
            <li key={(row.user && row.user.id) + ':' + (row.scope || '')} className="row-between">
              <span>{authorName(row.user)} <Badge tone={all ? 'info' : 'neutral'}>{all ? s.scopeAll : s.scopeForum}</Badge></span>
              <button type="button" className="button button-danger-outline button-sm" disabled={busy} onClick={() => run(() => client.revokeForumModerator(row.user.id, all ? '*' : forum.id), s.revoked)}>{s.revoke}</button>
            </li>
          );
        })}
      </ul>
      {list.loaded && !list.done && !list.loading && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{s.moreModerators}</button>}
    </Modal>
  );
}

/**
 * /admin/forums (PHASE3_CONTRACT.md section 12): every forum with section, archived, audience,
 * search, label, and tag filters; drag (or arrow buttons) to reorder, saved with
 * `PUT /admin/forums/order`; the create and edit drawer; and the row actions View, Edit, View JSON,
 * Archive or Unarchive, Delete (only when empty), and Moderators. A property has at most
 * `forums.maxForums` forums, so the table holds the whole bounded set (paged by continuation).
 */
function ForumsAdminView() {
  const { client } = useAdminAuth();
  const { property } = useProperty();
  const toast = useToast();
  const table = useTableState({ storageKey: 'tk.admin.forums', filterKeys: ADMIN_FORUM_FILTERS, defaultSort: '' });
  const query = useMemo(() => {
    const q = buildAdminForumsQuery(table.filters, { meta: table.meta, pageSize: 100 });
    return { search: q.search, labels: q.labels, labelMatch: q.labelMatch, tags: q.tags, filters: q.filters };
  }, [table.filters, table.meta]);
  const list = useForumList(client, query, { admin: true });
  const [order, setOrder] = useState(null);
  const [dragIndex, setDragIndex] = useState(null);
  const [editor, setEditor] = useState(null);
  const [moderators, setModerators] = useState(null);
  const [jsonRow, setJsonRow] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const [busy, setBusy] = useState(false);

  const sorted = useMemo(() => list.forums.slice().sort((a, b) => Number(a.isArchived) - Number(b.isArchived) || (a.sortOrder || 0) - (b.sortOrder || 0)), [list.forums]);
  const rows = order || sorted;
  const canReorder = !table.hasFilters;
  const active = rows.filter((f) => !f.isArchived);
  const reorder = (from, to) => {
    if (!canReorder || from === null || to === null || rows[to].isArchived || rows[from].isArchived) return;
    setOrder(moveItem(rows, from, to));
  };

  const run = async (fn, message, after) => {
    setBusy(true);
    try {
      await fn();
      toast.success(message);
      if (after) after();
      setOrder(null);
      list.reload();
    } catch (err) {
      toast.error(errorText(err, { ...forumErrors(), ...s.errors }));
    } finally {
      setBusy(false);
      setConfirm(null);
    }
  };

  const filterFields = [
    { key: 'search', label: s.searchLabel, placeholder: s.searchPlaceholder, grow: true },
    { key: 'section', label: s.section },
    { key: 'isArchived', label: s.archived, type: 'select', options: optionsFrom(s.archivedOptions) },
    { key: 'audience', label: s.audience, type: 'select', options: audienceOptions(property).map((a) => ({ value: a, label: fo.audiences[a] })) }
  ];

  const menu = (row) => {
    const items = [
      { label: c.view, icon: 'eye', onSelect: () => window.open(appForumPath(row), '_blank', 'noopener') },
      { label: c.edit, icon: 'edit', onSelect: () => setEditor({ forum: row }) },
      { label: c.viewJson, icon: 'code', onSelect: () => setJsonRow(row) },
      { label: s.moderators, icon: 'shield', onSelect: () => setModerators(row) },
      row.isArchived
        ? { label: s.unarchive, icon: 'refresh', onSelect: () => run(() => client.adminUnarchiveForum(row.id), s.unarchived) }
        : { label: s.archive, icon: 'lock', onSelect: () => setConfirm({ kind: 'archive', row }) }
    ];
    if (!row.threadCount) items.push({ label: strings.common.delete, icon: 'trash', danger: true, onSelect: () => setConfirm({ kind: 'delete', row }) });
    return items;
  };

  return (
    <div className="view">
      <PageHeader
        title={strings.admin.routes.forums.label}
        summary={strings.admin.routes.forums.summary}
        actions={<button type="button" className="button button-primary button-sm" onClick={() => setEditor({})}><Icon name="plus" size={14} /><span>{s.create}</span></button>}
      />
      <section className="panel">
        <AdminFilterPanel fields={filterFields} table={table} client={client} targetType="Forum" />
      </section>
      <section className="table-section">
        <div className="table-toolbar">
          <p className="muted small toolbar-left">{canReorder ? s.reorderHint : ''}</p>
          <div className="toolbar-right">
            {order && <button type="button" className="button button-secondary button-sm" onClick={() => setOrder(null)} disabled={busy}>{s.resetOrder}</button>}
            {order && <button type="button" className="button button-primary button-sm" onClick={() => run(() => client.adminOrderForums(active.map((f) => f.id)), s.orderSaved)} disabled={busy}>{s.saveOrder}</button>}
            <button type="button" className="icon-button" onClick={list.reload} aria-label={strings.common.refresh} title={strings.common.refresh}><Icon name="refresh" className={list.loading ? 'spin' : ''} /></button>
          </div>
        </div>
        {order && <Banner tone="info">{s.orderChanged}</Banner>}
        <ErrorBanner message={list.error ? c.unavailable(strings.admin.routes.forums.label) + ' ' + errorText(list.error, {}, '') : ''} onRetry={list.reload} />
        <div className="table-frame">
          <div className="table-scroll">
            <TableColumns id="tk.admin.ForumsAdminView"><table className="data-table">
              <caption className="visually-hidden">{strings.admin.routes.forums.label}</caption>
              <thead>
                <tr>
                  <th scope="col">{col.order}</th>
                  <th scope="col">{col.forum}</th>
                  <th scope="col">{col.section}</th>
                  <th scope="col">{col.audience}</th>
                  <th scope="col">{col.types}</th>
                  <th scope="col" className="num">{col.threads}</th>
                  <th scope="col" className="num">{col.last30}</th>
                  <th scope="col" className="num">{col.reports}</th>
                  <th scope="col" className="num">{col.moderators}</th>
                  <th scope="col">{col.id}</th>
                  <th scope="col" className="cell-actions"><span className="visually-hidden">{col.actions}</span></th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, i) => {
                  const movable = canReorder && !row.isArchived;
                  return (
                    <tr
                      key={row.id}
                      className={'forum-order-row' + (dragIndex === i ? ' dragging' : '')}
                      draggable={movable}
                      onDragStart={() => setDragIndex(i)}
                      onDragOver={(e) => { if (movable) e.preventDefault(); }}
                      onDrop={(e) => { e.preventDefault(); reorder(dragIndex, i); setDragIndex(null); }}
                      onDragEnd={() => setDragIndex(null)}
                    >
                      <td className="nowrap">
                        {movable ? (
                          <>
                            <button type="button" className="icon-button" aria-label={s.moveUp(row.name)} title={s.moveUp(row.name)} disabled={i === 0} onClick={() => reorder(i, i - 1)}><Icon name="chevronUp" size={14} /></button>
                            <button type="button" className="icon-button" aria-label={s.moveDown(row.name)} title={s.moveDown(row.name)} disabled={i === active.length - 1} onClick={() => reorder(i, i + 1)}><Icon name="chevronDown" size={14} /></button>
                          </>
                        ) : <span className="muted">-</span>}
                      </td>
                      <td>
                        <span className="badge-row"><ForumIcon forum={row} size={28} /><strong>{row.name}</strong>{row.isArchived && <Badge tone="neutral">{fo.archived}</Badge>}</span>
                        <span className="muted small mono">{row.slug}</span>
                      </td>
                      <td>{row.section || <span className="muted">-</span>}</td>
                      <td><Badge tone={row.audience === 'Public' ? 'success' : 'neutral'}>{fo.audiences[row.audience] || row.audience}</Badge></td>
                      <td className="small">{(row.allowedThreadTypes || []).map((x) => fo.types[x] || x).join(', ')}</td>
                      <td className="num">{formatNumber(row.threadCount || 0)}</td>
                      <td className="num">{formatNumber(row.threadsLast30d || 0)}</td>
                      <td className="num">{row.openReportCount ? <Badge tone="warning">{row.openReportCount}</Badge> : <span className="muted">0</span>}</td>
                      <td className="num">{formatNumber(row.moderatorCount || 0)}</td>
                      <td className="cell-id"><CopyableId value={row.id} label={col.id} /></td>
                      <td className="cell-actions"><ActionMenu label={c.rowActions(row.name)} items={menu(row)} /></td>
                    </tr>
                  );
                })}
                {!list.loading && !rows.length && !list.error && <tr><td colSpan={11} className="table-empty">{table.hasFilters ? s.emptyFiltered : s.empty}</td></tr>}
              </tbody>
            </table></TableColumns>
            {list.loading && <div className="table-loading"><Loading inline /></div>}
          </div>
        </div>
      </section>
      {editor && <ForumEditor forum={editor.forum || null} onClose={() => setEditor(null)} onSaved={() => { toast.success(editor.forum ? s.saved : s.createdToast); setEditor(null); setOrder(null); list.reload(); }} />}
      {moderators && <ModeratorsDrawer forum={moderators} onClose={() => setModerators(null)} />}
      <JsonViewerModal open={Boolean(jsonRow)} onClose={() => setJsonRow(null)} subtitle={jsonRow && jsonRow.id} value={jsonRow} />
      <ConfirmModal
        open={Boolean(confirm)}
        title={confirm ? (confirm.kind === 'archive' ? s.archiveTitle(confirm.row.name) : s.deleteTitle(confirm.row.name)) : ''}
        message={confirm ? (confirm.kind === 'archive' ? s.archiveBody : s.deleteBody) : ''}
        confirmLabel={confirm && confirm.kind === 'archive' ? s.archive : strings.common.delete}
        busy={busy}
        onCancel={() => setConfirm(null)}
        onConfirm={() => (confirm.kind === 'archive' ? run(() => client.adminArchiveForum(confirm.row.id), s.archivedToast) : run(() => client.adminDeleteForum(confirm.row.id), s.deleted))}
      />
    </div>
  );
}

export default ForumsAdminView;
