import { Linking } from 'react-native';
import { useProperty } from '../../property';
import { supportEmailOf } from '../../support/support';
import strings from '../../strings';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';

const t = strings.support;

/**
 * The property's support email, shown instead of the ticket screens when `features.support` is
 * off (contract section 0.2) and on the public side of the app.
 */
export default function SupportEmailCard({ testID = 'support-email' }) {
  const property = useProperty();
  const email = supportEmailOf(property);
  return (
    <Card testID={testID}>
      <AppText variant="heading" accessibilityRole="header">
        {t.emailTitle}
      </AppText>
      <AppText color="textMuted">{t.emailBody}</AppText>
      {email ? (
        <AppText selectable testID={testID + '-address'}>
          {email}
        </AppText>
      ) : null}
      {email ? <Button label={t.emailButton} accessibilityHint={t.emailHint} variant="secondary" onPress={() => Linking.openURL('mailto:' + email).catch(() => {})} testID={testID + '-open'} /> : null}
    </Card>
  );
}
