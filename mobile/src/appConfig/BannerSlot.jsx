import { useContext } from 'react';
import { Image } from 'expo-image';
import { useRouter } from 'expo-router';
import { View, useColorScheme } from 'react-native';
import { SafeAreaInsetsContext } from 'react-native-safe-area-context';
import { activeBanners, isBannerDismissible } from '@trelliskit/shared';
import AppText from '../components/AppText';
import IconButton from '../components/IconButton';
import LinkButton from '../components/LinkButton';
import MarkdownBody from '../components/feed/MarkdownBody';
import strings from '../strings';
import { openAppLink } from '../links';
import { useTheme } from '../theme';
import { useAppContent } from './AppContentProvider';

/** Most banners one slot shows at a time (highest priority first). */
export const MaxBannersPerSlot = 3;

const STYLE_COLORS = { Info: 'primary', Success: 'success', Warning: 'warning', Critical: 'danger' };

const ZERO_INSETS = { top: 0, right: 0, bottom: 0, left: 0 };

/** The banners a placement shows now (outside the provider: none). */
export function useBanners(placement) {
  const content = useAppContent();
  if (!content) return [];
  return activeBanners(content.state.banners, { placement, dismissed: content.dismissed, now: content.now }).slice(0, MaxBannersPerSlot);
}

function Banner({ banner, content, testID }) {
  const theme = useTheme();
  const router = useRouter();
  const scheme = useColorScheme();
  const t = strings.appContent;
  const accent = theme.colors[STYLE_COLORS[banner.style] || 'primary'];
  const styleLabel = t.styles[banner.style] || t.styles.Info;
  const link = banner.link || null;
  const image = banner.imageAssetId ? (content.state.assets || {})[banner.imageAssetId] || null : null;
  const imageUri = image ? content.assetUri(image, { dark: scheme === 'dark' }) : null;
  const openLink = () => {
    if (!link) return;
    openAppLink(link.path || link.url, { router }).catch(() => {});
  };
  return (
    <View
      testID={testID}
      accessibilityRole={banner.style === 'Critical' ? 'alert' : 'summary'}
      accessibilityLabel={t.bannerLabel(styleLabel, banner.title)}
      style={{
        flexDirection: 'row',
        alignItems: 'flex-start',
        gap: theme.spacing.sm,
        backgroundColor: theme.colors.surface,
        borderColor: theme.colors.border,
        borderWidth: 1,
        borderLeftWidth: 4,
        borderLeftColor: accent,
        borderRadius: theme.radii.md,
        padding: theme.spacing.md
      }}
    >
      <View style={{ flex: 1, gap: theme.spacing.xs }}>
        <AppText variant="label" accessibilityRole="header">
          {banner.title}
        </AppText>
        {banner.body ? <MarkdownBody body={banner.body} headings={false} testID={testID + '-body'} /> : null}
        {imageUri ? (
          <Image
            testID={testID + '-image'}
            source={{ uri: imageUri }}
            accessible
            accessibilityLabel={(image && image.altText) || banner.title}
            style={{ width: '100%', aspectRatio: image && image.width && image.height ? image.width / image.height : 2, borderRadius: theme.radii.sm }}
            contentFit="cover"
          />
        ) : null}
        {link && link.label && (link.path || link.url) ? <LinkButton label={link.label} onPress={openLink} accessibilityHint={link.url ? t.linkExternalHint : undefined} testID={testID + '-link'} role="link" /> : null}
      </View>
      {isBannerDismissible(banner) ? <IconButton icon="close" label={t.dismiss} accessibilityHint={t.dismissHint} onPress={() => content.dismiss(banner)} testID={testID + '-dismiss'} /> : null}
    </View>
  );
}

/**
 * Banners (announcements) for one placement (docs/LIVE_UPDATES_CONTRACT.md section 14.1): the
 * active, targeted banners of the manifest, highest priority first, inside their schedule and
 * not dismissed. Dismissible banners have a Dismiss button (never `Critical`); a dismissal holds
 * on this device until the banner's title or body changes. Renders nothing outside
 * AppContentProvider or when no banner applies.
 * @param {object} props
 * @param {string} [props.placement] `Global`, `Home`, `Feed`, `Forums`, `Library`, `Events`, `Directory`, `Messages`, `SignIn`, or `Account`.
 * @param {object} [props.style] Container style (for example padding where the slot sits outside a padded list).
 */
export function BannerSlot({ placement = 'Global', style, testID }) {
  const content = useAppContent();
  const theme = useTheme();
  const banners = useBanners(placement);
  if (!content || !banners.length) return null;
  const id = testID || 'banner-slot-' + placement;
  return (
    <View testID={id} style={[{ gap: theme.spacing.sm }, style]}>
      {banners.map((banner) => (
        <Banner key={banner.id} banner={banner} content={content} testID={'banner-' + banner.id} />
      ))}
    </View>
  );
}

/**
 * The `Global` banners above a navigator (the main tabs, phone and tablet). While a banner shows
 * it takes the top safe area, so the screens below get a zero top inset (no double gap).
 */
export function GlobalBannerFrame({ children }) {
  const theme = useTheme();
  const insets = useContext(SafeAreaInsetsContext) || ZERO_INSETS;
  const visible = useBanners('Global').length > 0;
  return (
    <View style={{ flex: 1 }}>
      {visible ? <BannerSlot placement="Global" style={{ paddingTop: insets.top + theme.spacing.sm, paddingBottom: theme.spacing.sm, paddingHorizontal: theme.spacing.lg, backgroundColor: theme.colors.background }} /> : null}
      <SafeAreaInsetsContext.Provider value={visible ? { ...insets, top: 0 } : insets}>{children}</SafeAreaInsetsContext.Provider>
    </View>
  );
}
