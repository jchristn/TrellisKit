import { useEffect, useRef, useState } from 'react';
import { Pressable, View } from 'react-native';
import { members as membersApi } from '../../api/community';
import { useAuth } from '../../auth/AuthProvider';
import { readEnumeration } from '../../content/enumeration';
import { insertMention, mentionQueryAt } from '../../content/mentions';
import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Avatar from '../Avatar';
import TextField from '../TextField';

/** Debounce before searching members (contract 18: 150 ms). */
export const MentionDebounceMs = 150;

/**
 * Multiline text field with @mention autocomplete: after `@` and one character it searches
 * members (POST /members/enumerate with `search`, with the post as context so its author and commenters rank
 * first) and inserts the chosen `@handle `. The server resolves handles on save.
 */
export default function MentionInput({ value, onChangeText, label, hint, error, placeholder, contextType, contextId, maxLength, testID, autoFocus, compact, onFocus, onBlur }) {
  const theme = useTheme();
  const { api } = useAuth();
  const [selection, setSelection] = useState({ start: value.length, end: value.length });
  const [controlledSelection, setControlledSelection] = useState(undefined);
  const [suggestions, setSuggestions] = useState([]);
  const requestRef = useRef(0);
  const query = mentionQueryAt(value, selection.end);
  const queryText = query ? query.query : null;

  useEffect(() => {
    if (!queryText) {
      // Hide suggestions when the cursor leaves a mention.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSuggestions([]);
      return undefined;
    }
    const id = (requestRef.current += 1);
    const timer = setTimeout(async () => {
      try {
        const result = await membersApi.search(api, queryText, { limit: 8, contextType, contextId });
        if (id === requestRef.current) setSuggestions(readEnumeration(result).items);
      } catch {
        if (id === requestRef.current) setSuggestions([]);
      }
    }, MentionDebounceMs);
    return () => clearTimeout(timer);
  }, [api, queryText, contextType, contextId]);

  function choose(member) {
    if (!query || !member.handle) return;
    const next = insertMention(value, query, member.handle);
    onChangeText(next.text);
    setSelection({ start: next.cursor, end: next.cursor });
    setControlledSelection({ start: next.cursor, end: next.cursor });
    setSuggestions([]);
  }

  return (
    <View style={{ gap: theme.spacing.xs }}>
      <TextField
        label={label}
        hint={hint}
        error={error}
        value={value}
        onChangeText={(text) => {
          setControlledSelection(undefined);
          onChangeText(text);
        }}
        onSelectionChange={(e) => {
          setControlledSelection(undefined);
          setSelection(e.nativeEvent.selection);
        }}
        // Only pass a selection while moving the cursor after a mention: a controlled selection keeps iOS from
        // starting a sentence with Shift on.
        {...(controlledSelection ? { selection: controlledSelection } : {})}
        placeholder={placeholder}
        multiline
        compact={compact}
        onFocus={onFocus}
        onBlur={onBlur}
        maxLength={maxLength}
        autoFocus={autoFocus}
        testID={testID}
      />
      {suggestions.length ? (
        <View accessibilityLabel={strings.composer.mentionSuggestions} style={{ borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radii.md, overflow: 'hidden' }} testID={testID ? testID + '-suggestions' : undefined}>
          {suggestions.map((member) => (
            <Pressable
              key={member.id}
              onPress={() => choose(member)}
              accessibilityRole="button"
              accessibilityLabel={member.displayName + ', @' + member.handle}
              accessibilityHint={strings.composer.mentionHint}
              testID={testID ? testID + '-suggestion-' + member.handle : undefined}
              style={({ pressed }) => ({ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.sm, minHeight: theme.layout.minTouchTarget, paddingHorizontal: theme.spacing.md, backgroundColor: pressed ? theme.colors.surface : theme.colors.background })}
            >
              <Avatar uri={member.avatarUrl ? api.url(member.avatarUrl) : null} token={api.accessToken} name={member.displayName} size={28} />
              <AppText style={{ flex: 1 }}>{member.displayName}</AppText>
              <AppText color="textMuted">{'@' + member.handle}</AppText>
            </Pressable>
          ))}
        </View>
      ) : null}
    </View>
  );
}
