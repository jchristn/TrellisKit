import { Fragment, useMemo } from 'react';
import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import { useProperty } from '../../context/PropertyContext.jsx';
import Icon from '../../components/Icon.jsx';
import useRouteMeta from './useRouteMeta.js';
import { parseInline, parseMarkdown, plainText, renderBlocks, renderInline, slugify } from './markdown.jsx';
import { applyContentConditions, parseAttributes, parseDirectives } from '../../property/content.js';
import { siteMetaFor } from './siteMeta.js';
import { CoverArt, JoinButton, Logo, Price, SignUpLink, StoreBadges } from './MarketingBits.jsx';
import PublicSamplePosts from './PublicSamplePosts.jsx';
import PublicSampleThreads from './PublicSampleThreads.jsx';
import PublicSampleCatalog from './PublicSampleCatalog.jsx';

/*
 * Renders a property content page (web/content/*.md). See docs in www/README.md ("Property
 * content"). Building blocks:
 *
 * - Markdown: headings (`## Title {#id .class icon=name}`), paragraphs (`Text {.lead}`), lists,
 *   blockquotes, tables, and `[PLACEHOLDER: ...]` (a paragraph that is only a placeholder renders
 *   as marked placeholder copy).
 * - Container directives (`::: kind attrs` ... `:::`, nest with more colons):
 *   section, aside, article, div, header (attrs: class, inner), excerpt, cards (class, item, tag,
 *   numbered, media), faq (each `###` question is a collapsible row), quotes, checks (class, label), actions (size, variant), photos (an item is placeholder text,
 *   or `![alt](/images/name.png) Caption` for a real photo), toc.
 * - Widgets, a paragraph of the form `@name attrs`: @logo, @price, @join (class),
 *   @signup-link (label, class), @store-badges, @cover (title, subtitle, size, label, src: an image such as /images/cover.jpg from web/images/),
 *   @media (label, class, src: a real image), @public-posts (limit): public sample posts, @public-threads (limit):
 *   public sample threads, @public-events, @public-venues, @public-lessons (limit): public events,
 *   directory listings, and sample lessons (PHASE4_CONTRACT sections 3.11, 4.9, 5.12), all rendered
 *   in the browser only. The home page shows each at the end when it has no widget of that kind.
 * - Access-dependent copy (applyContentConditions in property/content.js): `when=cond` on a
 *   directive or in a line's trailing attribute block keeps it only while the live access policy
 *   matches, and lines with purchase tokens such as `{price}` drop out when nothing is sold. The
 *   page renders the build-time property first (matching the prerendered HTML) and re-renders when
 *   MarketingLayout loads `GET /api/v1/property`.
 */

const ATTR_TOKEN = /^(#[\w-]+|\.[\w-]+|[A-Za-z][\w-]*=(?:"[^"]*"|'[^']*'|\S+))$/;
const LINK = /\[([^\]]+)\]\(([^)\s]+)\)/g;
const WIDGET = /^@([a-z][\w-]*)(?:\s+(.*))?$/;
/** A photos item with an image: `![alt](/images/name.png) Optional caption`. */
const PHOTO = /^!\[([^\]]*)\]\(([^)\s]+)\)\s*(.*)$/;
/** Image sources allowed in photos: site paths and https URLs. */
const SAFE_IMAGE = /^(\/(?!\/)|https:\/\/)/;
const PLACEHOLDER_ONLY = /^\[PLACEHOLDER:\s*([^\]]*)\]$/;

