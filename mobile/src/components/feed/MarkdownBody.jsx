import { useRouter } from 'expo-router';
import { useMemo } from 'react';
import { parseMarkdown } from '@trelliskit/shared';
import { Platform, ScrollView, Text, View } from 'react-native';
import { useProperty } from '../../property';
import { openAppLink } from '../../links';
import strings from '../../strings';
import { useTheme } from '../../theme';

/**
 * Renders a post or comment body from the shared Markdown subset (@trelliskit/shared markdown.js,
 * the same parser the website uses) as native Text and View elements: headings, emphasis, inline
 * and fenced code, lists, quotes, rules, tables, links, and mentions. Never HTML and never a
 * WebView. Links stay in native routes or an embedded external page; mentions that the server
 * resolved link to the member's profile.
 * @param {object} props
 * @param {string} props.body
 * @param {object[]} [props.mentions]
 * @param {boolean} [props.headings] Posts true; comments false.
 * @param {boolean} [props.code] Accepted for compatibility; code always renders as monospaced text.
 */
export default function MarkdownBody({ body, mentions, headings = true, code = false, testID }) {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const blocks = useMemo(() => parseMarkdown(body || '', { mentions: mentions || [], allowHeadings: headings }).children, [body, mentions, headings]);
  const mono = Platform.select({ ios: 'Menlo', default: 'monospace' });
  if (!blocks.length) return null;

  const openLink = (href) => openAppLink(href, { property, router }).catch(() => {});

  const inline = (nodes, keyPrefix) =>
    nodes.map((node, i) => {
      const key = keyPrefix + '.' + i;
      switch (node.type) {
        case 'text':
          return node.value;
        case 'break':
          return '\n';
        case 'code':
          return (
            <Text key={key} selectable style={{ fontFamily: mono, backgroundColor: theme.colors.surfaceRaised, color: theme.colors.text }} testID="markdown-inline-code">
              {node.value}
            </Text>
          );
        case 'strong':
          return (
            <Text key={key} style={{ fontWeight: '700' }}>
              {inline(node.children, key)}
            </Text>
          );
        case 'em':
          return (
            <Text key={key} style={{ fontStyle: 'italic' }}>
              {inline(node.children, key)}
            </Text>
          );
        case 'strike':
          return (
            <Text key={key} style={{ textDecorationLine: 'line-through' }}>
              {inline(node.children, key)}
            </Text>
          );
        case 'link':
          return (
            <Text key={key} accessibilityRole="link" onPress={() => openLink(node.href)} style={{ color: theme.colors.primary, textDecorationLine: 'underline' }}>
              {inline(node.children, key)}
            </Text>
          );
        case 'mention':
          return node.userId ? (
            <Text
              key={key}
              accessibilityRole="link"
              accessibilityHint={strings.feed.profileHint}
              onPress={() => router.push('/members/' + node.userId)}
              style={{ color: theme.colors.primary, fontWeight: '600' }}
              testID={'mention-' + node.handle}
            >
              {'@' + node.handle}
            </Text>
          ) : (
            '@' + node.handle
          );
        default:
          return null;
      }
    });

  const list = (node, key, nested = false) => (
    <View key={key} style={{ gap: theme.spacing.xxs, paddingLeft: nested ? theme.spacing.lg : 0 }}>
      {node.items.map((item, i) => (
        <View key={key + '.' + i} style={{ gap: theme.spacing.xxs }}>
          <View style={{ flexDirection: 'row', gap: theme.spacing.sm }}>
            <Text style={[theme.typography.body, { color: theme.colors.textMuted }]} importantForAccessibility="no" accessibilityElementsHidden>
              {node.ordered ? node.start + i + '.' : '•'}
            </Text>
            <Text style={[theme.typography.body, { color: theme.colors.text, flex: 1 }]}>{inline(item.children, key + '.' + i)}</Text>
          </View>
          {item.sublist ? list(item.sublist, key + '.' + i + '.l', true) : null}
        </View>
      ))}
    </View>
  );

  const block = (node, key) => {
    switch (node.type) {
      case 'heading':
        return (
          <Text key={key} accessibilityRole="header" style={[node.level === 3 ? theme.typography.heading : theme.typography.title, node.level === 2 ? { fontSize: 20, lineHeight: 28 } : null, { color: theme.colors.text }]}>
            {inline(node.children, key)}
          </Text>
        );
      case 'code':
        return (
          <ScrollView key={key} horizontal style={{ backgroundColor: theme.colors.surfaceRaised, borderColor: theme.colors.border, borderWidth: 1, borderRadius: theme.radii.md }} contentContainerStyle={{ padding: theme.spacing.md }} testID="markdown-code-block">
            <Text selectable style={{ fontFamily: mono, fontSize: 14, lineHeight: 20, color: theme.colors.text }}>
              {node.value}
            </Text>
          </ScrollView>
        );
      case 'rule':
        return <View key={key} style={{ height: 1, backgroundColor: theme.colors.border, marginVertical: theme.spacing.xs }} testID="markdown-rule" />;
      case 'table': {
        const cellStyle = (c, head) => ({ minWidth: 96, paddingVertical: theme.spacing.xs, paddingHorizontal: theme.spacing.sm, borderColor: theme.colors.border, borderRightWidth: c < node.header.length - 1 ? 1 : 0, backgroundColor: head ? theme.colors.surfaceRaised : undefined });
        const textAlign = (c) => node.align[c] || 'left';
        const row = (cells, r, head) => (
          <View key={key + '.' + r} style={{ flexDirection: 'row', borderColor: theme.colors.border, borderTopWidth: r === 'h' ? 0 : 1 }}>
            {cells.map((cell, c) => (
              <View key={c} style={cellStyle(c, head)}>
                <Text style={[theme.typography.caption, { color: theme.colors.text, fontWeight: head ? '700' : '400', textAlign: textAlign(c) }]}>{inline(cell, key + '.' + r + '.' + c)}</Text>
              </View>
            ))}
          </View>
        );
        return (
          <ScrollView key={key} horizontal testID="markdown-table">
            <View style={{ borderColor: theme.colors.border, borderWidth: 1, borderRadius: theme.radii.sm }}>
              {row(node.header, 'h', true)}
              {node.rows.map((cells, r) => row(cells, r, false))}
            </View>
          </ScrollView>
        );
      }
      case 'blockquote':
        return (
          <View key={key} style={{ borderLeftWidth: 3, borderLeftColor: theme.colors.border, paddingLeft: theme.spacing.md, gap: theme.spacing.sm }}>
            {node.children.map((child, i) => block(child, key + '.' + i))}
          </View>
        );
      case 'list':
        return list(node, key);
      default:
        return (
          <Text key={key} style={[theme.typography.body, { color: theme.colors.text }]}>
            {inline(node.children, key)}
          </Text>
        );
    }
  };

  return (
    <View testID={testID} style={{ gap: theme.spacing.sm }}>
      {blocks.map((node, i) => block(node, 'b' + i))}
    </View>
  );
}
