import { test } from 'node:test';
import assert from 'node:assert/strict';
import { firstLinkUrl, isInternalHref, isSafeHref, markdownExcerpt, parseInlineMarkdown, parseMarkdown, scalarLength, toPlainText } from '../src/index.js';

const text = (value) => ({ type: 'text', value });
const para = (body, options) => parseMarkdown(body, options).children[0].children;

/** Every link href anywhere in an AST. */
function hrefs(node, out = []) {
  if (!node || typeof node !== 'object') return out;
  if (node.type === 'link') out.push(node.href);
  for (const child of node.children || []) hrefs(child, out);
  for (const item of node.items || []) { for (const c of item.children) hrefs(c, out); if (item.sublist) hrefs(item.sublist, out); }
  return out;
}

/** Concatenated text of an AST (what a reader sees). */
function visible(node) {
  return toPlainText(node.type === 'root' ? node : { type: 'root', children: [node] });
}

test('paragraphs, line breaks, and emphasis', () => {
  const ast = parseMarkdown('One **bold** and *it* and _it2_ and ~~gone~~\nnext line\n\nSecond');
  assert.equal(ast.children.length, 2);
  assert.deepEqual(ast.children[0].children, [
    text('One '), { type: 'strong', children: [text('bold')] }, text(' and '), { type: 'em', children: [text('it')] }, text(' and '),
    { type: 'em', children: [text('it2')] }, text(' and '), { type: 'strike', children: [text('gone')] }, { type: 'break' }, text('next line')
  ]);
  assert.deepEqual(ast.children[1].children, [text('Second')]);
});

test('unmatched and intraword delimiters stay literal', () => {
  assert.deepEqual(para('2 * 3 = 6 and snake_case_name and **open'), [text('2 * 3 = 6 and snake_case_name and **open')]);
  assert.deepEqual(para('* not a list item'), parseMarkdown('* not a list item').children[0].children);
  assert.equal(parseMarkdown('* list').children[0].type, 'list');
});

test('lists with one nesting level, ordered start, and lazy continuation', () => {
  const ast = parseMarkdown('3. three\n4. four\n   - nested\ncontinued\n\n- a\n* b');
  const [ordered, bullets] = ast.children;
  assert.equal(ordered.ordered, true);
  assert.equal(ordered.start, 3);
  assert.equal(ordered.items.length, 2);
  assert.equal(ordered.items[1].sublist.items[0].children.length, 3);
  assert.equal(visible(ordered), 'three four nested continued');
  assert.equal(bullets.ordered, false);
  assert.equal(bullets.items.length, 2);
});

test('block quotes and headings (posts only)', () => {
  const ast = parseMarkdown('## Title\n### Sub\n#### four\n> quoted **text**\n> more');
  assert.deepEqual(ast.children.map((b) => b.type), ['heading', 'heading', 'paragraph', 'blockquote']);
  assert.equal(ast.children[0].level, 2);
  assert.equal(ast.children[1].level, 3);
  assert.deepEqual(ast.children[2].children, [text('#### four')]);
  const comment = parseMarkdown('## Not a heading', { allowHeadings: false });
  assert.deepEqual(comment.children, [{ type: 'paragraph', children: [text('## Not a heading')] }]);
  assert.equal(parseMarkdown('# single').children[0].type, 'heading');
  assert.equal(parseMarkdown('# single').children[0].level, 1);
  assert.equal(parseMarkdown('#nospace').children[0].type, 'paragraph');
});

test('links and bare URLs: http and https only, trailing punctuation removed', () => {
  assert.deepEqual(para('See [the site](https://example.test/a?b=1) now'), [text('See '), { type: 'link', href: 'https://example.test/a?b=1', children: [text('the site')] }, text(' now')]);
  assert.deepEqual(para('Go to https://example.test/path. Or (http://x.test/y).'), [
    text('Go to '), { type: 'link', href: 'https://example.test/path', children: [text('https://example.test/path')] }, text('. Or ('),
    { type: 'link', href: 'http://x.test/y', children: [text('http://x.test/y')] }, text(').')
  ]);
  assert.equal(firstLinkUrl('first https://a.test then [b](https://b.test)'), 'https://a.test');
  assert.equal(firstLinkUrl('no links here'), null);
});

