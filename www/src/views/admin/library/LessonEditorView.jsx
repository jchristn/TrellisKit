import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { LIBRARY_LIMITS, catalogFeatureEnabled, languageOptions, LessonKinds, ResourceKinds, formatDuration, libraryLevels, metadataForSave, metadataOf } from '@trelliskit/shared';
import strings from '../../../strings.js';
import { useAdminAuth } from '../../../context/AdminAuthContext.jsx';
import { useToast } from '../../../context/ToastContext.jsx';
import useApiQuery from '../../../hooks/useApiQuery.js';
import useCursorList from '../../../hooks/useCursorList.js';
import PageHeader from '../../../components/PageHeader.jsx';
import Icon from '../../../components/Icon.jsx';
import Modal from '../../../components/Modal.jsx';
import ConfirmModal from '../../../components/ConfirmModal.jsx';
import MetadataEditor from '../../../components/MetadataEditor.jsx';
import AudienceField, { useAudiencePicker } from '../../../components/AudienceField.jsx';
import { Badge } from '../../../components/Badges.jsx';
import { Banner, ErrorBanner, Loading } from '../../../components/States.jsx';
import { Field, Toggle } from '../../../components/admin/AdminParts.jsx';
import FileUploadButton from '../../../components/admin/FileUploadButton.jsx';
import { useAllSeries } from './LibraryAdminView.jsx';
import { authorName, errorText } from '../../../utils/engagement.js';
import { formatDateTime } from '../../../utils/format.js';
import { localInputToIso } from '../../../utils/time.js';
import { appPaths, phase4Errors } from '../../../utils/phase4.js';
import { useProperty } from '../../../context/PropertyContext.jsx';
import TableColumns from '../../../components/TableColumns.jsx';
import LessonTechniquesEditor from './LessonTechniquesEditor.jsx';

const s = strings.admin.libraryAdmin;
const lib = strings.library;
const POLL_MS = 5000;

function parseTime(text) {
  const parts = String(text || '').trim().split(':').map(Number);
  if (parts.some((p) => !Number.isFinite(p) || p < 0)) return null;
  return parts.reduce((total, p) => total * 60 + p, 0);
}

/** The PUT /admin/lessons/{id} body from the editor form (markers, resources, captions replace the bounded lists). */
export function lessonBody(form) {
  return {
    title: form.title.trim(), summary: form.summary.trim(), description: form.description, kind: form.kind, levels: form.levels, audience: form.audience,
    downloadable: Boolean(form.downloadable), commentsEnabled: Boolean(form.commentsEnabled), notifyStaffOnComment: Boolean(form.notifyStaffOnComment),
    ...(form.slug.trim() ? { slug: form.slug.trim() } : {}),
    ...(form.videoMediaId !== undefined ? { videoMediaId: form.videoMediaId } : {}),
    markers: form.markers.filter((m) => m.label.trim() && parseTime(m.time) !== null).map((m) => ({ seconds: parseTime(m.time), label: m.label.trim().slice(0, LIBRARY_LIMITS.markerLabelMax) })).sort((a, b) => a.seconds - b.seconds),
    resources: form.resources.filter((r) => r.title.trim()).map((r, i) => ({ kind: r.kind, title: r.title.trim(), mediaId: r.kind === 'Document' ? r.mediaId || null : null, url: r.kind === 'Link' ? r.url.trim() || null : null, text: r.kind === 'Reference' ? r.text.trim() || null : null, position: i + 1 })),
    captions: form.captions.filter((cap) => cap.mediaId && cap.language.trim()).map((cap, i) => ({ language: cap.language.trim(), label: cap.label.trim() || cap.language.trim(), mediaId: cap.mediaId, position: i + 1 })),
    ...metadataForSave({ labels: form.labels, tags: form.tags }, { isStaff: true })
  };
}

