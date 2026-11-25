import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Markdown, { parseInline, parseMarkdown, slugify, splitLegalDocument } from '../src/views/marketing/markdown.jsx';

function renderMd(source) {
  return render(<MemoryRouter><Markdown source={source} /></MemoryRouter>);
}

describe('markdown parser', () => {
  it('parses headings with slug ids, paragraphs, and rules', () => {
    const blocks = parseMarkdown('# Title\n\nFirst line\nsecond line\n\n## Under 18\n\n---\n');
    expect(blocks).toEqual([
      { type: 'heading', level: 1, text: 'Title', id: 'title' },
      { type: 'paragraph', text: 'First line second line' },
      { type: 'heading', level: 2, text: 'Under 18', id: 'under-18' },
      { type: 'hr' }
    ]);
    expect(slugify('Your choices & rights')).toBe('your-choices-rights');
  });

  it('parses ordered and unordered lists, nested lists, and continuation paragraphs', () => {
    const [list] = parseMarkdown('- **One.** intro:\n  - a\n  - b\n\n  After the list.\n- Two\n');
    expect(list.type).toBe('list');
    expect(list.ordered).toBe(false);
    expect(list.items).toHaveLength(2);
    expect(list.items[0].map((b) => b.type)).toEqual(['paragraph', 'list', 'paragraph']);
    expect(list.items[0][1].items).toHaveLength(2);
    const [ordered] = parseMarkdown('1. Sign in.\n2. Open settings.\n');
    expect(ordered.ordered).toBe(true);
    expect(ordered.items.map((i) => i[0].text)).toEqual(['Sign in.', 'Open settings.']);
  });

  it('parses blockquotes and pipe tables', () => {
    const [quote, table] = parseMarkdown('> **Note.** Read this.\n> Second line.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n');
    expect(quote).toEqual({ type: 'blockquote', blocks: [{ type: 'paragraph', text: '**Note.** Read this. Second line.' }] });
    expect(table).toEqual({ type: 'table', header: ['A', 'B'], rows: [['1', '2'], ['3', '4']] });
  });

  it('tokenizes inline strong, emphasis, code, links, and placeholders', () => {
    expect(parseInline('a **b** *c* `d` [e](/f) [PLACEHOLDER: g]')).toEqual([
      { type: 'text', value: 'a ' },
      { type: 'strong', children: [{ type: 'text', value: 'b' }] },
      { type: 'text', value: ' ' },
      { type: 'em', children: [{ type: 'text', value: 'c' }] },
      { type: 'text', value: ' ' },
      { type: 'code', value: 'd' },
      { type: 'text', value: ' ' },
      { type: 'link', href: '/f', children: [{ type: 'text', value: 'e' }] },
      { type: 'text', value: ' ' },
      { type: 'placeholder', value: 'g' }
    ]);
  });

  it('preserves substantive blockquotes in published legal documents', () => {
    const doc = splitLegalDocument('# Terms\n\n> A quoted statutory provision.\n\nPublished terms.');
    expect(doc.notice).toBeNull();
    expect(doc.blocks[0]).toMatchObject({ type: 'blockquote' });
  });

  it('splits a legal document into title, draft notice, and body', () => {
    const doc = splitLegalDocument('> **DRAFT** notice\n\n# Privacy Policy\n\n## Intro\n\nText.\n');
    expect(doc.title).toBe('Privacy Policy');
    expect(doc.notice.type).toBe('blockquote');
    expect(doc.blocks.map((b) => b.type)).toEqual(['heading', 'paragraph']);
  });
});

describe('markdown rendering', () => {
  it('renders React elements with internal router links, external links, and placeholder markers', () => {
    renderMd('## Contact\n\nSee [terms](/terms), [Amazon](https://www.amazon.com/), and [Top](#contact).\n\nEmail: [PLACEHOLDER: support email]\n');
    expect(screen.getByRole('heading', { level: 2, name: 'Contact' })).toHaveAttribute('id', 'contact');
    expect(screen.getByRole('link', { name: 'terms' })).toHaveAttribute('href', '/terms');
    const external = screen.getByRole('link', { name: 'Amazon' });
    expect(external).toHaveAttribute('target', '_blank');
    expect(external).toHaveAttribute('rel', 'noopener noreferrer');
    expect(screen.getByRole('link', { name: 'Top' })).toHaveAttribute('href', '#contact');
    const placeholder = screen.getByText('support email');
    expect(placeholder).toHaveClass('placeholder-copy');
    expect(within(placeholder).getByText('Placeholder')).toBeInTheDocument();
  });

  it('renders lists and tables with header cells', () => {
    renderMd('1. **One**\n2. Two\n\n| Item | Details |\n|---|---|\n| Price | $29.99 |\n');
    const items = screen.getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0].querySelector('strong')).toHaveTextContent('One');
    expect(screen.getByRole('columnheader', { name: 'Item' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: '$29.99' })).toBeInTheDocument();
  });

  it('never injects raw HTML', () => {
    const { container } = renderMd('<img src=x onerror="alert(1)"> and <script>bad()</script>\n');
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('script')).toBeNull();
    expect(container.textContent).toContain('<img src=x');
  });
});
