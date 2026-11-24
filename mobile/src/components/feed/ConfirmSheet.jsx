import strings from '../../strings';
import AppText from '../AppText';
import Button from '../Button';
import Notice from '../Notice';
import Sheet from '../Sheet';

/** A confirmation step (delete, block) with the action's error shown in place. */
export default function ConfirmSheet({ visible, title, body, confirmLabel, onConfirm, onClose, busy = false, error, testID = 'confirm' }) {
  return (
    <Sheet visible={visible} title={title} onClose={onClose} testID={testID}>
      {body ? <AppText color="textMuted">{body}</AppText> : null}
      <Notice tone="error" testID={testID + '-error'}>
        {error || null}
      </Notice>
      <Button label={confirmLabel} onPress={onConfirm} disabled={busy} testID={testID + '-confirm'} />
      <Button label={strings.common.cancel} variant="ghost" onPress={onClose} disabled={busy} testID={testID + '-cancel'} />
    </Sheet>
  );
}
