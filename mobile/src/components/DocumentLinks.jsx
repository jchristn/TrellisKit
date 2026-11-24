import { useRouter } from 'expo-router';
import { View } from 'react-native';
import { documentPath } from '../documents/documents';
import strings from '../strings';
import { useTheme } from '../theme';
import LinkButton from './LinkButton';

/**
 * Inline links that open property documents in the in-app document screen.
 * @param {{ documents: object[], testID?: string }} props `testID` prefixes each link (`<testID>-<key>`).
 */
export default function DocumentLinks({ documents, testID = 'document-link' }) {
  const theme = useTheme();
  const router = useRouter();
  if (!documents || !documents.length) return null;
  return (
    <View style={{ flexDirection: 'row', columnGap: theme.spacing.lg, flexWrap: 'wrap' }}>
      {documents.map((doc) => (
        <LinkButton
          key={doc.key}
          label={doc.title || doc.key}
          role="link"
          accessibilityHint={strings.documents.readHint(doc.title || doc.key)}
          onPress={() => router.push(documentPath(doc.key))}
          testID={testID + '-' + doc.key}
        />
      ))}
    </View>
  );
}
