import { Linking } from 'react-native';
import { render, screen, fireEvent } from '@testing-library/react-native';
import MarkdownBody from '../src/components/feed/MarkdownBody';
import { PropertyProvider } from '../src/property';

const mockRouter = { push: jest.fn() };
jest.mock('expo-router', () => ({ ...jest.requireActual('expo-router'), useRouter: () => mockRouter }));

const body = [
  '# Title',
  '',
  'Use `@x **y**` now and [docs](https://example.test/a).',
  '',
  '```js',
  'const a = "<b>";',
  '```',
  '',
  '---',
  '',
  '| Belt | Years |',
  '|:---|---:|',
  '| Blue | 2 |'
].join('\n');

describe('MarkdownBody (shared parser)', () => {
  test('renders headings, inline and fenced code, rules, and tables natively', async () => {
    await render(<PropertyProvider><MarkdownBody body={body} /></PropertyProvider>);
    expect(screen.getByRole('header')).toHaveTextContent('Title');
    expect(screen.getByTestId('markdown-inline-code')).toHaveTextContent('@x **y**');
    expect(screen.getByTestId('markdown-code-block')).toHaveTextContent('const a = "<b>";');
    expect(screen.getByTestId('markdown-rule')).toBeTruthy();
    const table = screen.getByTestId('markdown-table');
    for (const cell of ['Belt', 'Years', 'Blue', '2']) expect(table).toHaveTextContent(cell, { exact: false });
  });

  test('comments keep heading lines as text and links open only http(s)', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    await render(<PropertyProvider><MarkdownBody body={'# not a heading [bad](javascript:alert(1)) [ok](https://ok.test)'} headings={false} /></PropertyProvider>);
    expect(screen.queryByRole('header')).toBeNull();
    expect(screen.getByText(/# not a heading \[bad\]\(javascript:alert\(1\)\)/)).toBeTruthy();
    fireEvent.press(screen.getByRole('link'));
    expect(mockRouter.push).toHaveBeenCalledWith({ pathname: '/external-content', params: { url: 'https://ok.test/' } });
    expect(open).not.toHaveBeenCalled();
    open.mockRestore();
  });
});

describe('MarkdownBody site links', () => {
  test('site paths render as links and open native routes', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    await render(<PropertyProvider><MarkdownBody body={'Read the [Terms](/terms) and the [forums](/app/forums)'} /></PropertyProvider>);
    const links = screen.getAllByRole('link');
    expect(links).toHaveLength(2);
    fireEvent.press(links[1]);
    expect(mockRouter.push).toHaveBeenCalledWith('/forums');
    expect(open).not.toHaveBeenCalled();
    open.mockRestore();
  });
});
