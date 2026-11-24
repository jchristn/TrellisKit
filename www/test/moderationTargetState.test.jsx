import { describe, expect, it } from 'vitest';
import { moderationActionsFor } from '@trelliskit/shared';
import { targetStateOf } from '../src/views/admin/ModerationQueue.jsx';

describe('moderation target state', () => {
  it('uses the moderation state of provider objects over their lifecycle status', () => {
    expect(targetStateOf({ status: 'Active', moderationState: 'Hidden' })).toBe('Hidden');
    expect(targetStateOf({ status: 'Active', moderationState: 'Removed' })).toBe('Removed');
    expect(targetStateOf({ status: 'Active', moderationState: 'None' })).toBe('Visible');
    expect(moderationActionsFor(targetStateOf({ status: 'Active', moderationState: 'Hidden' }))).toEqual(['Unhide', 'Remove']);
    expect(moderationActionsFor(targetStateOf({ status: 'Active', moderationState: 'Removed' }))).toEqual(['Restore']);
  });

  it('keeps the state of posts, comments, and threads', () => {
    expect(targetStateOf({ state: 'Published' })).toBe('Published');
    expect(targetStateOf({ state: 'Hidden' })).toBe('Hidden');
    expect(targetStateOf({ isDeleted: true })).toBe('Deleted');
    expect(targetStateOf({})).toBe('Active');
    expect(targetStateOf(null)).toBe('');
  });
});
