import { useEffect, useState } from 'react';
import { PublicPageKind, SLUG_MAX, normalizeSlugInput, publicPagePathWord, publicPageUrl, publicPagesConfig, slugProblem } from '@trelliskit/shared';
import strings from '../strings.js';
import { useProperty } from '../context/PropertyContext.jsx';
import useDebounce from '../hooks/useDebounce.js';
import CopyButton from './CopyButton.jsx';
import Icon from './Icon.jsx';
import { ErrorBanner, Loading } from './States.jsx';

const t = strings.publicPages;

/** Slug statuses that block a save (the problem codes plus a taken slug). */
const BLOCKING = Object.freeze(['required', 'length', 'format', 'reserved', 'taken']);

/** Whether a slug status (from `useSlugStatus`) blocks a save. */
export function slugBlocks(status) {
  return BLOCKING.includes(status);
}

/** The origin page URLs are built on (the website origin; the property's web domain outside a browser). */
export function pageOrigin() {
  return typeof window !== 'undefined' && window.location ? window.location.origin : undefined;
}

/**
 * The inline message for a slug save failure: 409 `SlugInUse` reads as taken, a 400 with `fieldErrors.slug`
 * shows the server's text (or the matching problem text). Returns '' for other failures.
 */
export function slugServerError(err) {
  if (!err) return '';
  if (err.code === 'SlugInUse') return t.problems.taken;
  const fe = (err.fieldErrors) || (err.body && err.body.fieldErrors) || {};
  if (!fe.slug) return '';
  return t.problems[fe.slug] || String(fe.slug);
}

/**
 * Live slug validation: the shared format rules at once, then (after typing stops) the server's
 * availability check `POST /public-pages/slugs/check`. Statuses: `empty` (allowed empty), `current`
 * (the saved slug), `checking`, `available`, `unknown` (the check could not run; the server decides on
 * save), or a problem code (`required`, `length`, `format`, `reserved`, `taken`).
 * @param {{ client: object, kind: string, slug: string, objectId?: string|null, currentSlug?: string|null, allowEmpty?: boolean }} options
 */
export function useSlugStatus({ client, kind, slug, objectId = null, currentSlug = null, allowEmpty = false }) {
  const typed = String(slug || '').trim();
  const debounced = useDebounce(typed, 400);
  const [result, setResult] = useState({ slug: '', status: 'idle' });
  const canCheck = Boolean(client) && typeof client.checkPublicPageSlug === 'function';
  const shouldCheck = canCheck && Boolean(debounced) && !slugProblem(debounced) && debounced !== currentSlug;

  useEffect(() => {
    if (!shouldCheck) return undefined;
    const controller = new AbortController();
    client.checkPublicPageSlug({ kind, slug: debounced, ...(objectId ? { id: objectId } : {}) }, controller.signal).then(
      (res) => {
        if (controller.signal.aborted) return;
        const ok = res && res.available !== false && res.valid !== false && !res.problem;
        setResult({ slug: debounced, status: ok ? 'available' : (res && res.problem) || 'taken' });
      },
      () => { if (!controller.signal.aborted) setResult({ slug: debounced, status: 'unknown' }); }
    );
    return () => controller.abort();
  }, [client, kind, debounced, objectId, shouldCheck]);

  if (!typed) return allowEmpty ? 'empty' : 'required';
  const local = slugProblem(typed);
  if (local) return local;
  if (currentSlug && typed === currentSlug) return 'current';
  if (!canCheck) return 'unknown';
  if (typed !== debounced || result.slug !== typed) return 'checking';
  return result.status;
}

