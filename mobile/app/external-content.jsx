import { Stack, useFocusEffect, useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback, useRef, useState } from 'react';
import { BackHandler, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { WebView } from 'react-native-webview';
import AppText from '../src/components/AppText';
import Button from '../src/components/Button';
import ErrorState from '../src/components/ErrorState';
import Loading from '../src/components/Loading';
import { resolveAppLink } from '../src/linkTargets';
import { useProperty } from '../src/property';
import strings from '../src/strings';
import { useTheme } from '../src/theme';

/** Untrusted external pages are isolated from the app session and cannot launch other apps. */
export default function ExternalContentScreen() {
  const params = useLocalSearchParams();
  return <ExternalContentView key={JSON.stringify(params)} params={params} />;
}

function ExternalContentView({ params }) {
  const property = useProperty();
  const router = useRouter();
  const theme = useTheme();
  const web = useRef(null);
  const initial = resolveAppLink(typeof params.url === 'string' ? params.url : '', property);
  const [override, setOverride] = useState(null);
  const [failed, setFailed] = useState(false);
  const [canGoBack, setCanGoBack] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const url = override || (initial?.kind === 'web' ? initial.url : null);
  const t = strings.links;
  const mainUrl = useRef(url);

  useFocusEffect(useCallback(() => {
    const listener = BackHandler.addEventListener('hardwareBackPress', () => {
      if (!canGoBack || !web.current) return false;
      web.current.goBack();
      return true;
    });
    return () => listener.remove();
  }, [canGoBack]));

  const follow = (href, popup = false) => {
    const target = resolveAppLink(href, property);
    if (target?.kind === 'native') {
      router.replace(target.path);
      return false;
    }
    if (target?.kind !== 'web') {
      setBlocked(true);
      return false;
    }
    if (popup) { setOverride(target.url); setFailed(false); }
    return !popup;
  };

  return (
    <SafeAreaView edges={['left', 'right', 'bottom']} style={{ flex: 1, backgroundColor: theme.colors.background }} testID="screen-external-content">
      <Stack.Screen options={{ title: t.title }} />
      <Button label={strings.common.close} variant="ghost" onPress={() => router.canGoBack() ? router.back() : router.replace('/feed')} testID="web-content-close" />
      {!url ? <ErrorState title={t.unavailableTitle} message={t.unavailableBody} testID="link-unavailable" /> : <>
        <View style={{ padding: theme.spacing.sm, gap: theme.spacing.xs }}>
          <AppText variant="caption">{new URL(url).host}</AppText>
          {canGoBack ? <Button label={strings.common.back} variant="ghost" onPress={() => web.current?.goBack()} testID="web-content-back" /> : null}
          {blocked ? <AppText accessibilityRole="alert">{t.blocked}</AppText> : null}
        </View>
        {failed ? <ErrorState title={t.loadFailedTitle} message={t.loadFailedBody} onRetry={() => { setFailed(false); web.current?.reload(); }} testID="web-content-error" /> :
          <WebView
            ref={web}
            testID="web-content"
            source={{ uri: url }}
            originWhitelist={['*']}
            onShouldStartLoadWithRequest={(request) => follow(request.url)}
            onOpenWindow={({ nativeEvent }) => follow(nativeEvent.targetUrl, true)}
            onNavigationStateChange={(state) => { mainUrl.current = state.url || mainUrl.current; setCanGoBack(state.canGoBack); }}
            onLoadStart={({ nativeEvent }) => { mainUrl.current = nativeEvent.url; }}
            onError={() => setFailed(true)}
            onHttpError={({ nativeEvent }) => { if (nativeEvent.statusCode >= 400 && (nativeEvent.isTopFrame === true || nativeEvent.url === mainUrl.current)) setFailed(true); }}
            onFileDownload={() => setBlocked(true)}
            javaScriptCanOpenWindowsAutomatically={false}
            setSupportMultipleWindows
            sharedCookiesEnabled={false}
            thirdPartyCookiesEnabled={false}
            incognito
            allowFileAccess={false}
            allowFileAccessFromFileURLs={false}
            allowUniversalAccessFromFileURLs={false}
            mixedContentMode="never"
            allowsLinkPreview={false}
            startInLoadingState
            renderLoading={() => <Loading label={strings.common.loading} />}
            style={{ flex: 1 }}
          />}
      </>}
    </SafeAreaView>
  );
}
