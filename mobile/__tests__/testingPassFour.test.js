import { render, screen } from '@testing-library/react-native';
import * as Calendar from 'expo-calendar';
import ForumRow from '../src/components/forums/ForumRow';
import { continueItems } from '../src/components/library/LibraryHome';
import { notifyBlocksChanged, onBlocksChanged } from '../src/messaging/blockEvents';
import { requestAddAccess, supportsWriteOnlyAccess } from '../src/native/calendar';
import { resolveNotificationPath, staffWebPath } from '../src/notifications/deepLinks';
import { stillMissing } from '../src/profile/fields';

describe('notification deep links (parity with the website)', () => {
  test('every member notification type opens its screen', () => {
    expect(resolveNotificationPath('/ideas/ida_1?comment=cmt_2')).toBe('/ideas/ida_1?comment=cmt_2');
    expect(resolveNotificationPath('/me/events/proposals')).toBe('/events/proposals');
    expect(resolveNotificationPath('/me/directory/submissions')).toBe('/directory/submissions');
    expect(resolveNotificationPath('/series/ser_1?comment=cmt_1')).toBe('/series/ser_1?comment=cmt_1');
    expect(resolveNotificationPath('/venues/ven_1?comment=cmt_1')).toBe('/venues/ven_1?comment=cmt_1');
    expect(resolveNotificationPath('/threads/thr_1?comment=cmt_1')).toBe('/threads/thr_1?comment=cmt_1');
    expect(resolveNotificationPath('/events/evt_1')).toBe('/events/evt_1');
    expect(resolveNotificationPath('/lessons/les_1')).toBe('/lessons/les_1');
    expect(resolveNotificationPath('/messages/requests')).toBe('/messages/requests');
    expect(resolveNotificationPath('/support/tickets/tkt_1')).toBe('/support/tickets/tkt_1');
  });

  test('staff notifications open the admin console on the web', () => {
    expect(resolveNotificationPath('/admin/support/tickets/tkt_1')).toBeNull();
    expect(staffWebPath('/admin/support/tickets/tkt_1')).toBe('/admin/support/tickets/tkt_1');
    expect(staffWebPath('/admin/support?ticket=tkt_1')).toBe('/admin/support?ticket=tkt_1');
    expect(staffWebPath('/admin/directory?tab=submissions')).toBe('/admin/directory?tab=submissions');
    expect(staffWebPath('//evil.test/admin')).toBeNull();
    expect(staffWebPath('/feed/fpo_1')).toBeNull();
  });
});

describe('add to calendar asks for add-only access on iOS 17 and later', () => {
  test('write-only support by platform and version', () => {
    expect(supportsWriteOnlyAccess('ios', '17.2')).toBe(true);
    expect(supportsWriteOnlyAccess('ios', '18.0')).toBe(true);
    expect(supportsWriteOnlyAccess('ios', '16.4')).toBe(false);
    expect(supportsWriteOnlyAccess('android', 35)).toBe(false);
  });

  test('requests write-only access on iOS 17 and falls back to the regular permission elsewhere', async () => {
    Calendar.requestCalendarPermissions.mockClear();
    const ios = await requestAddAccess({ os: 'ios', version: '17.4' });
    expect(ios.writeOnly).toBe(true);
    expect(Calendar.requestCalendarPermissions).toHaveBeenLastCalledWith(true);
    const older = await requestAddAccess({ os: 'ios', version: '16.7' });
    expect(older.writeOnly).toBe(false);
    expect(Calendar.requestCalendarPermissions).toHaveBeenLastCalledWith();
    Calendar.requestCalendarPermissions.mockRejectedValueOnce(new Error('unsupported'));
    const fallback = await requestAddAccess({ os: 'ios', version: '17.0' });
    expect(fallback.writeOnly).toBe(false);
    expect(fallback.permission.granted).toBe(true);
  });

  test('keeps full access granted earlier, so the added event can still be updated', async () => {
    const original = Calendar.getCalendarPermissions;
    Calendar.getCalendarPermissions = jest.fn(async () => ({ granted: true, status: 'granted' }));
    try {
      const access = await requestAddAccess({ os: 'ios', version: '18.0' });
      expect(access.writeOnly).toBe(false);
    } finally {
      Calendar.getCalendarPermissions = original;
    }
  });
});

describe('profile setup "Still needed" follows the form', () => {
  const missing = [{ key: 'displayName' }, { key: 'level' }, { key: 'city' }];
  test('drops fields as they get a value', () => {
    expect(stillMissing(missing, null).map((m) => m.key)).toEqual(['displayName', 'level', 'city']);
    expect(stillMissing(missing, { displayName: '  ', profile: { level: { key: null, level: 0 }, city: '' } }).map((m) => m.key)).toEqual(['displayName', 'level', 'city']);
    expect(stillMissing(missing, { displayName: 'Ana', profile: { level: { key: 'Gold', level: 0 }, city: '' } }).map((m) => m.key)).toEqual(['city']);
    expect(stillMissing(missing, { displayName: 'Ana', profile: { level: { key: 'Gold' }, city: 'Lisbon' } })).toEqual([]);
  });
});

describe('continue watching', () => {
  test('never lists a completed lesson', () => {
    const page = { objects: [{ lesson: { id: 'les_1' }, progress: { completed: false } }, { lesson: { id: 'les_2' }, progress: { completed: true } }, null] };
    expect(continueItems(page).map((i) => i.lesson.id)).toEqual(['les_1']);
    expect(continueItems(null)).toEqual([]);
  });
});

describe('conversations come back after an unblock', () => {
  test('block changes reach the open lists', () => {
    const seen = [];
    const off = onBlocksChanged((change) => seen.push(change));
    notifyBlocksChanged('usr_2', false);
    off();
    notifyBlocksChanged('usr_3', true);
    expect(seen).toEqual([{ userId: 'usr_2', blocked: false }]);
  });
});

describe('forum tiles', () => {
  test('a forum without an emoji gets a neutral default icon', async () => {
    await render(<ForumRow forum={{ id: 'frm_1', name: 'General', threadCount: 2, icon: null }} onPress={() => {}} />);
    expect(screen.getByTestId('forum-frm_1-default-icon', { includeHiddenElements: true })).toBeTruthy();
  });

  test('a forum with an emoji shows it', async () => {
    await render(<ForumRow forum={{ id: 'frm_2', name: 'Off topic', threadCount: 0, icon: { emoji: '\u2615' } }} onPress={() => {}} />);
    expect(screen.queryByTestId('forum-frm_2-default-icon', { includeHiddenElements: true })).toBeNull();
    expect(screen.getByText('\u2615', { includeHiddenElements: true })).toBeTruthy();
  });
});