/** A page URL with a copy button and, when the page is live, a preview link that opens in a new tab. */
export function PublicPageUrl({ url, previewUrl = null, idPrefix }) {
  if (!url) return null;
  return (
    <div className="public-page-url" data-testid={idPrefix ? idPrefix + '-url' : undefined}>
      <span className="label-text">{t.urlLabel}</span>
      <span className="row-actions">
        <code className="mono small">{url}</code>
        <CopyButton value={url} label={t.copyLabel} />
        {previewUrl && <a href={previewUrl} target="_blank" rel="noopener noreferrer" className="small"><Icon name="external" size={12} /> {t.preview}</a>}
      </span>
    </div>
  );
}

function statusLine(status, serverError) {
  if (serverError) return <span className="field-error" role="alert">{serverError}</span>;
  if (t.problems[status]) return <span className="field-error" role="status">{t.problems[status]}</span>;
  if (status === 'checking') return <span className="muted small" role="status">{t.checking}</span>;
  if (status === 'available') return <span className="success-text small" role="status"><Icon name="check" size={12} /> {t.available}</span>;
  if (status === 'current') return <span className="muted small">{t.current}</span>;
  return null;
}

/**
 * A slug input for a public landing page: the path word as a prefix, the typed text normalized as it is typed,
 * the live status from `useSlugStatus` (the caller owns the hook so it can block a save), and the resulting URL
 * with copy (only while the property's public pages are on). An empty value on create uses `placeholder`.
 * @param {{ id: string, kind: string, value: string, onChange: (slug: string) => void, status: string, placeholder?: string, allowEmpty?: boolean, disabled?: boolean, serverError?: string, hint?: string, note?: string, previewUrl?: string|null }} props
 */
export function SlugField({ id, kind, value, onChange, status, placeholder = '', allowEmpty = false, disabled = false, serverError = '', hint = '', note = '', previewUrl = null }) {
  const { property } = useProperty();
  const config = publicPagesConfig(property);
  const typed = String(value || '').trim();
  const effective = typed || (allowEmpty ? placeholder : '');
  const url = config.enabled && effective && !slugProblem(effective) ? publicPageUrl(property, kind, effective, pageOrigin()) : null;
  const invalid = Boolean(serverError) || slugBlocks(status);
  const hintText = hint || (allowEmpty && placeholder && !slugProblem(placeholder) ? t.slugDerivedHint(placeholder) : allowEmpty ? t.slugAutoHint : t.slugHint);
  return (
    <div className="field">
      <label className="label-text" htmlFor={id}>{t.slugLabel}</label>
      <div className="input-prefix">
        <span aria-hidden="true">{'/' + publicPagePathWord(property, kind) + '/'}</span>
        <input id={id} className="mono" value={value} placeholder={placeholder || undefined} maxLength={SLUG_MAX} autoComplete="off" spellCheck="false"
          onChange={(e) => onChange(normalizeSlugInput(e.target.value))} disabled={disabled} aria-invalid={invalid} aria-describedby={id + '-hint'} />
      </div>
      <span id={id + '-hint'} className="muted small">{hintText}</span>
      {statusLine(status, serverError)}
      <PublicPageUrl url={url} previewUrl={previewUrl} idPrefix={id} />
      {note && config.enabled && <span className="muted small">{note}</span>}
    </div>
  );
}

/** "View public page" plus copy, for detail views of objects whose payload carries `publicUrl`. */
export function PublicPageLink({ url }) {
  if (!url) return null;
  return (
    <span className="row-actions small" data-testid="public-page-link">
      <a href={url} target="_blank" rel="noopener noreferrer"><Icon name="external" size={12} /> {t.view}</a>
      <CopyButton value={url} label={t.copyLabel} />
    </span>
  );
}

/**
 * The opt-in public profile page settings: an off-by-default switch, the slug (prefilled with the server's
 * `suggestedSlug`) with live validation, the resulting URL with copy and preview, and save. The member app
 * passes `getMyPublicPage`/`updateMyPublicPage`; the admin member drawer passes the admin methods.
 * @param {{ client: object, load: (signal: AbortSignal) => Promise<object>, save: (body: object) => Promise<object>, objectId?: string, idPrefix: string, title: string, intro: string, toggleLabel: string, toggleHint: string, savedText: string, onSaved?: (page: object) => void, onToast?: (text: string) => void, headingLevel?: number, className?: string }} props
 */
