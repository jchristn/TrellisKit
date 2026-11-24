import { snippetSegments } from '@trelliskit/shared';

/**
 * A search snippet (PHASE3_CONTRACT.md section 9.1): plain text with the server's highlight
 * offsets rendered as <mark>. The text is never treated as HTML.
 * @param {{ snippet: { text: string, highlights?: { start: number, length: number }[] }|null, className?: string }} props
 */
function SnippetText({ snippet, className = 'search-snippet small' }) {
  if (!snippet || !snippet.text) return null;
  return (
    <p className={className}>
      {snippetSegments(snippet.text, snippet.highlights).map((seg, i) => (seg.highlight ? <mark key={i}>{seg.text}</mark> : <span key={i}>{seg.text}</span>))}
    </p>
  );
}

export default SnippetText;
