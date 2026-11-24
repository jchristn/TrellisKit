import { describe, expect, it } from 'vitest';
import { suggestHandle } from '../src/components/ProfileForm.jsx';

describe('handle suggestion', () => {
  it('builds a valid handle from the display name, else the email', () => {
    expect(suggestHandle('Sam Rivera', 'x@example.com')).toBe('sam.rivera');
    expect(suggestHandle('  José  Ñandú-Smith ', '')).toBe('jose.nandu.smith');
    expect(suggestHandle('', 'coach_kim+test@example.com')).toBe('coach_kim.test');
    expect(suggestHandle('Al', '')).toBe('al.member');
    expect(suggestHandle('!!!', '')).toBe('');
    expect(suggestHandle('A'.repeat(50), '').length).toBe(30);
  });
});