export function PublicProfilePageSettings({ client, load, save, objectId = null, idPrefix, title, intro, toggleLabel, toggleHint, savedText, onSaved, onToast, headingLevel = 2, className = 'panel' }) {
  const { property } = useProperty();
  const [page, setPage] = useState(null);
  const [loadError, setLoadError] = useState(null);
  const [nonce, setNonce] = useState(0);
  const [enabled, setEnabled] = useState(false);
  const [slug, setSlug] = useState('');
  const [error, setError] = useState('');
  const [slugError, setSlugError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setLoadError(null);
    load(controller.signal).then(
      (res) => {
        if (controller.signal.aborted) return;
        const value = res || {};
        setPage(value);
        setEnabled(Boolean(value.enabled));
        setSlug(value.slug || value.suggestedSlug || '');
      },
      (err) => { if (!controller.signal.aborted) setLoadError(err); }
    );
    return () => controller.abort();
  }, [load, nonce]);

  const status = useSlugStatus({ client, kind: PublicPageKind.Profile, slug, objectId, currentSlug: page ? page.slug : null });
  const Heading = 'h' + headingLevel;
  const headingId = idPrefix + '-title';
  // A server without public pages answers 404: the section stays hidden.
  if (!publicPagesConfig(property).enabled || (loadError && loadError.status === 404)) return null;

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    setSlugError('');
    if (slugBlocks(status)) {
      setError(t.fixSlug);
      return;
    }
    setBusy(true);
    try {
      const saved = (await save({ enabled, slug: slug.trim() })) || {};
      setPage(saved);
      setEnabled(Boolean(saved.enabled));
      setSlug(saved.slug || slug.trim());
      if (onSaved) onSaved(saved);
      if (onToast) onToast(savedText);
    } catch (err) {
      const inline = slugServerError(err);
      if (inline) setSlugError(inline);
      else setError((err && err.message) || strings.common.errorGeneric);
    } finally {
      setBusy(false);
    }
  };

  const savedSlug = page && page.slug;
  const live = Boolean(page && page.enabled && savedSlug);
  const previewUrl = live ? page.url || publicPageUrl(property, PublicPageKind.Profile, savedSlug, pageOrigin()) : null;

  return (
    <section className={className} aria-labelledby={headingId} data-testid={idPrefix}>
      <Heading id={headingId}>{title}</Heading>
      <p className="muted small">{intro}</p>
      {!page && !loadError && <Loading inline />}
      <ErrorBanner message={loadError ? t.profile.loadError : ''} onRetry={() => setNonce((n) => n + 1)} />
      {page && (
        <form className="stack-form" onSubmit={submit} noValidate>
          <ErrorBanner message={error} />
          <label className="toggle toggle-hinted">
            <input type="checkbox" role="switch" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} disabled={busy} aria-describedby={idPrefix + '-toggle-hint'} />
            <span className="toggle-track"><span className="toggle-thumb" /></span>
            <span className="toggle-text"><span>{toggleLabel}</span><span id={idPrefix + '-toggle-hint'} className="muted small">{toggleHint}</span></span>
          </label>
          <SlugField id={idPrefix + '-slug'} kind={PublicPageKind.Profile} value={slug} onChange={(next) => { setSlug(next); setSlugError(''); }} status={status} disabled={busy}
            serverError={slugError} previewUrl={previewUrl} note={savedSlug ? t.slugRenameNote : ''} />
          {!live && <p className="muted small">{t.profile.offNote}</p>}
          <div className="form-actions form-actions-start">
            <button type="submit" className="button button-primary" disabled={busy}>{busy ? t.profile.saving : t.profile.save}</button>
          </div>
        </form>
      )}
    </section>
  );
}
