import { useEffect, useRef, useState } from 'react';
import { Pressable, View } from 'react-native';
import { readEnumeration } from '../../content/enumeration';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import TextField from '../TextField';

/** Debounce before asking the server for suggestions. */
export const SuggestDebounceMs = 200;

/**
 * A text field with server suggestions (labels, tag keys, tag values). `suggest(prefix)` returns
 * an EnumerationResult of `{ label|key|value, count }`; `pick` reads the text from an entry.
 * Suggestions in the `exclude` list (already chosen) and staff-only entries for non-staff are
 * hidden.
 */
export default function AutocompleteField({ label, value, onChangeText, onSubmit, suggest, pick, exclude = [], hideStaff = true, hint, testID, returnKeyType = 'done' }) {
  const theme = useTheme();
  const [suggestions, setSuggestions] = useState([]);
  const [focused, setFocused] = useState(false);
  const request = useRef(0);
  const suggestRef = useRef(suggest);
  const pickRef = useRef(pick);
  const excludeRef = useRef(exclude);
  useEffect(() => {
    suggestRef.current = suggest;
    pickRef.current = pick;
    excludeRef.current = exclude;
  });
  const enabled = Boolean(suggest);

  useEffect(() => {
    if (!focused || !enabled) {
      // Suggestions only show while the field is focused.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSuggestions([]);
      return undefined;
    }
    const id = (request.current += 1);
    const timer = setTimeout(async () => {
      try {
        const result = await suggestRef.current(value.trim());
        if (id !== request.current) return;
        const lower = excludeRef.current.map((e) => String(e).toLowerCase());
        const items = readEnumeration(result)
          .items.map(pickRef.current)
          .filter((text) => typeof text === 'string' && text && !lower.includes(text.toLowerCase()) && !(hideStaff && text.toLowerCase().startsWith('staff:')));
        setSuggestions(items.slice(0, 8));
      } catch {
        if (id === request.current) setSuggestions([]);
      }
    }, SuggestDebounceMs);
    return () => clearTimeout(timer);
  }, [focused, value, enabled, hideStaff]);

  return (
    <View style={{ gap: theme.spacing.xs }}>
      <TextField
        label={label}
        hint={hint}
        value={value}
        onChangeText={onChangeText}
        onFocus={() => setFocused(true)}
        onBlur={() => setTimeout(() => setFocused(false), 150)}
        onSubmitEditing={() => onSubmit && onSubmit(value)}
        autoCapitalize="none"
        autoCorrect={false}
        returnKeyType={returnKeyType}
        testID={testID}
      />
      {suggestions.length ? (
        <View accessibilityLabel={strings.filters.suggestions} style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs }} testID={testID ? testID + '-suggestions' : undefined}>
          {suggestions.map((text) => (
            <Pressable
              key={text}
              onPress={() => {
                setSuggestions([]);
                if (onSubmit) onSubmit(text);
                else onChangeText(text);
              }}
              accessibilityRole="button"
              accessibilityLabel={strings.filters.useSuggestion(text)}
              hitSlop={4}
              style={({ pressed }) => ({ minHeight: 36, justifyContent: 'center', paddingHorizontal: theme.spacing.md, borderRadius: theme.radii.pill, borderWidth: 1, borderColor: theme.colors.border, backgroundColor: pressed ? theme.colors.accentSoft : theme.colors.surface })}
              testID={testID ? testID + '-suggestion-' + text : undefined}
            >
              <AppText variant="caption">{text}</AppText>
            </Pressable>
          ))}
        </View>
      ) : null}
    </View>
  );
}
