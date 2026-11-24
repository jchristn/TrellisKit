import { Stack, useLocalSearchParams } from 'expo-router';
import { useMemo } from 'react';
import { View } from 'react-native';
import AppText from '../../src/components/AppText';
import ErrorState from '../../src/components/ErrorState';
import MarkdownBody from '../../src/components/feed/MarkdownBody';
import Loading from '../../src/components/Loading';
import Screen from '../../src/components/Screen';
import { getApiClient } from '../../src/api/client';
import { documents as documentsApi } from '../../src/api/endpoints';
import { useOptionalAuth } from '../../src/auth/AuthProvider';
import { documentByKey, documentMarkdown, formatDocumentDate } from '../../src/documents/documents';
import { useResource } from '../../src/hooks/useResource';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * `/documents/{key}`: one property document in full (GET /public/documents/{key}, the live
 * current version), with its version and when it was last updated. Rendered from Markdown as
 * native text; a failed load offers Retry.
 */
export default function DocumentScreen() {
  const theme = useTheme();
  const t = strings.documents;
  const auth = useOptionalAuth();
  const api = (auth && auth.api) || getApiClient();
  const property = useProperty();
  const params = useLocalSearchParams();
  const key = Array.isArray(params.key) ? params.key[0] : params.key;
  const known = documentByKey(property, key);
  const { data, status, reload } = useResource(() => documentsApi.get(api, key), key);
  const body = useMemo(() => documentMarkdown(data && data.content), [data]);
  const title = (data && data.title) || (known && known.title) || t.title;

  let content;
  if (status === 'loading') content = <Loading label={t.loading} testID="document-loading" />;
  else if (status === 'gone') content = <ErrorState title={t.notFoundTitle} message={t.notFoundBody} testID="document-not-found" />;
  else if (status === 'error' || !data) content = <ErrorState title={t.loadFailedTitle} message={t.loadFailedBody} onRetry={() => reload()} retryHint={t.retryHint} testID="document-error" />;
  else {
    const updated = formatDocumentDate(data.updatedUtc);
    content = (
      <View style={{ gap: theme.spacing.lg }}>
        <View style={{ gap: theme.spacing.xs }}>
          <AppText variant="title" accessibilityRole="header" testID="document-title">
            {title}
          </AppText>
          {data.version ? (
            <AppText color="textMuted" testID="document-version">
              {t.version(data.version)}
            </AppText>
          ) : null}
          {updated ? (
            <AppText color="textMuted" testID="document-updated">
              {t.updated(updated)}
            </AppText>
          ) : null}
        </View>
        {body ? <MarkdownBody body={body} headings testID="document-body" /> : <AppText color="textMuted">{t.empty}</AppText>}
      </View>
    );
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.lg }} testID="screen-document">
      <Stack.Screen options={{ title }} />
      {content}
    </Screen>
  );
}
