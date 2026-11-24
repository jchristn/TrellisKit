import { render, screen } from '@testing-library/react-native';
import Avatar, { initials } from '../src/components/Avatar';

describe('Avatar initials', () => {
  test('first and last word initials', () => {
    expect(initials('Ada Lovelace')).toBe('AL');
    expect(initials('  ada   b  lovelace ')).toBe('AL');
    expect(initials('Plato')).toBe('P');
  });

  test('never splits a surrogate pair when a name ends with an emoji', () => {
    const value = initials('Sample Member \u{1F94B}');
    expect(value).toBe('SM');
    expect(/[\uD800-\uDFFF]/.test(value.replace(/[\u{10000}-\u{10FFFF}]/gu, ''))).toBe(false);
  });

  test('astral letters and emoji-only names keep whole code points', () => {
    expect(initials('\u{1D400}lpha Beta')).toBe('\u{1D400}B');
    expect(initials('\u{1F94B}')).toBe('\u{1F94B}');
    expect(initials('')).toBe('?');
    expect(initials(null)).toBe('?');
  });

  test('renders the initials when there is no photo', async () => {
    await render(<Avatar name={'Sample Member \u{1F94B}'} testID="avatar" />);
    expect(screen.getByText('SM')).toBeTruthy();
  });
});
