import { cardFields, formatProfileValue, formatRankValue, hasProfileValue, historyEntryParts, optionColor, profilePageFields, urlHost } from '@trelliskit/shared';
import { useRouter } from 'expo-router';
import { Pressable, View } from 'react-native';
import { useProfileFields } from '../../profile/fields';
import strings from '../../strings';
import { openAppLink } from '../../links';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import VerificationBadge from '../ranks/VerificationBadge';

/**
 * Display components for configurable profile fields (docs/MULTIPROPERTY_CONTRACT.md section
 * 9.11): rank badges, formatted values with links, History lists in stored order, the profile
 * page field list, and the compact card fields of a `UserSummary`.
 */

const words = () => ({ yes: strings.profileFields.yes, no: strings.profileFields.no });

/** A round color swatch (rank and option colors). Light colors keep a visible border. */
export function Swatch({ color, size = 14, testID }) {
  const theme = useTheme();
  if (!color) return null;
  return <View testID={testID} style={{ width: size, height: size, borderRadius: theme.radii.pill, backgroundColor: color, borderWidth: 1, borderColor: theme.colors.border }} />;
}

/**
 * Rank value with its color: a swatch (profile pages) or a short color bar (cards), then the
 * shared rank text "<option label>, <level> <levelLabel>" from configuration.
 * @param {object} props
 * @param {object} props.field `Rank` field.
 * @param {{ key: string, level?: number }} props.value
 * @param {'swatch'|'bar'} [props.variant]
 * @param {'verified'|'selfReported'|null} [props.verification] Verification label when ranks are on.
 */
export function RankBadge({ field, value, variant = 'swatch', textVariant = 'body', textColor = 'text', testID, verification = null }) {
  const theme = useTheme();
  const text = formatRankValue(field, value);
  if (!text) return null;
  const color = optionColor(field, value && value.key);
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.spacing.xs }} testID={testID}>
      {color && variant === 'bar' ? <View testID={testID ? testID + '-bar' : undefined} style={{ width: 16, height: 6, borderRadius: 3, backgroundColor: color, borderWidth: 1, borderColor: theme.colors.border }} /> : null}
      {color && variant !== 'bar' ? <Swatch color={color} testID={testID ? testID + '-swatch' : undefined} /> : null}
      <AppText variant={textVariant} color={textColor} testID={testID ? testID + '-text' : undefined}>
        {text}
      </AppText>
      <VerificationBadge badge={verification} fieldLabel={field.label} testID={testID ? testID + '-verification' : undefined} />
    </View>
  );
}

function LinkText({ text, url, hint, testID }) {
  return (
    <Pressable onPress={() => openAppLink(url).catch(() => {})} accessibilityRole="link" accessibilityHint={hint} hitSlop={6} testID={testID}>
      <AppText color="primary" style={{ textDecorationLine: 'underline' }}>
        {text}
      </AppText>
    </Pressable>
  );
}

/**
 * One History value as a list in stored order. Each entry leads with its rank (color swatch and
 * text) or its first part, then the other parts; a `Member` part with a `userId` links to that
 * member's profile.
 */