/** Split a trailing `{#id .class key=value}` attribute block from heading or paragraph text. */
export function splitTrailingAttrs(text) {
  const m = /\s*\{([^{}]+)\}\s*$/.exec(text);
  if (!m) return { text, id: '', classes: [], attrs: {} };
  const tokens = m[1].trim().match(/(?:[^\s"']+|"[^"]*"|'[^']*')+/g) || [];
  if (!tokens.length || !tokens.every((t) => ATTR_TOKEN.test(t))) return { text, id: '', classes: [], attrs: {} };
  const id = (tokens.find((t) => t.startsWith('#')) || '').slice(1);
  const classes = tokens.filter((t) => t.startsWith('.')).map((t) => t.slice(1));
  const attrs = parseAttributes(tokens.filter((t) => !t.startsWith('#') && !t.startsWith('.')).join(' '));
  return { text: text.slice(0, m.index), id, classes, attrs };
}

function headingInfo(block) {
  const parts = splitTrailingAttrs(block.text);
  return { ...parts, id: parts.id || slugify(plainText(parts.text)), level: block.level };
}

function blocksOf(nodes) {
  return nodes.flatMap((n) => (n.type === 'markdown' ? parseMarkdown(n.text) : []));
}

function firstHeadingId(nodes) {
  for (const node of nodes) {
    if (node.type === 'markdown') {
      const heading = parseMarkdown(node.text).find((b) => b.type === 'heading');
      if (heading) return headingInfo(heading).id;
    } else {
      const inner = firstHeadingId(node.children);
      if (inner) return inner;
    }
  }
  return '';
}

/** Split blocks into items at each heading of the first heading level found. */
function splitItems(blocks) {
  const first = blocks.find((b) => b.type === 'heading');
  if (!first) return [];
  const items = [];
  for (const block of blocks) {
    if (block.type === 'heading' && block.level === first.level) items.push({ heading: headingInfo(block), blocks: [] });
    else if (items.length) items[items.length - 1].blocks.push(block);
  }
  return items;
}

function inline(text) {
  return renderInline(parseInline(text));
}

function placeholderParagraph(text, key, Tag = 'p') {
  return (
    <Tag key={key} className="placeholder-copy">
      <span className="placeholder-tag">{strings.common.placeholderTag}</span>
      {inline(text)}
    </Tag>
  );
}

function renderWidget(name, attrs, key) {
  switch (name) {
    case 'logo': return <Logo key={key} />;
    case 'price': return <Price key={key} />;
    case 'join': return <JoinButton key={key} className={attrs.class} />;
    case 'signup-link': return <SignUpLink key={key} label={attrs.label} className={attrs.class} />;
    case 'store-badges': return <StoreBadges key={key} />;
    case 'public-posts': return <PublicSamplePosts key={key} limit={Number(attrs.limit) || 3} />;
    case 'public-threads': return <PublicSampleThreads key={key} limit={Number(attrs.limit) || 3} />;
    case 'public-events': return <PublicSampleCatalog key={key} kind="events" limit={Number(attrs.limit) || 3} />;
    case 'public-venues': return <PublicSampleCatalog key={key} kind="venues" limit={Number(attrs.limit) || 3} />;
    case 'public-lessons': return <PublicSampleCatalog key={key} kind="lessons" limit={Number(attrs.limit) || 3} />;
    case 'cover': return <CoverArt key={key} title={attrs.title || ''} subtitle={attrs.subtitle} label={attrs.label} size={attrs.size} src={attrs.src} />;
    case 'media':
      if (attrs.src && SAFE_IMAGE.test(attrs.src)) return <img key={key} className={'media-image' + (attrs.class ? ' ' + attrs.class : '')} src={attrs.src} alt={attrs.label || ''} />;
      return <div key={key} className={'media-placeholder' + (attrs.class ? ' ' + attrs.class : '')} role="img" aria-label={attrs.label}>{attrs.label}</div>;
    default: return null;
  }
}

function renderParagraph(text, key) {
  const widget = WIDGET.exec(text.trim());
  if (widget) return renderWidget(widget[1], parseAttributes(widget[2] || ''), key);
  const { text: body, classes } = splitTrailingAttrs(text);
  const placeholder = PLACEHOLDER_ONLY.exec(body.trim());
  if (placeholder) return placeholderParagraph(placeholder[1], key);
  return <p key={key} className={classes.join(' ') || undefined}>{inline(body)}</p>;
}

function renderMarkdownBlocks(blocks, prefix) {
  return blocks.map((block, i) => {
    const key = prefix + '-' + i;
    if (block.type === 'heading') {
      const h = headingInfo(block);
      const Tag = 'h' + Math.min(Math.max(h.level, 1), 6);
      return <Tag key={key} id={h.id} className={h.classes.join(' ') || undefined}>{inline(h.text)}</Tag>;
    }
    if (block.type === 'paragraph') return renderParagraph(block.text, key);
    return <Fragment key={key}>{renderBlocks([block])}</Fragment>;
  });
}

/** Render an item's body: a single paragraph inline, otherwise as blocks. */
function itemBody(blocks, prefix) {
  return renderMarkdownBlocks(blocks, prefix);
}

function linksIn(blocks) {
  const text = blocks.map((b) => (b.type === 'paragraph' ? b.text : b.type === 'list' ? b.items.map((item) => item.map((x) => x.text || '').join(' ')).join(' ') : '')).join(' ');
  return [...text.matchAll(LINK)].map((m) => ({ label: m[1], href: m[2] }));
}

function ActionButtons({ blocks, attrs }) {
  const size = attrs.size ? ' button-' + attrs.size : '';
  return (
    <div className={attrs.class || 'hero-actions'}>
      {linksIn(blocks).map((link, i) => {
        const variant = attrs.variant || (i === 0 ? 'primary' : 'secondary');
        const className = 'button button-' + variant + size;
        if (/^https?:/.test(link.href)) {
          return (
            <a key={link.href + i} href={link.href} className={className} target="_blank" rel="noopener noreferrer">
              {inline(link.label)}
              <Icon name="external" size={16} />
              <span className="visually-hidden">{' ' + strings.marketing.opensInNewTab}</span>
            </a>
          );
        }
        return <Link key={link.href + i} to={link.href} className={className}>{inline(link.label)}</Link>;
      })}
    </div>
  );
}

function listItems(blocks) {
  const list = blocks.find((b) => b.type === 'list');
  return list ? list.items.map((item) => (item.length === 1 && item[0].type === 'paragraph' ? item[0].text : item.map((b) => b.text || '').join(' '))) : [];
}

function renderDirective(node, key) {
  const { kind, attrs, children } = node;
  const blocks = () => blocksOf(children);
  switch (kind) {
    case 'section':
    case 'aside':
    case 'article': {
      const Tag = kind;
      const labelledBy = firstHeadingId(children);
      const content = renderNodes(children, key);
      return (
        <Tag key={key} className={attrs.class || (kind === 'section' ? 'page-section' : undefined)} aria-labelledby={labelledBy || undefined}>
          {attrs.inner ? <div className={attrs.inner}>{content}</div> : content}
        </Tag>
      );
    }
    case 'div':
    case 'header': {
      const Tag = kind;
      return <Tag key={key} className={attrs.class}>{renderNodes(children, key)}</Tag>;
    }
    case 'excerpt':
      return <blockquote key={key} className={attrs.class || 'excerpt'}>{renderNodes(children, key)}</blockquote>;
    case 'cards': {
      const Tag = attrs.tag === 'ol' ? 'ol' : 'ul';
      return (
        <Tag key={key} className={attrs.class || 'feature-grid'}>
          {splitItems(blocks()).map((item, i) => {
            const HTag = 'h' + Math.min(Math.max(item.heading.level, 1), 6);
            return (
              <li key={item.heading.id + i} className={attrs.item || 'feature-card'}>
                {attrs.numbered && <span className="step-number" aria-hidden="true">{i + 1}</span>}
                {attrs.media && <div className="media-placeholder media-thumb" role="img" aria-label={strings.marketing.mediaPlaceholder(plainText(item.heading.text) + ' image')}>{strings.common.placeholderTag}</div>}
                {item.heading.attrs.icon && <span className="feature-icon"><Icon name={item.heading.attrs.icon} size={20} /></span>}
                <HTag>{inline(item.heading.text)}</HTag>
                {itemBody(item.blocks, key + '-' + i)}
              </li>
            );
          })}
        </Tag>
      );
    }
    case 'faq':
      return (
        <div key={key} className={attrs.class || 'faq'}>
          {splitItems(blocks()).map((item, i) => (
            <details key={item.heading.id + i} className="faq-item" id={item.heading.id || undefined}>
              <summary><span>{inline(item.heading.text)}</span><Icon name="chevronDown" size={18} className="faq-chevron" /></summary>
              <div className="faq-answer">{item.blocks.length === 1 && item.blocks[0].type === 'paragraph' ? <p>{inline(item.blocks[0].text)}</p> : itemBody(item.blocks, key + '-' + i)}</div>
            </details>
          ))}
        </div>
      );
    case 'quotes': {
      const figures = [];
      for (const block of blocks()) {
        if (block.type === 'blockquote') figures.push({ quote: block.blocks, caption: '' });
        else if (block.type === 'paragraph' && figures.length && !figures[figures.length - 1].caption) figures[figures.length - 1].caption = block.text;
      }
      return (
        <div key={key} className={attrs.class || 'testimonial-grid'}>
          {figures.map((f, i) => (
            <figure key={i} className="testimonial">
              <blockquote>{renderMarkdownBlocks(f.quote, key + '-q' + i)}</blockquote>
              {f.caption && <figcaption>{inline(f.caption)}</figcaption>}
            </figure>
          ))}
        </div>
      );
    }
    case 'checks':
      return (
        <ul key={key} className={attrs.class || 'check-list'} aria-label={attrs.label}>
          {listItems(blocks()).map((text, i) => <li key={i}><Icon name="check" size={16} />{' '}{inline(text)}</li>)}
        </ul>
      );
    case 'actions':
      return <ActionButtons key={key} blocks={blocks()} attrs={attrs} />;
    case 'photos':
      return (
        <ul key={key} className={attrs.class || 'photo-grid'}>
          {listItems(blocks()).map((text, i) => {
            const image = PHOTO.exec(text.trim());
            if (image && SAFE_IMAGE.test(image[2])) {
              const caption = image[3].trim();
              return (
                <li key={i} className="photo-item">
                  <figure>
                    <img src={image[2]} alt={image[1] || plainText(caption)} loading="lazy" decoding="async" />
                    {caption && <figcaption>{inline(caption)}</figcaption>}
                  </figure>
                </li>
              );
            }
            return <li key={i} className="media-placeholder photo-tile" role="img" aria-label={plainText(text)}>{inline(text)}</li>;
          })}
        </ul>
      );
    case 'toc':
      return (
        <ol key={key} className={attrs.class || 'toc-list'}>
          {listItems(blocks()).map((text, i) => {
            const placeholder = PLACEHOLDER_ONLY.exec(text.trim());
            return <li key={i}>{placeholder ? placeholderParagraph(placeholder[1], i, 'span') : inline(text)}</li>;
          })}
        </ol>
      );
    default:
      return <div key={key} className={attrs.class}>{renderNodes(children, key)}</div>;
  }
}

function renderNodes(nodes, prefix = 'n') {
  return nodes.map((node, i) => {
    const key = prefix + '.' + i;
    if (node.type === 'markdown') return <Fragment key={key}>{renderMarkdownBlocks(parseMarkdown(node.text), key)}</Fragment>;
    return renderDirective(node, key);
  });
}

/**
 * Render a content page body (tokens already filled) to React elements.
 * @param {string} body
 */
export function renderContent(body) {
  return renderNodes(parseDirectives(body));
}

/**
 * A marketing page from the property's web/content folder. Front matter `layout: sections`
 * renders the body edge to edge (each section brings its own container); the default wraps it
 * in the standard page container.
 * @param {{ path: string }} props
 */
function ContentPage({ path }) {
  const { site, property, fill } = useProperty();
  useRouteMeta(path);
  const page = siteMetaFor(site).pages.find((p) => p.path === path);
  const body = page ? page.body : '';
  const content = useMemo(() => renderContent(fill(applyContentConditions(body, property))), [body, fill, property]);
  if (!page) return null;
  const home = path === '/';
  const samples = home ? (
    <>
      {!/^@public-events\b/m.test(body) && <PublicSampleCatalog kind="events" />}
      {!/^@public-lessons\b/m.test(body) && <PublicSampleCatalog kind="lessons" />}
      {!/^@public-venues\b/m.test(body) && <PublicSampleCatalog kind="venues" />}
      {!/^@public-threads\b/m.test(body) && <PublicSampleThreads />}
      {!/^@public-posts\b/m.test(body) && <PublicSamplePosts />}
    </>
  ) : null;
  if (page.meta.layout === 'sections') return <>{content}{samples}</>;
  return <><div className="site-container page">{content}</div>{samples}</>;
}

export default ContentPage;
