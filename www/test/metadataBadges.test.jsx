import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import MetadataBadges from '../src/components/MetadataBadges.jsx';

describe('MetadataBadges', () => {
  it('shows labels and tags as badges with titles and collapses the rest', () => {
    render(<MetadataBadges labels={['technique']} tags={{ level: 'beginner', gi: '' }} max={2} />);
    expect(screen.getByText('technique')).toHaveAttribute('title', 'Label: technique');
    expect(screen.getByText('level: beginner')).toHaveAttribute('title', 'Tag: level: beginner');
    expect(screen.getByText('+1')).toHaveAttribute('title', 'gi');
  });

  it('renders nothing without labels or tags', () => {
    const { container } = render(<MetadataBadges />);
    expect(container.firstChild).toBeNull();
  });
});