test('mentions link only when listed, case-insensitively, never inside emails or link text', () => {
  const mentions = [{ userId: 'usr_1', handle: 'alexchen', displayName: 'Alex Chen' }];
  assert.deepEqual(para('Thanks @AlexChen. And @unknown, mail member@example.test', { mentions }), [
    text('Thanks '), { type: 'mention', handle: 'AlexChen', userId: 'usr_1' }, text('. And @unknown, mail member@example.test')
  ]);
  assert.deepEqual(para('@ab is too short'), [text('@ab is too short')]);
  const linked = para('[@alexchen](https://x.test)', { mentions });
  assert.deepEqual(linked[0].children, [text('@alexchen')]);
});

test('backslash escapes produce literal characters', () => {
  assert.deepEqual(para('\\*not italic\\* \\@alexchen \\[x\\](y) \\\\', { mentions: [{ handle: 'alexchen', userId: 'u' }] }), [
    text('*not italic* @alexchen [x](y) \\')
  ]);
  assert.equal(parseMarkdown('\\- not a list').children[0].type, 'paragraph');
  assert.equal(parseMarkdown('\\> not a quote').children[0].type, 'paragraph');
  assert.equal(parseMarkdown('\\## not a heading').children[0].type, 'paragraph');
});

test('XSS: HTML and dangerous schemes are literal text and never links', () => {
  const attacks = [
    '<script>alert(1)</script>',
    '<img src=x onerror=alert(1)>',
    '[click](javascript:alert(1))',
    '[click](JaVaScRiPt:alert(1))',
    '[click](  javascript:alert(1))',
    '[click](data:text/html;base64,PHNjcmlwdD4=)',
    '[click](vbscript:msgbox(1))',
    '[click](//evil.test/x)',
    '[click](https:/evil.test)',
    '[click](https://ok.test"onmouseover="alert(1))',
    '[click](https://ok.test/<script>)',
    'javascript:alert(1)',
    'https://ok.test/"><script>alert(1)</script>',
    '[x](file:///etc/passwd)',
    '![img](https://ok.test/a.png)',
    '<a href="javascript:alert(1)">x</a>',
    '`<script>alert(1)</script>` and ```[x](javascript:alert(1))```',
    '[a](https://a.test)[b](mailto:x@y.test)'
  ];
  for (const attack of attacks) {
    const ast = parseMarkdown(attack);
    for (const href of hrefs(ast)) {
      assert.ok(isSafeHref(href), attack + ' produced ' + href);
      assert.match(href, /^https?:\/\/[^\s<>"'`]+$/i, attack);
    }
    // Nothing is dropped: the reader sees what was typed (minus Markdown syntax that applied).
    const shown = visible(ast);
    if (!hrefs(ast).length && !attack.includes('`')) assert.equal(shown, attack.replace(/\s+/g, ' ').trim(), attack);
  }
  assert.deepEqual(hrefs(parseMarkdown('https://ok.test/"><script>alert(1)</script>')), ['https://ok.test/']);
  assert.deepEqual(hrefs(parseMarkdown('![img](https://ok.test/a.png)')), ['https://ok.test/a.png']);
  assert.deepEqual(hrefs(parseMarkdown('[a](https://a.test)[b](mailto:x@y.test)')), ['https://a.test']);
});

test('isSafeHref accepts only absolute http and https URLs', () => {
  assert.ok(isSafeHref('https://example.test'));
  assert.ok(isSafeHref('HTTP://example.test/a'));
  for (const bad of ['javascript:alert(1)', '/relative', '//proto.test', 'https://', 'https:///x', 'https://a b', 'ftp://x.test', 'https://x.test/\u0000', null, 42]) {
    assert.equal(isSafeHref(bad), false, String(bad));
  }
});

test('the AST contains only known node types and plain string values', () => {
  const allowed = new Set(['root', 'paragraph', 'heading', 'list', 'blockquote', 'text', 'strong', 'em', 'strike', 'link', 'mention', 'break']);
  const walk = (node) => {
    if (node.type) assert.ok(allowed.has(node.type), node.type);
    if (node.type === 'text') assert.equal(typeof node.value, 'string');
    for (const child of node.children || []) walk(child);
    for (const item of node.items || []) { item.children.forEach(walk); if (item.sublist) walk(item.sublist); }
  };
  walk(parseMarkdown('## h\n> **q** [l](https://a.test) <b>x</b>\n1. a\n   - b ~~c~~ _d_ @someone'));
});

test('pathological input stays fast', () => {
  const started = Date.now();
  parseMarkdown('*'.repeat(20000) + '_'.repeat(20000) + '['.repeat(5000) + '~~'.repeat(5000));
  parseMarkdown('**a '.repeat(3000));
  assert.ok(Date.now() - started < 2000);
});

test('plain text and excerpts', () => {
  assert.equal(toPlainText('## Hi **there** @sam\n\n- one\n- [two](https://x.test)'), 'Hi there @sam one two');
  const long = 'word '.repeat(100);
  const excerpt = markdownExcerpt(long, 200);
  assert.ok(excerpt.endsWith('...'));
  assert.ok(excerpt.length <= 203);
  assert.equal(markdownExcerpt('short'), 'short');
  assert.equal(scalarLength('a😀b'), 3);
  assert.deepEqual(parseInlineMarkdown('**x**'), [{ type: 'strong', children: [text('x')] }]);
});

test('inline code and fenced code blocks are literal', () => {
  const inline = parseMarkdown('Use `**not bold** @someone [x](https://a.test)` here').children[0].children;
  assert.deepEqual(inline, [text('Use '), { type: 'code', value: '**not bold** @someone [x](https://a.test)' }, text(' here')]);
  assert.deepEqual(parseMarkdown('a `` code with ` tick `` b').children[0].children[1], { type: 'code', value: 'code with ` tick' });
  assert.deepEqual(parseMarkdown('an `unclosed tick').children[0].children, [text('an `unclosed tick')]);
  const fenced = parseMarkdown('Before\n```js\nconst a = 1;\n\n# not a heading\n```\nAfter');
  assert.deepEqual(fenced.children.map((b) => b.type), ['paragraph', 'code', 'paragraph']);
  assert.deepEqual(fenced.children[1], { type: 'code', lang: 'js', value: 'const a = 1;\n\n# not a heading' });
  assert.deepEqual(parseMarkdown('~~~\nunterminated').children[0], { type: 'code', lang: null, value: 'unterminated' });
  assert.equal(toPlainText('x `y` z\n\n```\ncode\n```'), 'x y z code');
});

test('horizontal rules and tables', () => {
  assert.deepEqual(parseMarkdown('a\n\n---\n\nb').children.map((x) => x.type), ['paragraph', 'rule', 'paragraph']);
  assert.deepEqual(parseMarkdown('* * *').children, [{ type: 'rule' }]);
  assert.equal(parseMarkdown('- item').children[0].type, 'list');
  const table = parseMarkdown('| Belt | Years |\n|:---|---:|\n| Blue | **2** |\n| Purple |').children[0];
  assert.equal(table.type, 'table');
  assert.deepEqual(table.align, ['left', 'right']);
  assert.deepEqual(table.header, [[text('Belt')], [text('Years')]]);
  assert.deepEqual(table.rows[0], [[text('Blue')], [{ type: 'strong', children: [text('2')] }]]);
  assert.deepEqual(table.rows[1], [[text('Purple')], []], 'short rows are padded');
  assert.equal(parseMarkdown('a | b\nnot a separator').children[0].type, 'paragraph');
  assert.deepEqual(parseMarkdown('| `a|b` | c\\|d |\n|---|---|').children[0].header, [[{ type: 'code', value: 'a|b' }], [text('c|d')]]);
  assert.equal(toPlainText('| a | b |\n|---|---|\n| 1 | 2 |'), 'a b 1 2');
});

test('site paths are links; protocol-relative and other schemes stay text', () => {
  const nodes = parseMarkdown('See the [Terms](/terms) and [Privacy](/privacy#data) or [x](//evil.test/a)').children[0].children;
  const links = nodes.filter((n) => n.type === 'link').map((n) => n.href);
  assert.deepEqual(links, ['/terms', '/privacy#data']);
  assert.ok(isInternalHref('/app/forums/general?x=1'));
  assert.ok(!isInternalHref('//evil.test') && !isInternalHref('javascript:alert(1)') && !isInternalHref('/a b'));
});
