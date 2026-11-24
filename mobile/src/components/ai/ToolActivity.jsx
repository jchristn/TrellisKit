import { useState } from 'react';
import { useRouter } from 'expo-router';
import { Pressable, Text, View } from 'react-native';
import { toolCallDisplayItems } from '@trelliskit/shared';
import strings from '../../strings';
import { useTheme } from '../../theme';

const s = strings.assistants;
const FORMAT = { running: s.toolRunning, failed: s.toolFailed, fallbackLabel: s.toolDefault };

function ToolChip({ item, router }) {
  const theme = useTheme();
  const [open, setOpen] = useState(false);
  const chip = {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: theme.spacing.xs,
    borderWidth: 1,
    borderStyle: item.state === 'running' ? 'dashed' : 'solid',
    borderColor: theme.colors.border,
    borderRadius: theme.radii.pill,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: 2,
    opacity: item.state === 'failed' ? 0.7 : 1
  };
  const textStyle = [theme.typography.caption, { color: item.state === 'done' ? theme.colors.text : theme.colors.textMuted, fontStyle: item.state === 'failed' ? 'italic' : 'normal' }];
  const hasResults = item.state === 'done' && item.results.length > 0;
  if (!hasResults) {
    return (
      <View style={chip} testID={'tool-chip-' + item.id} accessibilityLiveRegion="polite" accessibilityLabel={item.text}>
        <Text style={textStyle}>{item.text}</Text>
      </View>
    );
  }
  const count = s.toolResults(item.resultCount ?? item.results.length);
  return (
    <View style={{ gap: theme.spacing.xs }}>
      <Pressable
        style={chip}
        onPress={() => setOpen((o) => !o)}
        accessibilityRole="button"
        accessibilityLabel={item.text + ', ' + count}
        accessibilityHint={s.toolResultsHint}
        accessibilityState={{ expanded: open }}
        accessibilityLiveRegion="polite"
        testID={'tool-chip-' + item.id}
      >
        <Text style={textStyle}>{item.text}</Text>
        <Text style={[theme.typography.caption, { color: theme.colors.textMuted }]}>{count}</Text>
      </Pressable>
      {open ? (
        <View style={{ gap: 2, paddingLeft: theme.spacing.sm }} testID={'tool-results-' + item.id}>
          {item.results.map((result) => (
            result.path ? (
              <Text key={result.key} accessibilityRole="link" onPress={() => router.push(result.path)} style={[theme.typography.caption, { color: theme.colors.primary }]} testID={'tool-result-' + result.key}>
                {result.title}
              </Text>
            ) : (
              <Text key={result.key} style={[theme.typography.caption, { color: theme.colors.textMuted }]}>{result.title}</Text>
            )
          ))}
        </View>
      ) : null}
    </View>
  );
}

/**
 * Compact tool activity above an assistant reply (docs/AI_CONTRACT.md section 8): a chip per tool
 * call, the running label while it runs, the completed label with its results (tap to show; app
 * paths open in place) when done, failures muted. Works for streamed and persisted tool calls.
 */
export default function ToolActivity({ message }) {
  const theme = useTheme();
  const router = useRouter();
  const items = toolCallDisplayItems(message, FORMAT);
  if (items.length === 0) return null;
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs }} accessibilityLabel={s.toolActivity} testID={'tool-activity-' + message.id}>
      {items.map((item) => <ToolChip key={item.id} item={item} router={router} />)}
    </View>
  );
}
