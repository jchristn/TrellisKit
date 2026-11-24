import { expect, test } from '@playwright/test';
import { mockApi, noHorizontalScroll } from './phase2Mocks.js';

/*
 * Phase 2 smoke tests against a mocked API (no backend needed): feed, post detail with comments,
 * notifications, and the admin moderation queue, at every project size and color scheme
 * (1280 and 360, light and dark). Names come from the mocks, never from a property.
 */


test.beforeEach(async ({ page: p }) => {
  await mockApi(p);
});

test('feed renders pinned and latest posts with reactions and the unread bell', async ({ page: p }) => {
  await p.goto('/app/feed');
  await expect(p.getByRole('heading', { level: 1, name: 'Feed' })).toBeVisible();
  await expect(p.getByRole('heading', { name: /Pinned/ })).toBeVisible();
  await expect(p.getByRole('link', { name: /Notifications, 3 unread/ })).toBeVisible();
  await expect(p.getByRole('button', { name: 'Write a post' })).toBeVisible();
  await expect(p.getByText('Public sample').first()).toBeVisible();
  await expect(p.getByRole('link', { name: '@sam' }).first()).toBeVisible();
  await noHorizontalScroll(p);
});

test('post detail shows the comment tree with placeholders', async ({ page: p }) => {
  await p.goto('/app/feed/fpo_1');
  await expect(p.getByRole('heading', { level: 1 })).toContainText('A post with a fairly long title');
  await expect(p.getByRole('heading', { name: 'Comments' })).toBeVisible();
  await expect(p.getByText('A reply.')).toBeVisible();
  await expect(p.getByText('[deleted]').first()).toBeVisible();
  await expect(p.getByRole('radio', { name: 'Top' })).toBeVisible();
  await noHorizontalScroll(p);
});

test('notification center lists notifications', async ({ page: p }) => {
  await p.goto('/app/notifications');
  await expect(p.getByRole('heading', { level: 1, name: 'Notifications' })).toBeVisible();
  await expect(p.getByText('Sam Lee replied to your comment')).toBeVisible();
  await expect(p.getByRole('button', { name: 'Mark all read' })).toBeEnabled();
  await noHorizontalScroll(p);
});

test('admin moderation queue shows overdue reports', async ({ page: p }) => {
  await p.goto('/admin/moderation');
  await expect(p.getByRole('heading', { level: 1, name: 'Moderation' })).toBeVisible();
  await expect(p.getByText('Reported comment text')).toBeVisible();
  await expect(p.getByText('Overdue', { exact: true })).toBeVisible();
  await expect(p.getByRole('tab', { name: 'Word filter' })).toBeVisible();
  await noHorizontalScroll(p);
});
