import { describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { loadFixtureSite, renderSite } from './helpers.jsx';
import { parseAttributes, parseDirectives, parseFrontMatter } from '../src/property/content.js';
import { renderContent, splitTrailingAttrs } from '../src/views/marketing/ContentPage.jsx';

const bookclub = loadFixtureSite('bookclub');

describe('property content files', () => {
  it('parses simple front matter with dotted keys and comments', () => {
    const { meta, body } = parseFrontMatter('---\ntitle: Hello {name}\n# a comment\nbook.name: "A Book"\nbook.isbn:\n---\n# Body\n');
    expect(meta).toEqual({ title: 'Hello {name}', book: { name: 'A Book', isbn: '' } });
    expect(body).toBe('# Body\n');
    expect(parseFrontMatter('# No front matter').meta).toEqual({});
  });

  it('parses nested container directives and attributes', () => {
    const nodes = parseDirectives('Intro\n\n:::: section class="a b" inner=c\n::: cards\n### One\n:::\n::::\nOutro');
    expect(nodes.map((n) => n.type)).toEqual(['markdown', 'directive', 'markdown']);
    expect(nodes[1]).toMatchObject({ kind: 'section', attrs: { class: 'a b', inner: 'c' } });
    expect(nodes[1].children[0]).toMatchObject({ kind: 'cards', children: [{ type: 'markdown', text: '### One' }] });
    expect(parseAttributes('flag x=1 y="two words"')).toEqual({ flag: 'true', x: '1', y: 'two words' });
  });

  it('splits trailing attribute blocks but keeps ordinary braces', () => {
    expect(splitTrailingAttrs('Title {#id .big icon=feed}')).toEqual({ text: 'Title', id: 'id', classes: ['big'], attrs: { icon: 'feed' } });
    expect(splitTrailingAttrs('Keep {unknown} text').text).toBe('Keep {unknown} text');
  });

  it('renders widgets, cards, and placeholders', () => {
    render(<MemoryRouter>{renderContent('::: cards\n### Feed {icon=feed}\n\n[PLACEHOLDER: later]\n:::\n\n@media label="Team photo" class="x"\n')}</MemoryRouter>);
    expect(screen.getByRole('heading', { level: 3, name: 'Feed' })).toBeInTheDocument();
    expect(screen.getByText('Placeholder')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Team photo' })).toHaveClass('media-placeholder', 'x');
  });

  it('renders photos with images and captions, placeholders, and ignores unsafe image sources', () => {
    const { container } = render(<MemoryRouter>{renderContent('::: photos\n- ![Two coaches](/images/coaches.png) At the academy\n- Seminar photo placeholder\n- ![Bad](javascript:alert(1)) Bad\n:::\n')}</MemoryRouter>);
    expect(screen.getByRole('img', { name: 'Two coaches' })).toHaveAttribute('src', '/images/coaches.png');
    expect(screen.getByText('At the academy').tagName).toBe('FIGCAPTION');
    expect(screen.getByRole('img', { name: 'Seminar photo placeholder' })).toHaveClass('media-placeholder');
    expect(container.querySelectorAll('img')).toHaveLength(1);
  });

  it('renders @media with a src as an image', () => {
    render(<MemoryRouter>{renderContent('@media src="/images/me.png" label="Portrait" class="about-photo"\n')}</MemoryRouter>);
    expect(screen.getByRole('img', { name: 'Portrait' })).toHaveAttribute('src', '/images/me.png');
  });

  it('renders faq items as collapsible details rows', () => {
    const { container } = render(<MemoryRouter>{renderContent('::: faq\n### Is there a trial?\n\nNo.\n:::\n')}</MemoryRouter>);
    const item = container.querySelector('details.faq-item');
    expect(item).not.toBeNull();
    expect(item.open).toBe(false);
    expect(item.querySelector('summary').textContent).toContain('Is there a trial?');
  });
});

describe('marketing pages (fixture property with ranks)', () => {
  it('renders the home page from web/content/home.md with tokens filled', async () => {
    renderSite('/');
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(screen.getByRole('heading', { level: 1, name: 'Practice together' })).toBeInTheDocument();
    const facts = screen.getByRole('list', { name: 'At a glance' });
    expect(within(facts).getByText('$45.00 per month')).toBeInTheDocument();
    expect(within(facts).getByText('Members 21 and older')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Join now' })).toHaveAttribute('href', '/membership');
    const logo = screen.getByRole('img', { name: 'Fixture Dojo logo' });
    expect(logo.querySelector('.theme-logo-light')).toHaveAttribute('src', '/logo.png');
    expect(logo.querySelector('.theme-logo-dark')).toHaveAttribute('src', '/logo-dark.png');
    await waitFor(() => expect(document.title).toBe('Fixture Dojo | Practice together'));
  });

  it('has landmarks and external buy links that open safely in a new tab', () => {
    renderSite('/guide');
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(screen.getByRole('contentinfo')).toBeInTheDocument();
    const buy = screen.getByRole('link', { name: /Buy the guide/ });
    expect(buy).toHaveAttribute('href', 'https://shop.dojo.test/guide');
    expect(buy).toHaveAttribute('rel', 'noopener noreferrer');
    expect(buy).toHaveAttribute('target', '_blank');
    expect(screen.getByRole('img', { name: 'The Fixture Guide placeholder' })).toBeInTheDocument();
  });

  it('builds header navigation and footer legal links from the property', () => {
    renderSite('/');
    const nav = screen.getByRole('navigation', { name: 'Primary' });
    expect(within(nav).getAllByRole('link').map((a) => a.getAttribute('href'))).toEqual(['/guide', '/membership', '/about', '/support', '/sign-in', '/sign-up']);
    const legal = screen.getByRole('navigation', { name: 'Legal' });
    expect(within(legal).getAllByRole('link').map((a) => a.getAttribute('href'))).toEqual(['/privacy', '/terms', '/subscription-terms', '/community-guidelines', '/delete-account']);
    expect(screen.getByText(/Copyright \d{4} Fixture Owner\. All rights reserved\./)).toBeInTheDocument();
    expect(screen.getByText('A fixture property with ranks and levels.')).toBeInTheDocument();
  });

  it('shows app store badge placeholders that are not links', () => {
    renderSite('/');
    const badges = screen.getByRole('list', { name: 'Mobile apps' });
    expect(within(badges).getByText('App Store')).toBeInTheDocument();
    expect(within(badges).queryAllByRole('link')).toHaveLength(0);
  });

  it('keeps unknown facts marked as placeholders', () => {
    renderSite('/about');
    const bio = screen.getByRole('region', { name: 'Bio' });
    expect(within(bio).getByText('Placeholder')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Photo one placeholder' })).toBeInTheDocument();
  });

  it('shows the price from property.json and the sign-up call to action on /membership', () => {
    renderSite('/membership');
    expect(screen.getByRole('heading', { level: 2, name: 'Dojo Membership' })).toBeInTheDocument();
    expect(screen.getByText('$45.00')).toBeInTheDocument();
    expect(screen.getByText('per month')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Create your account' })).toHaveAttribute('href', '/sign-up');
    expect(screen.getByRole('link', { name: 'Create your account with a code' })).toHaveAttribute('href', '/sign-up');
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'Subscription terms' })).toHaveAttribute('href', '/subscription-terms');
    expect(screen.getByText('No. Membership is $45.00 per month.')).toBeInTheDocument();
  });

  it('renders legal pages from the property legal folder with the draft banner', async () => {
    renderSite('/subscription-terms');
    expect(screen.getByRole('heading', { level: 1, name: 'Subscription terms' })).toBeInTheDocument();
    expect(screen.getByText('DRAFT for review by counsel; not legal advice.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Renewal' })).toBeInTheDocument();
    expect(screen.getAllByText('Placeholder').length).toBeGreaterThan(0);
    await waitFor(() => expect(document.title).toBe('Subscription Terms | Fixture Dojo'));
    expect(document.head.querySelector('link[rel="canonical"]').getAttribute('href')).toBe('https://dojo.test/subscription-terms');
  });

  it('updates Open Graph type and JSON-LD on client navigation', async () => {
    renderSite('/guide');
    await waitFor(() => expect(document.head.querySelector('script[type="application/ld+json"]').textContent).toContain('"Book"'));
    expect(document.head.querySelector('meta[property="og:type"]').getAttribute('content')).toBe('book');
  });
});

describe('marketing pages (second fixture: a book club without ranks)', () => {
  it('renders only the pages the property defines, with its own name and price', async () => {
    renderSite('/', { site: bookclub });
    expect(screen.getByRole('heading', { level: 1, name: 'Read together' })).toBeInTheDocument();
    expect(screen.getByText('Join Fixture Book Club for $12.00 per year.')).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: 'Primary' });
    expect(within(nav).getAllByRole('link').map((a) => a.getAttribute('href'))).toEqual(['/membership', '/sign-in', '/sign-up']);
    const legal = screen.getByRole('navigation', { name: 'Legal' });
    expect(within(legal).getAllByRole('link').map((a) => a.getAttribute('href'))).toEqual(['/privacy']);
    await waitFor(() => expect(document.title).toBe('Fixture Book Club | Read together'));
  });

  it('treats routes the property lacks as not found', () => {
    renderSite('/terms', { site: bookclub });
    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });
});
