import { useEffect, useId, useRef, useState } from 'react';
import { insertMention, mentionQueryAt, normalizeEnumerationResult, scalarLength } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import Avatar from '../Avatar.jsx';
import Icon from '../Icon.jsx';
import { authorName, mediaUrl } from '../../utils/engagement.js';
import MarkdownBody from './MarkdownBody.jsx';

const c = strings.composer;
const SEARCH_DEBOUNCE_MS = 150;

const TOOLS = [
  { id: 'bold', icon: 'bold', label: c.bold, wrap: ['**', '**'] },
  { id: 'italic', icon: 'italic', label: c.italic, wrap: ['*', '*'] },
  { id: 'strike', icon: 'strike', label: c.strike, wrap: ['~~', '~~'] },
  { id: 'list', icon: 'list', label: c.bulletList, prefix: '- ' },
  { id: 'quote', icon: 'quote', label: c.quote, prefix: '> ' },
  { id: 'link', icon: 'link', label: c.link, wrap: ['[', '](https://)'] }
];

/** Apply a toolbar action to `text` with a selection; returns the new text and selection. */
export function applyTool(text, start, end, tool) {
  const selected = text.slice(start, end);
  if (tool.wrap) {
    const [open, close] = tool.wrap;
    const next = text.slice(0, start) + open + selected + close + text.slice(end);
    return { text: next, start: start + open.length, end: start + open.length + selected.length };
  }
  const lineStart = text.lastIndexOf('\n', start - 1) + 1;
  const next = text.slice(0, lineStart) + tool.prefix + text.slice(lineStart);
  return { text: next, start: start + tool.prefix.length, end: end + tool.prefix.length };
}

/**
 * Markdown subset editor (contract section 3): write and preview tabs, a small formatting
 * toolbar, a character counter in Unicode scalar values, and `@mention` autocomplete from
 * `GET /members/search` (debounced 150 ms after `@` plus one character, ranked with the post as
 * context). Picked members are previewed as mentions; the server resolves them on save.
 *
 * @param {{ id?: string, label: string, value: string, onChange: (value: string) => void, client: object,
 *   maxLength: number, allowHeadings?: boolean, placeholder?: string, rows?: number, disabled?: boolean,
 *   context?: { type: string, id: string }|null, error?: string, autoFocus?: boolean, hideLabel?: boolean }} props
 */
