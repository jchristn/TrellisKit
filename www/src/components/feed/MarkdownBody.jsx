import { Fragment, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { parseMarkdown } from '@trelliskit/shared';
import { memberPath } from '../../utils/engagement.js';

function renderInline(nodes, key, options) {
  return (nodes || []).map((node, i) => {
    const k = key + '.' + i;
    switch (node.type) {
      case 'text': return <Fragment key={k}>{node.value}</Fragment>;
      case 'break': return <br key={k} />;
      case 'strong': return <strong key={k}>{renderInline(node.children, k, options)}</strong>;
      case 'em': return <em key={k}>{renderInline(node.children, k, options)}</em>;
      case 'strike': return <s key={k}>{renderInline(node.children, k, options)}</s>;
      case 'code': return <code key={k} className="md-inline-code">{node.value}</code>;
      case 'link':
        // Site paths (for example /terms) stay in the website; other links open externally.
        if (node.href.startsWith('/')) return <Link key={k} to={node.href} className="md-link">{renderInline(node.children, k, options)}</Link>;
        return <a key={k} href={node.href} target="_blank" rel="noopener noreferrer nofollow" className="md-link">{renderInline(node.children, k, options)}</a>;
      case 'mention':
        if (options.linkMentions && node.userId) return <Link key={k} to={memberPath(node.userId)} className="mention">@{node.handle}</Link>;
        return <span key={k} className="mention mention-plain">@{node.handle}</span>;
      default: return null;
    }
  });
}

function renderList(list, key, options) {
  const Tag = list.ordered ? 'ol' : 'ul';
  return (
    <Tag key={key} start={list.ordered && list.start !== 1 ? list.start : undefined}>
      {list.items.map((item, i) => (
        <li key={key + '.' + i}>
          {renderInline(item.children, key + '.' + i, options)}
          {item.sublist && renderList(item.sublist, key + '.' + i + '.s', options)}
        </li>
      ))}
    </Tag>
  );
}

function renderBlocks(blocks, key, options) {
  return (blocks || []).map((block, i) => {
    const k = key + '.' + i;
    switch (block.type) {
      case 'paragraph': return <p key={k}>{renderInline(block.children, k, options)}</p>;
      case 'heading': {
        // Page titles are h1 and section titles h2, so body headings start at h3.
        const Tag = block.level === 1 ? 'h3' : block.level === 2 ? 'h4' : 'h5';
        return <Tag key={k} className={'md-heading md-heading-' + block.level}>{renderInline(block.children, k, options)}</Tag>;
      }
      case 'list': return renderList(block, k, options);
      case 'blockquote': return <blockquote key={k}>{renderBlocks(block.children, k, options)}</blockquote>;
      case 'code': return <pre key={k} className="md-code-block" data-lang={block.lang || undefined}><code>{block.value}</code></pre>;
      case 'rule': return <hr key={k} className="md-rule" />;
      case 'table': return (
        <div key={k} className="md-table-wrap">
          <table className="md-table">
            <thead><tr>{block.header.map((cell, c) => <th key={c} style={{ textAlign: block.align[c] || undefined }}>{renderInline(cell, k + '.h' + c, options)}</th>)}</tr></thead>
            <tbody>{block.rows.map((row, r) => <tr key={r}>{row.map((cell, c) => <td key={c} style={{ textAlign: block.align[c] || undefined }}>{renderInline(cell, k + '.' + r + '.' + c, options)}</td>)}</tr>)}</tbody>
          </table>
        </div>
      );
      default: return null;
    }
  });
}

/**
 * A post or comment body rendered from the shared Markdown AST (contract section 3.1). Text is
 * rendered as React text nodes only (never HTML), links open externally with
 * `rel="noopener noreferrer nofollow"`, and `@handle` links to the member when it is in
 * `mentions`.
 *
 * @param {{ body: string, mentions?: Array<object>, allowHeadings?: boolean, linkMentions?: boolean, className?: string }} props
 */
function MarkdownBody({ body, mentions = [], allowHeadings = true, linkMentions = true, className = '' }) {
  const ast = useMemo(() => parseMarkdown(body || '', { mentions, allowHeadings }), [body, mentions, allowHeadings]);
  if (!ast.children.length) return null;
  return <div className={'md-body ' + className}>{renderBlocks(ast.children, 'b', { linkMentions })}</div>;
}

export default MarkdownBody;
