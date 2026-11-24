import { normalizeSlugInput, PublicPageKind, publicPagesConfig, publicPageUrl, slugProblem } from '@trelliskit/shared';
import { useCallback, useEffect, useState } from 'react';
import { Share, View } from 'react-native';
import { describeError, fieldErrorsOf } from '../../auth/errors';
import { useSlugAvailability } from '../../hooks/useSlugAvailability';
import { useProperty } from '../../property/PropertyProvider';
import strings from '../../strings';
import { openAppLink } from '../../links';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import CheckRow from '../CheckRow';
import Notice from '../Notice';
import TextField from '../TextField';

/** Text and color for a slug status from useSlugAvailability. */
export function slugStatusText(status) {
  const t = strings.publicPage;
  if (status === 'checking') return { text: t.checking, color: 'textMuted', error: null };
  if (status === 'available') return { text: t.available, color: 'success', error: null };
  if (status === 'current') return { text: t.current, color: 'textMuted', error: null };
  if (status === 'error') return { text: t.checkError, color: 'textMuted', error: null };
  if (t.problems[status]) return { text: null, color: 'danger', error: t.problems[status] };
  return { text: null, color: 'textMuted', error: null };
}

/** Open the share sheet for a link (the sheet offers Copy, so no clipboard module is needed). */
export function shareLink(url) {
  if (!url) return Promise.resolve(null);
  return Share.share({ message: url, url }).catch(() => null);
}

/**
 * Account > Edit profile: the member's opt-in public profile page (docs/PHASE4_CONTRACT.md
 * section 18). A switch turns it on (off by default), the page address is checked live (the
 * shared slug rules first, then the server for availability), and once saved the page's URL can
 * be shared or opened. Renders nothing when the property turns public pages off or the server
 * does not offer them.
 * @param {object} props
 * @param {object} props.api MobileApiClient.
 * @param {(page: object) => void} [props.onSaved] Called with the saved page settings.
 */
export default function PublicPageEditor({ api, onSaved }) {
  const theme = useTheme();
  const property = useProperty();
  const config = publicPagesConfig(property);
  const t = strings.publicPage;
  const [page, setPage] = useState(null);
  const [loadState, setLoadState] = useState('loading');
  const [enabled, setEnabled] = useState(false);
  const [slug, setSlug] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [slugError, setSlugError] = useState(null);
  const [saved, setSaved] = useState(false);

  const apply = useCallback((next) => {
    setPage(next);
    setEnabled(Boolean(next && next.enabled));
    setSlug((next && (next.slug || next.suggestedSlug)) || '');
  }, []);

  const load = useCallback(
    () =>
      Promise.resolve(api.getMyPublicPage())
        .then((next) => {
          apply(next);
          setLoadState('ready');
        })
        .catch((e) => setLoadState(e && e.status === 404 ? 'unavailable' : 'error')),
    [api, apply]
  );

  useEffect(() => {
    if (config.enabled) load();
  }, [config.enabled, load]);

  const status = useSlugAvailability(api, PublicPageKind.Profile, slug, { currentSlug: page && page.slug });

  if (!config.enabled || loadState === 'unavailable' || loadState === 'loading') return null;
  if (loadState === 'error') {
    return (
      <Card testID="public-page">
        <AppText variant="heading" accessibilityRole="header">
          {t.heading}
        </AppText>
        <Notice tone="error" testID="public-page-load-error">
          {t.loadError}
        </Notice>
        <Button
          label={strings.common.retry}
          variant="secondary"
          onPress={() => {
            setLoadState('loading');
            load();
          }}
          testID="public-page-retry"
        />
      </Card>
    );
  }

  const live = Boolean(page && page.enabled && page.slug);
  const unchanged = Boolean(page) && enabled === Boolean(page.enabled) && (!enabled || slug === (page.slug || ''));
  const liveUrl = live && unchanged ? page.url || publicPageUrl(property, PublicPageKind.Profile, page.slug) : null;
  const draftUrl = enabled && !slugProblem(slug) ? publicPageUrl(property, PublicPageKind.Profile, slug) : null;
  const shownUrl = liveUrl || draftUrl;
  const info = slugStatusText(status);
  const blocked = enabled && (Boolean(slugProblem(slug)) || status === 'taken' || status === 'reserved');

  async function save() {
    setError(null);
    setSlugError(null);
    setSaved(false);
    setBusy(true);
    try {
      const body = enabled ? { enabled: true, slug } : { enabled: false };
      const next = await api.updateMyPublicPage(body);
      apply(next);
      setSaved(true);
      if (onSaved) onSaved(next);
    } catch (e) {
      const fields = fieldErrorsOf(e);
      if (e && e.code === 'SlugInUse') setSlugError(fields.slug || t.problems.taken);
      else if (fields.slug) setSlugError(fields.slug);
      else setError(describeError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card testID="public-page">
      <View style={{ gap: theme.spacing.sm }}>
        <AppText variant="heading" accessibilityRole="header">
          {t.heading}
        </AppText>
        <AppText color="textMuted">{t.body}</AppText>
        <CheckRow
          label={t.toggle}
          value={enabled}
          onValueChange={(value) => {
            setSaved(false);
            setEnabled(value);
          }}
          testID="public-page-toggle"
        />
        {enabled ? (
          <TextField
            label={t.slug}
            value={slug}
            onChangeText={(text) => {
              setSaved(false);
              setSlugError(null);
              setSlug(normalizeSlugInput(text));
            }}
            hint={t.slugHint}
            status={slugError || info.error ? null : info.text}
            statusColor={info.color}
            error={slugError || info.error}
            autoCapitalize="none"
            autoCorrect={false}
            spellCheck={false}
            keyboardType="url"
            maxLength={80}
            testID="public-page-slug"
          />
        ) : null}
        {shownUrl ? (
          <View style={{ gap: theme.spacing.xxs }}>
            <AppText variant="label">{t.urlLabel}</AppText>
            <AppText selectable testID="public-page-url">
              {shownUrl}
            </AppText>
            {liveUrl ? null : (
              <AppText variant="caption" color="textMuted" testID="public-page-url-pending">
                {t.urlPending}
              </AppText>
            )}
          </View>
        ) : null}
        {liveUrl ? (
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm }}>
            <Button label={t.share} accessibilityHint={t.shareHint} variant="secondary" onPress={() => shareLink(liveUrl)} style={{ alignSelf: 'auto' }} testID="public-page-share" />
            <Button label={t.open} accessibilityHint={t.openHint} variant="ghost" onPress={() => openAppLink('/profile').catch(() => {})} style={{ alignSelf: 'auto' }} testID="public-page-open" />
          </View>
        ) : null}
        <Notice tone="error" testID="public-page-error">
          {error}
        </Notice>
        <Notice tone="success" testID="public-page-saved">
          {saved ? t.saved : null}
        </Notice>
        <Button label={t.save} variant="secondary" onPress={save} disabled={busy || unchanged || blocked} testID="public-page-save" />
      </View>
    </Card>
  );
}
