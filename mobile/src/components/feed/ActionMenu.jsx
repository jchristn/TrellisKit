import strings from '../../strings';
import Button from '../Button';
import Sheet from '../Sheet';

/**
 * A menu of actions in a sheet (Edit, Delete, Report, Block, ...). Each item closes the menu
 * before running.
 * @param {object} props
 * @param {{ key: string, label: string, onPress: () => void, destructive?: boolean }[]} props.items
 */
export default function ActionMenu({ visible, items, onClose, testID = 'action-menu' }) {
  return (
    <Sheet visible={visible} title={strings.postMenu.title} onClose={onClose} testID={testID}>
      {items.map((item) => (
        <Button
          key={item.key}
          label={item.label}
          variant={item.destructive ? 'ghost' : 'secondary'}
          testID={testID + '-' + item.key}
          onPress={() => {
            onClose();
            item.onPress();
          }}
        />
      ))}
    </Sheet>
  );
}
