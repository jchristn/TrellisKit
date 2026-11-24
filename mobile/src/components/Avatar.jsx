import { Image, View } from 'react-native';
import { identityHeaders } from '../api/identity';
import { useTheme } from '../theme';
import AppText from './AppText';

/**
 * Up to two initials for a display name: the first character of the first and last words that start
 * with a letter or digit (whole code points, so names with emoji or astral letters never render half
 * a surrogate pair). Falls back to the first code point of the name, then "?".
 */
export function initials(name) {
  const parts = String(name || '').trim().split(/\s+/).filter(Boolean);
  const words = parts.filter((p) => /^[\p{L}\p{N}]/u.test(p));
  const first = (word) => Array.from(word)[0] || '';
  if (words.length === 0) return (parts.length ? first(parts[0]) : '?').toUpperCase();
  return (first(words[0]) + (words.length > 1 ? first(words[words.length - 1]) : '')).toUpperCase();
}

/**
 * Round avatar image, or initials when there is no photo. Pass `token` so the request carries the
 * bearer token (avatars are members only in production). Remote avatars also carry the
 * identification headers (X-TK-Property, X-TK-App, X-Client-Update-Id).
 */
export default function Avatar({ uri, token, name, size = 72, testID }) {
  const theme = useTheme();
  const style = { width: size, height: size, borderRadius: size / 2, backgroundColor: theme.colors.secondary, alignItems: 'center', justifyContent: 'center', overflow: 'hidden' };
  if (uri) {
    const remote = /^https?:\/\//i.test(uri);
    const headers = { ...(remote ? identityHeaders() : null), ...(token ? { Authorization: 'Bearer ' + token } : null) };
    const source = Object.keys(headers).length ? { uri, headers } : { uri };
    return <Image testID={testID} source={source} style={style} accessibilityIgnoresInvertColors accessible accessibilityLabel={name} />;
  }
  return (
    <View testID={testID} style={style} accessible accessibilityLabel={name}>
      <AppText variant="heading" color="onSecondary">
        {initials(name)}
      </AppText>
    </View>
  );
}