function MarkdownEditor({ id, label, value, onChange, client, maxLength, allowHeadings = true, placeholder, rows = 5, disabled = false, context = null, error, autoFocus = false, hideLabel = false }) {
  const { fill } = useProperty();
  const autoId = useId();
  const inputId = id || 'md-' + autoId;
  const listId = inputId + '-mentions';
  const ref = useRef(null);
  const [tab, setTab] = useState('write');
  const [query, setQuery] = useState(null);
  const [results, setResults] = useState([]);
  const [active, setActive] = useState(0);
  const [picked, setPicked] = useState([]);
  const pendingCaret = useRef(null);
  const length = scalarLength(value);
  const over = length > maxLength;

  useEffect(() => {
    if (pendingCaret.current !== null && ref.current) {
      const [s, e] = pendingCaret.current;
      pendingCaret.current = null;
      ref.current.focus();
      ref.current.setSelectionRange(s, e);
    }
  });

  const q = query ? query.query : '';
  useEffect(() => {
    if (!q || !client || typeof client.searchMembers !== 'function') {
      setResults([]);
      return undefined;
    }
    const controller = new AbortController();
    const timer = setTimeout(() => {
      const params = { search: q, maxResults: 8, filters: {} };
      if (context && context.type && context.id) params.filters = { contextType: context.type, contextId: context.id };
      client.searchMembers(params, controller.signal).then(
        (body) => { if (!controller.signal.aborted) { setResults(normalizeEnumerationResult(body).objects); setActive(0); } },
        () => { if (!controller.signal.aborted) setResults([]); }
      );
    }, SEARCH_DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [q, client, context]);

  const updateQuery = (text, caret) => setQuery(mentionQueryAt(text, caret));

  const choose = (user) => {
    if (!query || !user || !user.handle) return;
    const out = insertMention(value, query, user.handle);
    onChange(out.text);
    setPicked((list) => (list.some((m) => m.userId === user.id) ? list : [...list, { userId: user.id, handle: user.handle, displayName: user.displayName }]));
    setQuery(null);
    setResults([]);
    pendingCaret.current = [out.caret, out.caret];
  };

  const open = Boolean(query && results.length);
  const onKeyDown = (e) => {
    if (!open) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActive((i) => (i + 1) % results.length);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((i) => (i - 1 + results.length) % results.length);
    } else if (e.key === 'Enter' || e.key === 'Tab') {
      e.preventDefault();
      choose(results[active]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      setQuery(null);
      setResults([]);
    }
  };

  const tool = (t) => {
    const el = ref.current;
    if (!el) return;
    const out = applyTool(value, el.selectionStart, el.selectionEnd, t);
    onChange(out.text);
    pendingCaret.current = [out.start, out.end];
  };

  return (
    <div className={'md-editor' + (error || over ? ' has-error' : '')}>
      <div className="md-editor-head">
        <label className={'label-text' + (hideLabel ? ' visually-hidden' : '')} htmlFor={inputId}>{label}</label>
        <div className="segmented md-tabs" role="tablist" aria-label={label}>
          <button type="button" role="tab" aria-selected={tab === 'write'} className={'segment' + (tab === 'write' ? ' active' : '')} onClick={() => setTab('write')}>{c.write}</button>
          <button type="button" role="tab" aria-selected={tab === 'preview'} className={'segment' + (tab === 'preview' ? ' active' : '')} onClick={() => setTab('preview')}>{c.preview}</button>
        </div>
      </div>
      {tab === 'write' ? (
        <div className="md-write">
          <div className="md-toolbar" role="toolbar" aria-label={c.formattingHelp}>
            {TOOLS.map((t) => (
              <button key={t.id} type="button" className="icon-button" onClick={() => tool(t)} aria-label={t.label} title={t.label} disabled={disabled}>
                <Icon name={t.icon} size={14} />
              </button>
            ))}
            <span className="md-hint muted small" title={c.formattingBody}>{c.formattingHelp}</span>
          </div>
          <div className="md-input-wrap">
            <textarea
              id={inputId}
              ref={ref}
              rows={rows}
              value={value}
              placeholder={placeholder ? fill(placeholder) : undefined}
              disabled={disabled}
              autoFocus={autoFocus}
              aria-invalid={Boolean(error || over)}
              aria-describedby={inputId + '-count' + (error ? ' ' + inputId + '-error' : '')}
              role="combobox"
              aria-autocomplete="list"
              aria-expanded={open}
              aria-controls={open ? listId : undefined}
              aria-activedescendant={open ? listId + '-' + active : undefined}
              onChange={(e) => {
                onChange(e.target.value);
                updateQuery(e.target.value, e.target.selectionStart);
              }}
              onKeyDown={onKeyDown}
              onClick={(e) => updateQuery(e.target.value, e.target.selectionStart)}
              onKeyUp={(e) => { if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) updateQuery(e.target.value, e.target.selectionStart); }}
              onBlur={() => setTimeout(() => setQuery(null), 150)}
            />
            {open && (
              <ul id={listId} className="mention-list" role="listbox" aria-label={c.mentionSuggestions}>
                {results.map((user, i) => (
                  <li
                    key={user.id}
                    id={listId + '-' + i}
                    role="option"
                    aria-selected={i === active}
                    className={'mention-option' + (i === active ? ' active' : '')}
                    onMouseDown={(e) => { e.preventDefault(); choose(user); }}
                  >
                    <Avatar src={mediaUrl(user.avatarUrl)} name={authorName(user)} size={24} />
                    <span className="mention-option-name">{authorName(user)}</span>
                    <span className="muted small">@{user.handle}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      ) : (
        <div className="md-preview" aria-live="polite">
          {value.trim() ? <MarkdownBody body={value} mentions={picked} allowHeadings={allowHeadings} linkMentions={false} /> : <p className="muted">{c.previewEmpty}</p>}
        </div>
      )}
      <div className="md-footer">
        {error ? <span id={inputId + '-error'} className="field-error" role="alert">{error}</span> : <span />}
        <span id={inputId + '-count'} className={'small ' + (over ? 'field-error' : 'muted')} aria-live="polite">
          {over ? c.tooLong(maxLength) + ' ' : ''}{c.counter(length, maxLength)}
        </span>
      </div>
    </div>
  );
}

export default MarkdownEditor;
