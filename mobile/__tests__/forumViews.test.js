import { act, fireEvent, render, screen } from '@testing-library/react-native';
import { Pressable, Text } from 'react-native';
import ForumRow from '../src/components/forums/ForumRow';
import { resetStoredChoices, useStoredChoice } from '../src/hooks/useStoredChoice';

const FORUM = { id: 'f1', name: 'General', description: 'Anything.', threadCount: 2, commentCount: 5, unreadThreadCount: 3, icon: { emoji: 'x' } };

describe('forum index views', () => {
  test('list and cards show the description; cards add the comment count; gallery shows only the name and thread count', async () => {
    const view = await render(<ForumRow forum={FORUM} onPress={() => {}} view="list" />);
    expect(screen.getByText('Anything.')).toBeOnTheScreen();
    expect(screen.queryByText(/5 comments/)).toBeNull();
    await view.rerender(<ForumRow forum={FORUM} onPress={() => {}} view="cards" />);
    expect(screen.getByText('Anything.')).toBeOnTheScreen();
    expect(screen.getByText(/2 threads · 5 comments/)).toBeOnTheScreen();
    await view.rerender(<ForumRow forum={FORUM} onPress={() => {}} view="gallery" />);
    expect(screen.queryByText('Anything.')).toBeNull();
    expect(screen.getByText('General')).toBeOnTheScreen();
    expect(screen.getByText('2 threads')).toBeOnTheScreen();
    // The unread badge and the press target are the same in every view.
    expect(screen.getByTestId('forum-f1-unread')).toBeOnTheScreen();
    expect(screen.getByTestId('forum-f1').props.accessibilityLabel).toContain('General');
  });

  test('a view choice is remembered on the device, and an unknown stored value falls back', async () => {
    const files = {};
    const factory = (name) => ({
      get exists() {
        return name in files;
      },
      textSync: () => files[name],
      create: () => {
        files[name] = '';
      },
      write: (text) => {
        files[name] = text;
      }
    });
    const OPTIONS = ['list', 'cards', 'gallery'];
    function Probe() {
      const [value, setValue] = useStoredChoice('forums-view', OPTIONS, 'list');
      return (
        <Pressable testID="probe" onPress={() => setValue('gallery')}>
          <Text>{value}</Text>
        </Pressable>
      );
    }
    resetStoredChoices(factory);
    const first = await render(<Probe />);
    expect(screen.getByText('list')).toBeOnTheScreen();
    await act(async () => {
      fireEvent.press(screen.getByTestId('probe'));
    });
    expect(screen.getByText('gallery')).toBeOnTheScreen();
    expect(JSON.parse(files['tk-choice-forums-view.json'])).toEqual({ version: 1, value: 'gallery' });
    await first.unmount();
    resetStoredChoices(factory);
    const second = await render(<Probe />);
    expect(screen.getByText('gallery')).toBeOnTheScreen();
    await second.unmount();
    files['tk-choice-forums-view.json'] = JSON.stringify({ version: 1, value: 'mosaic' });
    resetStoredChoices(factory);
    await render(<Probe />);
    expect(screen.getByText('list')).toBeOnTheScreen();
    resetStoredChoices();
  });
});
