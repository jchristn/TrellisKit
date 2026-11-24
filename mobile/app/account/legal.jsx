import { useRouter } from 'expo-router';
import { useCallback } from 'react';
import { View } from 'react-native';
import AppText from '../../src/components/AppText';
import Button from '../../src/components/Button';
import ErrorState from '../../src/components/ErrorState';
import Loading from '../../src/components/Loading';
import Screen from '../../src/components/Screen';
import Section from '../../src/components/Section';
import { documents as documentsApi } from '../../src/api/endpoints';
import { useAuth } from '../../src/auth/AuthProvider';
import { documentPath, documentsOf, formatDocumentDate } from '../../src/documents/documents';
import { useCursorList } from '../../src/hooks/useCursorList';
import { useProperty } from '../../src/property';
import strings from '../../src/strings';
import { useTheme } from '../../src/theme';

/**
 * Account > Legal: every document of the property (title and current version), each opening
 * the document screen, and the member's own acceptance history
 * (POST /me/documents/acceptances/enumerate, one bounded page at a time with Load more).
 */
export default function LegalScreen() {
  const theme = useTheme();
  const router = useRouter();
  const property = useProperty();
  const { api } = useAuth();
  const t = strings.documents;
  const list = documentsOf(property);
  const fetchPage = useCallback((continuationToken) => documentsApi.enumerateAcceptances(api, continuationToken ? { continuationToken } : {}), [api]);
  const history = useCursorList(fetchPage);

  let historyBody;
  if (history.status === 'loading') historyBody = <Loading label={strings.common.loading} testID="legal-history-loading" />;
  else if (history.status === 'error') historyBody = <ErrorState title={t.historyLoadFailed} onRetry={history.reload} testID="legal-history-error" />;
  else if (!history.items.length) historyBody = <AppText color="textMuted" testID="legal-history-empty">{t.historyEmpty}</AppText>;
  else {
    historyBody = (
      <View style={{ gap: theme.spacing.md }}>
        {history.items.map((item) => {
          const date = formatDocumentDate(item.acceptedUtc);
          return (
            <View key={item.id} accessible style={{ gap: theme.spacing.xxs }} testID={'legal-acceptance-' + item.id}>
              <AppText style={{ fontWeight: '600' }}>{t.historyItem(item.documentTitle || item.documentKey, item.version)}</AppText>
              <AppText variant="caption" color="textMuted">
                {[date ? t.acceptedOn(date) : null, t.methods[item.method] || null].filter(Boolean).join(' · ')}
              </AppText>
            </View>
          );
        })}
        {history.moreError ? <AppText color="danger">{t.historyLoadFailed}</AppText> : null}
        {history.hasMore ? <Button label={t.loadMore} variant="secondary" onPress={history.loadMore} disabled={history.loadingMore} testID="legal-history-more" /> : null}
      </View>
    );
  }

  return (
    <Screen contentStyle={{ gap: theme.spacing.xl }} testID="screen-legal">
      <Section title={strings.account.legal}>
        {list.length ? (
          list.map((doc) => (
            <Button
              key={doc.key}
              label={doc.version ? doc.title + ', ' + t.version(doc.version) : doc.title}
              accessibilityHint={t.readHint(doc.title)}
              variant="secondary"
              onPress={() => router.push(documentPath(doc.key))}
              testID={'legal-document-' + doc.key}
            />
          ))
        ) : (
          <AppText color="textMuted">{t.legalEmpty}</AppText>
        )}
      </Section>
      <Section title={t.historyTitle} testID="legal-history">
        {historyBody}
      </Section>
    </Screen>
  );
}