export function HistoryList({ field, value, fields, testID }) {
  const theme = useTheme();
  const router = useRouter();
  const entries = Array.isArray(value) ? value : [];
  return (
    <View style={{ gap: theme.spacing.sm }} testID={testID}>
      {entries.map((entry, index) => {
        const parts = historyEntryParts(field, entry, { fields, ...words() });
        if (!parts.length) return null;
        const leadIndex = Math.max(0, parts.findIndex((p) => p.type === 'RankValue'));
        const lead = parts[leadIndex];
        const rest = parts.filter((_, i) => i !== leadIndex);
        const entryId = testID ? testID + '-' + index : undefined;
        return (
          <View key={(entry && entry.id) || index} style={{ flexDirection: 'row', gap: theme.spacing.sm }} testID={entryId}>
            <View style={{ paddingTop: 4, width: 14 }}>{lead.color ? <Swatch color={lead.color} testID={entryId ? entryId + '-swatch' : undefined} /> : null}</View>
            <View style={{ flex: 1, gap: theme.spacing.xxs }}>
              <AppText variant="label" testID={entryId ? entryId + '-lead' : undefined}>
                {lead.text}
              </AppText>
              {rest.length ? (
                <View style={{ flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', columnGap: theme.spacing.xs }}>
                  {rest.map((part, i) => {
                    const sep = i < rest.length - 1 ? ',' : '';
                    if (part.type === 'Member') {
                      const prefix = part.label ? part.label + ' ' : '';
                      if (part.userId) {
                        return (
                          <View key={part.key} style={{ flexDirection: 'row', alignItems: 'center' }}>
                            <AppText variant="caption" color="textMuted">
                              {prefix}
                            </AppText>
                            <Pressable onPress={() => router.push('/members/' + part.userId)} accessibilityRole="link" accessibilityHint={strings.profileFields.viewMember(part.text)} hitSlop={6} testID={entryId ? entryId + '-' + part.key + '-link' : undefined}>
                              <AppText variant="caption" color="primary" style={{ textDecorationLine: 'underline' }}>
                                {part.text}
                              </AppText>
                            </Pressable>
                            <AppText variant="caption" color="textMuted">
                              {sep}
                            </AppText>
                          </View>
                        );
                      }
                      return (
                        <AppText key={part.key} variant="caption" color="textMuted" testID={entryId ? entryId + '-' + part.key : undefined}>
                          {prefix + part.text + sep}
                        </AppText>
                      );
                    }
                    return (
                      <AppText key={part.key} variant="caption" color="textMuted" testID={entryId ? entryId + '-' + part.key : undefined}>
                        {part.text + sep}
                      </AppText>
                    );
                  })}
                </View>
              ) : null}
            </View>
          </View>
        );
      })}
    </View>
  );
}

/**
 * A field value for display: rank badge, History list, links for URLs, email, and phone, and
 * formatted text otherwise (locale numbers and dates, Yes or No, option labels, country names).
 */
export function ProfileValue({ field, value, fields, testID, verification = null }) {
  if (!field || !hasProfileValue(field, value)) return null;
  switch (field.type) {
    case 'Rank':
      return <RankBadge field={field} value={value} testID={testID} verification={verification} />;
    case 'History':
      return <HistoryList field={field} value={value} fields={fields} testID={testID} />;
    case 'Url':
      return <LinkText text={urlHost(value)} url={String(value)} hint={strings.profileFields.openLink(urlHost(value))} testID={testID} />;
    case 'Email':
      return <LinkText text={String(value)} url={'mailto:' + value} testID={testID} />;
    case 'Phone':
      return <LinkText text={String(value)} url={'tel:' + value} testID={testID} />;
    default:
      return <AppText testID={testID}>{formatProfileValue(field, value, { fields, ...words() })}</AppText>;
  }
}

/**
 * The profile page body: every field with `showOnProfile` and a value, in profile order, each
 * with its label and formatted value. Keys without a definition are skipped.
 * @param {object} props
 * @param {Record<string, any>} props.profile `profile` map (Me or public profile).
 * @param {object[]} [props.fields] Definitions (defaults to the property's).
 * @param {object} [props.rankBadges] Rank field key to 'verified' or 'selfReported' (`rankBadgeMap`).
 */
export function ProfileFieldList({ profile, fields: fieldsProp, testID = 'profile-fields', rankBadges = null }) {
  const theme = useTheme();
  const propertyFields = useProfileFields();
  const fields = fieldsProp || propertyFields;
  const shown = profilePageFields(fields, profile || {});
  if (!shown.length) return null;
  return (
    <View style={{ gap: theme.spacing.md }} testID={testID}>
      {shown.map((field) => (
        <View key={field.key} style={{ gap: theme.spacing.xxs }} testID={testID + '-' + field.key}>
          <AppText variant="caption" color="textMuted" accessibilityRole={field.type === 'History' ? 'header' : undefined}>
            {field.label}
          </AppText>
          <ProfileValue field={field} value={profile[field.key]} fields={fields} testID={testID + '-' + field.key + '-value'} verification={rankBadges ? rankBadges[field.key] || null : null} />
        </View>
      ))}
    </View>
  );
}

/**
 * Compact card fields of a `UserSummary` or profile (`showOnCard` with a value, at most
 * `maxCardFields`, in profile order): `Rank` as a color bar plus its text, others as short text.
 * `rankBadges` maps Rank field keys to 'verified' or 'selfReported' when ranks are on.
 */
export function CardFields({ profile, fields: fieldsProp, testID, rankBadges = null }) {
  const theme = useTheme();
  const propertyFields = useProfileFields();
  const fields = fieldsProp || propertyFields;
  const shown = cardFields(fields, profile || {});
  if (!shown.length) return null;
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', columnGap: theme.spacing.sm, rowGap: theme.spacing.xxs }} testID={testID}>
      {shown.map((field) =>
        field.type === 'Rank' ? (
          <RankBadge key={field.key} field={field} value={profile[field.key]} variant="bar" textVariant="caption" textColor="textMuted" testID={testID ? testID + '-' + field.key : undefined} verification={rankBadges ? rankBadges[field.key] || null : null} />
        ) : (
          <AppText key={field.key} variant="caption" color="textMuted" testID={testID ? testID + '-' + field.key : undefined}>
            {formatProfileValue(field, profile[field.key], { fields, ...words() })}
          </AppText>
        )
      )}
    </View>
  );
}
