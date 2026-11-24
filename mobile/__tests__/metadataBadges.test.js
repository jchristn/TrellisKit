import { render, screen } from '@testing-library/react-native';
import MetadataBadges from '../src/components/MetadataBadges';
import { PropertyProvider } from '../src/property';

describe('MetadataBadges', () => {
  test('shows labels and tags as badges and collapses the rest', async () => {
    await render(<PropertyProvider><MetadataBadges labels={['technique', 'guard']} tags={{ level: 'beginner', gi: '' }} max={3} testID="meta" /></PropertyProvider>);
    const row = screen.getByTestId('meta');
    for (const text of ['technique', 'guard', 'level: beginner', '+1']) expect(row).toHaveTextContent(text, { exact: false });
    expect(screen.getByLabelText('Label technique')).toBeTruthy();
    expect(screen.getByLabelText('1 more labels and tags')).toBeTruthy();
  });

  test('renders nothing without labels or tags', async () => {
    await render(<PropertyProvider><MetadataBadges labels={[]} tags={{}} testID="meta" /></PropertyProvider>);
    expect(screen.queryByTestId('meta')).toBeNull();
  });
});