function formFrom(lesson) {
  return {
    title: lesson.title || '', slug: lesson.slug || '', summary: lesson.summary || '', description: lesson.description || '', kind: lesson.kind || 'Video',
    levels: (lesson.levels || []).map((l) => (typeof l === 'object' ? l.key : l)), audience: lesson.audience || 'Members', downloadable: lesson.downloadable !== false,
    commentsEnabled: lesson.commentsEnabled !== false, notifyStaffOnComment: Boolean(lesson.notifyStaffOnComment), videoMediaId: undefined,
    markers: (lesson.markers || []).map((m) => ({ time: formatDuration(m.seconds), label: m.label })),
    resources: (lesson.resources || []).map((r) => ({ kind: r.kind, title: r.title || '', mediaId: r.media ? r.media.id : null, mediaName: r.media ? r.media.fileName || r.media.id : '', url: r.url || '', text: r.text || '' })),
    captions: (lesson.captions || []).map((cap) => ({ language: cap.language || '', label: cap.label || '', mediaId: cap.media ? cap.media.id : null })),
    ...metadataOf(lesson)
  };
}

function Viewers({ client, lessonId }) {
  const fetchPage = useCallback((cursor, signal) => client.adminEnumerateLessonViewers(lessonId, { continuationToken: cursor, maxResults: 50 }, signal), [client, lessonId]);
  const list = useCursorList(fetchPage);
  return (
    <section className="panel stack" aria-labelledby="lesson-viewers">
      <h2 id="lesson-viewers" className="section-title">{s.viewersTitle}</h2>
      <p className="muted small">{s.historyAudited}</p>
      <ErrorBanner message={list.error ? errorText(list.error, phase4Errors()) : ''} onRetry={list.refresh} />
      {list.loading && !list.loaded && <Loading inline />}
      {list.loaded && !list.items.length && <p className="muted small">{s.viewersEmpty}</p>}
      {list.items.length > 0 && (
        <TableColumns id="tk.admin.library.LessonEditorView"><table className="data-table">
          <caption className="visually-hidden">{s.viewersTitle}</caption>
          <thead><tr><th scope="col">{s.columns.member}</th><th scope="col" className="num">{s.columns.percent}</th><th scope="col">{s.columns.completed}</th><th scope="col">{s.columns.lastWatched}</th></tr></thead>
          <tbody>{list.items.map((r, i) => <tr key={(r.user && r.user.id) || i}><td>{authorName(r.user)}</td><td className="num">{(r.progress && r.progress.percent) || 0}%</td><td>{r.progress && r.progress.completed ? strings.admin.common.yes : strings.admin.common.no}</td><td>{r.progress && r.progress.lastWatchedUtc ? formatDateTime(r.progress.lastWatchedUtc) : '-'}</td></tr>)}</tbody>
        </table></TableColumns>
      )}
      {list.loaded && !list.done && <button type="button" className="button button-secondary button-sm" onClick={list.loadMore}>{lib.loadMore}</button>}
    </section>
  );
}

function ListEditor({ legend, items, onChange, blank, max, render, addLabel }) {
  return (
    <fieldset className="fieldset">
      <legend>{legend}</legend>
      {items.map((item, i) => (
        <div key={i} className="form-grid">
          {render(item, (changes) => onChange(items.map((x, j) => (j === i ? { ...x, ...changes } : x))), i)}
          <button type="button" className="icon-button" aria-label={s.remove} title={s.remove} onClick={() => onChange(items.filter((_, j) => j !== i))}><Icon name="trash" size={14} /></button>
        </div>
      ))}
      {items.length < max && <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...items, blank()])}><Icon name="plus" size={14} /><span>{addLabel}</span></button>}
    </fieldset>
  );
}

/**
 * /admin/library/lessons/:id (section 5.10): every lesson field, video upload with processing status
 * (polled while Processing), markers, resources (documents, links, references), captions (WebVTT),
 * levels, audience, publish, schedule, archive, move to another series, delete, and the audited
 * per-lesson viewers list (resolved decision 17).
 */
function LessonEditorView() {
  const { id } = useParams();
  const { client } = useAdminAuth();
  const fetcher = useCallback((signal) => client.adminGetLesson(id, signal), [client, id]);
  const query = useApiQuery(fetcher);
  if (!query.data) return <div className="view">{query.loading && <Loading />}<ErrorBanner message={query.error ? errorText(query.error, phase4Errors()) : ''} onRetry={query.reload} /></div>;
  return <LessonEditor key={query.data.id} lesson={query.data} reload={query.reload} />;
}

