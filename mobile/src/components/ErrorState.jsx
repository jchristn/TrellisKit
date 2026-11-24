import strings from '../strings';
import { useTheme } from '../theme';
import AppText from './AppText';
import Button from './Button';
import Card from './Card';

/**
 * Standard load failure: a heading, the reason, and Retry. Used by every screen that loads data
 * so failures look and behave the same.
 * @param {object} props
 * @param {string} props.title
 * @param {string} [props.message]
 * @param {() => void} [props.onRetry] Omit to hide the Retry button.
 * @param {string} [props.retryHint]
 */
export default function ErrorState({ title, message, onRetry, retryHint, testID }) {
  const theme = useTheme();
  return (
    <Card testID={testID} style={{ borderColor: theme.colors.danger }}>
      <AppText variant="heading" accessibilityRole="alert">
        {title}
      </AppText>
      {message ? <AppText color="textMuted">{message}</AppText> : null}
      {onRetry ? <Button label={strings.common.retry} accessibilityHint={retryHint} variant="secondary" onPress={onRetry} testID={testID ? testID + '-retry' : undefined} /> : null}
    </Card>
  );
}