function LessonEditor({ lesson, reload }) {
  const { client, isSystemAdmin } = useAdminAuth();
  const { property } = useProperty();
  const toast = useToast();
  const navigate = useNavigate();
  const [form, setForm] = useState(() => formFrom(lesson));
  const [lastId, setLastId] = useState(lesson.updatedUtc);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [dialog, setDialog] = useState(null);
  const [publishAt, setPublishAt] = useState('');
  const [move, setMove] = useState({ seriesId: '', position: '' });
  const series = useAllSeries(client);
  const audience = useAudiencePicker({ feature: 'library', isSystemAdmin, current: lesson.audience });
  const levels = libraryLevels(property);

  if (lastId !== lesson.updatedUtc) {
    setLastId(lesson.updatedUtc);
    setForm(formFrom(lesson));
  }
  const processing = Boolean(lesson.kind === 'Video' && (lesson.status === 'Processing' || (lesson.mediaStatus && lesson.mediaStatus !== 'Ready' && lesson.mediaStatus !== 'Failed')));
  useEffect(() => {
    if (!processing) return undefined;
    const timer = setInterval(reload, POLL_MS);
    return () => clearInterval(timer);
  }, [processing, reload]);

  const set = (changes) => setForm((f) => ({ ...f, ...changes }));
  const run = async (fn, message) => {
    setBusy(true);
    setError('');
    try {
      await fn();
      toast.success(message);
      setDialog(null);
      reload();
      return true;
    } catch (err) {
      setError(errorText(err, phase4Errors()));
      return false;
    } finally {
      setBusy(false);
    }
  };
  const save = (e) => {
    e.preventDefault();
    if (form.title.trim().length < 2) {
      setError(s.problems.titleRequired);
      return;
    }
    run(() => client.adminUpdateLesson(lesson.id, lessonBody({ ...form, audience: audience.value })), strings.admin.common.saved);
  };

  return (
    <div className="view">
      <Link to="/admin/library?tab=lessons" className="back-link small"><Icon name="chevronLeft" size={14} /><span>{s.backToLessons}</span></Link>
      <PageHeader
        title={lesson.title}
        summary={lesson.series ? s.inSeries(lesson.series.title, lesson.position) : null}
        actions={(
          <>
            <Badge tone={lesson.status === 'Published' ? 'success' : 'neutral'}>{s.statuses[lesson.status] || lesson.status}</Badge>
            <a className="button button-secondary button-sm" href={appPaths.lesson(lesson.id)} target="_blank" rel="noopener noreferrer"><Icon name="eye" size={14} /><span>{strings.admin.common.view}</span></a>
            {lesson.status !== 'Published' && <button type="button" className="button button-primary button-sm" disabled={busy} onClick={() => run(() => client.adminPublishLesson(lesson.id), s.published)}>{s.publish}</button>}
            <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={() => setDialog('schedule')}><Icon name="clock" size={14} /><span>{s.schedule}</span></button>
            {lesson.status === 'Archived' ? <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={() => run(() => client.adminUnarchiveLesson(lesson.id), s.unarchived)}>{s.unarchive}</button>
              : <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={() => run(() => client.adminArchiveLesson(lesson.id), s.archived)}>{s.archive}</button>}
            <button type="button" className="button button-secondary button-sm" disabled={busy} onClick={() => setDialog('move')}>{s.move}</button>
            <button type="button" className="button button-danger-outline button-sm" disabled={busy} onClick={() => setDialog('delete')}>{strings.common.delete}</button>
          </>
        )}
      />
      {lesson.publishAtUtc && lesson.status === 'Scheduled' && <Banner tone="info" icon="clock">{s.scheduledFor(formatDateTime(lesson.publishAtUtc))}</Banner>}
      {processing && <Banner tone="info" icon="clock">{s.processingNote}</Banner>}
      <ErrorBanner message={error} />
      <form className="panel stack-form" onSubmit={save} noValidate aria-label={s.editLesson}>
        <div className="form-grid">
          <Field id="lesson-title" label={s.fields.title}><input id="lesson-title" value={form.title} maxLength={140} onChange={(e) => set({ title: e.target.value })} disabled={busy} /></Field>
          <Field id="lesson-slug" label={s.fields.slug} hint={s.slugHint}><input id="lesson-slug" value={form.slug} maxLength={80} onChange={(e) => set({ slug: e.target.value.toLowerCase() })} disabled={busy} /></Field>
          <Field id="lesson-kind" label={s.fields.kind}><select id="lesson-kind" value={form.kind} onChange={(e) => set({ kind: e.target.value })} disabled={busy}>{LessonKinds.map((k) => <option key={k} value={k}>{lib.kinds[k]}</option>)}</select></Field>
        </div>
        <Field id="lesson-summary" label={s.fields.summary}><input id="lesson-summary" value={form.summary} maxLength={300} onChange={(e) => set({ summary: e.target.value })} disabled={busy} /></Field>
        <Field id="lesson-description" label={form.kind === 'Article' ? s.fields.article : s.fields.description}><textarea id="lesson-description" rows={form.kind === 'Article' ? 14 : 6} value={form.description} onChange={(e) => set({ description: e.target.value })} disabled={busy} /></Field>
        {form.kind === 'Video' && (
          <fieldset className="fieldset">
            <legend>{s.fields.video}</legend>
            <p className="small">{lesson.video ? s.videoAttached(lesson.mediaStatus || lesson.video.status || 'Ready', formatDuration(lesson.durationSeconds)) : s.noVideo}</p>
            <FileUploadButton client={client} kind="Video" accept="video/mp4,video/quicktime,video/webm" label={lesson.video ? s.replaceVideo : s.uploadVideo} disabled={busy} onUploaded={(media) => run(() => client.adminUpdateLesson(lesson.id, { videoMediaId: media.id }), s.videoUploaded)} />
            <span className="muted small">{s.videoHint}</span>
          </fieldset>
        )}
        {levels.length > 0 && (
          <fieldset className="fieldset">
            <legend>{s.fields.levels}</legend>
            <div className="chip-row">{levels.map((l) => { const on = form.levels.includes(l.key); return <button key={l.key} type="button" className={'chip-toggle' + (on ? ' active' : '')} aria-pressed={on} onClick={() => set({ levels: on ? form.levels.filter((x) => x !== l.key) : [...form.levels, l.key].slice(0, 10) })}>{l.label}</button>; })}</div>
          </fieldset>
        )}
        <AudienceField id="lesson-audience" picker={audience} disabled={busy} />
        <div className="stack">
          <Toggle checked={form.commentsEnabled} onChange={(v) => set({ commentsEnabled: v })} label={s.fields.commentsEnabled} disabled={busy} />
          <Toggle checked={form.notifyStaffOnComment} onChange={(v) => set({ notifyStaffOnComment: v })} label={s.fields.notifyStaffOnComment} disabled={busy} />
          <Toggle checked={form.downloadable} onChange={(v) => set({ downloadable: v })} label={s.fields.downloadable} disabled={busy} />
        </div>
        <ListEditor legend={s.fields.markers} items={form.markers} onChange={(markers) => set({ markers })} blank={() => ({ time: '0:00', label: '' })} max={LIBRARY_LIMITS.maxMarkers} addLabel={s.addMarker}
          render={(m, change, i) => (
            <>
              <Field id={'marker-time-' + i} label={s.fields.markerTime} hint={s.markerTimeHint}><input id={'marker-time-' + i} value={m.time} onChange={(e) => change({ time: e.target.value })} /></Field>
              <Field id={'marker-label-' + i} label={s.fields.markerLabel}><input id={'marker-label-' + i} value={m.label} maxLength={80} onChange={(e) => change({ label: e.target.value })} /></Field>
            </>
          )} />
        <ListEditor legend={s.fields.resources} items={form.resources} onChange={(resources) => set({ resources })} blank={() => ({ kind: 'Link', title: '', mediaId: null, mediaName: '', url: '', text: '' })} max={LIBRARY_LIMITS.maxResources} addLabel={s.addResource}
          render={(r, change, i) => (
            <>
              <Field id={'res-kind-' + i} label={s.fields.resourceKind}><select id={'res-kind-' + i} value={r.kind} onChange={(e) => change({ kind: e.target.value })}>{ResourceKinds.map((k) => <option key={k} value={k}>{s.resourceKinds[k]}</option>)}</select></Field>
              <Field id={'res-title-' + i} label={s.fields.resourceTitle}><input id={'res-title-' + i} value={r.title} maxLength={140} onChange={(e) => change({ title: e.target.value })} /></Field>
              {r.kind === 'Link' && <Field id={'res-url-' + i} label={s.fields.resourceUrl}><input id={'res-url-' + i} type="url" value={r.url} placeholder="https://" onChange={(e) => change({ url: e.target.value })} /></Field>}
              {r.kind === 'Reference' && <Field id={'res-text-' + i} label={s.fields.resourceText}><input id={'res-text-' + i} value={r.text} maxLength={300} onChange={(e) => change({ text: e.target.value })} /></Field>}
              {r.kind === 'Document' && <div className="field"><span className="label-text">{s.fields.resourceFile}</span><FileUploadButton client={client} kind="Document" accept="application/pdf,.pdf" label={r.mediaName || s.uploadPdf} onUploaded={(media) => change({ mediaId: media.id, mediaName: media.fileName || s.uploaded })} /><span className="muted small">{s.pdfNote}</span></div>}
            </>
          )} />
        <ListEditor legend={s.fields.captions} items={form.captions} onChange={(captions) => set({ captions })} blank={() => ({ language: 'en', label: '', mediaId: null })} max={LIBRARY_LIMITS.maxCaptions} addLabel={s.addCaption}
          render={(cap, change, i) => (
            <>
              <Field id={'cap-lang-' + i} label={s.fields.captionLanguage}><select id={'cap-lang-' + i} value={cap.language} onChange={(e) => change({ language: e.target.value })}>{languageOptions(cap.language).map((o) => <option key={o.code} value={o.code}>{o.name}</option>)}</select></Field>
              <Field id={'cap-label-' + i} label={s.fields.captionLabel}><input id={'cap-label-' + i} value={cap.label} maxLength={60} onChange={(e) => change({ label: e.target.value })} /></Field>
              <div className="field"><span className="label-text">{s.fields.captionFile}</span><FileUploadButton client={client} kind="Document" accept="text/vtt,.vtt" label={cap.mediaId ? s.uploaded : s.uploadVtt} onUploaded={(media) => change({ mediaId: media.id })} /></div>
            </>
          )} />
        <MetadataEditor value={{ labels: form.labels, tags: form.tags }} onChange={(meta) => set(meta)} isStaff client={client} targetType="Lesson" disabled={busy} />
        <div className="form-actions"><button type="submit" className="button button-primary" disabled={busy}>{strings.common.save}</button></div>
      </form>
      {catalogFeatureEnabled(property) && <LessonTechniquesEditor client={client} lessonId={lesson.id} />}
      <Viewers client={client} lessonId={lesson.id} />
      {dialog === 'schedule' && (
        <Modal open size="sm" onClose={() => setDialog(null)} title={s.schedule}
          footer={<><button type="button" className="button button-secondary" onClick={() => setDialog(null)}>{strings.common.cancel}</button><button type="button" className="button button-primary" disabled={!publishAt || busy} onClick={() => run(() => client.adminScheduleLesson(lesson.id, localInputToIso(publishAt)), s.scheduled)}>{s.schedule}</button></>}>
          <Field id="lesson-publish-at" label={s.fields.publishAt} hint={s.scheduleHint}><input id="lesson-publish-at" type="datetime-local" value={publishAt} onChange={(e) => setPublishAt(e.target.value)} /></Field>
        </Modal>
      )}
      {dialog === 'move' && (
        <Modal open size="sm" onClose={() => setDialog(null)} title={s.move}
          footer={<><button type="button" className="button button-secondary" onClick={() => setDialog(null)}>{strings.common.cancel}</button><button type="button" className="button button-primary" disabled={!move.seriesId || busy} onClick={() => run(() => client.adminMoveLesson(lesson.id, { seriesId: move.seriesId, position: Number(move.position) || undefined }), s.moved)}>{s.move}</button></>}>
          <Field id="move-series" label={s.fields.series}><select id="move-series" value={move.seriesId} onChange={(e) => setMove({ ...move, seriesId: e.target.value })}><option value="">-</option>{series.items.map((x) => <option key={x.id} value={x.id}>{x.title}</option>)}</select></Field>
          <Field id="move-position" label={s.fields.position}><input id="move-position" type="number" min={1} value={move.position} onChange={(e) => setMove({ ...move, position: e.target.value })} /></Field>
        </Modal>
      )}
      <ConfirmModal open={dialog === 'delete'} title={s.deleteLesson(lesson.title)} message={s.deleteLessonBody} confirmLabel={strings.common.delete} busy={busy} onCancel={() => setDialog(null)} onConfirm={() => run(() => client.adminDeleteLesson(lesson.id), s.deleted).then((ok) => { if (ok) navigate('/admin/library?tab=lessons'); })} />
    </div>
  );
}

export default LessonEditorView;
